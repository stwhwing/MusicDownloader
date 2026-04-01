namespace MusicDownloader.Models;

/// <summary>
/// 用户偏好设置（持久化到 settings.json）
/// </summary>
public class AppSettings
{
    public bool FilterCover { get; set; } = true;
    public bool FilterDj { get; set; } = true;
    public bool FilterAi { get; set; } = true;

    public bool SourceNetease { get; set; } = true;
    public bool SourceKuwo { get; set; } = true;
    public bool SourceKugou { get; set; } = true;
    public bool SourceMigge { get; set; } = true;

    public string DownloadPath { get; set; } = @"D:\Downloads\音乐下载";
    public int SearchLimit { get; set; } = 100;

    public bool FormatFlac { get; set; } = true;
    public bool FormatMp3 { get; set; } = true;
    public bool FormatAac { get; set; } = false;
    public bool FormatWav { get; set; } = false;
}
