# 🎵 音乐下载器

> 多源音乐搜索下载工具，支持网易云音乐、酷我、酷狗、咪咕四大平台，自动识别并过滤非原唱版本。

[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-blue.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![Platform](https://img.shields.io/badge/platform-Windows%20%F0%9F%80%95-00a1d6.svg)](https://github.com/stwhwing/MusicDownloader)
[![GitHub stars](https://img.shields.io/github/stars/stwhwing/MusicDownloader?style=flat)](https://github.com/stwhwing/MusicDownloader/stargazers)
[![GitHub issues](https://img.shields.io/github/issues/stwhwing/MusicDownloader)](https://github.com/stwhwing/MusicDownloader/issues)

---

## ✨ 功能特性

- **多源搜索** — 一次搜索同时查询网易云、酷我、酷狗、咪咕，结果合并去重
- **自动过滤** — 自动识别并过滤 Live 版、Cover、DJ、Remix、伴奏等非原唱
- **多格式支持** — MP3 / FLAC / M4A / AAC / OGG / WAV
- **智能命名** — 自动清洗文件名（去除括号版本号、特殊字符等）
- **ID3 标签** — 下载后自动写入歌曲名、歌手、专辑、时长信息
- **可信源验证** — 网易云曲目经过官方可信度验证，提升下载可靠性
- **健康检查** — 多数据源自动健康监测，优先使用稳定来源
- **断点续传** — 下载失败自动重试（最多 2 次）
- **文件核对** — 一键核对已下载文件，智能批量重命名

---

## 📦 下载使用

### 方式一：直接运行（推荐）

下载最新发布版 `MusicDownloader.exe`，双击直接运行，**无需安装 .NET 运行时**（已自包含打包）。

> ⚠️ Windows Defender 或安全软件可能误报，请添加信任或查看源码自行编译。

### 方式二：自行编译

```bash
# 克隆后，在项目目录执行：
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
# 可执行文件输出到 bin/Release/net8.0-windows/win-x64/publish/MusicDownloader.exe
```

**编译依赖：**
- .NET 8.0 SDK（[下载地址](https://dotnet.microsoft.com/download/dotnet/8.0)）
- Windows 10/11

---

## 🖥️ 使用说明

### 搜索下载
1. 输入歌曲名或歌手名
2. 点击「搜索」，稍等片刻
3. 勾选想要下载的歌曲（可批量选择）
4. 选择下载目录，点击「开始下载」
5. 等待完成，查看下载报告

### 文件核对
1. 点击「文件工具」
2. 选择包含已下载音乐的文件夹
3. 点击「开始核对」预览匹配结果
4. 确认无误后点击「执行重命名」

---

## 🗂️ 项目结构

```
MusicDownloader/
├── Services/              # 核心服务层
│   ├── DownloadService.cs      # 下载队列 + 文件操作
│   ├── MusicSearchService.cs   # 多源搜索聚合
│   ├── NeteaseService.cs      # 网易云音乐
│   ├── KuwoService.cs          # 酷我音乐
│   ├── KugouService.cs         # 酷狗音乐
│   ├── MiggeService.cs         # 咪咕音乐
│   ├── NeteaseVerificationService.cs  # 可信源验证
│   ├── HealthCheckService.cs   # 数据源健康检查
│   ├── AudioFileService.cs     # 文件核对 + 批量重命名
│   ├── FileNameCleanerService.cs # 文件名清洗
│   ├── TagService.cs           # ID3 标签写入
│   ├── HttpClientFactory.cs    # HttpClient 生命周期管理
│   ├── SettingsService.cs      # 设置持久化
│   ├── DialogService.cs       # 统一弹窗服务
│   └── ProgressService.cs      # 进度报告服务
├── ViewModels/            # MVVM 视图模型
│   ├── MainViewModel.cs        # 主界面逻辑
│   └── FileToolViewModel.cs    # 文件工具逻辑
├── Windows/               # 弹窗窗口
│   └── RenamePreviewWindow.xaml.cs
├── MainWindow.xaml(.cs)  # 主窗口
├── Converters.cs          # WPF 值转换器
└── MusicDownloader.csproj  # 项目配置
```

---

## ⚙️ 技术栈

| 类别 | 技术 |
|------|------|
| 框架 | .NET 8.0 + WPF |
| 架构 | MVVM（CommunityToolkit.Mvvm）|
| HTTP | HttpClientFactory（带生命周期管理）|
| 标签 | TagLibSharp |
| 序列化 | Newtonsoft.Json |
| 并发 | async/await + CancellationToken |

---

## 🔧 配置说明

首次运行后配置保存在 `%APPDATA%/MusicDownloader/settings.json`。

---

## 📄 开源协议

本项目基于 [MIT License](LICENSE) 开源。

> ⚠️ 本工具仅供个人学习研究使用，请勿用于商业目的或侵犯版权。下载内容请遵守相关法律法规。

---

## 📥 下载 Releases

前往 [GitHub Releases](https://github.com/stwhwing/MusicDownloader/releases) 下载最新版本 `MusicDownloader.exe`，双击即可运行。
