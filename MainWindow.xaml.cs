using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MusicDownloader.Models;
using MusicDownloader.ViewModels;

namespace MusicDownloader;

public partial class MainWindow : Window
{
    private bool _isSyncing;
    private MainViewModel? _mainVm; // 持有引用，防止 GC

    public MainWindow()
    {
        InitializeComponent();

        // 在 Window 完全构造后（InitializeComponent 之后）才创建 MainViewModel
        // 这样 Application.Current.Dispatcher 一定可用，避免 XAML 构造期间的 Dispatcher 状态问题
        _mainVm = new MainViewModel();
        DataContext = _mainVm;
    }

    /// <summary>
    /// 窗口关闭时：强制保存设置 + Dispose 所有服务资源，确保进程干净退出
    /// 不调用 Dispose 会导致 DispatcherTimer / HttpClient / CancellationTokenSource 泄漏，进程无法退出
    /// FileToolViewModel 通过 StaticResource 创建，WPF 不会自动 Dispose，需手动释放
    /// </summary>
    private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.ForceSaveSettings();
            vm.Dispose();
        }

        // FileToolViewModel 是 Window.Resources 中的 StaticResource，WPF 不自动调用 Dispose
        if (Resources["FileTool"] is ViewModels.FileToolViewModel fileTool)
            fileTool.Dispose();
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is MainViewModel vm)
        {
            vm.SearchCommand.Execute(null);
        }
    }

    private void ResultsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            // 防止 SyncOtherColumns 通过设置 ResultsList.SelectedItem 触发本方法时，
            // 再次清空 SelectedSongs（否则会覆盖 SyncOtherColumns 从 QueueList/FilenameList
            // 同步过来的正确值）。
            // 只有用户直接操作 ResultsList 时才重建 SelectedSongs。
            if (_isSyncing) return;

            vm.SelectedSongs.Clear();
            foreach (Song s in ResultsList.SelectedItems)
                vm.SelectedSongs.Add(s);
        }
        if (!_isSyncing)
            SyncOtherColumns();
    }

    /// <summary>
    /// 三栏联动：搜索结果 / 下载队列 / 文件名编辑，任选一栏选中，
    /// 其余两栏自动滚动并选中同一歌曲（以 Artist+Title 匹配）。
    /// </summary>
    private void SyncSelection(object sender, SelectionChangedEventArgs e)
    {
        if (!_isSyncing)
            SyncOtherColumns();
    }

    /// <summary>
    /// 统一联动逻辑：当选中 ResultsList / QueueList / FilenameList 其一时，
    /// 同步联动其余两栏 + 同步 SelectedSongs。使用 _isSyncing 防止递归。
    /// </summary>
    private void SyncOtherColumns()
    {
        if (DataContext is not MainViewModel vm) return;
        if (_isSyncing) return;

        _isSyncing = true;
        try
        {
            Song? matchedSong = null;
            DownloadTask? matchedTask = null;
            bool isFromQueue = false;

            // 确定当前选中的歌曲和任务（三个入口共享同一个匹配逻辑）
            if (ResultsList.SelectedItem is Song song)
            {
                matchedSong = song;
                matchedTask = vm.DownloadQueue.FirstOrDefault(t =>
                    t.Song.Artist == song.Artist && t.Song.Title == song.Title);
            }
            else if (QueueList.SelectedItem is DownloadTask task)
            {
                matchedTask = task;
                matchedSong = vm.SearchResults.FirstOrDefault(s =>
                    s.Artist == task.Song.Artist && s.Title == task.Song.Title);
                isFromQueue = true;
            }
            else if (FilenameList.SelectedItem is DownloadTask ftask)
            {
                matchedTask = ftask;
                matchedSong = vm.SearchResults.FirstOrDefault(s =>
                    s.Artist == ftask.Song.Artist && s.Title == ftask.Song.Title);
                isFromQueue = true;
            }

            // 三个列表全部同步
            if (matchedSong != null && ResultsList.SelectedItem != matchedSong)
            {
                ResultsList.SelectedItem = matchedSong;
                ResultsList.ScrollIntoView(matchedSong);
            }
            if (matchedTask != null)
            {
                if (QueueList.SelectedItem != matchedTask)
                {
                    QueueList.SelectedItem = matchedTask;
                    QueueList.ScrollIntoView(matchedTask);
                }
                if (FilenameList.SelectedItem != matchedTask)
                {
                    FilenameList.SelectedItem = matchedTask;
                    FilenameList.ScrollIntoView(matchedTask);
                }
            }

            // 如果是从下载队列/文件名栏点选，需要同步 SelectedSongs
            if (isFromQueue && matchedSong != null)
            {
                // 只选中这一首（单选模式）
                vm.SelectedSongs.Clear();
                vm.SelectedSongs.Add(matchedSong);
                // 同步 Song 对象的 IsSelected 标记
                foreach (var s in vm.SearchResults)
                    s.IsSelected = s == matchedSong;
            }
        }
        finally
        {
            _isSyncing = false;
        }
    }
}
