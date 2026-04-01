using System.Net.Http;
using System.Web;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using MusicDownloader.Models;

namespace MusicDownloader.Services;

public class KugouService
{
    private readonly HttpClient _http;       // 酷狗专用：Android UA（用于酷狗搜索/下载 API）
    private readonly HttpClient _httpKuwo;  // 标准 UA（降级到酷我 API 时使用）

    public KugouService(HttpClient http)
    {
        _http = http;
        // User-Agent 由 HttpClientFactory.CreateKugouClient() 统一设置（"Android"），此处不重复设置
        // 降级路径需要独立的标准 UA HttpClient，避免酷我 API 收到 Android UA 请求失败
        _httpKuwo = HttpClientFactory.CreateMusicClient();
    }

    public async Task<List<Song>> SearchAsync(string keyword, int limit = 100, CancellationToken ct = default)
    {
        var results = new List<Song>();
        try
        {
            var encoded = HttpUtility.UrlEncode(keyword);
            var pagesize = Math.Min(limit, 60); // 酷狗每页最多60
            var url = $"http://mobilecdn.kugou.com/api/v3/search/song?keyword={encoded}&page=1&pagesize={pagesize}&showtype=1";

            var resp = await _http.GetAsync(url, ct);
            var json = await resp.Content.ReadAsStringAsync(ct);
            var obj = JObject.Parse(json);

            if (obj["status"]?.ToString() != "1") return results;
            var list = obj["data"]?["info"] as JArray;
            if (list == null) return results;

            foreach (var item in list)
            {
                results.Add(new Song
                {
                    Id = item["hash"]?.ToString() ?? "",
                    Artist = item["singername"]?.ToString() ?? "",
                    Title = item["songname"]?.ToString() ?? "",
                    Album = "",
                    Duration = int.TryParse(item["duration"]?.ToString(), out var d) ? d : 0,
                    Format = "mp3",
                    Source = "酷狗"
                });
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[KugouService.SearchAsync] 搜索失败: {ex.Message}");
        }
        return results;
    }

    public async Task<string?> GetDownloadUrlAsync(Song song, CancellationToken ct = default)
    {
        // 酷狗直接下载受限，降级到酷我同名搜索获取下载链接
        // 注意：此处必须使用 _httpKuwo（标准 Mozilla UA），而非酷狗专用的 Android UA，
        // 否则酷我 API 可能拒绝请求。
        try
        {
            var keyword = $"{song.Artist} {song.Title}";
            var encoded = HttpUtility.UrlEncode(keyword);
            var url = $"http://search.kuwo.cn/r.s?all={encoded}&ft=music&client=kt&pn=0&rn=3&rformat=json&encoding=utf8";
            var resp = await _httpKuwo.GetAsync(url, ct);
            var json = await resp.Content.ReadAsStringAsync(ct);
            var obj = JObject.Parse(json);
            var list = obj["abslist"] as JArray;
            if (list != null && list.Count > 0)
            {
                var rid = list[0]["MUSICRID"]?.ToString()?.Replace("MUSIC_", "");
                if (!string.IsNullOrEmpty(rid))
                {
                    var dlUrl = $"http://antiserver.kuwo.cn/anti.s?type=convert_url&rid={rid}&format=mp3&response=url";
                    var dlResp = await _httpKuwo.GetAsync(dlUrl, ct);
                    var content = (await dlResp.Content.ReadAsStringAsync(ct)).Trim();
                    if (content.StartsWith("http")) return content;
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[KugouService.GetDownloadUrlAsync] 获取链接失败: {ex.Message}");
        }
        return null;
    }
}
