using Loadpath.Core.Analysis;
using Loadpath.Presentation;
using SkiaSharp;

namespace Loadpath.Workspace;

/// <summary>
/// Draws one RenderSnapshot as a blueprint sheet. All paints and fonts are allocated once.
/// Order: paper, frame, grid, dimensions, deflected shape, members, force labels and pills, part names, supports,
/// loads, reactions, nodes, tool overlay, figure tags and legend. Each layer honours its Reduce-noise flag.
/// Force sign is carried by line style, never by color alone: tension is an open ribbon, compression a hatched
/// ribbon, over capacity a red hatched ribbon, zero force a dashed line.
/// </summary>
public sealed class WorkspaceRenderer : IDisposable
{
    /// <summary>Inset of the dashed drawing frame from the canvas edge.</summary>
    public const float FrameInset = 16;

    private enum Stroke { Tension, Compression, Over, Zero, Unsolved }

    private readonly SKPaint _fill = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _stroke = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
    private readonly SKPaint _text = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKFont _mono;
    private readonly SKFont _monoSmall;
    private readonly SKFont _monoBold;
    private readonly SKFont _ui;
    private readonly SKPathEffect _dash = SKPathEffect.CreateDash([4, 4], 0);
    private readonly SKPathEffect _zeroDash = SKPathEffect.CreateDash([5, 4], 0);
    private readonly SKPathEffect _deflectDash = SKPathEffect.CreateDash([4, 3], 0);
    private readonly SKPathEffect _frameDash = SKPathEffect.CreateDash([3, 3], 0);
    private readonly SKPathEffect _extDash = SKPathEffect.CreateDash([2, 3], 0);
    private readonly SKPathEffect _haloDash = SKPathEffect.CreateDash([2, 2.5f], 0);
    private readonly SKPath _path = new();
    /// <summary>Label boxes drawn this frame; later labels steer around them.</summary>
    private readonly List<SKRect> _labelRects = new(64);
    private readonly List<double> _xs = new(32);

    public WorkspaceRenderer()
    {
        var mono = LoadTypeface("JetBrainsMono-Regular.ttf") ?? SKTypeface.FromFamilyName("monospace") ?? SKTypeface.Default;
        var monoMedium = LoadTypeface("JetBrainsMono-Medium.ttf") ?? mono;
        var ui = LoadTypeface("Inter-Regular.ttf") ?? SKTypeface.Default;
        _mono = new SKFont(monoMedium, 11.5f) { Edging = SKFontEdging.SubpixelAntialias, Subpixel = true };
        _monoSmall = new SKFont(mono, 10.5f) { Edging = SKFontEdging.SubpixelAntialias, Subpixel = true };
        _monoBold = new SKFont(monoMedium, 10.5f) { Edging = SKFontEdging.SubpixelAntialias, Subpixel = true };
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
        // The XAML tool palette floats over the top-left of the sheet (MainPage: Margin 40,24, ~56 x 300).
        _labelRects.Add(new SKRect(36, 20, 100, 330));
        var solved = s.Status == AnalysisStatus.Solved;
        DrawFrame(canvas, s);
        if (s.ShowGrid) DrawGrid(canvas, s);
        if (s.ShowDimensions) DrawDimensions(canvas, s);
        if (s.ShowDeflection && solved) DrawDeflectedShape(canvas, s);
        DrawMembers(canvas, s);
        if (solved) DrawMemberLabels(canvas, s);
        if (s.ShowPartNames) DrawPartNames(canvas, s);
        if (s.ShowSupports) DrawSupports(canvas, s);
        if (s.ShowLoads) DrawLoads(canvas, s);
        if (s.ShowReactions && solved) DrawReactions(canvas, s);
        DrawNodes(canvas, s);
        DrawOverlay(canvas, s);
        if (s.ShowLegend) DrawLegend(canvas, s);
    }

    // ---- sheet: dashed frame, registration crosses and minor dots ----

