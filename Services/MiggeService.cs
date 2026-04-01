using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using MusicDownloader.Models;

namespace MusicDownloader.Services;

/// <summary>
/// 咪咕音乐服务
/// 咪咕版权丰富，支持 VIP 歌曲，格式: flac(无损) / mp3(高品)
/// </summary>
public class MiggeService
{
    private readonly HttpClient _http;
    private const string SearchBase = "https://pd.musicapp.migu.cn/MIGUM2.0/v1.0/content/search_all.do";
    private const string DownloadBase = "https://app.pd.nf.migu.cn/MIGUM2.0/v1.0/content/sub/listenSong.do";

    public MiggeService(HttpClient http)
    {
        _http = http;
        // User-Agent 和 Referer 已在 MusicSearchService 的共享 HttpClient 上统一设置
        // 这里不重复添加，避免 header 冲突
    }

    public async Task<List<Song>> SearchAsync(string keyword, int limit = 100, CancellationToken ct = default)
    {
        var results = new List<Song>();
        try
        {
            var pageSize = Math.Min(limit, 30); // 咪咕每页最多30
            var url = $"{SearchBase}?ua=Android_migu&version=5.0.1&text={Uri.EscapeDataString(keyword)}&pageNo=1&pageSize={pageSize}&searchSwitch={{\"song\":1,\"album\":0,\"singer\":0,\"tagSong\":0,\"mvSong\":0,\"songlist\":0,\"bestShow\":1}}";

            var resp = await _http.GetAsync(url, ct);
            var json = await resp.Content.ReadAsStringAsync(ct);
            var obj = JObject.Parse(json);

            var songList = obj["songListData"] as JArray;
            if (songList == null)
            {
                // 尝试另一种返回格式
                var data = obj["data"]?["song"] ?? obj["songList"];
                songList = data as JArray;
            }
            if (songList == null) return results;

            foreach (var s in songList)
            {
                // 咪咕格式判断：从 singers 字段或 newRateFormats 取
                var formats = s["newRateFormats"] as JArray;
                var singers = s["singer"] as JArray;
                var singerName = singers?.FirstOrDefault()?["name"]?.ToString() ?? "";

                // 优先取 flac，无则取 mp3
                string format = "mp3";
                if (formats != null)
                {
                    var flacItem = formats.FirstOrDefault(f =>
                        f["format"]?.ToString()?.Contains("SQ") == true ||
                        f["format"]?.ToString()?.Contains("FLAC") == true);
                    if (flacItem != null) format = "flac";
                    else
                    {
                        var mp3Item = formats.FirstOrDefault(f =>
                            f["format"]?.ToString()?.Contains("HQ") == true ||
                            f["format"]?.ToString()?.Contains("MP3") == true);
                        if (mp3Item != null) format = "mp3";
                    }
                }

                // contentId 用于下载
                var contentId = s["contentId"]?.ToString() ?? s["id"]?.ToString() ?? "";

                results.Add(new Song
                {
                    Id = contentId,
                    Artist = singerName,
                    Title = s["songName"]?.ToString() ?? s["name"]?.ToString() ?? "",
                    Album = s["albumName"]?.ToString() ?? "",
                    Duration = ParseDuration(s["duration"]?.ToString() ?? "0"),
                    Format = format,
                    Source = "咪咕"
                });
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MiggeService.SearchAsync] 搜索失败: {ex.Message}");
        }
        return results;
    }

    public async Task<string?> GetDownloadUrlAsync(Song song, CancellationToken ct = default)
    {
        // 优先尝试 FLAC 无损
        var url = await TryGetUrl(song.Id, "SQ", "E", ct);
        if (!string.IsNullOrEmpty(url)) return url;

        // 回退 MP3 高品
        url = await TryGetUrl(song.Id, "HQ", "2", ct);
        if (!string.IsNullOrEmpty(url)) return url;

        // 再试 LR 品质
        url = await TryGetUrl(song.Id, "LR", "6", ct);
        return url;
    }

    private async Task<string?> TryGetUrl(string contentId, string toneFlag, string resourceType, CancellationToken ct)
    {
        try
        {
            var url = $"{DownloadBase}?toneFlag={toneFlag}&netType=00&userId=&ua=Android_migu&version=5.1&copyrightId=0&contentId={contentId}&resourceType={resourceType}&channel=0";
            var resp = await _http.GetAsync(url, ct);
            var json = await resp.Content.ReadAsStringAsync(ct);
            var obj = JObject.Parse(json);
            return obj["data"]?["url"]?.ToString();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MiggeService.TryGetUrl] 获取下载链接失败: {ex.Message}");
            return null;
        }
    }

    private static int ParseDuration(string dur)
    {
        // 咪咕 duration 可能是毫秒字符串
        if (int.TryParse(dur, out int ms))
            return ms / 1000;
        return 0;
    }
}
