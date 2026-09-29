using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace Loadpath.Controls;

/// <summary>bool → Visibility. ConverterParameter "invert" flips it.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var b = value is bool v && v;
        if (parameter is string p && p == "invert") b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => value is Visibility.Visible;
}

/// <summary>Non-empty string → Visible.</summary>
public sealed class TextToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}

/// <summary>Resource key (e.g. "DangerBrush") → Brush from application resources.</summary>
public sealed class KeyToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var key = value as string;
        if (string.IsNullOrEmpty(key)) key = parameter as string ?? "InkBrush";
        return Application.Current.Resources.TryGetValue(key, out var brush) && brush is Brush b ? b : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}

/// <summary>bool → 0.5 when true (dimmed), else 1.</summary>
public sealed class BoolToDimConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var b = value is true;
        if (parameter is string p && p == "invert") b = !b;
        return b ? 0.5 : 1.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}

/// <summary>bool ↔ bool? for ToggleButton.IsChecked two-way bindings.</summary>
public sealed class NullableBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) => value is true;
    public object ConvertBack(object value, Type targetType, object parameter, string language) => value is true;
}