    private void DrawFrame(SKCanvas canvas, RenderSnapshot s)
    {
        _stroke.StrokeWidth = 1f;
        _stroke.Color = SkiaPalette.AccentFaint;
        _stroke.PathEffect = _frameDash;
        canvas.DrawRect(FrameInset + 0.5f, FrameInset + 0.5f, s.Width - 2 * FrameInset - 1, s.Height - 2 * FrameInset - 1, _stroke);
        _stroke.PathEffect = null;
    }

    private void DrawGrid(SKCanvas canvas, RenderSnapshot s)
    {
        var major = (float)(s.GridMajor * s.Scale);
        var minor = (float)(s.GridMinor * s.Scale);
        float l = FrameInset + 4, t = FrameInset + 4, r = s.Width - FrameInset - 4, b = s.Height - FrameInset - 4;
        if (minor >= 12)
        {
            _fill.Color = SkiaPalette.GridMark;
            for (var x = Start(s.Origin.X, minor, l); x <= r; x += minor)
                for (var y = Start(s.Origin.Y, minor, t); y <= b; y += minor)
                    canvas.DrawCircle(x, y, 0.8f, _fill);
        }
        if (major >= 24)
        {
            _stroke.StrokeWidth = 1f;
            _stroke.Color = SkiaPalette.AccentFaint;
            for (var x = Start(s.Origin.X, major, l); x <= r; x += major)
                for (var y = Start(s.Origin.Y, major, t); y <= b; y += major)
                {
                    var px = MathF.Round(x) + 0.5f; var py = MathF.Round(y) + 0.5f;
                    canvas.DrawLine(px - 3, py, px + 3, py, _stroke);
                    canvas.DrawLine(px, py - 3, px, py + 3, _stroke);
                }
        }

        static float Start(float origin, float step, float min)
        {
            var v = origin % step;
            if (v < 0) v += step;
            while (v < min) v += step;
            return v;
        }
    }

    // ---- members ----

    private static Stroke StrokeOf(RenderSnapshot s, in RenderSnapshot.MemberItem m)
    {
        if (s.Status != AnalysisStatus.Solved) return Stroke.Unsolved;
        if (m.Overstressed && s.ShowOverCapacity) return Stroke.Over;
        if (Math.Abs(m.ForceKn) < 0.05) return Stroke.Zero;
        return m.ForceKn > 0 ? Stroke.Tension : Stroke.Compression;
    }

    /// <summary>Ribbon width: grows with the force (or utilization) share; over-capacity ribbons never read thin.</summary>
    private static float RibbonWidth(RenderSnapshot s, in RenderSnapshot.MemberItem m, Stroke kind)
    {
        if (kind is Stroke.Unsolved or Stroke.Zero) return 0;
        var share = s.Mode == DisplayMode.Utilization
            ? Math.Clamp(m.Utilization, 0, 1)
            : s.MaxAbsForceKn > 1e-9 ? Math.Abs(m.ForceKn) / s.MaxAbsForceKn : 0;
        var w = (float)(1.5 + 9 * share);
        // Hatching is how compression reads, so compression always gets a ribbon wide enough to hatch.
        return kind == Stroke.Over ? Math.Max(w, 8) : kind == Stroke.Compression ? Math.Max(w, 7) : w;
    }

    private static SKColor ColorOf(RenderSnapshot s, in RenderSnapshot.MemberItem m, Stroke kind)
    {
        var c = kind switch
        {
            Stroke.Over => SkiaPalette.Danger,
            Stroke.Unsolved => SkiaPalette.InkTertiary,
            _ => s.Mode == DisplayMode.Utilization ? SkiaPalette.Utilization(m.Utilization) : SkiaPalette.Accent,
        };
        return m.Dimmed ? c.WithAlpha(0.3) : c;
    }

