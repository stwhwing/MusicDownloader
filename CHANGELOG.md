# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/lang/zh-CN/).

---

## [2.8.16] - 2026-04-05

> 兼容性修复版 · 修复 Win10 教育版/企业版无法启动的问题

### Fixed

- **[🔴 严重]** `app.manifest` — 新增 `<compatibility>/<supportedOS>` 声明，修复 Win10 教育版/企业版弹出"此应用无法在你的电脑上运行"的问题
  - 声明支持 Windows 7 / 8 / 8.1 / 10 / 11（含官方 GUID）
  - 添加 Per-Monitor V2 DPI 感知声明，修复高 DPI 屏下界面模糊问题
- **`MusicDownloader.csproj`** — `TargetFramework` 升为 `net8.0-windows10.0.17763.0`，与 `SupportedOSPlatformVersion=10.0.17763.0` 保持一致
- **`MusicDownloader.csproj`** — 排除 `MusicDownloader.Tests\**`，防止 WPF 构建生成的临时 csproj 误包含测试文件导致构建失败

### Changed

- 发布体积：147.8 MB → 170.9 MB（引入 Win10 SDK API 引用包，属正常现象）

---

## [2.8.15] - 2026-04-01

> 三轮深度审查（9轮次）全面修复版 · GitHub首发版

### Added

- **`.gitignore`** — 标准 .NET Git 忽略规则（bin/obj/publish 等）
- **`README.md`** — 完整项目说明文档
- **`LICENSE`** — MIT 开源协议

### Fixed

- **[🔴 严重]** `KugouService.cs` — 降级到酷我 API 时使用正确 UA（新增 `_httpKuwo`），修复 Android UA 被酷我拒绝导致下载降级失败的 Bug
- **[🔴 严重]** `DownloadService.cs` — 下载取消后正确抛出 OCE，MainViewModel 正确显示"下载已取消"（不再误显示"下载完成"）
- **[🟡 重要]** `FileToolViewModel.cs` — `DeleteTypeFilesAsync` errors 匹配改用精确文件名比较，修复同前缀文件误匹配 Bug
- **[🟡 重要]** `TagService.cs` — 添加 `.aac` 格式支持（WriteTags 格式白名单此前遗漏 aac）
- **[🟡 重要]** `ResultReportWindow.xaml.cs` — 确认操作异常捕获，窗口不再卡死
- **[🟡 重要]** `DownloadService.cs` — 下载前新增 `DriveInfo` 磁盘空间检查，空间不足时弹窗警告并取消下载
- **[🟡 重要]** `DownloadService.cs` — 下载速度计算添加 `downloaded > 0` 双重保护，极快网络下载小文件不再崩溃
- **[💭 代码质量]** `NeteaseService.cs` / `KuwoService.cs` — 移除构造函数中冗余 UA 检查（工厂已统一设置）
- **[💭 代码质量]** `FileToolViewModel.cs` — 移除 `DeleteTypeFilesAsync` lambda 中冗余 `IsWorking = false`（finally 已保证）
- **[💭 代码质量]** `.wav` 格式白名单 — `TagService.cs` 添加 `.wav` 支持（TagLibSharp 可安全读取 WAV 元数据）

### Changed

- `TagService.cs` — Duration 时长写入降级为已知限制（TagLibSharp API 限制，`Properties.Duration` 对 M4A/FLAC 只读，`Tag.Length` 为曲目编号而非时长）

### Performance

- `MainViewModel.cs` — `AddToQueueAsync` 传递 `CancellationToken`，应用退出时可正确取消

---

## [2.8.14] - 2026-04-01

> 三轮深度审查（第二轮）

### Fixed

- **[🔴 严重]** `HttpClientFactory.cs` — 新增 `CreateKugouClient()` 方法，提供带 Android UA 的独立缓存实例
- **[🔴 严重]** `MusicSearchService.cs` — 酷狗改用 `CreateKugouClient()`，解决三服务共享同一 HttpClient 导致 UA 设置冲突（酷狗 "Android" UA 因 `Contains` 检查从未被正确设置的 Bug）
- **[🔴 严重]** `KugouService.cs` — 移除构造函数中手动设置 User-Agent（改由工厂统一管理）
- **[💭 代码质量]** `AudioFileService.cs` — `Dispose()` 中 `_disposed=true` 提前设置防重复释放
- **[💭 代码质量]** `MainViewModel.cs` — `StartDownloadAsync` 中 `_downloadCts` 赋值前先 Dispose 旧实例，防止 WaitHandle 泄漏
- **[💭 代码质量]** `MainViewModel.cs` — `Dispose()` 精确解绑 `_healthCheckTimer.Tick -= HealthCheckTimer_Tick`，彻底切断引用链

