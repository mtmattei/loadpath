using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Loadpath.Controls;

/// <summary>A 0..1 bar. The fill scales with Value; the color is set by the caller (Foreground).</summary>
public sealed partial class UtilizationBar : Control
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(UtilizationBar), new PropertyMetadata(0.0));

    public UtilizationBar()
    {
        DefaultStyleKey = typeof(UtilizationBar);
        IsTabStop = false;
    }

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, Math.Clamp(value, 0, 1)); }
}
