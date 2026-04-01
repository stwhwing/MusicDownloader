using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicDownloader.Models;
using MusicDownloader.Services;

namespace MusicDownloader.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly MusicSearchService _searchService;
    private readonly DownloadService _downloadService;
    private readonly DialogService _dialogService;
    private readonly HealthCheckService _healthCheckService;
    private readonly DispatcherTimer _saveDebounceTimer;
    private readonly DispatcherTimer _healthCheckTimer;
    private bool _disposed = false;
    private bool _healthCheckRunning = false; // 防止健康检查重入（替代错误的"只执行一次"标志）

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _isSearching;
    [ObservableProperty] private string _statusText = "就绪";

    // 健康检查状态
    [ObservableProperty] private string _neteaseHealth = "⚪";
    [ObservableProperty] private string _kuwoHealth = "⚪";
    [ObservableProperty] private string _kugouHealth = "⚪";
    [ObservableProperty] private string _miggeHealth = "⚪";

    [ObservableProperty] private bool _filterCover = true;
    [ObservableProperty] private bool _filterDj = true;
    [ObservableProperty] private bool _filterAi = true;

    [ObservableProperty] private bool _sourceNetease = true;
    [ObservableProperty] private bool _sourceKuwo = true;
    [ObservableProperty] private bool _sourceKugou = true;
    [ObservableProperty] private bool _sourceMigge = true;

    [ObservableProperty] private string _downloadPath = @"D:\Downloads\音乐下载";

    [ObservableProperty] private int _searchLimit = 100;

    [ObservableProperty] private bool _formatFlac = true;
    [ObservableProperty] private bool _formatMp3 = true;
    [ObservableProperty] private bool _formatAac = false;
    [ObservableProperty] private bool _formatWav = false;

    public List<string> EnabledFormats
    {
        get
        {
            var list = new List<string>();
            if (FormatFlac) list.Add("flac");
            if (FormatMp3) list.Add("mp3");
            if (FormatAac) list.Add("aac");
            if (FormatWav) list.Add("wav");
            if (list.Count == 0) list.Add("mp3");
            return list;
        }
    }

    public ObservableCollection<Song> SearchResults { get; } = new();
    public ObservableCollection<DownloadTask> DownloadQueue { get; } = new();
    public ObservableCollection<Song> SelectedSongs { get; } = new();
    public ObservableCollection<string> SearchHistory { get; } = SearchHistoryService.GetHistory();

    private CancellationTokenSource? _searchCts;
    private CancellationTokenSource? _downloadCts;
    [ObservableProperty] private bool _isDownloading = false;

    public MainViewModel()
    {
        _searchService = new MusicSearchService();
        _dialogService = new DialogService();
        _downloadService = new DownloadService(_searchService, _dialogService);
        _healthCheckService = new HealthCheckService();

        // 防抖定时器：路径输入停止 1 秒后才保存
        _saveDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _saveDebounceTimer.Tick += (_, _) =>
        {
            _saveDebounceTimer.Stop();
            SaveSettings();
        };

        // 健康检查定时器（只初始化，不在此启动）
        _healthCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
        _healthCheckTimer.Tick += HealthCheckTimer_Tick;

        // 先加载保存的用户偏好（必须在所有异步操作之前完成）
        var settings = SettingsService.Load();
        _filterCover = settings.FilterCover;
        _filterDj = settings.FilterDj;
        _filterAi = settings.FilterAi;
        _sourceNetease = settings.SourceNetease;
        _sourceKuwo = settings.SourceKuwo;
        _sourceKugou = settings.SourceKugou;
        _sourceMigge = settings.SourceMigge;
        _downloadPath = settings.DownloadPath;
        _searchLimit = settings.SearchLimit;
        _formatFlac = settings.FormatFlac;
        _formatMp3 = settings.FormatMp3;
        _formatAac = settings.FormatAac;
        _formatWav = settings.FormatWav;

        // 构造完成后再启动健康检查定时器和初次检查
        // 使用 Dispatcher.BeginInvoke 确保在窗口完全加载后执行，避免构造期间触发 UI 绑定更新
        _healthCheckTimer.Start();
        Application.Current.Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Loaded,
            new Action(() => _ = CheckSourcesHealthAsync()));
    }

    // ════════════════════ 健康检查 ════════════════════

    private async void HealthCheckTimer_Tick(object? sender, EventArgs e)
    {
        // 防重入：如果上次检查还未完成，跳过本次（每 5 分钟触发，通常不会并发）
        if (_healthCheckRunning) return;
        await CheckSourcesHealthAsync();
    }

    [RelayCommand]
    private async Task CheckSourcesHealthAsync()
    {
        if (_healthCheckRunning) return; // 防重入
        _healthCheckRunning = true;
        try
        {
            var sources = GetEnabledSources();
            var results = await _healthCheckService.CheckAllAsync(sources);

            foreach (var r in results)
            {
                var emoji = r.IsHealthy ? "🟢" : "🔴";
                switch (r.Source)
                {
                    case "网易云": NeteaseHealth = emoji; break;
                    case "酷我": KuwoHealth = emoji; break;
                    case "酷狗": KugouHealth = emoji; break;
                    case "咪咕": MiggeHealth = emoji; break;
                }
            }
        }
        catch (OperationCanceledException) { /* 应用关闭时取消，静默忽略 */ }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[CheckSourcesHealthAsync] 健康检查异常: {ex.Message}"); }
        finally
        {
            _healthCheckRunning = false;
        }
    }

    // ════════════════════ 偏好持久化回调 ════════════════════
    // CommunityToolkit.Mvvm 为每个 [ObservableProperty] 生成 partial OnXxxChanged 方法
    // 这里实现它们来触发自动保存

    private AppSettings _currentSettings = new();
    private void SaveSettings()
    {
        _currentSettings.FilterCover = FilterCover;
        _currentSettings.FilterDj = FilterDj;
        _currentSettings.FilterAi = FilterAi;
        _currentSettings.SourceNetease = SourceNetease;
        _currentSettings.SourceKuwo = SourceKuwo;
        _currentSettings.SourceKugou = SourceKugou;
        _currentSettings.SourceMigge = SourceMigge;
        _currentSettings.DownloadPath = DownloadPath;
        _currentSettings.SearchLimit = SearchLimit;
        _currentSettings.FormatFlac = FormatFlac;
        _currentSettings.FormatMp3 = FormatMp3;
        _currentSettings.FormatAac = FormatAac;
        _currentSettings.FormatWav = FormatWav;
        SettingsService.Save(_currentSettings);
    }

    /// <summary>强制立即保存设置（用于窗口关闭时，防止防抖节流丢失）</summary>
    public void ForceSaveSettings()
    {
        _saveDebounceTimer.Stop();
        SaveSettings();
    }

    // 每个设置属性变化时自动保存
    partial void OnFilterCoverChanged(bool value) => SaveSettings();
    partial void OnFilterDjChanged(bool value) => SaveSettings();
    partial void OnFilterAiChanged(bool value) => SaveSettings();
    partial void OnSourceNeteaseChanged(bool value) => SaveSettings();
    partial void OnSourceKuwoChanged(bool value) => SaveSettings();
    partial void OnSourceKugouChanged(bool value) => SaveSettings();
    partial void OnSourceMiggeChanged(bool value) => SaveSettings();
    // 下载路径使用防抖：用户停止输入 1 秒后才保存，避免频繁 IO
    partial void OnDownloadPathChanged(string value)
    {
        _saveDebounceTimer.Stop();
        _saveDebounceTimer.Start();
    }
    partial void OnSearchLimitChanged(int value) => SaveSettings();
    partial void OnFormatFlacChanged(bool value) => SaveSettings();
    partial void OnFormatMp3Changed(bool value) => SaveSettings();
    partial void OnFormatAacChanged(bool value) => SaveSettings();
    partial void OnFormatWavChanged(bool value) => SaveSettings();

    [RelayCommand]
    private void BrowseDownloadPath()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "选择下载保存路径",
            InitialDirectory = System.IO.Directory.Exists(DownloadPath) ? DownloadPath : null
        };
        if (dialog.ShowDialog() == true)
            DownloadPath = dialog.FolderName;
    }

    public HashSet<string> GetEnabledSources()
    {
        var sources = new HashSet<string>();
        if (SourceNetease) sources.Add("网易云");
        if (SourceKuwo) sources.Add("酷我");
        if (SourceKugou) sources.Add("酷狗");
        if (SourceMigge) sources.Add("咪咕");
        return sources;
    }

    // 预编译过滤正则（避免每次搜索结果过滤都重新编译）
    [GeneratedRegex(@"\(Cover\)|（Cover）|\(cover\)|（cover）", RegexOptions.IgnoreCase)]
    private static partial Regex CoverFilterRx();
    [GeneratedRegex(@"\(?\s*DJ[\p{P}]?\s*[^\s\)】\)]+\)?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex DjFilterRx();
    [GeneratedRegex(@"\(AI[\p{P}]?版\)|（AI版）|\(AI\)|（AI）", RegexOptions.IgnoreCase)]
    private static partial Regex AiFilterRx();

    public bool IsFilteredSong(Song song)
    {
        var name = $"{song.Artist} {song.Title}";
        if (FilterCover && CoverFilterRx().IsMatch(name))
            return true;
        if (FilterDj && DjFilterRx().IsMatch(name))
            return true;
        if (FilterAi && AiFilterRx().IsMatch(name))
            return true;
        return false;
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchText))
            return;

        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();

        // 保存搜索历史
        SearchHistoryService.AddToHistory(SearchText);

        SearchResults.Clear();
        IsSearching = true;
        StatusText = $"正在搜索: {SearchText}...";

        try
        {
            var results = await _searchService.SearchAllAsync(
                SearchText,
                GetEnabledSources(),
                new HashSet<string>(EnabledFormats.Select(f => f.ToLowerInvariant())),
                SearchLimit,
                _searchCts.Token);

            var filtered = results.Where(s => !IsFilteredSong(s)).ToList();
            foreach (var s in filtered)
                SearchResults.Add(s);

            StatusText = $"找到 {filtered.Count} 首歌曲（已过滤 {results.Count - filtered.Count} 首非原唱）";
        }
        catch (OperationCanceledException)
        {
            StatusText = "搜索已取消";
        }
        catch (Exception ex)
        {
            StatusText = $"搜索失败: {ex.Message}";
        }
        finally
        {
            IsSearching = false;
        }
    }

    [RelayCommand]
    private void SelectAll()
    {
        SelectedSongs.Clear();
        foreach (var s in SearchResults)
        {
            s.IsSelected = true;
            SelectedSongs.Add(s);
        }
        StatusText = $"已选中 {SelectedSongs.Count} 首";
    }

    [RelayCommand]
    private void SelectNone()
    {
        foreach (var s in SearchResults)
            s.IsSelected = false;
        SelectedSongs.Clear();
        StatusText = "已取消全选";
    }

    [RelayCommand]
    private async Task AddToQueueAsync()
    {
        // 去重键：源+歌手+歌名（避免同一首歌从同一平台重复加入，同名不同歌手/平台可并存）
        var toAdd = SelectedSongs.Where(s =>
            !DownloadQueue.Any(t =>
                t.Song.Source == s.Source &&
                t.Song.Artist == s.Artist &&
                t.Song.Title == s.Title))
            .ToList();

        foreach (var s in toAdd)
        {
            // 网易云歌曲调用详情 API 获取权威歌名/艺术家/时长
            if (s.Source == "网易云" && !string.IsNullOrEmpty(s.Id))
            {
                try
                {
                    var (artist, title, duration) = await _searchService.GetVerifiedSongInfoAsync(s, _searchCts?.Token ?? CancellationToken.None);
                    if (!string.IsNullOrEmpty(artist)) s.Artist = artist;
                    if (!string.IsNullOrEmpty(title)) s.Title = title;
                    if (duration > 0) s.Duration = duration;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[AddToQueueAsync] 网易云验证异常: {ex.Message}"); }
            }
            DownloadQueue.Add(new DownloadTask(s));
            // 加入后清除勾选（保留 SelectedSongs 引用，下次可继续"加入下载"）
            s.IsSelected = false;
        }
        // 同步 SelectedSongs 集合（移除已取消勾选的引用）
        var stillSelected = SelectedSongs.Where(s => s.IsSelected).ToList();
        SelectedSongs.Clear();
        foreach (var s in stillSelected) SelectedSongs.Add(s);
        StatusText = $"已加入下载队列 {toAdd.Count} 首，当前队列 {DownloadQueue.Count} 首";
    }

    [RelayCommand]
    private void ClearQueue()
    {
        DownloadQueue.Clear();
        StatusText = "下载队列已清空";
    }

    [RelayCommand]
    private async Task StartDownloadAsync()
    {
        var pending = DownloadQueue.Where(t => t.Status == "等待中" && t.IsSelected).ToList();
        if (pending.Count == 0)
        {
            StatusText = "没有可下载的任务";
            return;
        }

        _downloadCts?.Dispose(); // Dispose 旧实例，防止 WaitHandle 系统资源泄漏
        _downloadCts = new CancellationTokenSource();
        IsDownloading = true;
        try
        {
            var (done, fail) = await _downloadService.ExecuteAsync(
                DownloadPath, DownloadQueue, s => StatusText = s, _downloadCts.Token);

            var (summary, rows) = _downloadService.BuildReport(pending, done, fail, DownloadPath);
            _dialogService.ShowReport(
                "📥 下载完成报告",
                $"下载路径: {DownloadPath}  |  完成时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                summary, rows,
                Application.Current.MainWindow);
        }
        catch (OperationCanceledException)
        {
            StatusText = "下载已取消";
        }
        catch (Exception ex)
        {
            StatusText = $"下载出错: {ex.Message}";
            _dialogService.ShowError($"下载过程中发生错误：\n{ex.Message}", "下载错误");
        }
        finally
        {
            IsDownloading = false;
        }
    }

    [RelayCommand]
    private void CancelDownload()
    {
        if (_downloadCts != null && !_downloadCts.IsCancellationRequested)
        {
            _downloadCts.Cancel();
            StatusText = "正在取消下载...";
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _saveDebounceTimer.Stop(); // 匿名 lambda 无法用 -= 解绑；Stop 后 VM 无强引用时会被 GC
        _healthCheckTimer.Stop();
        _healthCheckTimer.Tick -= HealthCheckTimer_Tick; // 具名方法可精确解绑
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _downloadCts?.Cancel();
        _downloadCts?.Dispose();
        _searchService.Dispose();
        _downloadService.Dispose();
        _healthCheckService.Dispose();
    }
}
