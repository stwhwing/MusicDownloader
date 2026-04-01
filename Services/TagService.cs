using System.Diagnostics;
using System.IO;
using TagLib;

namespace MusicDownloader.Services;

public static class TagService
{
    /// <summary>
    /// 静默写入音频文件 ID3 标签。
    /// 失败时静默跳过，不影响下载状态。
    /// </summary>
    public static void WriteTags(string filePath, string? artist, string? title,
        string? album, int durationSec)
    {
        try
        {
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (ext != ".mp3" && ext != ".flac" && ext != ".m4a"
                && ext != ".aac" && ext != ".ogg" && ext != ".wav")
                return;

            using var file = TagLib.File.Create(filePath);
            file.Tag.Title = title ?? "";
            file.Tag.Performers = string.IsNullOrEmpty(artist)
                ? Array.Empty<string>()
                : new[] { artist };
            file.Tag.Album = album ?? "";

            // 时长写入说明（已知限制）：
            // - MP3: ID3v2 TLEN 帧需通过 TextFrame/TLEN Frame 实例写入，
            //        TagLibSharp 当前版本不支持直接覆写，跳过（音频流自带时长信息）
            // - M4A / FLAC: Properties.Duration 为只读，由音频流元数据决定
            // - OGG / AAC / WAV: TagLib# 不支持覆写时长，静默跳过

            file.Save();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TagService] 写入 ID3 标签失败 ({filePath}): {ex.Message}");
        }
    }
}
