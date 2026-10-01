using Microsoft.UI.Xaml.Media;

namespace Loadpath.Controls;

/// <summary>Static helpers for x:Bind function bindings. Keeps converters out of the XAML.</summary>
public static class Fmt
{
    public static Visibility Vis(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    public static Visibility VisNot(bool value) => value ? Visibility.Collapsed : Visibility.Visible;
    public static Visibility VisText(string? value) => string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;
    public static bool Not(bool value) => !value;
    public static double Opacity(bool value) => value ? 1 : 0;
    public static double DimIf(bool value) => value ? 0.5 : 1;
    public static string Dirty(bool dirty) => dirty ? "●" : "";
    public static string Percent(double v) => $"{v * 100:0}%";
    public static Brush Pick(bool condition, Brush whenTrue, Brush whenFalse) => condition ? whenTrue : whenFalse;

    public static Brush StatusBrush(bool danger, bool muted)
    {
        var key = danger ? "DangerBrush" : muted ? "InkTertiaryBrush" : "OkBrush";
        return (Brush)Application.Current.Resources[key];
    }

    public static Brush ForceBrush(bool tension, bool compression, bool danger)
    {
        var key = danger ? "DangerBrush" : tension ? "TensionBrush" : compression ? "CompressionBrush" : "InkTertiaryBrush";
        return (Brush)Application.Current.Resources[key];
    }

    public static Brush UtilBrush(double util)
    {
        var key = util >= 1 ? "DangerBrush" : util >= 0.75 ? "TensionBrush" : "OkBrush";
        return (Brush)Application.Current.Resources[key];
    }

    public static Brush SelectedBrush(bool selected)
    {
        var key = selected ? "SurfaceHoverBrush" : "TransparentBrush";
        return (Brush)Application.Current.Resources[key];
    }

    public static Brush AccentIf(bool on)
    {
        var key = on ? "AccentBrush" : "InkSecondaryBrush";
        return (Brush)Application.Current.Resources[key];
    }
}
