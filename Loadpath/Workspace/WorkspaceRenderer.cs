using Loadpath.Core.Analysis;
using Loadpath.Core.Geometry;
using Loadpath.Presentation;
using SkiaSharp;

namespace Loadpath.Workspace;

/// <summary>
/// Draws one RenderSnapshot as a drafting sheet. All paints and fonts are allocated once.
/// Order: paper, grid marks, dimensions, deflected shape, members, labels, callouts, supports, loads, reactions,
/// nodes, tool overlay, rulers, figure caption and legend.
/// Force sign is carried by line style, never by color alone: tension is a solid line, compression a hatched
/// line, over capacity a red hatched line, zero force a dashed line.
/// </summary>
public sealed class WorkspaceRenderer : IDisposable
{
    public const float RulerSize = 24;

    private enum Stroke { Tension, Compression, Over, Zero, Unsolved }

    private readonly SKPaint _fill = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _stroke = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
    private readonly SKPaint _hairline = new() { IsAntialias = false, Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
    private readonly SKPaint _text = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKFont _mono;
    private readonly SKFont _monoSmall;
    private readonly SKFont _monoBold;
    private readonly SKFont _ui;
    private readonly SKPathEffect _dash = SKPathEffect.CreateDash([4, 4], 0);
    private readonly SKPathEffect _zeroDash = SKPathEffect.CreateDash([5, 4], 0);
    private readonly SKPathEffect _dots = SKPathEffect.CreateDash([1.5f, 3.5f], 0);
    private readonly SKPathEffect _extDash = SKPathEffect.CreateDash([2, 3], 0);
    private readonly SKPath _path = new();
    /// <summary>Label boxes drawn this frame; later labels steer around them.</summary>
    private readonly List<SKRect> _labelRects = new(64);
    private readonly List<double> _xs = new(32);
    private readonly List<int> _over = new(16);
    private static readonly float[] CalloutDistances = [90, 130, 170, 60];

    public WorkspaceRenderer()
    {
        var mono = LoadTypeface("JetBrainsMono-Regular.ttf") ?? SKTypeface.FromFamilyName("monospace") ?? SKTypeface.Default;
        var monoMedium = LoadTypeface("JetBrainsMono-Medium.ttf") ?? mono;
        var ui = LoadTypeface("Inter-Regular.ttf") ?? SKTypeface.Default;
        _mono = new SKFont(mono, 11.5f) { Edging = SKFontEdging.SubpixelAntialias, Subpixel = true };
        _monoSmall = new SKFont(mono, 10.5f) { Edging = SKFontEdging.SubpixelAntialias, Subpixel = true };
        _monoBold = new SKFont(monoMedium, 11.5f) { Edging = SKFontEdging.SubpixelAntialias, Subpixel = true };
        _ui = new SKFont(ui, 12.5f) { Edging = SKFontEdging.SubpixelAntialias, Subpixel = true };
    }

    private static SKTypeface? LoadTypeface(string file)
    {
        foreach (var dir in new[] { AppContext.BaseDirectory, Path.Combine(AppContext.BaseDirectory, "Assets"), Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts") })
        {
            var p = Path.Combine(dir, file);
            if (!File.Exists(p)) p = Path.Combine(dir, "Fonts", file);
            if (File.Exists(p))
            {
                try { return SKTypeface.FromFile(p); } catch { /* fall through */ }
            }
        }
        return null;
    }

    public void Render(SKCanvas canvas, RenderSnapshot s)
    {
        canvas.Clear(SkiaPalette.Canvas);
        _labelRects.Clear();
        // The XAML tool palette floats over the top-left of the sheet (MainPage: Margin 40,40, ~56 x 300).
        _labelRects.Add(new SKRect(36, 36, 100, 340));
        // The XAML tool palette floats over the top-left of the sheet (MainPage: Margin 40,40, ~56 x 300).
        _labelRects.Add(new SKRect(36, 36, 100, 340));
        var solved = s.Status == AnalysisStatus.Solved;
        if (s.ShowGrid) DrawGrid(canvas, s);
        DrawDimensions(canvas, s);
        if (s.ShowDeflection && solved) DrawDeflectedShape(canvas, s);
        DrawMembers(canvas, s);
        if (s.ShowLabels && solved) DrawMemberLabels(canvas, s);
        if (solved) DrawCallouts(canvas, s);
        DrawSupports(canvas, s);
        DrawLoads(canvas, s);
        if (s.ShowReactions && solved) DrawReactions(canvas, s);
        DrawNodes(canvas, s);
        DrawOverlay(canvas, s);
        DrawRulers(canvas, s);
        DrawLegend(canvas, s);
    }

    // ---- grid: registration crosses at major steps, dotted datum lines through the origin ----

    private void DrawGrid(SKCanvas canvas, RenderSnapshot s)
    {
        var step = (float)(s.GridMajor * s.Scale);
        if (step >= 24)
        {
            _stroke.StrokeWidth = 1f;
            _stroke.Color = SkiaPalette.HairlineStrong;
            var startX = s.Origin.X % step; if (startX < 0) startX += step;
            var startY = s.Origin.Y % step; if (startY < 0) startY += step;
            for (var x = startX; x <= s.Width; x += step)
            {
                for (var y = startY; y <= s.Height; y += step)
                {
                    var px = MathF.Round(x) + 0.5f; var py = MathF.Round(y) + 0.5f;
                    canvas.DrawLine(px - 3, py, px + 3, py, _stroke);
                    canvas.DrawLine(px, py - 3, px, py + 3, _stroke);
                }
            }
        }
        _stroke.StrokeWidth = 1f;
        _stroke.Color = SkiaPalette.InkTertiary.WithAlpha(0.7);
        _stroke.PathEffect = _dots;
        if (s.Origin.X >= RulerSize && s.Origin.X <= s.Width) canvas.DrawLine(s.Origin.X, RulerSize, s.Origin.X, s.Height, _stroke);
        if (s.Origin.Y >= RulerSize && s.Origin.Y <= s.Height) canvas.DrawLine(RulerSize, s.Origin.Y, s.Width, s.Origin.Y, _stroke);
        _stroke.PathEffect = null;
    }

    // ---- members ----

    private static Stroke StrokeOf(RenderSnapshot s, in RenderSnapshot.MemberItem m)
    {
        if (s.Status != AnalysisStatus.Solved) return Stroke.Unsolved;
        if (m.Overstressed) return Stroke.Over;
        if (Math.Abs(m.ForceKn) < 0.05) return Stroke.Zero;
        return m.ForceKn > 0 ? Stroke.Tension : Stroke.Compression;
    }

    private static float Weight(RenderSnapshot s, in RenderSnapshot.MemberItem m)
    {
        if (s.Status != AnalysisStatus.Solved) return 0;
        if (s.Mode == DisplayMode.Utilization) return (float)Math.Clamp(m.Utilization, 0, 1);
        return s.MaxAbsForceKn > 1e-9 ? (float)(Math.Abs(m.ForceKn) / s.MaxAbsForceKn) : 0;
    }

    private static SKColor ColorOf(RenderSnapshot s, in RenderSnapshot.MemberItem m, Stroke kind)
    {
        var c = kind switch
        {
            Stroke.Over => SkiaPalette.Danger,
            Stroke.Zero or Stroke.Unsolved => SkiaPalette.InkTertiary,
            _ => s.Mode == DisplayMode.Utilization ? SkiaPalette.Utilization(m.Utilization) : SkiaPalette.Ink,
        };
        return m.Dimmed ? c.WithAlpha(0.3) : c;
    }

    /// <summary>One member in its line style. Weight 0..1 thickens tension lines and lengthens compression ticks.</summary>
    private void DrawMemberLine(SKCanvas canvas, SKPoint a, SKPoint b, Stroke kind, float weight, SKColor color)
    {
        _stroke.Color = color;
        switch (kind)
        {
            case Stroke.Tension:
                _stroke.StrokeWidth = 1.4f + 3.2f * weight;
                canvas.DrawLine(a, b, _stroke);
                break;
            case Stroke.Unsolved:
                _stroke.StrokeWidth = 1.6f;
                canvas.DrawLine(a, b, _stroke);
                break;
            case Stroke.Zero:
                _stroke.StrokeWidth = 1.1f;
                _stroke.PathEffect = _zeroDash;
                canvas.DrawLine(a, b, _stroke);
                _stroke.PathEffect = null;
                break;
            case Stroke.Compression:
            case Stroke.Over:
                _stroke.StrokeWidth = 1.2f;
                canvas.DrawLine(a, b, _stroke);
                var d = new SKPoint(b.X - a.X, b.Y - a.Y);
                var len = MathF.Sqrt(d.X * d.X + d.Y * d.Y);
                if (len < 8) break;
                var ux = d.X / len; var uy = d.Y / len;
                var half = (kind == Stroke.Over ? 4f : 2.5f) + 3f * weight;
                var nx = -uy * half; var ny = ux * half;
                _path.Reset();
                for (var t = 6f; t < len - 4; t += 5f)
                {
                    var px = a.X + ux * t; var py = a.Y + uy * t;
                    _path.MoveTo(px - nx, py - ny);
                    _path.LineTo(px + nx, py + ny);
                }
                _stroke.StrokeWidth = kind == Stroke.Over ? 1.3f : 1.1f;
                _stroke.StrokeCap = SKStrokeCap.Butt;
                canvas.DrawPath(_path, _stroke);
                _stroke.StrokeCap = SKStrokeCap.Round;
                break;
        }
    }

    private void DrawMembers(SKCanvas canvas, RenderSnapshot s)
    {
        // Selection and hover washes first so the line work sits on top.
        foreach (ref readonly var m in s.Members.AsSpan())
        {
            if (!m.Selected && !m.Hovered && s.Overlay.HighlightMember != m.ElementId) continue;
            _stroke.StrokeWidth = 16;
            _stroke.Color = m.Selected ? SkiaPalette.AccentSoft : SkiaPalette.SurfaceRaised;
            canvas.DrawLine(m.A, m.B, _stroke);
        }

        foreach (ref readonly var m in s.Members.AsSpan())
        {
            var kind = StrokeOf(s, m);
            DrawMemberLine(canvas, m.A, m.B, kind, Weight(s, m), ColorOf(s, m, kind));
        }
    }

    private void DrawMemberLabels(SKCanvas canvas, RenderSnapshot s)
    {
        foreach (ref readonly var m in s.Members.AsSpan())
        {
            var d = new SKPoint(m.B.X - m.A.X, m.B.Y - m.A.Y);
            var len = MathF.Sqrt(d.X * d.X + d.Y * d.Y);
            if (len < 60) continue;
            var mid = new SKPoint((m.A.X + m.B.X) / 2, (m.A.Y + m.B.Y) / 2);
            var nx = -d.Y / len; var ny = d.X / len;
            // Push the label away from the structure's center so labels fan outward instead of colliding at joints.
            if (nx * (s.Center.X - mid.X) + ny * (s.Center.Y - mid.Y) > 0) { nx = -nx; ny = -ny; }
            var text = s.Mode == DisplayMode.Utilization
                ? $"{m.Utilization * 100:0}%"
                : Math.Abs(m.ForceKn) < 0.05 ? "0.0" : (m.ForceKn >= 0 ? "+" : "−") + $"{Math.Abs(m.ForceKn):0.0}";
            var width = _monoSmall.MeasureText(text);
            const float off = 13;
            var cx = mid.X + nx * off; var cy = mid.Y + ny * off;
            var rect = new SKRect(cx - width / 2 - 3, cy - 7, cx + width / 2 + 3, cy + 7);
            _labelRects.Add(rect);
            _fill.Color = SkiaPalette.Canvas.WithAlpha(0.9);
            canvas.DrawRect(rect, _fill);
            _text.Color = m.Overstressed ? SkiaPalette.Danger : m.Dimmed ? SkiaPalette.InkTertiary : SkiaPalette.Ink;
            if (m.Dimmed && m.Overstressed) _text.Color = SkiaPalette.Danger.WithAlpha(0.45);
            canvas.DrawText(text, cx - width / 2, cy + 3.8f, _monoSmall, _text);
        }
    }

    /// <summary>Leader-line callouts for the worst members over capacity: "M5 / 179% of capacity".</summary>
    private void DrawCallouts(SKCanvas canvas, RenderSnapshot s)
    {
        _over.Clear();
        for (var i = 0; i < s.Members.Length; i++) if (s.Members[i].Overstressed && !s.Members[i].Dimmed) _over.Add(i);
        if (_over.Count == 0) return;
        _over.Sort((x, y) => s.Members[y].Utilization.CompareTo(s.Members[x].Utilization));
        var count = Math.Min(2, _over.Count);
        for (var k = 0; k < count; k++)
        {
            ref readonly var m = ref s.Members[_over[k]];
            var d = new SKPoint(m.B.X - m.A.X, m.B.Y - m.A.Y);
            var len = MathF.Sqrt(d.X * d.X + d.Y * d.Y);
            if (len < 30) continue;
            var anchor = new SKPoint(m.A.X + d.X * 0.32f, m.A.Y + d.Y * 0.32f);
            var nx = -d.Y / len; var ny = d.X / len;
            if (nx * (s.Center.X - anchor.X) + ny * (s.Center.Y - anchor.Y) > 0) { nx = -nx; ny = -ny; }
            var title = $"M{m.ElementId}";
            var body = $"{m.Utilization * 100:0}% of capacity";
            var w = Math.Max(_monoBold.MeasureText(title), _monoSmall.MeasureText(body)) + 6;
            const float h = 30;

            // Try a few distances out along the normal, nudged upward, until the box is clear.
            SKRect box = default;
            var placed = false;
            for (var c = 0; c < CalloutDistances.Length * 2 && !placed; c++)
            {
                // First along the outward normal (nudged up), then straight out sideways at the anchor's height.
                var dist = CalloutDistances[c % CalloutDistances.Length];
                var sideways = c >= CalloutDistances.Length;
                var px = sideways ? anchor.X + MathF.Sign(nx) * dist : anchor.X + nx * dist;
                var py = sideways ? anchor.Y - 12 : anchor.Y + ny * dist - 30;
                var left = nx >= 0 ? px : px - w;
                box = new SKRect(left, py - h, left + w, py);
                box = Clamp(box, s);
                var clear = true;
                foreach (var r in _labelRects) if (r.IntersectsWith(box)) { clear = false; break; }
                if (clear) { placed = true; break; }
            }
            if (!placed) continue;
            _labelRects.Add(box);

            // Leader from the member to the near bottom corner of the text block.
            var end = new SKPoint(nx >= 0 ? box.Left : box.Right, box.Bottom + 2);
            _stroke.StrokeWidth = 0.9f;
            _stroke.Color = SkiaPalette.InkSecondary;
            canvas.DrawLine(anchor, end, _stroke);
            _fill.Color = SkiaPalette.InkSecondary;
            canvas.DrawCircle(anchor, 1.8f, _fill);
            _text.Color = SkiaPalette.Danger;
            var tx = nx >= 0 ? box.Left + 2 : box.Right - _monoBold.MeasureText(title) - 2;
            canvas.DrawText(title, tx, box.Top + 11, _monoBold, _text);
            _text.Color = SkiaPalette.InkSecondary;
            var bx = nx >= 0 ? box.Left + 2 : box.Right - _monoSmall.MeasureText(body) - 2;
            canvas.DrawText(body, bx, box.Top + 26, _monoSmall, _text);
        }
    }

    private static SKRect Clamp(SKRect r, RenderSnapshot s)
    {
        var dx = r.Left < RulerSize + 8 ? RulerSize + 8 - r.Left : r.Right > s.Width - 8 ? s.Width - 8 - r.Right : 0;
        var dy = r.Top < RulerSize + 8 ? RulerSize + 8 - r.Top : 0;
        r.Offset(dx, dy);
        return r;
    }

    // ---- dimensions: chain along the lowest chord, overall span, overall height ----

    private void DrawDimensions(SKCanvas canvas, RenderSnapshot s)
    {
        if (s.Nodes.Length < 2) return;
        double minY = double.MaxValue, maxY = double.MinValue, minX = double.MaxValue, maxX = double.MinValue;
        float maxScreenX = float.MinValue, maxScreenY = float.MinValue, topScreenY = float.MaxValue;
        foreach (ref readonly var n in s.Nodes.AsSpan())
        {
            minY = Math.Min(minY, n.World.Y); maxY = Math.Max(maxY, n.World.Y);
            minX = Math.Min(minX, n.World.X); maxX = Math.Max(maxX, n.World.X);
            maxScreenX = Math.Max(maxScreenX, n.Screen.X);
            maxScreenY = Math.Max(maxScreenY, n.Screen.Y);
            topScreenY = Math.Min(topScreenY, n.Screen.Y);
        }
        if (maxX - minX < 1e-6 && maxY - minY < 1e-6) return;
        float sx(double v) => (float)(s.Origin.X + v * s.Scale);
        float sy(double v) => (float)(s.Origin.Y - v * s.Scale);

        var below = maxScreenY + (s.ShowReactions && s.Status == AnalysisStatus.Solved ? 136f : 56f);
        var chainY = below;
        var spanY = below + 34;

        // Chain: distinct x of the nodes on the lowest level.
        _xs.Clear();
        foreach (ref readonly var n in s.Nodes.AsSpan())
        {
            if (Math.Abs(n.World.Y - minY) > 1e-6) continue;
            var x = Math.Round(n.World.X, 6);
            if (!_xs.Contains(x)) _xs.Add(x);
        }
        _xs.Sort();
        var drawChain = _xs.Count > 2 || (_xs.Count == 2 && (Math.Abs(_xs[0] - minX) > 1e-6 || Math.Abs(_xs[1] - maxX) > 1e-6));
        if (maxX - minX > 1e-6)
        {
            // Extension lines from the lowest chord down to the dimension lines.
            _stroke.StrokeWidth = 0.8f;
            _stroke.Color = SkiaPalette.InkTertiary;
            _stroke.PathEffect = _extDash;
            var from = sy(minY) + 30;
            if (drawChain) foreach (var x in _xs) canvas.DrawLine(sx(x), from, sx(x), chainY + 5, _stroke);
            canvas.DrawLine(sx(minX), from, sx(minX), spanY + 5, _stroke);
            canvas.DrawLine(sx(maxX), from, sx(maxX), spanY + 5, _stroke);
            _stroke.PathEffect = null;

            if (drawChain)
            {
                DimLine(canvas, sx(_xs[0]), chainY, sx(_xs[^1]), chainY);
                for (var i = 0; i < _xs.Count; i++) Slash(canvas, sx(_xs[i]), chainY);
                for (var i = 0; i + 1 < _xs.Count; i++)
                {
                    var a = sx(_xs[i]); var b = sx(_xs[i + 1]);
                    if (b - a > 34) DimText(canvas, $"{_xs[i + 1] - _xs[i]:0.000}", (a + b) / 2, chainY - 5);
                }
            }
            DimLine(canvas, sx(minX), spanY, sx(maxX), spanY);
            Slash(canvas, sx(minX), spanY);
            Slash(canvas, sx(maxX), spanY);
            DimText(canvas, $"{maxX - minX:0.000} m", (sx(minX) + sx(maxX)) / 2, spanY - 5);
        }

        if (maxY - minY > 1e-6)
        {
            var x = maxScreenX + 60;
            _stroke.StrokeWidth = 0.8f;
            _stroke.Color = SkiaPalette.InkTertiary;
            _stroke.PathEffect = _extDash;
            canvas.DrawLine(FindScreenX(s, maxY) + 10, topScreenY, x + 5, topScreenY, _stroke);
            canvas.DrawLine(maxScreenX + 16, sy(minY), x + 5, sy(minY), _stroke);
            _stroke.PathEffect = null;
            DimLine(canvas, x, topScreenY, x, sy(minY));
            Slash(canvas, x, topScreenY);
            Slash(canvas, x, sy(minY));
            var text = $"{maxY - minY:0.000}";
            var w = _monoSmall.MeasureText(text);
            canvas.Save();
            canvas.Translate(x - 6, (topScreenY + sy(minY)) / 2 + w / 2);
            canvas.RotateDegrees(-90);
            _text.Color = SkiaPalette.InkSecondary;
            canvas.DrawText(text, 0, 0, _monoSmall, _text);
            canvas.Restore();
        }
    }

    private static float FindScreenX(RenderSnapshot s, double worldY)
    {
        var best = float.MinValue;
        foreach (ref readonly var n in s.Nodes.AsSpan()) if (Math.Abs(n.World.Y - worldY) < 1e-6) best = Math.Max(best, n.Screen.X);
        return best;
    }

    private void DimLine(SKCanvas canvas, float x1, float y1, float x2, float y2)
    {
        _stroke.StrokeWidth = 0.9f;
        _stroke.Color = SkiaPalette.InkSecondary;
        canvas.DrawLine(x1, y1, x2, y2, _stroke);
    }

    private void Slash(SKCanvas canvas, float x, float y)
    {
        _stroke.StrokeWidth = 1.2f;
        _stroke.Color = SkiaPalette.Ink;
        canvas.DrawLine(x - 4, y + 4, x + 4, y - 4, _stroke);
    }

    private void DimText(SKCanvas canvas, string text, float cx, float baseline)
    {
        var w = _monoSmall.MeasureText(text);
        _text.Color = SkiaPalette.InkSecondary;
        canvas.DrawText(text, cx - w / 2, baseline, _monoSmall, _text);
    }

    // ---- deflected shape ----

    private void DrawDeflectedShape(SKCanvas canvas, RenderSnapshot s)
    {
        _stroke.StrokeWidth = 1.1f;
        _stroke.Color = SkiaPalette.InkTertiary;
        _stroke.PathEffect = _dots;
        foreach (ref readonly var m in s.Members.AsSpan()) canvas.DrawLine(m.DeflectedA, m.DeflectedB, _stroke);
        _stroke.PathEffect = null;
        _fill.Color = SkiaPalette.InkTertiary;
        foreach (ref readonly var n in s.Nodes.AsSpan()) canvas.DrawCircle(n.Deflected, 1.6f, _fill);
    }

    // ---- supports, loads, reactions ----

    private void DrawSupports(SKCanvas canvas, RenderSnapshot s)
    {
        foreach (ref readonly var n in s.Nodes.AsSpan())
        {
            if (n.Support == SupportKind.None) continue;
            var p = n.Screen;
            var ink = n.Dimmed ? SkiaPalette.Ink.WithAlpha(0.35) : SkiaPalette.Ink;
            canvas.Save();
            canvas.Translate(p.X, p.Y);
            if (n.Support == SupportKind.RollerX) canvas.RotateDegrees(90);
            _path.Reset();
            _path.MoveTo(0, 5);
            _path.LineTo(-10, 19);
            _path.LineTo(10, 19);
            _path.Close();
            _fill.Color = SkiaPalette.Canvas;
            canvas.DrawPath(_path, _fill);
            _stroke.StrokeWidth = 1.4f;
            _stroke.Color = ink;
            canvas.DrawPath(_path, _stroke);
            var groundY = 19f;
            if (n.Support == SupportKind.RollerY || n.Support == SupportKind.RollerX)
            {
                _stroke.StrokeWidth = 1.2f;
                canvas.DrawCircle(-5f, 22.5f, 3f, _stroke);
                canvas.DrawCircle(5f, 22.5f, 3f, _stroke);
                groundY = 26f;
            }
            _stroke.StrokeWidth = 1.4f;
            canvas.DrawLine(-14, groundY, 14, groundY, _stroke);
            _stroke.StrokeWidth = 1f;
            for (var x = -12f; x <= 12f; x += 4f) canvas.DrawLine(x, groundY, x - 4f, groundY + 5f, _stroke);
            canvas.Restore();
        }
    }

    private void DrawLoads(SKCanvas canvas, RenderSnapshot s)
    {
        foreach (ref readonly var n in s.Nodes.AsSpan())
        {
            if (n.LoadKn.LengthSquared < 1e-12) continue;
            var color = n.Dimmed ? SkiaPalette.Ink.WithAlpha(0.35) : SkiaPalette.Ink;
            DrawArrow(canvas, n.LoadTail, n.Screen, 8f, color, 1.4f, n.Selected || n.Hovered, true);
            DrawArrowLabel(canvas, n.LoadTail, n.Screen, $"{n.LoadKn.Length:0.#} kN", color, s.Center);
        }
    }

    private void DrawReactions(SKCanvas canvas, RenderSnapshot s)
    {
        foreach (ref readonly var n in s.Nodes.AsSpan())
        {
            if (n.Support == SupportKind.None || n.ReactionKn.LengthSquared < 1e-8) continue;
            if (n.Support.FixesY() && Math.Abs(n.ReactionKn.Y) > 1e-6)
            {
                var dir = n.ReactionKn.Y > 0 ? 1f : -1f; // positive Y reaction pushes up on screen (−y)
                var tail = new SKPoint(n.Screen.X, n.Screen.Y + dir * 92);
                var head = new SKPoint(n.Screen.X, n.Screen.Y + dir * 30);
                DrawArrow(canvas, tail, head, 7f, SkiaPalette.Ink, 1.2f, false, false);
                ReactionLabel(canvas, tail, dir, $"{Math.Abs(n.ReactionKn.Y):0.0} kN");
            }
            if (n.Support.FixesX() && Math.Abs(n.ReactionKn.X) > 1e-6)
            {
                var dir = n.ReactionKn.X > 0 ? -1f : 1f;
                var tail = new SKPoint(n.Screen.X + dir * 76, n.Screen.Y);
                var head = new SKPoint(n.Screen.X + dir * 16, n.Screen.Y);
                DrawArrow(canvas, tail, head, 7f, SkiaPalette.Ink, 1.2f, false, false);
                DrawArrowLabel(canvas, tail, head, $"{Math.Abs(n.ReactionKn.X):0.0} kN", SkiaPalette.InkSecondary, s.Center);
            }
        }
    }

    private void ReactionLabel(SKCanvas canvas, SKPoint tail, float dir, string text)
    {
        var w = _monoSmall.MeasureText(text);
        var y = dir > 0 ? tail.Y + 16 : tail.Y - 8;
        var box = new SKRect(tail.X - w / 2 - 3, y - 11, tail.X + w / 2 + 3, y + 3);
        _labelRects.Add(box);
        _text.Color = SkiaPalette.InkSecondary;
        canvas.DrawText(text, tail.X - w / 2, y, _monoSmall, _text);
    }

    private void DrawArrow(SKCanvas canvas, SKPoint tail, SKPoint head, float headSize, SKColor color, float width, bool emphasized, bool handle)
    {
        var d = new SKPoint(head.X - tail.X, head.Y - tail.Y);
        var len = MathF.Sqrt(d.X * d.X + d.Y * d.Y);
        if (len < 1) return;
        var ux = d.X / len; var uy = d.Y / len;
        var gap = handle ? 8f : 0f; // loads stop short of the node; reactions already start clear of the support
        var tip = new SKPoint(head.X - ux * gap, head.Y - uy * gap);
        _stroke.StrokeWidth = width;
        _stroke.Color = color;
        canvas.DrawLine(tail, new SKPoint(tip.X - ux * headSize * 0.6f, tip.Y - uy * headSize * 0.6f), _stroke);
        _path.Reset();
        _path.MoveTo(tip);
        _path.LineTo(tip.X - ux * headSize - uy * headSize * 0.42f, tip.Y - uy * headSize + ux * headSize * 0.42f);
        _path.LineTo(tip.X - ux * headSize + uy * headSize * 0.42f, tip.Y - uy * headSize - ux * headSize * 0.42f);
        _path.Close();
        _fill.Color = color;
        canvas.DrawPath(_path, _fill);
        if (!handle) return;
        // Tail handle: a hollow ring the user can drag to aim the load.
        _fill.Color = SkiaPalette.Canvas;
        canvas.DrawCircle(tail, 4.5f, _fill);
        _stroke.StrokeWidth = 1.5f;
        _stroke.Color = emphasized ? SkiaPalette.Accent : color;
        canvas.DrawCircle(tail, 4.5f, _stroke);
    }

    private void DrawArrowLabel(SKCanvas canvas, SKPoint tail, SKPoint head, string text, SKColor color, SKPoint center)
    {
        var d = new SKPoint(head.X - tail.X, head.Y - tail.Y);
        var len = MathF.Sqrt(d.X * d.X + d.Y * d.Y);
        if (len < 1) return;
        var ux = d.X / len; var uy = d.Y / len;
        var px = -uy; var py = ux; // perpendicular, flipped to point away from the structure
        if (px * (center.X - tail.X) + py * (center.Y - tail.Y) > 0) { px = -px; py = -py; }
        var width = _mono.MeasureText(text);
        // Candidates in order of preference: beyond the tail on the shaft axis, further out, then beside the tail
        // on the outer side, then the inner side. Take the first that misses every label drawn so far this frame.
        Span<SKPoint> anchors = stackalloc SKPoint[4];
        anchors[0] = new SKPoint(tail.X - ux * 10, tail.Y - uy * 10);
        anchors[1] = new SKPoint(tail.X - ux * 24, tail.Y - uy * 24);
        anchors[2] = new SKPoint(tail.X + px * 10, tail.Y + py * 10);
        anchors[3] = new SKPoint(tail.X - px * 10, tail.Y - py * 10);
        var chosen = Box(anchors[0], -ux, -uy, width);
        for (var i = 0; i < anchors.Length; i++)
        {
            var dirX = i < 2 ? -ux : (i == 2 ? px : -px);
            var dirY = i < 2 ? -uy : (i == 2 ? py : -py);
            var box = Box(anchors[i], dirX, dirY, width);
            var clear = true;
            foreach (var r in _labelRects) if (r.IntersectsWith(box)) { clear = false; break; }
            if (clear) { chosen = box; break; }
        }
        _labelRects.Add(chosen);
        _fill.Color = SkiaPalette.Canvas.WithAlpha(0.9);
        canvas.DrawRect(chosen, _fill);
        _text.Color = color;
        canvas.DrawText(text, chosen.Left + 3, chosen.MidY + 4, _mono, _text);

        // Text box grown from an anchor in direction (dx, dy): centered across the direction, flush along it.
        static SKRect Box(SKPoint a, float dx, float dy, float width)
        {
            var w = width + 6; const float h = 15;
            var left = dx > 0.3f ? a.X : dx < -0.3f ? a.X - w : a.X - w / 2;
            var top = dy > 0.3f ? a.Y : dy < -0.3f ? a.Y - h : a.Y - h / 2;
            return new SKRect(left, top, left + w, top + h);
        }
    }

    // ---- nodes ----

    private void DrawNodes(SKCanvas canvas, RenderSnapshot s)
    {
        foreach (ref readonly var n in s.Nodes.AsSpan())
        {
            var p = n.Screen;
            if (n.Selected)
            {
                _fill.Color = SkiaPalette.AccentSoft;
                canvas.DrawCircle(p, 13, _fill);
                _fill.Color = SkiaPalette.Canvas;
                canvas.DrawCircle(p, 7, _fill);
                _stroke.StrokeWidth = 1.8f;
                _stroke.Color = SkiaPalette.Accent;
                canvas.DrawCircle(p, 7, _stroke);
                _fill.Color = SkiaPalette.Accent;
                canvas.DrawCircle(p, 2.6f, _fill);
                continue;
            }
            if (n.Hovered)
            {
                _fill.Color = SkiaPalette.Hairline;
                canvas.DrawCircle(p, 10, _fill);
            }
            _fill.Color = SkiaPalette.Canvas;
            canvas.DrawCircle(p, 4.6f, _fill);
            _stroke.StrokeWidth = 1.5f;
            _stroke.Color = n.Dimmed ? SkiaPalette.Ink.WithAlpha(0.35) : SkiaPalette.Ink;
            canvas.DrawCircle(p, 4.6f, _stroke);
        }

        if (s.Overlay.SnapFlashNode is { } flash)
        {
            foreach (ref readonly var n in s.Nodes.AsSpan())
            {
                if (n.ElementId != flash) continue;
                _stroke.StrokeWidth = 1.5f;
                _stroke.Color = SkiaPalette.Accent.WithAlpha(0.8);
                canvas.DrawCircle(n.Screen, 11, _stroke);
            }
        }
    }

    // ---- tool overlay ----

    private void DrawOverlay(SKCanvas canvas, RenderSnapshot s)
    {
        var o = s.Overlay;
        if (o.Guides.Count > 0)
        {
            _stroke.StrokeWidth = 1f;
            _stroke.Color = SkiaPalette.Accent.WithAlpha(0.6);
            _stroke.PathEffect = _dash;
            foreach (var g in o.Guides)
            {
                if (g.Axis == GuideAxis.Vertical)
                {
                    var x = (float)(g.Value * s.Scale + s.Origin.X);
                    canvas.DrawLine(x, 0, x, s.Height, _stroke);
                }
                else
                {
                    var y = (float)(-g.Value * s.Scale + s.Origin.Y);
                    canvas.DrawLine(0, y, s.Width, y, _stroke);
                }
            }
            _stroke.PathEffect = null;
        }

        if (o.RubberBand is { } band)
        {
            _stroke.StrokeWidth = 1.6f;
            _stroke.Color = SkiaPalette.Accent;
            canvas.DrawLine(band.From, band.To, _stroke);
        }

        if (o.LoadPreview is { } lp)
        {
            DrawArrow(canvas, lp.Tail, lp.Node, 8f, SkiaPalette.Accent, 1.4f, true, true);
            DrawArrowLabel(canvas, lp.Tail, lp.Node, $"{lp.MagnitudeKn:0.#} kN", SkiaPalette.Accent, s.Center);
        }

        if (o.GhostNode is { } ghost)
        {
            _fill.Color = SkiaPalette.AccentSoft;
            canvas.DrawCircle(ghost, 6f, _fill);
            _stroke.StrokeWidth = 1.5f;
            _stroke.Color = SkiaPalette.Accent;
            canvas.DrawCircle(ghost, 5f, _stroke);
        }

        if (o.Marquee is { } r)
        {
            _fill.Color = SkiaPalette.Accent.WithAlpha(0.06);
            canvas.DrawRect(r, _fill);
            _stroke.StrokeWidth = 1f;
            _stroke.Color = SkiaPalette.Accent;
            canvas.DrawRect(r, _stroke);
        }
    }

    // ---- rulers ----

    private void DrawRulers(SKCanvas canvas, RenderSnapshot s)
    {
        _fill.Color = SkiaPalette.Canvas;
        canvas.DrawRect(0, 0, s.Width, RulerSize, _fill);
        canvas.DrawRect(0, 0, RulerSize, s.Height, _fill);
        _hairline.Color = SkiaPalette.HairlineStrong;
        canvas.DrawLine(0, RulerSize - 0.5f, s.Width, RulerSize - 0.5f, _hairline);
        canvas.DrawLine(RulerSize - 0.5f, 0, RulerSize - 0.5f, s.Height, _hairline);

        var minor = s.GridMinor; var major = s.GridMajor;
        var showMinor = minor * s.Scale >= 6;
        _hairline.Color = SkiaPalette.InkSecondary;
        _text.Color = SkiaPalette.InkSecondary;

        // Top ruler.
        var x0 = Math.Floor((RulerSize - s.Origin.X) / s.Scale / minor) * minor;
        for (var v = x0; ; v += minor)
        {
            var px = MathF.Round((float)(s.Origin.X + v * s.Scale)) + 0.5f;
            if (px > s.Width) break;
            if (px < RulerSize) continue;
            var isMajor = Math.Abs(v / major - Math.Round(v / major)) < 1e-6;
            if (!isMajor && !showMinor) continue;
            canvas.DrawLine(px, RulerSize - (isMajor ? 9 : 4), px, RulerSize, _hairline);
            if (isMajor) canvas.DrawText(Label(v), px + 3, 12, _monoSmall, _text);
        }

        // Left ruler.
        var y0 = Math.Floor((s.Origin.Y - s.Height) / s.Scale / minor) * minor;
        for (var v = y0; ; v += minor)
        {
            var py = MathF.Round((float)(s.Origin.Y - v * s.Scale)) + 0.5f;
            if (py < RulerSize) break;
            if (py > s.Height) continue;
            var isMajor = Math.Abs(v / major - Math.Round(v / major)) < 1e-6;
            if (!isMajor && !showMinor) continue;
            canvas.DrawLine(RulerSize - (isMajor ? 9 : 4), py, RulerSize, py, _hairline);
            if (isMajor) canvas.DrawText(Label(v), 4, py - 4, _monoSmall, _text);
        }

        // Corner unit.
        _fill.Color = SkiaPalette.Canvas;
        canvas.DrawRect(0, 0, RulerSize - 1, RulerSize - 1, _fill);
        _text.Color = SkiaPalette.InkTertiary;
        canvas.DrawText("m", 8, 15, _monoSmall, _text);

        // Selection markers on both rulers.
        _fill.Color = SkiaPalette.Accent;
        foreach (ref readonly var n in s.Nodes.AsSpan())
        {
            if (!n.Selected) continue;
            if (n.Screen.X > RulerSize)
            {
                _path.Reset();
                _path.MoveTo(n.Screen.X - 4, RulerSize - 7);
                _path.LineTo(n.Screen.X + 4, RulerSize - 7);
                _path.LineTo(n.Screen.X, RulerSize - 1);
                _path.Close();
                canvas.DrawPath(_path, _fill);
            }
            if (n.Screen.Y > RulerSize)
            {
                _path.Reset();
                _path.MoveTo(RulerSize - 7, n.Screen.Y - 4);
                _path.LineTo(RulerSize - 7, n.Screen.Y + 4);
                _path.LineTo(RulerSize - 1, n.Screen.Y);
                _path.Close();
                canvas.DrawPath(_path, _fill);
            }
        }

        static string Label(double v) => Math.Abs(v) < 1e-9 ? "0" : $"{v:0.##}".Replace("-", "−");
    }

    // ---- figure caption and legend ----

    private void DrawLegend(SKCanvas canvas, RenderSnapshot s)
    {
        if (s.Nodes.Length < 2 || s.Members.Length == 0) return;
        double minX = double.MaxValue, maxX = double.MinValue;
        foreach (ref readonly var n in s.Nodes.AsSpan()) { minX = Math.Min(minX, n.World.X); maxX = Math.Max(maxX, n.World.X); }
        var name = string.IsNullOrWhiteSpace(s.DocumentName) || s.DocumentName == "Untitled" ? "Truss" : s.DocumentName;
        var what = s.Mode == DisplayMode.Utilization ? "Utilization as a share of capacity." : "Axial force in kN, tension positive.";
        var caption = $"{name}, {maxX - minX:0.000} m span. {what}";

        var left = RulerSize + 16;
        var captionY = s.Height - 48;
        _text.Color = SkiaPalette.Ink;
        canvas.DrawText("Fig. 01", left, captionY, _monoBold, _text);
        _text.Color = SkiaPalette.InkSecondary;
        canvas.DrawText(caption, left + _monoBold.MeasureText("Fig. 01") + 14, captionY, _ui, _text);

        var y = s.Height - 22;
        var x = left;
        x = LegendItem(canvas, x, y, Stroke.Tension, SkiaPalette.Ink, "Tension");
        x = LegendItem(canvas, x, y, Stroke.Compression, SkiaPalette.Ink, "Compression");
        x = LegendItem(canvas, x, y, Stroke.Over, SkiaPalette.Danger, "Over capacity");
        x = LegendItem(canvas, x, y, Stroke.Zero, SkiaPalette.InkTertiary, "Zero force");
        if (s.ShowDeflection)
        {
            _stroke.StrokeWidth = 1.1f;
            _stroke.Color = SkiaPalette.InkTertiary;
            _stroke.PathEffect = _dots;
            canvas.DrawLine(x, y - 4, x + 24, y - 4, _stroke);
            _stroke.PathEffect = null;
            _text.Color = SkiaPalette.InkSecondary;
            canvas.DrawText($"Deflected ×{s.Exaggeration:0.0}", x + 32, y, _monoSmall, _text);
        }
    }

    private float LegendItem(SKCanvas canvas, float x, float y, Stroke kind, SKColor color, string label)
    {
        DrawMemberLine(canvas, new SKPoint(x, y - 4), new SKPoint(x + 24, y - 4), kind, kind == Stroke.Tension ? 0.4f : 0.2f, color);
        _text.Color = SkiaPalette.InkSecondary;
        canvas.DrawText(label, x + 32, y, _monoSmall, _text);
        return x + 32 + _monoSmall.MeasureText(label) + 26;
    }

    public void Dispose()
    {
        _fill.Dispose();
        _stroke.Dispose();
        _hairline.Dispose();
        _text.Dispose();
        _mono.Dispose();
        _monoSmall.Dispose();
        _monoBold.Dispose();
        _ui.Dispose();
        _dash.Dispose();
        _zeroDash.Dispose();
        _dots.Dispose();
        _extDash.Dispose();
        _path.Dispose();
    }
}
