using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Loadpath.Controls;

/// <summary>A 16-unit stroked line icon that follows Foreground. Kind is a name in IconLibrary.</summary>
public sealed partial class Icon : Control
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(string), typeof(Icon), new PropertyMetadata("close", (d, _) => ((Icon)d).UpdateGeometry()));

    public static readonly DependencyProperty GeometryProperty = DependencyProperty.Register(
        nameof(Geometry), typeof(Geometry), typeof(Icon), new PropertyMetadata(null));

    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
        nameof(StrokeThickness), typeof(double), typeof(Icon), new PropertyMetadata(1.5));

    public Icon()
    {
        DefaultStyleKey = typeof(Icon);
        IsTabStop = false;
        IsHitTestVisible = false;
        UpdateGeometry();
    }

    public string Kind
    {
        get => (string)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public Geometry? Geometry
    {
        get => (Geometry?)GetValue(GeometryProperty);
        private set => SetValue(GeometryProperty, value);
    }

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    private void UpdateGeometry() => Geometry = IconLibrary.Get(Kind ?? "close");
}
