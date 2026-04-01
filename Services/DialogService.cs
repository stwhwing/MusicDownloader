using System.Windows;
using MusicDownloader.Windows;

namespace MusicDownloader.Services;

/// <summary>
/// 统一的弹窗服务：消除所有 ViewModel 中的 MessageBox / ResultReportWindow / RenamePreviewWindow 调用
/// </summary>
public class DialogService
{
    /// <summary>信息提示弹窗</summary>
    public void ShowInfo(string message, string title = "提示")
        => MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    /// <summary>警告弹窗</summary>
    public void ShowWarning(string message, string title = "警告")
        => MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);

    /// <summary>错误弹窗</summary>
    public void ShowError(string message, string title = "错误")
        => MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    /// <summary>确认弹窗</summary>
    public bool Confirm(string message, string title = "确认")
        => MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    /// <summary>
    /// 显示结果报告弹窗（下载/核对/重命名/删除完成报告）
    /// </summary>
    public void ShowReport(string title, string subtitle, List<SummaryItem> summary, List<ReportRow> rows, Window? owner = null)
    {
        var win = new ResultReportWindow();
        if (owner != null) win.Owner = owner;
        win.SetReport(title, subtitle, summary, rows);
        win.Show();
    }

    /// <summary>
    /// 显示拟删除文件确认弹窗（带自定义确认按钮，异步版本）
    /// 删除逻辑在 onConfirm 中执行，用户点击按钮后可看到进度更新
    /// 注意：onConfirm 不负责关闭窗口，Close 由 AddConfirmButton 统一处理
    /// </summary>
    public Task<bool> ShowConfirmWithButtonAsync(
        string title, string subtitle, List<SummaryItem> summary, List<ReportRow> rows,
        string confirmButtonText, Func<Task> confirmAction, Window? owner = null)
    {
        var win = new ResultReportWindow();
        if (owner != null) win.Owner = owner;
        win.SetReport(title, subtitle, summary, rows);
        var tcs = new TaskCompletionSource<bool>();

        // confirmAction 只负责执行业务逻辑，Close/Confirmed 由 AddConfirmButton 的 btn.Click 统一管理
        win.AddConfirmButton(confirmButtonText, confirmAction);

        // 监听窗口关闭：区分用户点击确认 vs 点击 X / ESC
        win.Closed += (_, _) => tcs.TrySetResult(win.Confirmed);
        win.ShowDialog();
        // ShowDialog() 会在窗口关闭后才继续执行，但 result 已通过 TCS 异步返回
        return tcs.Task;
    }

    /// <summary>
    /// 显示文件名重命名预览弹窗（可编辑目标文件名）
    /// </summary>
    public bool ShowRenamePreview(List<RenameItem> plan, Window? owner = null)
    {
        var win = new RenamePreviewWindow();
        if (owner != null) win.Owner = owner;
        win.SetPlan(plan);
        win.ShowDialog();
        return win.Confirmed;
    }
}
