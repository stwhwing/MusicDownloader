using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace MusicDownloader;

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c) =>
        v is bool b && b ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object v, Type t, object p, CultureInfo c) =>
        (v as Visibility?) == Visibility.Visible ? true : false;
}

public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c) =>
        v is bool b && b ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object v, Type t, object p, CultureInfo c) =>
        (v as Visibility?) == Visibility.Visible ? false : true;
}

public class InverseBoolConverter : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c) =>
        v is bool b ? !b : true;
    public object ConvertBack(object v, Type t, object p, CultureInfo c) =>
        v is bool b ? !b : false;
}

public class ZeroToVisibleConverter : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c)
    {
        if (v is int i) return i == 0 ? Visibility.Visible : Visibility.Collapsed;
        return Visibility.Collapsed;
    }
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => 0;
}

public class EmptyToVisibleConverter : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c)
    {
        if (v is int len) return len == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (v is string s) return string.IsNullOrEmpty(s) ? Visibility.Visible : Visibility.Collapsed;
        return Visibility.Collapsed;
    }
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => 0;
}

public class StatusToColorConverter : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c)
    {
        var s = v?.ToString() ?? "";
        return s switch
        {
            "已完成" => new SolidColorBrush(Color.FromRgb(34, 197, 94)),
            "下载中" => new SolidColorBrush(Color.FromRgb(124, 58, 237)),
            "失败" => new SolidColorBrush(Color.FromRgb(239, 68, 68)),
            "已取消" => new SolidColorBrush(Color.FromRgb(245, 158, 11)),
            _ => new SolidColorBrush(Color.FromRgb(64, 64, 96))
        };
    }
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => null!;
}

public class StatusToTextColorConverter : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c)
    {
        var s = v?.ToString() ?? "";
        return s switch
        {
            "已完成" => new SolidColorBrush(Color.FromRgb(34, 197, 94)),
            "下载中" => new SolidColorBrush(Color.FromRgb(167, 139, 250)),
            "失败" => new SolidColorBrush(Color.FromRgb(239, 68, 68)),
            "已取消" => new SolidColorBrush(Color.FromRgb(245, 158, 11)),
            _ => new SolidColorBrush(Color.FromRgb(96, 96, 112))
        };
    }
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => null!;
}

/// <summary>
/// 格式 → 背景色（flac 红色，mp3 蓝色，其他 灰色）</summary>
public class FormatToBgConverter : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c)
    {
        var f = v?.ToString()?.ToLower() ?? "";
        return f switch
        {
            "flac" => new SolidColorBrush(Color.FromRgb(239, 68, 68)),   // 红色
            "wav" => new SolidColorBrush(Color.FromRgb(249, 115, 22)),  // 橙色
            "ape" => new SolidColorBrush(Color.FromRgb(234, 179, 8)),    // 黄色
            "m4a" => new SolidColorBrush(Color.FromRgb(34, 197, 94)),   // 绿色
            "mp3" => new SolidColorBrush(Color.FromRgb(59, 130, 246)),  // 蓝色
            _ => new SolidColorBrush(Color.FromRgb(107, 114, 128))       // 灰色
        };
    }
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => null!;
}

/// <summary>
/// 格式 → 显示文本（全部大写）</summary>
public class FormatTextConverter : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c) =>
        (v?.ToString() ?? "mp3").ToUpperInvariant();
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => null!;
}