    /// <summary>One member in its line style. Wide members are ribbons: open for tension, hatched for compression.</summary>
    private void DrawMember(SKCanvas canvas, SKPoint a, SKPoint b, Stroke kind, float width, SKColor color)
    {
        _stroke.Color = color;
        if (kind == Stroke.Zero)
        {
            _stroke.StrokeWidth = 1.1f;
            _stroke.PathEffect = _zeroDash;
            canvas.DrawLine(a, b, _stroke);
            _stroke.PathEffect = null;
            return;
        }
        if (width < 3.5f)
        {
            _stroke.StrokeWidth = kind == Stroke.Unsolved ? 1.6f : 1.4f;
            canvas.DrawLine(a, b, _stroke);
            return;
        }

        var d = new SKPoint(b.X - a.X, b.Y - a.Y);
        var len = MathF.Sqrt(d.X * d.X + d.Y * d.Y);
        if (len < 1) return;
        var nx = -d.Y / len * width / 2; var ny = d.X / len * width / 2;
        _path.Reset();
        _path.MoveTo(a.X + nx, a.Y + ny);
        _path.LineTo(b.X + nx, b.Y + ny);
        _path.LineTo(b.X - nx, b.Y - ny);
        _path.LineTo(a.X - nx, a.Y - ny);
        _path.Close();
        _fill.Color = kind == Stroke.Over ? SkiaPalette.DangerSoft : SkiaPalette.Canvas;
        canvas.DrawPath(_path, _fill);

        if (kind is Stroke.Compression or Stroke.Over)
        {
            // 45° hatch clipped to the ribbon.
            var bounds = _path.Bounds;
            canvas.Save();
            canvas.ClipPath(_path, SKClipOperation.Intersect, true);
            _stroke.StrokeWidth = 1f;
            for (var c = bounds.Left - bounds.Height; c < bounds.Right; c += 4.5f)
                canvas.DrawLine(c, bounds.Bottom, c + bounds.Height, bounds.Top, _stroke);
            canvas.Restore();
        }

        _stroke.StrokeWidth = 1.2f;
        _stroke.StrokeJoin = SKStrokeJoin.Miter;
        canvas.DrawPath(_path, _stroke);
        _stroke.StrokeJoin = SKStrokeJoin.Round;
    }

    private void DrawMembers(SKCanvas canvas, RenderSnapshot s)
    {
        foreach (ref readonly var m in s.Members.AsSpan())
        {
            if (!m.Selected && !m.Hovered && s.Overlay.HighlightMember != m.ElementId) continue;
            _stroke.StrokeWidth = RibbonWidth(s, m, StrokeOf(s, m)) + 12;
            _stroke.Color = m.Selected ? SkiaPalette.AccentSoft : SkiaPalette.AccentSoft.WithAlpha(0.6);
            canvas.DrawLine(m.A, m.B, _stroke);
        }
        foreach (ref readonly var m in s.Members.AsSpan())
        {
            var kind = StrokeOf(s, m);
            DrawMember(canvas, m.A, m.B, kind, RibbonWidth(s, m, kind), ColorOf(s, m, kind));
        }
    }

    /// <summary>Plain force numbers (member-forces layer) and red pills for members over capacity (over-capacity layer).</summary>
    private void DrawMemberLabels(SKCanvas canvas, RenderSnapshot s)
    {
        foreach (ref readonly var m in s.Members.AsSpan())
        {
            var pill = m.Overstressed && s.ShowOverCapacity;
            if (!pill && !s.ShowLabels) continue;
            var d = new SKPoint(m.B.X - m.A.X, m.B.Y - m.A.Y);
            var len = MathF.Sqrt(d.X * d.X + d.Y * d.Y);
            if (len < 60) continue;
            var mid = new SKPoint((m.A.X + m.B.X) / 2, (m.A.Y + m.B.Y) / 2);
            var nx = -d.Y / len; var ny = d.X / len;
            if (nx * (s.Center.X - mid.X) + ny * (s.Center.Y - mid.Y) > 0) { nx = -nx; ny = -ny; }
            var force = Math.Abs(m.ForceKn) < 0.05 ? "0.0" : (m.ForceKn >= 0 ? "+" : "−") + $"{Math.Abs(m.ForceKn):0.0}";
            var text = pill ? $"{force} · {m.Utilization * 100:0}%"
                : s.Mode == DisplayMode.Utilization ? $"{m.Utilization * 100:0}%" : force;
            var font = pill ? _monoBold : _monoSmall;
            var width = font.MeasureText(text);
            var off = RibbonWidth(s, m, StrokeOf(s, m)) / 2 + (pill ? 16 : 11);
            var cx = mid.X + nx * off; var cy = mid.Y + ny * off;
            var rect = new SKRect(cx - width / 2 - 6, cy - 8, cx + width / 2 + 6, cy + 8);
            _labelRects.Add(rect);
            if (pill)
            {
                _fill.Color = m.Dimmed ? SkiaPalette.DangerSoft.WithAlpha(0.5) : SkiaPalette.DangerSoft;
                canvas.DrawRoundRect(rect, 8, 8, _fill);
                _text.Color = m.Dimmed ? SkiaPalette.Danger.WithAlpha(0.45) : SkiaPalette.Danger;
            }
            else
            {
                _fill.Color = SkiaPalette.Canvas.WithAlpha(0.85);
                canvas.DrawRect(rect, _fill);
                _text.Color = m.Dimmed ? SkiaPalette.AccentFaint : SkiaPalette.Accent;
            }
            canvas.DrawText(text, cx - width / 2, cy + 3.8f, font, _text);
        }
    }

