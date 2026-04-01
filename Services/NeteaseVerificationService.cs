using System.Net.Http;
using MusicDownloader.Models;
using Newtonsoft.Json.Linq;

namespace MusicDownloader.Services;

/// <summary>
/// 网易云可信源验证服务：从 AudioFileService.VerifyWithNeteaseAsync 抽离
/// </summary>
public class NeteaseVerificationService : IDisposable
{
    private readonly HttpClient _httpClient;

    public NeteaseVerificationService()
    {
        // 使用缓存的标准客户端（30秒，含 Referer header），避免每次创建新实例
        // 验证操作通常在后台进行，30秒超时足够
        _httpClient = HttpClientFactory.CreateNeteaseClient();
    }

    /// <summary>
    /// 用网易云 API 做可信源验证
    /// </summary>
    /// <param name="keyword">搜索关键词（优先用标签信息，没有则用文件名信息）</param>
    /// <param name="localStatus">本地核对状态（用于判断验证失败时的处理方式）</param>
    /// <returns>验证结果</returns>
    public async Task<NeteaseVerifyResult> VerifyAsync(string keyword, FileCheckStatus localStatus, CancellationToken ct = default)
    {
        var result = new NeteaseVerifyResult();

        try
        {
            var payload = new Dictionary<string, string>
            {
                ["s"] = keyword,
                ["type"] = "1",
                ["offset"] = "0",
                ["limit"] = "3",
                ["total"] = "true"
            };

            using var content = new FormUrlEncodedContent(payload);
            var resp = await _httpClient.PostAsync("https://music.163.com/api/search/get/web", content, ct);

            if (!resp.IsSuccessStatusCode)
            {
                result.Note = "（网易云搜索失败）";
                return result;
            }

            var json = await resp.Content.ReadAsStringAsync(ct);
            var obj = JObject.Parse(json);
            var songs = obj["result"]?["songs"] as JArray;
            if (songs == null || songs.Count == 0)
            {
                result.IsVerified = false;
                result.Note = "（网易云未找到）";
                return result;
            }

            var s = songs[0];
            var verArtist = s["artists"]?[0]?["name"]?.ToString() ?? "";
            var verTitle = s["name"]?.ToString() ?? "";
            result.VerifiedArtist = System.Net.WebUtility.HtmlDecode(verArtist);
            result.VerifiedTitle = System.Net.WebUtility.HtmlDecode(verTitle);
            result.IsVerified = true;
        }
        catch (OperationCanceledException) { throw; }
        catch (HttpRequestException ex) { result.Note = $"（网易云网络错误: {ex.Message}）"; }
        catch (Exception ex) { result.Note = $"（网易云查询错误: {ex.Message}）"; }

        return result;
    }

    // _httpClient 来自 HttpClientFactory 静态缓存，由工厂统一管理生命周期，此处不释放
    public void Dispose() { /* 静态缓存 HttpClient 不在此处释放 */ }
}

/// <summary>
/// 网易云验证结果
/// </summary>
public class NeteaseVerifyResult
{
    public bool IsVerified { get; set; }
    public string VerifiedArtist { get; set; } = "";
    public string VerifiedTitle { get; set; } = "";
    public string Note { get; set; } = "";
}
