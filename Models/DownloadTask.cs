using CommunityToolkit.Mvvm.ComponentModel;
using MusicDownloader.Services;

namespace MusicDownloader.Models;

/// <summary>
/// 下载任务数据模型
/// 清洗逻辑委托给 FileNameCleanerService（从 DownloadTask.CleanFileNamePart 等抽离）
/// </summary>
public partial class DownloadTask : ObservableObject
{
    public Song Song { get; set; } = new();

    [ObservableProperty] private string _status = "等待中";
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _isSelected = true;

    // 下载进度详情
    [ObservableProperty] private string _speedText = ""; // 如 "1.2 MB/s"
    [ObservableProperty] private string _remainingText = ""; // 如 "剩余 0:32"

    /// <summary>最终文件名（不含扩展名，可编辑）</summary>
    private string _finalFileName = "";

    public string FinalFileName
    {
        get => _finalFileName;
        set
        {
            string final;
            if (string.IsNullOrEmpty(value) && Song != null)
            {
                // 默认值：清洗后的"歌手 - 歌名"（委托给 FileNameCleanerService）
                final = FileNameCleanerService.GenerateCleanFileName(null, Song.Artist, Song.Title);
            }
            else
            {
                final = value ?? "";
            }
            SetProperty(ref _finalFileName, final);
            OnPropertyChanged(nameof(FinalFileNameWithExt));
            OnPropertyChanged(nameof(FinalFileNameWithExtDisplay));
        }
    }

    /// <summary>带扩展名的完整文件名（用于显示）</summary>
    public string FinalFileNameWithExt => $"{FinalFileName}.{Song.Format}";
    public string FinalFileNameWithExtDisplay => FinalFileName;

    public DownloadTask() { }

    public DownloadTask(Song song)
    {
        Song = song;
        FinalFileName = ""; // 触发属性赋值以生成默认值
    }
}
