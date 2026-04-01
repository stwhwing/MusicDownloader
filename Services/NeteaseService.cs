using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using MusicDownloader.Models;

namespace MusicDownloader.Services;

public class NeteaseService
{
    private readonly HttpClient _http;

    public NeteaseService(HttpClient http)
    {
        _http = http;
        // User-Agent 和 Referer 由 HttpClientFactory.CreateNeteaseClient() 统一设置，此处不重复设置
    }

    public async Task<List<Song>> SearchAsync(string keyword, int limit = 100, CancellationToken ct = default)
    {
        var results = new List<Song>();
        try
        {
            var payload = new Dictionary<string, object>
            {
                ["s"] = keyword,
                ["type"] = 1,
                ["offset"] = 0,
                ["limit"] = limit,
                ["total"] = true
            };
            var json = JsonConvert.SerializeObject(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/x-www-form-urlencoded");

            var resp = await _http.PostAsync("https://music.163.com/api/search/get/web", content, ct);
            var html = await resp.Content.ReadAsStringAsync(ct);
            var obj = JObject.Parse(html);

            var songs = obj["result"]?["songs"] as JArray;
            if (songs == null) return results;

            foreach (var s in songs)
            {
                var artists = s["artists"] as JArray;
                var artist = artists?.FirstOrDefault()?["name"]?.ToString() ?? "";

                results.Add(new Song
                {
                    Id = s["id"]?.ToString() ?? "",
                    Artist = HtmlDecode(artist),
                    Title = HtmlDecode(s["name"]?.ToString() ?? ""),
                    Album = HtmlDecode(s["album"]?["name"]?.ToString() ?? ""),
                    Duration = (s["duration"]?.Value<int>() ?? 0) / 1000,
                    Format = "mp3", // 格式在下载时通过 URL 判断
                    Source = "网易云"
                });
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { Debug.WriteLine($"[NeteaseService.SearchAsync] 网络错误: {ex.Message}"); }
        return results;
    }

    public async Task<(string? Artist, string? Title, int Duration)> GetVerifiedSongInfoAsync(string songId, CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.GetAsync($"https://music.163.com/api/song/detail/?ids=[{songId}]", ct);
            var json = await resp.Content.ReadAsStringAsync(ct);
            var obj = JObject.Parse(json);
            var song = obj["songs"]?[0];
            if (song == null) return (null, null, 0);

            var artists = song["artists"] as JArray;
            var artist = artists?.FirstOrDefault()?["name"]?.ToString() ?? "";
            var title = song["name"]?.ToString() ?? "";
            // duration 字段为毫秒，转秒
            var durationMs = song["duration"]?.Value<int>() ?? 0;
            var durationSec = durationMs / 1000;
            return (HtmlDecode(artist), HtmlDecode(title), durationSec);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { Debug.WriteLine($"[NeteaseService.GetVerifiedSongInfoAsync] 网络错误: {ex.Message}"); }
        return (null, null, 0);
    }

    public async Task<string?> GetDownloadUrlAsync(Song song, CancellationToken ct = default)
    {
        try
        {
            // 尝试获取歌曲详情以获取下载URL
            var resp = await _http.GetAsync($"https://music.163.com/api/song/detail/?ids=[{song.Id}]", ct);
            var html = await resp.Content.ReadAsStringAsync(ct);
            var obj = JObject.Parse(html);
            var mp3Url = obj["songs"]?[0]?["mp3Url"]?.ToString();
            if (!string.IsNullOrEmpty(mp3Url)) return mp3Url;

            // 备用：通过播放页面获取
            var pageResp = await _http.GetAsync($"https://music.163.com/song?id={song.Id}", ct);
            var page = await pageResp.Content.ReadAsStringAsync(ct);
            var match = Regex.Match(page, @"\[""url""\]\s*:\s*""([^""]+)""");
            if (match.Success) return match.Groups[1].Value;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { Debug.WriteLine($"[NeteaseService.GetDownloadUrlAsync] 网络错误: {ex.Message}"); }
        return null;
    }

    private static string HtmlDecode(string s)
    {
        return System.Net.WebUtility.HtmlDecode(s)
            .Replace("&nbsp;", " ");
    }
}
