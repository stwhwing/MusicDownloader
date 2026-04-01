using CommunityToolkit.Mvvm.ComponentModel;

namespace MusicDownloader.Models;

public partial class Song : ObservableObject
{
    public string Id { get; set; } = "";

    [ObservableProperty] private string _artist = "";
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _album = "";
    [ObservableProperty] private int _duration;
    [ObservableProperty] private string _format = "mp3";
    [ObservableProperty] private string _source = "";

    [ObservableProperty]
    private bool _isSelected;

    public string DurationStr => Duration > 0
        ? $"{Duration / 60:D2}:{Duration % 60:D2}"
        : "--:--";

    public string FileName => $"{Artist} - {Title}.{Format}";

    // 当 Artist/Title/Format 改变时，通知依赖属性 FileName 也更新
    partial void OnArtistChanged(string value) => OnPropertyChanged(nameof(FileName));
    partial void OnTitleChanged(string value) => OnPropertyChanged(nameof(FileName));
    partial void OnFormatChanged(string value) => OnPropertyChanged(nameof(FileName));
}
