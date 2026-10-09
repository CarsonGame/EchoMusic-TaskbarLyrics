$ErrorActionPreference = 'Stop'
$taskPluginRoot = $PSScriptRoot
$taskWpfRoot = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF'
New-Item -ItemType Directory -Force (Join-Path $taskPluginRoot 'bin') | Out-Null
$taskCompilerArgs = @(
    '/nologo', '/target:winexe', '/platform:x64', '/optimize+',
    ('/out:' + (Join-Path $taskPluginRoot 'bin\TaskbarLyrics.exe')),
    ('/win32manifest:' + (Join-Path $taskPluginRoot 'native\app.manifest')),
    ('/resource:' + (Join-Path $taskPluginRoot 'native\LyricsWindow.xaml') + ',LyricsWindow.xaml'),
    '/reference:System.Net.Http.dll', '/reference:System.Web.Extensions.dll', '/reference:System.Xaml.dll',
    ('/reference:' + (Join-Path $taskWpfRoot 'PresentationFramework.dll')),
    ('/reference:' + (Join-Path $taskWpfRoot 'PresentationCore.dll')),
    ('/reference:' + (Join-Path $taskWpfRoot 'WindowsBase.dll')),
    (Join-Path $taskPluginRoot 'native\TaskbarLyrics.cs')
)
& 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' @taskCompilerArgs
if ($LASTEXITCODE -ne 0) { throw '原生程序编译失败' }
