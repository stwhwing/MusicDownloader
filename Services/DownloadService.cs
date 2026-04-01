using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using MusicDownloader.Models;

namespace MusicDownloader.Services;

/// <summary>
/// 下载服务：封装下载队列管理、文件路径生成、HTTP 下载、ID3 标签写入
/// 从 MainViewModel.StartDownloadAsync / GetUniqueFilePath / SanitizeFileName 抽离
/// </summary>
public class DownloadService : IDisposable
{
    private readonly MusicSearchService _searchService;
    private readonly DialogService _dialogService;
    private readonly HttpClient _httpClient;
    private const int MaxRetryCount = 2; // 最大重试次数

    public DownloadService(MusicSearchService searchService, DialogService dialogService)
    {
        _searchService = searchService;
        _dialogService = dialogService;
        // DownloadClient 是 HttpClientFactory 的静态缓存实例，生命周期由工厂管理
        // DownloadService.Dispose() 不应释放它，避免破坏其他服务的 HttpClient 引用
        _httpClient = HttpClientFactory.CreateDownloadClient();
    }

    /// <summary>
    /// 确保下载目录存在且可写
    /// </summary>
    /// <returns>错误信息；null 表示正常</returns>
    public string? ValidateAndCreateDirectory(string downloadPath)
    {
        if (string.IsNullOrWhiteSpace(downloadPath))
            return "下载路径不能为空";

        try
        {
            if (!Directory.Exists(downloadPath))
                Directory.CreateDirectory(downloadPath);

            // 写入测试文件验证可写性
            var testFile = Path.Combine(downloadPath, ".write_test");
            System.IO.File.WriteAllText(testFile, "");
            System.IO.File.Delete(testFile);
            return null;
        }
        catch (UnauthorizedAccessException)
            { return "无权写入该目录（权限不足）"; }
        catch (Exception ex)
            { return $"无法创建目录：{ex.Message}"; }
    }

    /// <summary>
    /// 获取安全的目标路径（处理重名）
    /// </summary>
    public string GetUniqueFilePath(string downloadPath, string fileName)
    {
        var safeName = FileNameCleanerService.SanitizeFileName(fileName);
        var filePath = Path.Combine(downloadPath, safeName);

        if (!File.Exists(filePath))
            return filePath;

        var dir = Path.GetDirectoryName(filePath)!;
        var name = Path.GetFileNameWithoutExtension(filePath);
        var ext = Path.GetExtension(filePath);
        var counter = 1;
        while (File.Exists(filePath))
        {
            safeName = $"{name} ({counter++}){ext}";
            filePath = Path.Combine(dir, safeName);
        }
        return filePath;
    }

