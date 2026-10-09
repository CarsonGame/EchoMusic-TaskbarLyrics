# 微光 · EchoMusic 任务栏歌词

简洁的 Windows 任务栏歌词插件，让歌词随播放进度显示在任务栏左侧。

## 功能

- 单行或双行歌词；第二行可显示下一句、翻译或音译。
- 主屏幕、指定屏幕、全部屏幕同步显示；屏幕断开时自动回退。
- 按各屏幕 DPI 和任务栏高度自动适配。
- 两行独立字号和颜色，使用播放器字体列表，设置上方实时预览。
- 可开启歌词进度遮罩，支持逐字歌词及普通 LRC。
- 长歌词随进度滚动，暂停、跳转、倍速与歌词偏移同步。
- 透明、鼠标穿透、不抢焦点；可选择暂停或全屏应用时隐藏。

## 安装

需要 **Windows x64** 和 **EchoMusic 2.3.2-beta.9 或更新版本**。

### 在线安装

在 EchoMusic 的插件管理中添加以下插件源，然后安装“任务栏歌词 · 微光”：

```text
https://github.com/CarsonGame/EchoMusic-TaskbarLyrics
```

### 手动安装

下载本仓库 ZIP，将其中的 `taskbar-lyrics` 文件夹放入 `%APPDATA%\echo-music\plugins`，在播放器插件管理中刷新并启用。

## 设置

默认在主屏幕任务栏左侧显示。打开插件设置，调整后点击“保存并应用”。屏幕编号按当前连接屏幕连续显示，主屏优先；内部设备标识用于保存选择。任务栏左侧已有图标时，可调整宽度和左侧距离。

## 原生辅助程序

插件包含源码与预编译的 `taskbar-lyrics/bin/TaskbarLyrics.exe`，无需自行编译。辅助程序使用 Windows 自带的 .NET Framework WPF，界面由 XAML 声明。

插件声明 `process` 和 `webServer` 能力，通过随机端口及访问令牌的 `127.0.0.1` HTTP 服务传递歌词。首次运行和升级后，播放器可能询问是否允许启动辅助程序，请阅读提示后决定。禁用插件或退出播放器会终止辅助程序。插件不修改系统任务栏设置。

## 构建与测试

在 `taskbar-lyrics` 目录执行：

```powershell
npm install
npm run build
npm test
powershell -ExecutionPolicy Bypass -File .\build-native.ps1
```

发布时同时提交生成的 `index.js` 和 `bin/TaskbarLyrics.exe`，在线安装不会执行构建。原生源码位于 `native/`，设置模板位于 `src/SettingsPanel.html`。

## 许可证

[MIT](LICENSE)。开发使用 [EchoMusic 官方插件 API](https://github.com/hoowhoami/EchoMusicPlugins/blob/main/docs/plugin-development.md)。
