using System.Net.Http;
using System.Web;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using MusicDownloader.Models;

namespace MusicDownloader.Services;

public class KuwoService
{
    private readonly HttpClient _http;

    public KuwoService(HttpClient http)
    {
        _http = http;
        // User-Agent 由 HttpClientFactory.CreateMusicClient() 统一设置（"Mozilla/5.0..."），此处不重复设置
    }

    public async Task<List<Song>> SearchAsync(string keyword, int limit = 100, CancellationToken ct = default)
    {
        var results = new List<Song>();
        try
        {
            var encoded = HttpUtility.UrlEncode(keyword);
            var pn = 0;
            var rn = Math.Min(limit, 100); // 酷我每页最多100
            var url = $"http://search.kuwo.cn/r.s?all={encoded}&ft=music&itemset=web_2013&client=kt&pn={pn}&rn={rn}&rformat=json&encoding=utf8";

            var resp = await _http.GetAsync(url, ct);
            var json = await resp.Content.ReadAsStringAsync(ct);
            var obj = JObject.Parse(json);

            var list = obj["abslist"] as JArray;
            if (list == null) return results;

            foreach (var item in list)
            {
                results.Add(new Song
                {
                    Id = item["MUSICRID"]?.ToString()?.Replace("MUSIC_", "") ?? "",
                    Artist = item["ARTIST"]?.ToString() ?? "",
                    Title = item["SONGNAME"]?.ToString() ?? "",
                    Album = item["ALBUM"]?.ToString() ?? "",
                    Duration = int.TryParse(item["DURATION"]?.ToString(), out var d) ? d : 0,
                    Format = "mp3",
                    Source = "酷我"
                });
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[KuwoService.SearchAsync] 搜索失败: {ex.Message}");
        }
        return results;
    }

    public async Task<string?> GetDownloadUrlAsync(Song song, CancellationToken ct = default)
    {
        try
        {
            var url = $"http://antiserver.kuwo.cn/anti.s?type=convert_url&rid={song.Id}&format=mp3&response=url";
            var resp = await _http.GetAsync(url, ct);
            var content = await resp.Content.ReadAsStringAsync(ct);
            content = content.Trim();
            if (content.StartsWith("http")) return content;

            // 备用
            var url2 = $"http://mobi.kuwo.cn/mobi.s?f=web&type=convert_url_with_sign&rid={song.Id}&format=mp3";
            var resp2 = await _http.GetAsync(url2, ct);
            var json2 = await resp2.Content.ReadAsStringAsync(ct);
            var obj = JObject.Parse(json2);
            return obj["data"]?["url"]?.ToString();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[KuwoService.GetDownloadUrlAsync] 获取链接失败: {ex.Message}");
        }
        return null;
    }
}