    // ---- part names: one leader-line label per part kind ----

    private void DrawPartNames(SKCanvas canvas, RenderSnapshot s)
    {
        if (s.Members.Length == 0) return;
        int top = -1, bottom = -1, king = -1;
        float bottomMinX = float.MaxValue, bottomMaxX = float.MinValue;
        foreach (ref readonly var m in s.Members.AsSpan())
            if (m.Part == Core.Geometry.PartKind.BottomChord) { bottomMinX = Math.Min(bottomMinX, Math.Min(m.A.X, m.B.X)); bottomMaxX = Math.Max(bottomMaxX, Math.Max(m.A.X, m.B.X)); }
        var bottomTarget = bottomMinX + (bottomMaxX - bottomMinX) * 0.6f;
        for (var i = 0; i < s.Members.Length; i++)
        {
            ref readonly var m = ref s.Members[i];
            var midX = (m.A.X + m.B.X) / 2;
            switch (m.Part)
            {
                case Core.Geometry.PartKind.TopChord:
                    if (top < 0 || midX < (s.Members[top].A.X + s.Members[top].B.X) / 2) top = i;
                    break;
                case Core.Geometry.PartKind.BottomChord:
                    if (bottom < 0 || Math.Abs(midX - bottomTarget) < Math.Abs((s.Members[bottom].A.X + s.Members[bottom].B.X) / 2 - bottomTarget)) bottom = i;
                    break;
                case Core.Geometry.PartKind.KingPost:
                    king = i;
                    break;
            }
        }

        if (top >= 0)
        {
            ref readonly var m = ref s.Members[top];
            // Near the member's upper end: the force pill sits at its midpoint.
            var anchor = m.A.Y < m.B.Y ? Lerp(m.B, m.A, 0.82f) : Lerp(m.A, m.B, 0.82f);
            Leader(canvas, new SKPoint(anchor.X - 150, anchor.Y), new SKPoint(anchor.X - 8, anchor.Y), "TOP CHORD", TextSide.Left);
        }
        if (king >= 0)
        {
            ref readonly var m = ref s.Members[king];
            var anchor = Lerp(m.A, m.B, 0.6f);
            Leader(canvas, new SKPoint(anchor.X + 22, anchor.Y), new SKPoint(anchor.X + 8, anchor.Y), "KING POST", TextSide.Right);
        }
        if (bottom >= 0)
        {
            ref readonly var m = ref s.Members[bottom];
            var anchor = Lerp(m.A, m.B, 0.5f);
            Leader(canvas, new SKPoint(anchor.X, anchor.Y + 60), new SKPoint(anchor.X, anchor.Y + 8), "BOTTOM CHORD", TextSide.Below);
        }
    }

    private enum TextSide { Left, Right, Below }

