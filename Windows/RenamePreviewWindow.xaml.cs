using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using MusicDownloader.Services;

namespace MusicDownloader.Windows;

/// <summary>
/// 重命名预览行（带编辑支持 + 可信源显示）
/// </summary>
public class RenamePreviewRow : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public int Index { get; set; }
    public string OriginalFileName { get; set; } = "";

    // NewFileName 直接绑定到 RenameItem.NewFileName（双向）
    private RenameItem _item = null!;
    public RenameItem Item
    {
        get => _item;
        init
        {
            _item = value;
            // 当 Item.NewFileName 改变时也通知 UI
            _item.PropertyChanged += (_, _) => OnPropertyChanged(nameof(NewFileName));
        }
    }

    public string NewFileName
    {
        get => _item.NewFileName;
        set => _item.NewFileName = value;
    }

    public bool IsEditable => _item.Status == RenameStatus.Pending;

    /// <summary>可信源匹配（网易云）</summary>
    public string VerifiedMatch => _item.IsVerified
        ? $"{_item.VerifiedArtist} - {_item.VerifiedTitle}"
        : "";

    public string StatusLabel => _item.Status switch
    {
        RenameStatus.Pending => _item.WillConflict ? "⚠️ 冲突" : "待改名",
        RenameStatus.Skipped => "无需改",
        RenameStatus.Done => "✅ 完成",
        RenameStatus.Failed => "❌ 失败",
        _ => ""
    };

    public string StatusColor => _item.Status switch
    {
        RenameStatus.Pending => _item.WillConflict ? "#E74C3C" : "#7C3AED",
        RenameStatus.Skipped => "#606070",
        RenameStatus.Done => "#27AE60",
        RenameStatus.Failed => "#E74C3C",
        _ => "#9090A0"
    };

    /// <summary>备注（可信源验证状态）</summary>
    public string VerifyNote => _item.Note;
}

public partial class RenamePreviewWindow : Window
{
    private List<RenameItem> _plan = new();
    public bool Confirmed { get; private set; } = false;

    public RenamePreviewWindow()
    {
        InitializeComponent();
    }

    public void SetPlan(List<RenameItem> plan)
    {
        _plan = plan;

        var toRename = plan.Count(p => p.Status == RenameStatus.Pending);
        var skipCount = plan.Count(p => p.Status == RenameStatus.Skipped);
        var verifiedCount = plan.Count(p => p.IsVerified);
        var conflictCount = plan.Count(p => p.WillConflict);

        SubTitleText.Text = $"共 {plan.Count} 个文件 · 将重命名 {toRename} 个 · 跳过 {skipCount} 个"
                           + (verifiedCount > 0 ? $" · 🌐 网易云已验证 {verifiedCount} 个" : "")
                           + (conflictCount > 0 ? $" · ⚠️ {conflictCount} 个冲突" : "");

        // 统计面板
        SummaryPanel.Children.Clear();
        AddSummaryCard("共扫描", plan.Count, "#E4E4F0");
        AddSummaryCard("将重命名", toRename, "#7C3AED");
        AddSummaryCard("网易云验证", verifiedCount, "#2980B9");
        AddSummaryCard("跳过（无变化）", skipCount, "#9090A0");
        if (conflictCount > 0) AddSummaryCard("目标冲突", conflictCount, "#E74C3C");

        // 列表数据
        var rows = plan.Select((item, i) => new RenamePreviewRow
        {
            Index = i + 1,
            OriginalFileName = item.OriginalFileName,
            Item = item
        }).ToList();

        RenameList.ItemsSource = rows;
        Title = $"核对与重命名预览（{toRename} 个待处理）— 可编辑后确认执行";
    }

    private void AddSummaryCard(string label, int count, string colorHex)
    {
        var panel = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Vertical,
            Margin = new Thickness(0, 0, 32, 0)
        };
        panel.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = count.ToString(),
            FontSize = 22,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex)),
            HorizontalAlignment = HorizontalAlignment.Center
        });
        panel.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = label,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0x90, 0x90, 0xA0)),
            HorizontalAlignment = HorizontalAlignment.Center
        });
        SummaryPanel.Children.Add(panel);
    }

    private void ConfirmBtn_Click(object sender, RoutedEventArgs e)
    {
        // 把 Skipped 的记录中如果用户改了名字则重设为 Pending
        foreach (var item in _plan)
        {
            if (item.Status == RenameStatus.Skipped &&
                !string.Equals(item.OriginalFileName, item.NewFileName, StringComparison.OrdinalIgnoreCase))
            {
                item.Status = RenameStatus.Pending;
            }
        }
        Confirmed = true;
        Close();
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e) => Close();
}
