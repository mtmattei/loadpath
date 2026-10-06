using System.Globalization;
using Microsoft.UI.Xaml.Media;

namespace Loadpath.Controls;

/// <summary>
/// A line drawing from pixel-space segments ("x1,y1,x2,y2;…"), used for preset thumbnails.
/// Segments are already fitted to the box by the model, so the control only strokes them.
/// </summary>
public sealed partial class Sketch : Grid
{
    public static readonly DependencyProperty SegmentsProperty = DependencyProperty.Register(
        nameof(Segments), typeof(string), typeof(Sketch), new PropertyMetadata("", (d, _) => ((Sketch)d).Rebuild()));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(Sketch), new PropertyMetadata(null, (d, _) => ((Sketch)d).Rebuild()));

    public Sketch() => IsHitTestVisible = false;

    public string Segments { get => (string)GetValue(SegmentsProperty); set => SetValue(SegmentsProperty, value); }
    public Brush? Stroke { get => (Brush?)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }

    private void Rebuild()
    {
        Children.Clear();
        if (string.IsNullOrEmpty(Segments)) return;
        var geometry = new PathGeometry();
        foreach (var seg in Segments.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var v = seg.Split(',');
            if (v.Length != 4) continue;
            static double D(string s) => double.Parse(s, CultureInfo.InvariantCulture);
            var figure = new PathFigure { StartPoint = new Windows.Foundation.Point(D(v[0]), D(v[1])), IsClosed = false };
            figure.Segments.Add(new LineSegment { Point = new Windows.Foundation.Point(D(v[2]), D(v[3])) });
            geometry.Figures.Add(figure);
        }
        Children.Add(new Microsoft.UI.Xaml.Shapes.Path { Data = geometry, Stroke = Stroke, StrokeThickness = 1, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
    }
}