    /// <summary>A label at <paramref name="from"/> with a thin leader ending in an arrowhead at <paramref name="to"/>.</summary>
    private void Leader(SKCanvas canvas, SKPoint from, SKPoint to, string text, TextSide side)
    {
        var w = _monoSmall.MeasureText(text);
        var box = side switch
        {
            TextSide.Left => new SKRect(from.X - w - 8, from.Y - 7, from.X - 2, from.Y + 7),
            TextSide.Right => new SKRect(from.X + 4, from.Y - 7, from.X + w + 10, from.Y + 7),
            _ => new SKRect(from.X - w / 2 - 3, from.Y + 4, from.X + w / 2 + 3, from.Y + 18),
        };
        foreach (var r in _labelRects) if (r.IntersectsWith(box)) return;
        _labelRects.Add(box);
        _stroke.StrokeWidth = 0.9f;
        _stroke.Color = SkiaPalette.Accent;
        canvas.DrawLine(from, to, _stroke);
        var d = new SKPoint(to.X - from.X, to.Y - from.Y);
        var len = MathF.Sqrt(d.X * d.X + d.Y * d.Y);
        if (len > 1)
        {
            var ux = d.X / len; var uy = d.Y / len;
            _path.Reset();
            _path.MoveTo(to);
            _path.LineTo(to.X - ux * 6 - uy * 2.5f, to.Y - uy * 6 + ux * 2.5f);
            _path.LineTo(to.X - ux * 6 + uy * 2.5f, to.Y - uy * 6 - ux * 2.5f);
            _path.Close();
            _fill.Color = SkiaPalette.Accent;
            canvas.DrawPath(_path, _fill);
        }
        _text.Color = SkiaPalette.Accent;
        canvas.DrawText(text, box.Left + 3, box.MidY + 3.8f, _monoSmall, _text);
    }

    private static SKPoint Lerp(SKPoint a, SKPoint b, float t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

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

        var below = maxScreenY + (s.ShowReactions && s.Status == AnalysisStatus.Solved ? 128f : 60f);
        var chainY = below;
        var spanY = below + 30;

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
            _stroke.StrokeWidth = 0.8f;
            _stroke.Color = SkiaPalette.AccentFaint;
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
            var x = maxScreenX + 70;
            _stroke.StrokeWidth = 0.8f;
            _stroke.Color = SkiaPalette.AccentFaint;
            _stroke.PathEffect = _extDash;
            canvas.DrawLine(FindScreenX(s, maxY) + 12, topScreenY, x + 5, topScreenY, _stroke);
            canvas.DrawLine(maxScreenX + 18, sy(minY), x + 5, sy(minY), _stroke);
            _stroke.PathEffect = null;
            DimLine(canvas, x, topScreenY, x, sy(minY));
            Slash(canvas, x, topScreenY);
            Slash(canvas, x, sy(minY));
            var text = $"{maxY - minY:0.000}";
            var w = _monoSmall.MeasureText(text);
            canvas.Save();
            canvas.Translate(x - 6, (topScreenY + sy(minY)) / 2 + w / 2);
            canvas.RotateDegrees(-90);
            _text.Color = SkiaPalette.Accent;
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
        _stroke.Color = SkiaPalette.Accent.WithAlpha(0.75);
        canvas.DrawLine(x1, y1, x2, y2, _stroke);
    }

    private void Slash(SKCanvas canvas, float x, float y)
    {
        _stroke.StrokeWidth = 1.1f;
        _stroke.Color = SkiaPalette.Accent;
        canvas.DrawLine(x - 4, y + 4, x + 4, y - 4, _stroke);
    }

    private void DimText(SKCanvas canvas, string text, float cx, float baseline)
    {
        var w = _monoSmall.MeasureText(text);
        _text.Color = SkiaPalette.Accent;
        canvas.DrawText(text, cx - w / 2, baseline, _monoSmall, _text);
    }

    // ---- deflected shape ----

    private void DrawDeflectedShape(SKCanvas canvas, RenderSnapshot s)
    {
        _stroke.StrokeWidth = 1f;
        _stroke.Color = SkiaPalette.AccentFaint;
        _stroke.PathEffect = _deflectDash;
        foreach (ref readonly var m in s.Members.AsSpan()) canvas.DrawLine(m.DeflectedA, m.DeflectedB, _stroke);
        _stroke.PathEffect = null;
    }

    // ---- supports, loads, reactions ----

