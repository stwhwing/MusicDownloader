using System.Net.Http;
using System.Text.RegularExpressions;
using MusicDownloader.Models;

namespace MusicDownloader.Services;

public class MusicSearchService : IDisposable
{
    private readonly HttpClient _httpNetease;
    private readonly HttpClient _httpKuwo;
    private readonly HttpClient _httpKugou;
    private readonly HttpClient _httpMigge;

    private readonly NeteaseService _netease;
    private readonly KuwoService _kuwo;
    private readonly KugouService _kugou;
    private readonly MiggeService _migge;

    public MusicSearchService()
    {
        // 每个服务独立 HttpClient，避免 header 冲突（由 HttpClientFactory 统一管理）
        // 注意：酷狗需要 "Android" User-Agent，与酷我/咪咕的 "Mozilla/5.0" 不同，
        // 必须使用独立的 HttpClient 实例，否则共享缓存实例会导致 UA 设置冲突。
        _httpNetease = HttpClientFactory.CreateNeteaseClient();
        _httpKuwo = HttpClientFactory.CreateMusicClient();
        _httpKugou = HttpClientFactory.CreateKugouClient(); // 酷狗专用：Android UA
        _httpMigge = HttpClientFactory.CreateMusicClient();

        _netease = new NeteaseService(_httpNetease);
        _kuwo = new KuwoService(_httpKuwo);
        _kugou = new KugouService(_httpKugou);
        _migge = new MiggeService(_httpMigge);
    }

    public async Task<List<Song>> SearchAllAsync(
        string keyword,
        HashSet<string> enabledSources,
        HashSet<string> enabledFormats,
        int limit = 100,
        CancellationToken ct = default)
    {
        var tasks = new List<Task<List<Song>>>();

        if (enabledSources.Contains("网易云"))
            tasks.Add(_netease.SearchAsync(keyword, limit, ct));
        if (enabledSources.Contains("酷我"))
            tasks.Add(_kuwo.SearchAsync(keyword, limit, ct));
        if (enabledSources.Contains("酷狗"))
            tasks.Add(_kugou.SearchAsync(keyword, limit, ct));
        if (enabledSources.Contains("咪咕"))
            tasks.Add(_migge.SearchAsync(keyword, limit, ct));

        if (tasks.Count == 0)
            return new List<Song>();

        List<Song>[] results;
        try
        {
            results = await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException) { throw; }
        catch (AggregateException ae) when (ae.InnerExceptions.Any(e => e is OperationCanceledException))
        {
            throw new OperationCanceledException(ct);
        }
        var allSongs = results.SelectMany(x => x).ToList();

        // 按用户选择的音频格式过滤（区分大小写）
        if (enabledFormats != null && enabledFormats.Count > 0)
        {
            allSongs = allSongs.Where(s => enabledFormats.Contains(s.Format.ToLowerInvariant())).ToList();
        }

        // 去重：同歌手+同歌名优先保留无损格式（flac > aac > mp3）
        var formatPriority = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["flac"] = 3, ["aac"] = 2, ["mp3"] = 1, ["wav"] = 1, ["m4a"] = 1
        };
        var seen = new Dictionary<string, Song>(StringComparer.OrdinalIgnoreCase);

        foreach (var song in allSongs.OrderByDescending(s => formatPriority.GetValueOrDefault(s.Format, 0)))
        {
            var key = $"{song.Artist}|{song.Title}";
            if (!seen.ContainsKey(key))
            {
                seen[key] = song;
            }
            else
            {
                var existing = seen[key];
                if (formatPriority.GetValueOrDefault(existing.Format, 0) < formatPriority.GetValueOrDefault(song.Format, 0))
                    seen[key] = song;
            }
        }

        return seen.Values.ToList();
    }

    public async Task<(string? Artist, string? Title, int Duration)> GetVerifiedSongInfoAsync(Song song, CancellationToken ct = default)
    {
        if (song.Source == "网易云")
            return await _netease.GetVerifiedSongInfoAsync(song.Id, ct);
        return (null, null, 0);
    }

    /// <summary>
    /// 获取下载链接及失败原因
    /// </summary>
    /// <returns>(url, reason) — reason 仅在 url==null 时有值</returns>
    public async Task<(string? Url, string? Reason)> GetDownloadUrlWithReasonAsync(Song song, CancellationToken ct = default)
    {
        string? url;
        string? reason;
        try
        {
            switch (song.Source)
            {
                case "网易云":
                    url = await _netease.GetDownloadUrlAsync(song, ct);
                    reason = url == null ? "网易云版权受限或歌曲已下架" : null;
                    break;
                case "酷我":
                    url = await _kuwo.GetDownloadUrlAsync(song, ct);
                    reason = url == null ? "酷我链接转换失败（版权限制）" : null;
                    break;
                case "酷狗":
                    url = await _kugou.GetDownloadUrlAsync(song, ct);
                    reason = url == null ? "酷狗版权受限（备选酷我也失败）" : null;
                    break;
                case "咪咕":
                    url = await _migge.GetDownloadUrlAsync(song, ct);
                    reason = url == null ? "咪咕无可用音质或版权受限" : null;
                    break;
                default:
                    url = null;
                    reason = $"不支持的来源：{song.Source}";
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            url = null;
            reason = "网络连接失败，请检查网络";
        }
        catch
        {
            url = null;
            reason = "获取下载链接时发生未知错误";
        }
        return (url, reason);
    }

    /// <summary>
    /// 兼容旧接口（仅返回 URL，失败原因统一显示"无法获取下载链接"）
    /// </summary>
    public async Task<string?> GetDownloadUrlAsync(Song song, CancellationToken ct = default)
    {
        var (url, _) = await GetDownloadUrlWithReasonAsync(song, ct);
        return url;
    }

    public async Task<string> GetSongFormatAsync(Song song, CancellationToken ct = default)
    {
        if (!string.IsNullOrEmpty(song.Format) && song.Format != "mp3")
            return song.Format;

        var url = await GetDownloadUrlAsync(song, ct);
        if (!string.IsNullOrEmpty(url))
        {
            // 优先解析 toneFlag（咪咕格式标准参数）
            if (TryParseToneFlag(url, out string format))
                return format;
            // 回退：URL 路径猜测
            if (url.Contains(".flac") || url.Contains("/flac/"))
                return "flac";
            if (url.Contains(".m4a") || url.Contains("/m4a/"))
                return "m4a";
        }
        return "mp3";
    }

    /// <summary>
    /// 咪咕格式参数 toneFlag 标准：SQ=flac, HQ=mp3, LR=AAC
    /// </summary>
    private static bool TryParseToneFlag(string url, out string format)
    {
        format = "mp3";
        try
        {
            var m = Regex.Match(url, @"[?&]toneFlag=([^&\s]+)", RegexOptions.IgnoreCase);
            if (!m.Success) return false;
            var flag = m.Groups[1].Value.ToUpperInvariant();
            if (flag is "SQ" or "LOSSLESS") { format = "flac"; return true; }
            if (flag is "HQ" or "STD") { format = "mp3"; return true; }
            if (flag is "LR") { format = "aac"; return true; }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MusicSearchService.TryParseToneFlag] 解析 toneFlag 失败: {ex.Message}");
        }
        return false;
    }

    // 注意：HttpClient 实例来自 HttpClientFactory 的静态缓存，
    // 缓存实例由工厂统一管理生命周期，不应在此处释放。
    // 释放 MusicSearchService 仅清理子服务。
    public void Dispose()
    {
        // 不再释放 HttpClient（由 HttpClientFactory 统一管理）
    }
}
