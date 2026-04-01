using System.Text;
using System.Windows;
using System.Windows.Media;
using MusicDownloader.Services;

namespace MusicDownloader.Windows;

/// <summary>
/// 报告弹窗中的一行数据
/// </summary>
// ReportRow / SummaryItem 定义在 Services/ProgressService.cs 中

public partial class ResultReportWindow : Window
{
    /// <summary>弹窗是否被用户确认（而非直接关闭）</summary>
    public bool Confirmed { get; set; } = false;

    public ResultReportWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 设置报告标题、摘要和详细行列表
    /// </summary>
    public void SetReport(string title, string subtitle, List<SummaryItem> summary, List<ReportRow> rows)
    {
        TitleText.Text = title;
        SubTitleText.Text = subtitle;

        // 摘要面板
        SummaryPanel.Children.Clear();
        foreach (var item in summary)
        {
            var panel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Vertical,
                Margin = new Thickness(0, 0, 32, 0)
            };
            var countText = new System.Windows.Controls.TextBlock
            {
                Text = item.Count.ToString(),
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(item.Color)),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            var labelText = new System.Windows.Controls.TextBlock
            {
                Text = item.Label,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0x90, 0x90, 0xA0)),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            panel.Children.Add(countText);
            panel.Children.Add(labelText);
            SummaryPanel.Children.Add(panel);
        }

        // 详细列表
        ResultList.ItemsSource = rows;
        // 更新行数显示
        Title = $"操作结果报告（共 {rows.Count} 条）";
    }

    /// <summary>
    /// 向底部按钮区域追加一个「确认执行」按钮（点击后设置 Confirmed=true，然后关闭）
    /// </summary>
    public void AddConfirmButton(string label, Func<Task> onConfirm)
    {
        var btn = new System.Windows.Controls.Button
        {
            Content = label,
            Style = (Style)Resources["ActionBtn"],
            Margin = new Thickness(0, 0, 8, 0)
        };
        btn.Click += async (_, _) =>
        {
            btn.IsEnabled = false;
            try
            {
                await onConfirm();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ResultReportWindow] 确认操作异常: {ex.Message}");
            }
            // 无论成功或异常，都要设置 Confirmed 并关闭窗口
            Confirmed = true;
            Close();
        };
        // ButtonPanel 是底部 StackPanel 的 x:Name
        ButtonPanel.Children.Insert(0, btn);
    }

    private void CopyToClipboard(object sender, RoutedEventArgs e)
    {
        if (ResultList.ItemsSource is List<ReportRow> rows)
        {
            var sb = new StringBuilder();
            sb.AppendLine(TitleText.Text);
            sb.AppendLine(SubTitleText.Text);
            sb.AppendLine(new string('-', 60));
            foreach (var row in rows)
            {
                sb.AppendLine($"{row.Index}. {row.FileName}");
                if (!string.IsNullOrEmpty(row.SubText))
                    sb.AppendLine($"   {row.SubText}");
                if (!string.IsNullOrEmpty(row.Note))
                    sb.AppendLine($"   → {row.Note}");
            }
            try { Clipboard.SetText(sb.ToString()); }
            catch { }
            Title = "操作结果报告（已复制到剪贴板）";
        }
    }

    private void CloseWindow(object sender, RoutedEventArgs e) => Close();
}
