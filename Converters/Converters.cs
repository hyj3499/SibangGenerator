using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using SibangGenerator.Models;

namespace SibangGenerator.Converters;

public sealed class SeverityBrushConverter : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c) => v switch
    {
        Severity.Error   => new SolidColorBrush(Color.FromRgb(0xA4, 0x30, 0x2A)),
        Severity.Warning => new SolidColorBrush(Color.FromRgb(0xB8, 0x79, 0x0C)),
        Severity.Pass    => new SolidColorBrush(Color.FromRgb(0x2F, 0x61, 0x46)),
        _                => new SolidColorBrush(Color.FromRgb(0x8F, 0x8B, 0x80))
    };
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => Binding.DoNothing;
}

/// <summary>결과 행 배경. 통과는 배경 없음.</summary>
public sealed class SeverityRowBrushConverter : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c) => v switch
    {
        Severity.Error   => new SolidColorBrush(Color.FromArgb(0x1E, 0xA4, 0x30, 0x2A)),
        Severity.Warning => new SolidColorBrush(Color.FromArgb(0x22, 0xB8, 0x79, 0x0C)),
        _                => Brushes.Transparent
    };
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => Binding.DoNothing;
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c) =>
        (v is bool b && b) ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object v, Type t, object p, CultureInfo c) =>
        v is Visibility vis && vis == Visibility.Visible;
}

/// <summary>0이면 흐리게, 아니면 잉크색.</summary>
public sealed class CountToBrushConverter : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c) =>
        (v is int n && n > 0)
            ? new SolidColorBrush(Color.FromRgb(0x12, 0x16, 0x1C))
            : new SolidColorBrush(Color.FromRgb(0xC7, 0xC4, 0xBB));
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => Binding.DoNothing;
}

/// <summary>최상위 섹션(0. 1. 2. ...) 이면 연노랑, 아니면 기본 패널색.</summary>
public sealed class SectionHeaderBrushConverter : IValueConverter
{
    static readonly SolidColorBrush Section = new(Color.FromRgb(0xF7, 0xEF, 0xC8));  // 연노랑
    static readonly SolidColorBrush Normal = new(Color.FromRgb(0xF4, 0xF3, 0xEF));   // Panel

    public object Convert(object v, Type t, object p, CultureInfo c) =>
        (v is bool b && b) ? Section : Normal;
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => Binding.DoNothing;
}