    /// <summary>
    /// 执行下载（遍历队列中所有已选"等待中"任务）
    /// </summary>
    /// <param name="downloadPath">下载目录</param>
    /// <param name="queue">下载队列（ObservableCollection）</param>
    /// <param name="statusUpdate">状态文本更新回调</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>下载完成统计 (done, fail)</returns>
    public async Task<(int done, int fail)> ExecuteAsync(
        string downloadPath,
        ObservableCollection<DownloadTask> queue,
        Action<string> statusUpdate,
        CancellationToken ct = default)
    {
        var pathError = ValidateAndCreateDirectory(downloadPath);
        if (pathError != null)
        {
            _dialogService.ShowError(pathError, "路径错误");
            statusUpdate("下载路径无效，已取消");
            return (0, 0);
        }

        // 检查磁盘剩余空间（每个文件预留 50MB）
        var pending = queue.Where(t => t.Status == "等待中" && t.IsSelected).ToList();
        const long minFreeBytesPerFile = 50L * 1024 * 1024;
        var pendingCount = pending.Count;
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(downloadPath) ?? downloadPath);
            var requiredSpace = minFreeBytesPerFile * pendingCount;
            if (drive.IsReady && drive.AvailableFreeSpace < requiredSpace)
            {
                var availMb = drive.AvailableFreeSpace / 1024 / 1024;
                var reqMb = requiredSpace / 1024 / 1024;
                _dialogService.ShowError(
                    $"磁盘空间不足。剩余 {availMb} MB，下载 {pendingCount} 首歌曲至少需要 {reqMb} MB。",
                    "磁盘空间不足");
                statusUpdate("磁盘空间不足，已取消");
                return (0, 0);
            }
        }
        catch (Exception ex)
        {
            // 磁盘信息获取失败不阻塞下载，仅记录日志
            System.Diagnostics.Debug.WriteLine($"[DownloadService] 获取磁盘空间失败: {ex.Message}");
        }
        if (pending.Count == 0)
        {
            statusUpdate("没有可下载的任务");
            return (0, 0);
        }

        statusUpdate($"开始下载 {pending.Count} 首歌曲...");
        int done = 0, fail = 0;
        bool wasCancelled = false;

        foreach (var task in pending)
        {
            if (ct.IsCancellationRequested) break;
            task.Status = "下载中";
            statusUpdate($"下载中: {task.Song.Artist} - {task.Song.Title}");

            string? currentFilePath = null; // 提升作用域，用于 OCE 时清理不完整文件

            try
            {
                var (url, reason) = await _searchService.GetDownloadUrlWithReasonAsync(task.Song, ct);
                if (string.IsNullOrEmpty(url))
                {
                    task.Status = "失败";
                    task.ErrorMessage = reason ?? "无法获取下载链接";
                    fail++;
                    continue;
                }

                // 使用 FileNameCleanerService 生成干净文件名
                var baseName = FileNameCleanerService.GenerateCleanFileName(
                    task.FinalFileName, task.Song.Artist, task.Song.Title);
                var ext = string.IsNullOrEmpty(task.Song.Format) ? ".mp3" : $".{task.Song.Format}";
                var fileName = FileNameCleanerService.SanitizeFileName(baseName) + ext;
                currentFilePath = GetUniqueFilePath(downloadPath, fileName);

                task.Progress = 0;
                var downloadSuccess = await DownloadFileWithRetryAsync(url, currentFilePath, task, ct);
                if (!downloadSuccess)
                {
                    task.Status = "失败";
                    task.ErrorMessage = "下载失败（已重试多次）";
                    // 清理不完整的下载文件
                    try { if (File.Exists(currentFilePath)) File.Delete(currentFilePath); }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"清理不完整文件失败: {currentFilePath} - {ex.Message}");
                    }
                    fail++;
                    continue;
                }
                task.Progress = 100;
                task.Status = "已完成";
                done++;

                // 静默写入 ID3 标签
                TagService.WriteTags(currentFilePath,
                    task.Song.Artist,
                    task.Song.Title,
                    task.Song.Album,
                    task.Song.Duration);
            }
            catch (OperationCanceledException)
            {
                task.Status = "已取消";
                wasCancelled = true;
                // 清理下载中断导致的不完整文件（文件路径已知时才尝试删除）
                if (currentFilePath != null)
                {
                    try { if (File.Exists(currentFilePath)) File.Delete(currentFilePath); }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[DownloadService] 清理取消残留文件失败: {currentFilePath} - {ex.Message}");
                    }
                }
                break;
            }
            catch (Exception ex)
            {
                task.Status = "失败";
                task.ErrorMessage = ex.Message;
                fail++;
            }
        }

        // 被取消时抛出 OCE，让调用方（MainViewModel）正确显示"下载已取消"
        // 而不是显示误导性的"下载完成"
        if (wasCancelled)
        {
            ct.ThrowIfCancellationRequested();
            // 若 ct 已经复位（极罕见），手动抛出
            throw new OperationCanceledException("下载已被用户取消");
        }

        statusUpdate($"下载完成: 成功 {done}, 失败 {fail}");
        return (done, fail);
    }

    /// <summary>
    /// 带重试机制的下载
    /// </summary>
    private async Task<bool> DownloadFileWithRetryAsync(string url, string filePath, DownloadTask task, CancellationToken ct)
    {
        for (int retry = 0; retry <= MaxRetryCount; retry++)
        {
            try
            {
                task.Progress = 0;
                await DownloadFileAsync(url, filePath, task, ct);
                return true;
            }
            catch (HttpRequestException) when (retry < MaxRetryCount)
            {
                // 网络错误，等待后重试
                await Task.Delay(1000 * (retry + 1), ct);
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested && retry < MaxRetryCount)
            {
                // 超时，等待后重试
                await Task.Delay(1000 * (retry + 1), ct);
            }
            catch (OperationCanceledException) { throw; } // 不吞掉取消异常
            catch (Exception ex)
            {
                // 其他错误，不重试
                System.Diagnostics.Debug.WriteLine($"[DownloadService.DownloadFileWithRetryAsync] 下载失败: {ex.Message}");
                return false;
            }
        }
        return false;
    }

    /// <summary>
    /// HTTP 流式下载文件，带进度更新
    /// </summary>
    private async Task DownloadFileAsync(string url, string filePath, DownloadTask task, CancellationToken ct)
    {
        using var resp = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        var totalBytes = resp.Content.Headers.ContentLength ?? -1;
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        await using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

        var buffer = new byte[8192];
        long downloaded = 0;
        int bytesRead;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        long lastUpdate = 0;

        while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
        {
            await fs.WriteAsync(buffer, 0, bytesRead, ct);
            downloaded += bytesRead;

            if (totalBytes > 0)
            {
                task.Progress = Math.Min(100, (double)downloaded / totalBytes * 100);

                // 每 0.5 秒更新一次速度
                var elapsed = sw.ElapsedMilliseconds;
                if (elapsed - lastUpdate > 500)
                {
                    lastUpdate = elapsed;
                    var speedBps = elapsed > 0 && downloaded > 0 ? downloaded * 1000.0 / elapsed : 0;
                    task.SpeedText = FormatSpeed(speedBps);

                    // 计算剩余时间
                    if (speedBps > 0)
                    {
                        var remainingBytes = totalBytes - downloaded;
                        var remainingSecs = (int)(remainingBytes / speedBps);
                        task.RemainingText = remainingSecs < 60
                            ? $"剩余 {remainingSecs}秒"
                            : $"剩余 {remainingSecs / 60}:{remainingSecs % 60:D2}";
                    }
                }
            }
        }
    }

    private static string FormatSpeed(double bytesPerSecond)
    {
        if (bytesPerSecond < 1024)
            return $"{bytesPerSecond:F0} B/s";
        if (bytesPerSecond < 1024 * 1024)
            return $"{bytesPerSecond / 1024:F1} KB/s";
        return $"{bytesPerSecond / 1024 / 1024:F1} MB/s";
    }

    /// <summary>
    /// 构建下载报告数据
    /// </summary>
    public (List<SummaryItem> summary, List<ReportRow> rows) BuildReport(
        List<DownloadTask> tasks, int done, int fail, string downloadPath)
    {
        var rows = tasks.Select((t, i) => new ReportRow
        {
            Index = i + 1,
            FileName = t.FinalFileNameWithExt,
            SubText = t.Status == "失败" ? t.ErrorMessage ?? "" : $"{t.Song.Source} · {t.Song.Format.ToUpperInvariant()}",
            Note = t.Status == "已完成" ? "✅ 下载成功"
                : t.Status == "失败" ? $"❌ {t.ErrorMessage}"
                : t.Status,
            NoteColor = t.Status == "已完成" ? StatusColors.Match
                : t.Status == "失败" ? StatusColors.Error
                : StatusColors.Pending
        }).ToList();

        var summary = new List<SummaryItem>
        {
            new() { Label = "共下载", Count = tasks.Count, Color = "#E4E4F0" },
            new() { Label = "✅ 成功", Count = done, Color = StatusColors.Match },
            new() { Label = "❌ 失败", Count = fail, Color = fail > 0 ? StatusColors.Error : "#606070" },
        };

        return (summary, rows);
    }

    // HttpClient 来自工厂静态缓存，由 HttpClientFactory 统一管理生命周期，此处不释放
    public void Dispose() { /* 静态缓存 HttpClient 不在此处释放 */ }
}