    private void DrawSupports(SKCanvas canvas, RenderSnapshot s)
    {
        foreach (ref readonly var n in s.Nodes.AsSpan())
        {
            if (n.Support == SupportKind.None) continue;
            var p = n.Screen;
            var ink = n.Dimmed ? SkiaPalette.Accent.WithAlpha(0.35) : SkiaPalette.Accent;
            canvas.Save();
            canvas.Translate(p.X, p.Y);
            if (n.Support == SupportKind.RollerX) canvas.RotateDegrees(90);
            _path.Reset();
            _path.MoveTo(0, 6);
            _path.LineTo(-11, 19);
            _path.LineTo(11, 19);
            _path.Close();
            _fill.Color = SkiaPalette.AccentSoft;
            canvas.DrawPath(_path, _fill);
            _stroke.StrokeWidth = 1.3f;
            _stroke.Color = ink;
            canvas.DrawPath(_path, _stroke);
            var groundY = 19f;
            if (n.Support == SupportKind.RollerY || n.Support == SupportKind.RollerX)
            {
                _stroke.StrokeWidth = 1.1f;
                canvas.DrawCircle(-5f, 22.5f, 3f, _stroke);
                canvas.DrawCircle(5f, 22.5f, 3f, _stroke);
                groundY = 26f;
            }
            _stroke.StrokeWidth = 1.2f;
            canvas.DrawLine(-15, groundY, 15, groundY, _stroke);
            _stroke.StrokeWidth = 0.9f;
            for (var x = -13f; x <= 13f; x += 4f) canvas.DrawLine(x, groundY, x - 4f, groundY + 5f, _stroke);
            canvas.Restore();

            if (s.ShowPartNames)
            {
                var name = n.Support == SupportKind.Pin ? "PIN" : n.Support == SupportKind.RollerX ? "ROLLER Y" : "ROLLER";
                var w = _monoSmall.MeasureText(name);
                var left = p.X < s.Center.X;
                var x = left ? p.X - 22 - w : p.X + 22;
                var box = new SKRect(x - 2, p.Y + 10, x + w + 2, p.Y + 24);
                var clear = true;
                foreach (var r in _labelRects) if (r.IntersectsWith(box)) { clear = false; break; }
                if (!clear) continue;
                _labelRects.Add(box);
                _text.Color = SkiaPalette.Accent;
                canvas.DrawText(name, x, p.Y + 21, _monoSmall, _text);
            }
        }
    }

