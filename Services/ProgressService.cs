namespace MusicDownloader.Services;

/// <summary>
/// 统一的进度报告服务：消除所有 ViewModel 中 Progress 匿名函数
/// </summary>
public class ProgressService<T>
{
    private readonly Action<T> _reporter;

    public ProgressService(Action<T> reporter) => _reporter = reporter;

    public void Report(T value) => _reporter(value);

    public static ProgressService<(int current, int total, string file)> ForFile(string label, Action<string> statusUpdate)
    {
        return new ProgressService<(int current, int total, string file)>(p =>
            statusUpdate($"{label} ({p.current}/{p.total}): {p.file}"));
    }
}

/// <summary>
/// 报告行（用于 ResultReportWindow）
/// </summary>
public class ReportRow
{
    public int Index { get; set; }
    public string FileName { get; set; } = "";
    public string SubText { get; set; } = "";
    /// <summary>SubText 是否非空（用于 XAML 绑定 Visibility）</summary>
    public bool HasSubText => !string.IsNullOrEmpty(SubText);
    public string Note { get; set; } = "";
    public string NoteColor { get; set; } = "#9090A0";
}

/// <summary>
/// 摘要项（用于 ResultReportWindow）
/// </summary>
public class SummaryItem
{
    public string Label { get; set; } = "";
    public int Count { get; set; }
    public string Color { get; set; } = "#E4E4F0";
}

/// <summary>
/// 核对状态的颜色辅助
/// </summary>
public static class StatusColors
{
    public const string Match = "#27AE60";
    public const string Mismatch = "#E74C3C";
    public const string NoTag = "#F39C12";
    public const string NotFound = "#9090A0";
    public const string Error = "#E74C3C";
    public const string Done = "#27AE60";
    public const string Pending = "#9090A0";
}
