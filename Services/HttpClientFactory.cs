using System.Net.Http;

namespace MusicDownloader.Services;

/// <summary>
/// HttpClient 工厂：统一创建各音乐服务的 HttpClient 实例
/// 确保每个服务有独立的 HttpClient（避免 header 冲突），
/// 同时复用一致的默认超时和请求头
/// 
/// 【重要】所有 HttpClient 均缓存为静态实例（长生命周期），
/// 避免每次请求创建新实例导致 socket 耗尽（TIME_WAIT 堆积）。
/// HttpClient 设计为可复用，不要在每次请求时创建新实例。
/// </summary>
public static class HttpClientFactory
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    // 缓存各服务的 HttpClient（进程级复用，避免 socket 耗尽）
    private static HttpClient? _cachedMusicClient;
    private static HttpClient? _cachedNeteaseClient;
    private static HttpClient? _cachedDownloadClient;
    private static HttpClient? _cachedKugouClient; // 酷狗专用（Android UA）
    private static readonly object _lock = new();

    /// <summary>
    /// 创建/获取标准音乐服务 HttpClient（30秒超时 + 通用 UA）
    /// 同一进程内多次调用返回同一实例
    /// </summary>
    public static HttpClient CreateMusicClient(TimeSpan? timeout = null)
    {
        if (timeout != null)
            // 自定义超时：创建新实例（不影响缓存的默认实例）
            return CreateMusicClientCore(timeout!.Value);

        if (_cachedMusicClient == null)
        {
            lock (_lock)
            {
                _cachedMusicClient ??= CreateMusicClientCore(DefaultTimeout);
            }
        }
        return _cachedMusicClient;
    }

    private static HttpClient CreateMusicClientCore(TimeSpan timeout)
    {
        var client = new HttpClient { Timeout = timeout };
        // Contains + Add：避免静态缓存后重复添加（多服务复用同一 HttpClient 时可能已存在）
        if (!client.DefaultRequestHeaders.Contains("User-Agent"))
            client.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        return client;
    }

    /// <summary>
    /// 创建/获取网易云专用 HttpClient（含 Referer header）
    /// 同一进程内多次调用返回同一实例
    /// </summary>
    public static HttpClient CreateNeteaseClient(TimeSpan? timeout = null)
    {
        if (timeout != null)
        {
            // 自定义超时：创建新实例（不影响缓存的默认实例）
            return CreateNeteaseClientCore(timeout.Value);
        }

        if (_cachedNeteaseClient == null)
        {
            lock (_lock)
            {
                _cachedNeteaseClient ??= CreateNeteaseClientCore(DefaultTimeout);
            }
        }
        return _cachedNeteaseClient;
    }

    private static HttpClient CreateNeteaseClientCore(TimeSpan timeout)
    {
        var client = CreateMusicClientCore(timeout);
        // Contains + Add：静态缓存实例可能在其他场景已被添加 Referer
        if (!client.DefaultRequestHeaders.Contains("Referer"))
            client.DefaultRequestHeaders.Add("Referer", "https://music.163.com/");
        return client;
    }

    /// <summary>
    /// 创建/获取高超时下载专用 HttpClient（120秒，适合大文件）
    /// 同一进程内多次调用返回同一实例
    /// </summary>
    public static HttpClient CreateDownloadClient()
    {
        if (_cachedDownloadClient == null)
        {
            lock (_lock)
            {
                _cachedDownloadClient ??= new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
                if (!_cachedDownloadClient.DefaultRequestHeaders.Contains("User-Agent"))
                    _cachedDownloadClient.DefaultRequestHeaders.Add("User-Agent",
                        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            }
        }
        return _cachedDownloadClient;
    }

    /// <summary>
    /// 创建/获取酷狗专用 HttpClient（Android User-Agent）
    /// 酷狗 API 对 User-Agent 有特殊要求，必须使用独立实例，
    /// 避免与酷我/咪咕共享同一 HttpClient 导致 UA 设置冲突。
    /// </summary>
    public static HttpClient CreateKugouClient(TimeSpan? timeout = null)
    {
        if (timeout != null)
        {
            var client = new HttpClient { Timeout = timeout.Value };
            if (!client.DefaultRequestHeaders.Contains("User-Agent"))
                client.DefaultRequestHeaders.Add("User-Agent", "Android");
            return client;
        }

        if (_cachedKugouClient == null)
        {
            lock (_lock)
            {
                if (_cachedKugouClient == null)
                {
                    _cachedKugouClient = new HttpClient { Timeout = DefaultTimeout };
                    _cachedKugouClient.DefaultRequestHeaders.Add("User-Agent", "Android");
                }
            }
        }
        return _cachedKugouClient;
    }
}
