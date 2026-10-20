param(
    [Parameter(Mandatory)][string]$SourceDirectory,
    [string[]]$Files = @('bin\TaskbarLyrics.exe'),
    [ValidateSet('Auto','Native','Plugin')][string]$ReloadMode = 'Auto',
    [switch]$ForceReload
)
$ErrorActionPreference = 'Stop'
$pluginRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$sourceRoot = (Resolve-Path -LiteralPath $SourceDirectory).Path
if ($pluginRoot -eq $sourceRoot) { throw '更新来源必须是独立的构建目录' }
$nativePath = Join-Path $pluginRoot 'bin\TaskbarLyrics.exe'

function Get-Connection {
    $process = Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -eq $nativePath } | Select-Object -First 1
    if (!$process) { throw '未找到运行中的歌词程序，请先启用插件' }
    $match = [regex]::Match($process.CommandLine, 'http://127\.0\.0\.1:[0-9]+/state\?token=[A-Za-z0-9-]+')
    if (!$match.Success) { throw '未找到插件本机更新通道' }
    $uri = [Uri]$match.Value
    return @{ Origin = $uri.GetLeftPart([UriPartial]::Authority); Query = $uri.Query.TrimStart('?'); Pid = $process.ProcessId }
}

function Invoke-Maintenance($Connection, [string]$Action, [string]$Ticket = '', [string]$Mode = 'native') {
    $url = $Connection.Origin + '/maintenance?' + $Connection.Query + '&action=' + $Action + '&mode=' + $Mode
    if ($Ticket) { $url += '&ticket=' + [Uri]::EscapeDataString($Ticket) }
    return Invoke-RestMethod -Uri $url -Method Post -TimeoutSec 20
}

function Wait-HelperStopped {
    $until = [DateTime]::UtcNow.AddSeconds(10)
    do {
        $running = @(Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -eq $nativePath })
        if (!$running.Count) { return }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $until)
    throw '歌词程序尚未退出，未替换文件'
}

function Set-FileAtomic([string]$Source, [string]$Destination) {
    $directory = [IO.Path]::GetDirectoryName($Destination)
    if (!(Test-Path -LiteralPath $directory)) { New-Item -ItemType Directory -Path $directory | Out-Null }
    $temporary = Join-Path $directory ('.update-' + [Guid]::NewGuid().ToString('N'))
    try {
        Copy-Item -LiteralPath $Source -Destination $temporary
        if ((Get-FileHash -LiteralPath $Source).Hash -ne (Get-FileHash -LiteralPath $temporary).Hash) { throw '复制校验失败' }
        Move-Item -LiteralPath $temporary -Destination $Destination -Force
    } finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force } }
}

# 提前验证所有路径；更新入口不能触碰播放器、其他插件或 Git 元数据。
$plan = @()
foreach ($file in ($Files | Select-Object -Unique)) {
    if ([IO.Path]::IsPathRooted($file) -or $file.Contains(':') -or ($file -split '[\\/]') -contains '..' -or $file -match '^([.]git|node_modules)([\\/]|$)') { throw ('不允许的更新路径：' + $file) }
    $destination = [IO.Path]::GetFullPath((Join-Path $pluginRoot $file))
    $source = [IO.Path]::GetFullPath((Join-Path $sourceRoot $file))
    if (!$destination.StartsWith($pluginRoot + '\', [StringComparison]::OrdinalIgnoreCase) -or !$source.StartsWith($sourceRoot.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw '更新路径越界' }
    $cursor = $destination
    while ($cursor -and $cursor -ne $pluginRoot) {
        if ((Test-Path -LiteralPath $cursor) -and ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw '更新目标不能经过链接或目录联接' }
        $cursor = [IO.Path]::GetDirectoryName($cursor)
    }
    if (!(Test-Path -LiteralPath $source -PathType Leaf)) { throw ('缺少构建文件：' + $file) }
    if ((Test-Path -LiteralPath $destination) -and !(Test-Path -LiteralPath $destination -PathType Leaf)) { throw ('更新目标必须是文件：' + $file) }
    if (Test-Path -LiteralPath $destination -PathType Leaf) {
        if ((Get-FileHash -LiteralPath $source).Hash -eq (Get-FileHash -LiteralPath $destination).Hash) { continue }
    }
    $plan += @{ Relative = $file; Source = $source; Destination = $destination; Existed = (Test-Path -LiteralPath $destination) }
}
if (!$plan.Count -and !$ForceReload) { Write-Output '文件已是最新，无需重新加载。'; return }
if ($ReloadMode -eq 'Native' -and @($plan | Where-Object { $_.Relative -ne 'bin\TaskbarLyrics.exe' -and $_.Relative -ne 'bin/TaskbarLyrics.exe' }).Count) { throw '脚本或设置界面更新必须重新加载插件，请使用 Auto 或 Plugin 模式' }
$mode = if ($ReloadMode -eq 'Native') { 'native' } elseif ($ReloadMode -eq 'Plugin' -or @($plan | Where-Object { $_.Relative -ne 'bin\TaskbarLyrics.exe' -and $_.Relative -ne 'bin/TaskbarLyrics.exe' }).Count) { 'plugin' } else { 'native' }
$connection = Get-Connection
$backup = Join-Path $env:TEMP ('echomusic-plugin-backup-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $backup | Out-Null
foreach ($item in $plan) {
    if ($item.Existed) {
        $save = Join-Path $backup $item.Relative
        New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($save)) | Out-Null
        Copy-Item -LiteralPath $item.Destination -Destination $save
    }
}
$ticket = ''
$copied = @()
try {
    $session = Invoke-Maintenance $connection 'prepare' '' $mode
    if (!$session.ok -or !$session.ticket) { throw '插件未确认进入更新状态' }
    $ticket = $session.ticket
    Wait-HelperStopped
    foreach ($item in $plan) {
        Invoke-Maintenance $connection 'renew' $ticket $mode | Out-Null
        Set-FileAtomic $item.Source $item.Destination
        $copied += $item
    }
    Invoke-Maintenance $connection 'commit' $ticket $mode | Out-Null
    $ticket = ''
    $until = [DateTime]::UtcNow.AddSeconds(30)
    $restored = $false
    do {
        try {
            $next = Get-Connection
            if ($next.Pid -ne $connection.Pid) {
                $state = Invoke-RestMethod -Uri ($next.Origin + '/state?' + $next.Query) -TimeoutSec 3
                if ($state.settings) { $restored = $true; break }
            }
        } catch { }
        Start-Sleep -Milliseconds 200
    } while ([DateTime]::UtcNow -lt $until)
    if (!$restored) { throw '文件已替换，但未确认插件恢复；备份已保留，请检查插件管理状态' }
    Write-Output ('更新完成，已自动恢复。模式：' + $mode + '；备份：' + $backup)
} catch {
    $failure = $_
    if ($ticket) {
        try {
            foreach ($item in $copied) {
                if ($item.Existed) { Set-FileAtomic (Join-Path $backup $item.Relative) $item.Destination }
                elseif (Test-Path -LiteralPath $item.Destination) { Remove-Item -LiteralPath $item.Destination -Force }
            }
            Invoke-Maintenance $connection 'abort' $ticket $mode | Out-Null
        } catch { Write-Warning ('自动恢复未完成，备份：' + $backup + '；原因：' + $_.Exception.Message) }
    }
    throw $failure
}