---

## [2.8.13] - 2026-04-01

> 三轮深度审查（第一轮）

### Fixed

- **[🔴 严重]** `MainViewModel.cs` — `SearchAsync` 中 `_searchCts` 旧实例在 Cancel 后立即 Dispose，防止 WaitHandle 系统资源泄漏
- **[🔴 严重]** `MusicSearchService.cs` — `SearchAllAsync` 中 `Task.WhenAll` OCE 处理，AggregateException 含 OCE 时正确传播取消信号（修复：取消搜索显示"搜索失败"而非"搜索已取消"）
- **[🔴 严重]** `DownloadService.cs` — `ExecuteAsync` 中 `currentFilePath` 提升到 try 外作用域，OCE 时精确清理下载中断的不完整文件
- **[💭 代码质量]** `FileToolViewModel.cs` — `Dispose()` 中 `_disposed=true` 提前设置；`_opCts` Cancel/Dispose 顺序调整到 audioService 之前
- **[💭 代码质量]** `FileToolViewModel.cs` — `ExecuteRenameAsync` 取消时不再显示"重命名完成"误导报告，正确退出
- **[💭 代码质量]** `MainViewModel.cs` — `IsFilteredSong` 三个过滤正则 `[GeneratedRegex]` 预编译（原用 `Regex.IsMatch` 热路径每次重编译）

---

## [2.8.12] - 2026-04-01

> 第三轮审查全量修复版

### Fixed

- **Services 层 OCE 传播** — `NeteaseService` / `KuwoService` / `KugouService` / `MiggeService` 全部添加 `catch (OperationCanceledException) { throw; }`
- `HealthCheckService.CheckAllAsync` — `Task.WhenAll` OCE 不再被 `catch(Exception)` 静默吞掉
- `DownloadService.DownloadFileWithRetryAsync` — 裸 `catch {}` → `catch(OCE){throw;}` + `catch(Exception)` with Debug 日志
- `MainViewModel.StartDownloadAsync` — 添加 catch(OCE) + catch(Exception) 块，网络错误有用户提示
- `MainViewModel.CheckSourcesHealthAsync` — OCE 单独处理（静默忽略，表示应用关闭）
- `MainViewModel.AddToQueueAsync` — 添加 OCE 传播
- `MainWindow.xaml` GridSplitter — 独占 Row=4（新增 Height="6" 行），状态栏 Row=3 不再被遮挡
- `RenamePreviewWindow.xaml` — ToolTip 由"含扩展名"改为"不含扩展名"
- `Converters.cs` — `EmptyToVisibleConverter` 新增 string 类型处理，防止将来绑定 string 时静默失效

### Changed

- `AppSettings.cs` — 移除冗余 `using Newtonsoft.Json`
- `DownloadTask.cs` — 移除冗余 `using MusicDownloader.Models`

---

## [2.8.10] - 2026-03-31

> 三轮审查全面修复版

### Fixed

- **[🔴 严重]** `AudioFileService.cs` — `BuildVerifiedRenamePlanAsync` 中局部变量 shadow 实例字段（限速状态不跨批次持久化）→ 移除局部变量，使用 `_rateDelayMs` / `_consecutiveSuccesses`
- **[🔴 严重]** `MainViewModel.cs` — `SearchAsync` 每次搜索清空 `SelectedSongs` → 移除该行，保留跨搜索的已选项
- **[🟡 重要]** `AudioFileService.cs` — 清洗逻辑加空值保护（`?? ""`）
- **[🟡 重要]** `FileToolViewModel.cs` — 状态时序优化（`IsOperationRunning` / `CanPause` 移至预览确认后）
- **[🟡 重要]** `FileToolViewModel.cs` — 取消异常文本区分阶段（"核对阶段已取消" vs "重命名阶段已取消"）
- **[🟡 重要]** `AudioFileService.cs` — `ExecuteRenamePlan` 新增 `CancellationToken ct` 支持，重命名阶段可响应暂停

---

## [2.8.9] - 2026-03-31

### Fixed

- **`Converters.cs`** — `BoolToVisibilityConverter.ConvertBack` 和 `InverseBoolToVisibilityConverter.ConvertBack` 逻辑错误修复（`v is Visibility.Visible` 永远为 false → 改用 `(v as Visibility?) == Visibility.Visible`）

---

## [2.8.8] - 2026-03-31

> Superpowers 三轮深度审查全面修复

### Fixed