    private void DrawLoads(SKCanvas canvas, RenderSnapshot s)
    {
        foreach (ref readonly var n in s.Nodes.AsSpan())
        {
            if (n.LoadKn.LengthSquared < 1e-12) continue;
            var color = n.Dimmed ? SkiaPalette.Accent.WithAlpha(0.35) : SkiaPalette.Accent;
            DrawArrow(canvas, n.LoadTail, n.Screen, 8f, color, 1.3f, n.Selected || n.Hovered, true);
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
                var head = new SKPoint(n.Screen.X, n.Screen.Y + dir * 32);
                DrawArrow(canvas, tail, head, 7f, SkiaPalette.Accent, 1.1f, false, false);
                var text = $"R {Math.Abs(n.ReactionKn.Y):0.0} kN";
                var w = _monoSmall.MeasureText(text);
                var y = dir > 0 ? tail.Y + 15 : tail.Y - 7;
                _labelRects.Add(new SKRect(tail.X - w / 2 - 3, y - 11, tail.X + w / 2 + 3, y + 3));
                _text.Color = SkiaPalette.Accent;
                canvas.DrawText(text, tail.X - w / 2, y, _monoSmall, _text);
            }
            if (n.Support.FixesX() && Math.Abs(n.ReactionKn.X) > 1e-6)
            {
                var dir = n.ReactionKn.X > 0 ? -1f : 1f;
                var tail = new SKPoint(n.Screen.X + dir * 76, n.Screen.Y);
                var head = new SKPoint(n.Screen.X + dir * 16, n.Screen.Y);
                DrawArrow(canvas, tail, head, 7f, SkiaPalette.Accent, 1.1f, false, false);
                DrawArrowLabel(canvas, tail, head, $"R {Math.Abs(n.ReactionKn.X):0.0} kN", SkiaPalette.Accent, s.Center);
            }
        }
    }

    private void DrawArrow(SKCanvas canvas, SKPoint tail, SKPoint head, float headSize, SKColor color, float width, bool emphasized, bool handle)
    {
        var d = new SKPoint(head.X - tail.X, head.Y - tail.Y);
        var len = MathF.Sqrt(d.X * d.X + d.Y * d.Y);
        if (len < 1) return;
        var ux = d.X / len; var uy = d.Y / len;
        var gap = handle ? 9f : 0f; // loads stop short of the node; reactions already start clear of the support
        var tip = new SKPoint(head.X - ux * gap, head.Y - uy * gap);
        _stroke.StrokeWidth = width;
        _stroke.Color = color;
        canvas.DrawLine(tail, new SKPoint(tip.X - ux * headSize * 0.6f, tip.Y - uy * headSize * 0.6f), _stroke);
        _path.Reset();
        _path.MoveTo(tip);
        _path.LineTo(tip.X - ux * headSize - uy * headSize * 0.4f, tip.Y - uy * headSize + ux * headSize * 0.4f);
        _path.LineTo(tip.X - ux * headSize + uy * headSize * 0.4f, tip.Y - uy * headSize - ux * headSize * 0.4f);
        _path.Close();
        _fill.Color = color;
        canvas.DrawPath(_path, _fill);
        if (!handle) return;
        // Tail handle: a hollow ring the user can drag to aim the load.
        _fill.Color = SkiaPalette.Canvas;
        canvas.DrawCircle(tail, 4f, _fill);
        _stroke.StrokeWidth = 1.4f;
        _stroke.Color = emphasized ? SkiaPalette.Accent : color;
        canvas.DrawCircle(tail, 4f, _stroke);
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

    // ---- nodes: blueprint bullseyes ----

    private void DrawNodes(SKCanvas canvas, RenderSnapshot s)
    {
        foreach (ref readonly var n in s.Nodes.AsSpan())
        {
            var p = n.Screen;
            var ink = n.Dimmed ? SkiaPalette.Accent.WithAlpha(0.35) : SkiaPalette.Accent;
            if (n.Selected)
            {
                _fill.Color = SkiaPalette.AccentSoft;
                canvas.DrawCircle(p, 15, _fill);
                _stroke.StrokeWidth = 1f;
                _stroke.Color = SkiaPalette.Accent.WithAlpha(0.6);
                _stroke.PathEffect = _haloDash;
                canvas.DrawCircle(p, 15, _stroke);
                _stroke.PathEffect = null;
            }
            else if (n.Hovered)
            {
                _fill.Color = SkiaPalette.AccentSoft;
                canvas.DrawCircle(p, 11, _fill);
            }
            _fill.Color = SkiaPalette.Canvas;
            canvas.DrawCircle(p, 5.8f, _fill);
            _stroke.StrokeWidth = n.Selected ? 1.8f : 1.4f;
            _stroke.Color = ink;
            canvas.DrawCircle(p, 5.8f, _stroke);
            _fill.Color = ink;
            canvas.DrawCircle(p, n.Selected ? 2.6f : 2f, _fill);
        }

        if (s.Overlay.SnapFlashNode is { } flash)
        {
            foreach (ref readonly var n in s.Nodes.AsSpan())
            {
                if (n.ElementId != flash) continue;
                _stroke.StrokeWidth = 1.5f;
                _stroke.Color = SkiaPalette.Accent.WithAlpha(0.8);
                canvas.DrawCircle(n.Screen, 12, _stroke);
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
            DrawArrow(canvas, lp.Tail, lp.Node, 8f, SkiaPalette.Accent, 1.3f, true, true);
            DrawArrowLabel(canvas, lp.Tail, lp.Node, $"{lp.MagnitudeKn:0.#} kN", SkiaPalette.Accent, s.Center);
        }

        if (o.GhostNode is { } ghost)
        {
            _fill.Color = SkiaPalette.AccentSoft;
            canvas.DrawCircle(ghost, 7f, _fill);
            _stroke.StrokeWidth = 1.5f;
            _stroke.Color = SkiaPalette.Accent;
            canvas.DrawCircle(ghost, 5.8f, _stroke);
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

    // ---- figure tags (on the frame) and the caption + legend ----

    private void DrawLegend(SKCanvas canvas, RenderSnapshot s)
    {
        // Frame tags, set vertically on the frame edges like a drawing sheet.
        _text.Color = SkiaPalette.Accent.WithAlpha(0.8);
        VerticalText(canvas, "FIG_001", FrameInset + 16, FrameInset + 12);
        if (s.Members.Length > 0)
        {
            var tag = $"[ {s.StructureKind.ToUpperInvariant()} · {s.Members.Length} {(s.Members.Length == 1 ? "MEMBER" : "MEMBERS")} ]";
            VerticalText(canvas, tag, s.Width - FrameInset - 18, FrameInset + 12);
        }
        VerticalText(canvas, "[ kN · m ]", s.Width - FrameInset - 18, s.Height - FrameInset - 12 - _monoSmall.MeasureText("[ kN · m ]"));

        if (s.Nodes.Length < 2 || s.Members.Length == 0) return;
        double minX = double.MaxValue, maxX = double.MinValue;
        foreach (ref readonly var n in s.Nodes.AsSpan()) { minX = Math.Min(minX, n.World.X); maxX = Math.Max(maxX, n.World.X); }
        var what = s.Mode == DisplayMode.Utilization ? "Utilization as a share of capacity." : "Axial force in kN, tension positive.";
        var caption = $"{s.StructureKind}, {maxX - minX:0.000} m span. {what}";

        var left = FrameInset + 24;
        _text.Color = SkiaPalette.InkSecondary;
        canvas.DrawText(caption, left, s.Height - FrameInset - 42, _ui, _text);

        var y = s.Height - FrameInset - 16;
        var x = left;
        x = LegendItem(canvas, x, y, Stroke.Tension, SkiaPalette.Accent, "TENSION");
        x = LegendItem(canvas, x, y, Stroke.Compression, SkiaPalette.Accent, "COMPRESSION");
        x = LegendItem(canvas, x, y, Stroke.Over, SkiaPalette.Danger, "OVER CAPACITY");
        x = LegendItem(canvas, x, y, Stroke.Zero, SkiaPalette.Accent, "ZERO FORCE");
        if (s.ShowDeflection)
        {
            _stroke.StrokeWidth = 1f;
            _stroke.Color = SkiaPalette.AccentFaint;
            _stroke.PathEffect = _deflectDash;
            canvas.DrawLine(x, y - 4, x + 26, y - 4, _stroke);
            _stroke.PathEffect = null;
            _text.Color = SkiaPalette.Accent;
            canvas.DrawText($"DEFLECTED ×{s.Exaggeration:0.0}", x + 34, y, _monoSmall, _text);
        }
    }

    private void VerticalText(SKCanvas canvas, string text, float x, float top)
    {
        canvas.Save();
        canvas.Translate(x, top);
        canvas.RotateDegrees(90);
        canvas.DrawText(text, 0, 0, _monoSmall, _text);
        canvas.Restore();
    }

    private float LegendItem(SKCanvas canvas, float x, float y, Stroke kind, SKColor color, string label)
    {
        DrawMember(canvas, new SKPoint(x, y - 4), new SKPoint(x + 26, y - 4), kind, kind == Stroke.Zero ? 0 : 8, color);
        _text.Color = kind == Stroke.Over ? SkiaPalette.Danger : SkiaPalette.Accent;
        canvas.DrawText(label, x + 34, y, _monoSmall, _text);
        return x + 34 + _monoSmall.MeasureText(label) + 26;
    }

    public void Dispose()
    {
        _fill.Dispose();
        _stroke.Dispose();
        _text.Dispose();
        _mono.Dispose();
        _monoSmall.Dispose();
        _monoBold.Dispose();
        _ui.Dispose();
        _dash.Dispose();
        _zeroDash.Dispose();
        _deflectDash.Dispose();
        _frameDash.Dispose();
        _extDash.Dispose();
        _haloDash.Dispose();
        _path.Dispose();
    }
}
