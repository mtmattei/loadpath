using System.Globalization;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Loadpath.Controls;

/// <summary>
/// 16-unit line icons as path mini-language (M/L/C/Z + circles as 'O cx cy r').
/// Parsed by hand into PathGeometry so no platform string→Geometry conversion is needed.
/// </summary>
public static class IconLibrary
{
    private static readonly Dictionary<string, string> Paths = new()
    {
        ["select"] = "M3 2 L3 13 L6.2 10.2 L8.4 14.5 L10.4 13.6 L8.2 9.4 L12.4 9 Z",
        ["node"] = "O8 8 3.2 M8 1.5 L8 3.4 M8 12.6 L8 14.5 M1.5 8 L3.4 8 M12.6 8 L14.5 8",
        ["member"] = "O3 12.5 1.8 O13 3.5 1.8 M4.3 11.2 L11.7 4.8",
        ["load"] = "M8 1.5 L8 11.5 M4.5 8 L8 11.5 L11.5 8 M2.5 14.2 L13.5 14.2",
        ["support"] = "M8 2.5 L12.5 10 L3.5 10 Z M1.5 13 L14.5 13 M3.5 13 L2 14.5 M6.5 13 L5 14.5 M9.5 13 L8 14.5 M12.5 13 L11 14.5",
        ["pan"] = "M6 14 L6 7 C6 5.8 7.6 5.8 7.6 7 L7.6 9.5 M7.6 6.5 C7.6 5.2 9.3 5.2 9.3 6.5 L9.3 9.5 M9.3 7.2 C9.3 6 11 6 11 7.2 L11 10.5 C11 13 9.5 14.5 7.2 14.5 C5.5 14.5 4.6 13.7 3.6 12 L2.2 9.5 C1.7 8.6 2.9 7.8 3.6 8.6 L5 10.3 M6 7 L6 3 C6 1.7 7.6 1.7 7.6 3 L7.6 6.5",
        ["undo"] = "M6 4 L2.5 7.5 L6 11 M2.5 7.5 L10 7.5 C12.2 7.5 13.5 8.8 13.5 10.5 C13.5 12.2 12.2 13.5 10 13.5 L7 13.5",
        ["redo"] = "M10 4 L13.5 7.5 L10 11 M13.5 7.5 L6 7.5 C3.8 7.5 2.5 8.8 2.5 10.5 C2.5 12.2 3.8 13.5 6 13.5 L9 13.5",
        ["search"] = "O7 7 4.5 M10.3 10.3 L14 14",
        ["delete"] = "M3 4.5 L13 4.5 M6.5 4.5 L6.5 2.5 L9.5 2.5 L9.5 4.5 M4.5 4.5 L5.2 13.5 L10.8 13.5 L11.5 4.5 M6.8 7 L7 11 M9.2 7 L9 11",
        ["duplicate"] = "M5.5 5.5 L5.5 2.5 L13.5 2.5 L13.5 10.5 L10.5 10.5 M2.5 5.5 L10.5 5.5 L10.5 13.5 L2.5 13.5 Z",
        ["close"] = "M4 4 L12 12 M12 4 L4 12",
        ["chevron-down"] = "M4 6 L8 10 L12 6",
        ["chevron-right"] = "M6 4 L10 8 L6 12",
        ["fit"] = "M2.5 6 L2.5 2.5 L6 2.5 M10 2.5 L13.5 2.5 L13.5 6 M13.5 10 L13.5 13.5 L10 13.5 M6 13.5 L2.5 13.5 L2.5 10",
        ["save"] = "M2.5 2.5 L11 2.5 L13.5 5 L13.5 13.5 L2.5 13.5 Z M5 2.5 L5 6 L10 6 L10 2.5 M4.5 13.5 L4.5 9 L11.5 9 L11.5 13.5",
        ["folder"] = "M2 4 L6 4 L7.5 5.5 L14 5.5 L14 13 L2 13 Z",
        ["new"] = "M4 2 L10 2 L13 5 L13 14 L4 14 Z M10 2 L10 5 L13 5",
        ["panel"] = "M2 3 L14 3 L14 13 L2 13 Z M10 3 L10 13",
        ["target"] = "O8 8 5 O8 8 1.2 M8 1.5 L8 4 M8 12 L8 14.5 M1.5 8 L4 8 M12 8 L14.5 8",
        ["check"] = "M3 8.5 L6.5 12 L13 4.5",
        ["warning"] = "M8 2 L14.5 13.5 L1.5 13.5 Z M8 6.5 L8 9.5 M8 11.2 L8 11.8",
        ["mark"] = "M2.5 13.5 L2.5 8 L8 2.5 L13.5 8 L13.5 13.5 M2.5 10 L8 4.5 L13.5 10",
    };

    public static IEnumerable<string> Names => Paths.Keys;

    public static Geometry Get(string name)
    {
        if (!Paths.TryGetValue(name, out var data)) data = Paths["close"];
        return Parse(data);
    }

    private static Geometry Parse(string data)
    {
        var group = new GeometryGroup();
        var path = new PathGeometry();
        group.Children.Add(path);
        PathFigure? figure = null;
        var tokens = Tokenize(data);
        var i = 0;
        var current = new Point();
        double Next() => tokens[i++].Value;
        while (i < tokens.Count)
        {
            var cmd = tokens[i++].Command;
            switch (cmd)
            {
                case 'M':
                    current = new Point(Next(), Next());
                    figure = new PathFigure { StartPoint = current, IsClosed = false, IsFilled = false };
                    path.Figures.Add(figure);
                    break;
                case 'L':
                    current = new Point(Next(), Next());
                    figure?.Segments.Add(new LineSegment { Point = current });
                    break;
                case 'C':
                    var c1 = new Point(Next(), Next());
                    var c2 = new Point(Next(), Next());
                    current = new Point(Next(), Next());
                    figure?.Segments.Add(new BezierSegment { Point1 = c1, Point2 = c2, Point3 = current });
                    break;
                case 'Z':
                    if (figure is not null) figure.IsClosed = true;
                    break;
                case 'O':
                    var cx = Next();
                    var cy = Next();
                    var r = Next();
                    group.Children.Add(new EllipseGeometry { Center = new Point(cx, cy), RadiusX = r, RadiusY = r });
                    break;
            }
        }
        return group;
    }

    private readonly record struct Token(char Command, double Value);

    private static List<Token> Tokenize(string data)
    {
        var list = new List<Token>();
        var i = 0;
        while (i < data.Length)
        {
            var c = data[i];
            if (char.IsWhiteSpace(c) || c == ',') { i++; continue; }
            if (char.IsLetter(c)) { list.Add(new Token(char.ToUpperInvariant(c), 0)); i++; continue; }
            var start = i;
            while (i < data.Length && (char.IsDigit(data[i]) || data[i] is '.' or '-' or 'e' or 'E')) i++;
            list.Add(new Token('\0', double.Parse(data.AsSpan(start, i - start), CultureInfo.InvariantCulture)));
        }
        return list;
    }
}
