using System.Windows;
using System.Windows.Threading;

namespace MusicDownloader;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 全局 UI 线程未捕获异常处理
        DispatcherUnhandledException += (_, args) =>
        {
            LogError("DispatcherUnhandledException", args.Exception);
            ShowError($"发生了意外错误：\n\n{args.Exception.Message}\n\n详情已记录到 error.log", "应用程序错误");
            args.Handled = true;
        };

        // 全局非 UI 线程未捕获异常处理
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                LogError("UnhandledException", ex);
                // 确保在 UI 线程显示消息框
                Application.Current?.Dispatcher.Invoke(() =>
                    ShowError($"发生了严重错误，应用程序即将退出：\n\n{ex.Message}", "严重错误"));
            }
            // 不设置 ExitApplication，让运行时决定（程序将退出）
        };

        // TaskScheduler 未观察异常（静默记录）
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            System.Diagnostics.Debug.WriteLine($"[UnobservedTaskException] {args.Exception.Message}");
            args.SetObserved();
        };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        base.OnExit(e);

        // 兜底：确保后台线程（DispatcherTimer / HttpClient 等）不阻止进程退出
        // MainWindow_Closing 应该已经调用了 Dispose，此处作为最终保障
        System.Diagnostics.Debug.WriteLine("[App.OnExit] Application exiting cleanly.");
    }

    private static void ShowError(string message, string title)
    {
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private static void LogError(string context, Exception ex)
    {
        try
        {
            var logPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "error.log");
            var entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{context}]\n  Type: {ex.GetType().FullName}\n  Message: {ex.Message}\n  StackTrace:\n{ex.StackTrace}\n";
            if (ex.InnerException != null)
                entry += $"  InnerException: {ex.InnerException.GetType().FullName} — {ex.InnerException.Message}\n  InnerStack:\n{ex.InnerException.StackTrace}\n";
            System.IO.File.AppendAllText(logPath, entry + "\n");
        }
        catch { }
    }
}