- **`HealthCheckService.cs`** — `FormUrlEncodedContent` 生命周期问题修复（`using var` 模式）
- **`HttpClientFactory.cs`** — 修复 socket 耗尽问题（实现静态缓存 + 双重检查锁定）
- **`AudioFileService.cs`** — 修复线程安全问题（`_rateLock` 保护限速器状态）
- **`KuwoService.cs`** / **`HealthCheckService.cs`** / **`MiggeService.cs`** — 修复空 catch 块（添加 `Debug.WriteLine` 诊断日志）
- **`KugouService.cs`** — 修复危险的 `ContinueWith` 用法（改用 await 模式）
- **`MusicSearchService.cs`** — 修复 `Dispose()` 错误释放缓存的 HttpClient
- **`DownloadService.cs`** — 修复静默 catch 块（添加异常日志）

---

## [2.8.5] - 2026-03-31

> 绑定异常根本原因诊断修复

### Fixed

- **`MainViewModel.cs`** — 根本原因诊断：`_ = CheckSourcesHealthAsync()` 在 settings 加载**之前**就以 fire-and-forget 方式启动，健康检查异步方法在网络请求完成后回调时 WPF 绑定可能尚未完成初始化
- Settings 加载移到构造函数最前（在异步操作之前）
- `CheckSourcesHealthAsync()` 通过 `Dispatcher.BeginInvoke(DispatcherPriority.Loaded, ...)` 延迟执行，确保窗口完全加载后才执行
- `_healthCheckTimer.Start()` 也移到 settings 加载之后

---

## [2.8.4] - 2026-03-30

### Fixed

- **`FileNameCleanerService.cs`** — 正则双重转义 Bug（`\\s*` → `\s*`）
- **`FileToolViewModel.cs`** — Resource Dispose 问题
- **`DialogService.cs`** — 双重关闭修复

---

## [2.8.3] - 2026-03-30

### Fixed

- **`MainWindow.xaml.cs`** — `Closing` 事件补充 `Dispose()`
- **`App.xaml`** — 添加 `ShutdownMode`
- **`HealthCheckService.cs`** — 健康检查防重入改为 `_healthCheckRunning`

---

## [2.8.2] - 2026-03-30

> 优化版本：新增单元测试 + 健康检查 + 下载进度细化 + 历史搜索

### Added

- **`Services/HealthCheckService.cs`** — 搜索服务健康检查（每5分钟自动检查四平台状态，🟢/🔴/🟡/⚪ 四色指示）
- **`Services/SearchHistoryService.cs`** — 历史搜索记录（最多50条，自动去重，支持清空/单条删除）
- **`Models/DownloadTask.cs`** — 新增 `SpeedText` / `RemainingText` 属性
- **`Services/DownloadService.cs`** — 实时下载速度 + 预计剩余时间显示

### Changed

- **`MusicDownloader.Tests/`** — 新增 xUnit 单元测试项目（32个用例，覆盖 FileNameCleanerService 核心方法）
- `SettingsService.cs` — 窗口关闭强制保存设置
- `DownloadService.cs` — 下载重试机制（最多2次）

---

## [2.8.1] - 2026-03-30

> 初始发布版本

### Added

- **四平台搜索**：网易云（无上限）/ 酷我（≤100/页）/ 酷狗（≤60/页）/ 咪咕（≤30/页，flac 无损）
- **多格式支持**：FLAC / MP3 / AAC / WAV
- **智能文件名清洗**：`\u0026` → `&`、`\&` → `&`、TVB元数据移除、个人化标签移除、孤立括号修复、碰撞自动加 `(1)(2)` 后缀
- **网易云权威验证**：`/api/song/detail` 获取可信歌名/艺术家，TagLibSharp 读取本地标签对比
- **文件工具**：Cover/DJ/AI 识别 → 回收站安全删除、批量重命名预览确认
- **自适应限速**：初始300ms，成功5次后缩短至100ms，失败重置500ms
- **MVVM 架构**：CommunityToolkit.Mvvm、16个服务层、独立 DialogService/ProgressService/DownloadService
- **全局异常处理**：DispatcherUnhandledException + UnhandledException + UnobservedTaskException
- **设置持久化**：settings.json 防抖保存（1s）

### Tech Stack

- .NET 8 + WPF
- CommunityToolkit.Mvvm
- TagLibSharp
- Newtonsoft.Json
- 发布大小：155 MB（自包含单文件）

---

## [2.5] - 2026-03-27

> 设计规划阶段

### Planned

- TagLibSharp NuGet 引入（`TagLibSharp 2.3.0`）
- 截断括号修复（`FixTruncatedParen()` 方法）
- DJ 版识别正则增强
- 三栏联动（搜索结果/下载队列/文件名编辑）
- ID3 标签静默写入

---

## Older

更早版本（v2.5 之前）的变更记录暂无文档。
