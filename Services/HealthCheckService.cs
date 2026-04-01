using System.Net.Http;
using MusicDownloader.Services;

namespace MusicDownloader.Services;

/// <summary>
/// 搜索服务健康检查器
/// </summary>
public class HealthCheckService : IDisposable
{
    public class SourceHealth
    {
        public string Source { get; set; } = "";
        public bool IsHealthy { get; set; }
        public string Message { get; set; } = "";
        public int ResponseTimeMs { get; set; }
        public DateTime LastChecked { get; set; }
    }

    private readonly HttpClient _http;

    public HealthCheckService()
    {
        // 使用 HttpClientFactory 创建 5 秒超时的健康检查专用 client
        _http = HttpClientFactory.CreateMusicClient(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// 检查所有启用的来源
    /// </summary>
    public async Task<List<SourceHealth>> CheckAllAsync(HashSet<string> enabledSources, CancellationToken ct = default)
    {
        var tasks = new List<Task<SourceHealth>>();

        if (enabledSources.Contains("网易云"))
            tasks.Add(CheckNeteaseAsync(ct));
        if (enabledSources.Contains("酷我"))
            tasks.Add(CheckKuwoAsync(ct));
        if (enabledSources.Contains("酷狗"))
            tasks.Add(CheckKugouAsync(ct));
        if (enabledSources.Contains("咪咕"))
            tasks.Add(CheckMiggeAsync(ct));

        if (tasks.Count == 0)
            return new List<SourceHealth>();

        try
        {
            var results = await Task.WhenAll(tasks);
            return results.ToList();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[HealthCheckService.CheckAllAsync] 健康检查异常: {ex.Message}");
            return new List<SourceHealth>();
        }
    }

    /// <summary>
    /// 快速检查（仅检查可用性，不返回详细信息）
    /// </summary>
    public async Task<Dictionary<string, bool>> QuickCheckAsync(HashSet<string> enabledSources, CancellationToken ct = default)
    {
        var results = new Dictionary<string, bool>();
        var fullResults = await CheckAllAsync(enabledSources, ct);
        foreach (var r in fullResults)
            results[r.Source] = r.IsHealthy;
        return results;
    }

    private async Task<SourceHealth> CheckNeteaseAsync(CancellationToken ct)
    {
        var health = new SourceHealth { Source = "网易云" };
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // 使用搜索 API 健康检查端点
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["s"] = "test",
                ["type"] = "1",
                ["limit"] = "1",
                ["offset"] = "0"
            });

            var response = await _http.PostAsync(
                "https://music.163.com/api/search/get/web",
                content,
                ct);

            sw.Stop();
            health.ResponseTimeMs = (int)sw.ElapsedMilliseconds;

            if (response.IsSuccessStatusCode)
            {
                health.IsHealthy = true;
                health.Message = $"正常 ({health.ResponseTimeMs}ms)";
            }
            else
            {
                health.IsHealthy = false;
                health.Message = $"HTTP {(int)response.StatusCode}";
            }
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            health.ResponseTimeMs = (int)sw.ElapsedMilliseconds;
            health.IsHealthy = false;
            health.Message = "超时";
        }
        catch (HttpRequestException)
        {
            sw.Stop();
            health.ResponseTimeMs = (int)sw.ElapsedMilliseconds;
            health.IsHealthy = false;
            health.Message = "网络错误";
        }
        catch
        {
            sw.Stop();
            health.ResponseTimeMs = (int)sw.ElapsedMilliseconds;
            health.IsHealthy = false;
            health.Message = "未知错误";
        }

        health.LastChecked = DateTime.Now;
        return health;
    }

    private async Task<SourceHealth> CheckKuwoAsync(CancellationToken ct)
    {
        var health = new SourceHealth { Source = "酷我" };
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var response = await _http.GetAsync(
                "http://search.kuwo.cn/r.s?pn=0&rn=1&key=test",
                ct);

            sw.Stop();
            health.ResponseTimeMs = (int)sw.ElapsedMilliseconds;

            if (response.IsSuccessStatusCode)
            {
                health.IsHealthy = true;
                health.Message = $"正常 ({health.ResponseTimeMs}ms)";
            }
            else
            {
                health.IsHealthy = false;
                health.Message = $"HTTP {(int)response.StatusCode}";
            }
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            health.ResponseTimeMs = (int)sw.ElapsedMilliseconds;
            health.IsHealthy = false;
            health.Message = "超时";
        }
        catch (HttpRequestException)
        {
            sw.Stop();
            health.ResponseTimeMs = (int)sw.ElapsedMilliseconds;
            health.IsHealthy = false;
            health.Message = "网络错误";
        }
        catch
        {
            sw.Stop();
            health.ResponseTimeMs = (int)sw.ElapsedMilliseconds;
            health.IsHealthy = false;
            health.Message = "未知错误";
        }

        health.LastChecked = DateTime.Now;
        return health;
    }

    private async Task<SourceHealth> CheckKugouAsync(CancellationToken ct)
    {
        var health = new SourceHealth { Source = "酷狗" };
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var response = await _http.GetAsync(
                "http://mobilecdn.kugou.com/api/v3/search/song?keyword=test&page=1&pagesize=1",
                ct);

            sw.Stop();
            health.ResponseTimeMs = (int)sw.ElapsedMilliseconds;

            if (response.IsSuccessStatusCode)
            {
                health.IsHealthy = true;
                health.Message = $"正常 ({health.ResponseTimeMs}ms)";
            }
            else
            {
                health.IsHealthy = false;
                health.Message = $"HTTP {(int)response.StatusCode}";
            }
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            health.ResponseTimeMs = (int)sw.ElapsedMilliseconds;
            health.IsHealthy = false;
            health.Message = "超时";
        }
        catch (HttpRequestException)
        {
            sw.Stop();
            health.ResponseTimeMs = (int)sw.ElapsedMilliseconds;
            health.IsHealthy = false;
            health.Message = "网络错误";
        }
        catch
        {
            sw.Stop();
            health.ResponseTimeMs = (int)sw.ElapsedMilliseconds;
            health.IsHealthy = false;
            health.Message = "未知错误";
        }

        health.LastChecked = DateTime.Now;
        return health;
    }

    private async Task<SourceHealth> CheckMiggeAsync(CancellationToken ct)
    {
        var health = new SourceHealth { Source = "咪咕" };
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var response = await _http.GetAsync(
                "https://pd.musicapp.migu.cn/MIGUM2.0/v1.0/content/search_all.do?keyword=test&pageSize=1&pageNo=1",
                ct);

            sw.Stop();
            health.ResponseTimeMs = (int)sw.ElapsedMilliseconds;

            if (response.IsSuccessStatusCode)
            {
                health.IsHealthy = true;
                health.Message = $"正常 ({health.ResponseTimeMs}ms)";
            }
            else
            {
                health.IsHealthy = false;
                health.Message = $"HTTP {(int)response.StatusCode}";
            }
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            health.ResponseTimeMs = (int)sw.ElapsedMilliseconds;
            health.IsHealthy = false;
            health.Message = "超时";
        }
        catch (HttpRequestException)
        {
            sw.Stop();
            health.ResponseTimeMs = (int)sw.ElapsedMilliseconds;
            health.IsHealthy = false;
            health.Message = "网络错误";
        }
        catch
        {
            sw.Stop();
            health.ResponseTimeMs = (int)sw.ElapsedMilliseconds;
            health.IsHealthy = false;
            health.Message = "未知错误";
        }

        health.LastChecked = DateTime.Now;
        return health;
    }

    public void Dispose()
    {
        _http.Dispose();
    }
}
