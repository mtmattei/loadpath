using Loadpath.Core.Analysis;
using Loadpath.Core.Geometry;
using Loadpath.Presentation;
using SkiaSharp;

namespace Loadpath.Workspace;

/// <summary>
/// Draws one RenderSnapshot. All paints are allocated once. Order: canvas, grid, axes, deflected ghost,
/// members (force ribbons), labels, loads, supports, reactions, nodes, tool overlay.
/// </summary>
public sealed class WorkspaceRenderer : IDisposable
{
    private readonly SKPaint _fill = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _stroke = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
    private readonly SKPaint _hairline = new() { IsAntialias = false, Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
    private readonly SKPaint _text = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKFont _mono;
    private readonly SKFont _monoSmall;
    private readonly SKPathEffect _dash = SKPathEffect.CreateDash([4, 4], 0);
    private readonly SKPathEffect _ghostDash = SKPathEffect.CreateDash([6, 5], 0);
    private readonly SKPath _path = new();

    public WorkspaceRenderer()
    {
        var typeface = LoadTypeface("JetBrainsMono-Medium.ttf") ?? SKTypeface.FromFamilyName("monospace") ?? SKTypeface.Default;
        _mono = new SKFont(typeface, 11) { Edging = SKFontEdging.SubpixelAntialias, Subpixel = true };
        _monoSmall = new SKFont(typeface, 10) { Edging = SKFontEdging.SubpixelAntialias, Subpixel = true };
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
        if (s.ShowGrid) DrawGrid(canvas, s);
        if (s.ShowDeflection && s.Status == AnalysisStatus.Solved) DrawDeflectedGhost(canvas, s);
        DrawMembers(canvas, s);
        if (s.ShowLabels && s.Status == AnalysisStatus.Solved) DrawMemberLabels(canvas, s);
        DrawSupports(canvas, s);
        DrawLoads(canvas, s);
        if (s.ShowReactions && s.Status == AnalysisStatus.Solved) DrawReactions(canvas, s);
        DrawNodes(canvas, s);
        DrawOverlay(canvas, s);
    }

    // ---- grid ----

    private void DrawGrid(SKCanvas canvas, RenderSnapshot s)
    {
        var minorPx = (float)(s.GridMinor * s.Scale);
        var majorPx = (float)(s.GridMajor * s.Scale);
        DrawGridLines(canvas, s, minorPx, SkiaPalette.White.WithAlpha(0.035));
        DrawGridLines(canvas, s, majorPx, SkiaPalette.White.WithAlpha(0.075));
        // Axes through the origin, a touch stronger.
        _hairline.Color = SkiaPalette.White.WithAlpha(0.14);
        if (s.Origin.X >= 0 && s.Origin.X <= s.Width) canvas.DrawLine(s.Origin.X, 0, s.Origin.X, s.Height, _hairline);
        if (s.Origin.Y >= 0 && s.Origin.Y <= s.Height) canvas.DrawLine(0, s.Origin.Y, s.Width, s.Origin.Y, _hairline);
    }

    private void DrawGridLines(SKCanvas canvas, RenderSnapshot s, float stepPx, SKColor color)
    {
        if (stepPx < 4) return;
        _hairline.Color = color;
        var startX = s.Origin.X % stepPx;
        for (var x = startX; x <= s.Width; x += stepPx) canvas.DrawLine(MathF.Round(x) + 0.5f, 0, MathF.Round(x) + 0.5f, s.Height, _hairline);
        var startY = s.Origin.Y % stepPx;
        for (var y = startY; y <= s.Height; y += stepPx) canvas.DrawLine(0, MathF.Round(y) + 0.5f, s.Width, MathF.Round(y) + 0.5f, _hairline);
    }

    // ---- members ----

    private float RibbonWidth(RenderSnapshot s, in RenderSnapshot.MemberItem m)
    {
        if (s.Status != AnalysisStatus.Solved) return 2f;
        if (s.Mode == DisplayMode.Utilization) return (float)(2.5 + 7 * Math.Clamp(m.Utilization, 0, 1));
        var rel = s.MaxAbsForceKn > 1e-9 ? Math.Abs(m.ForceKn) / s.MaxAbsForceKn : 0;
        return (float)(2 + 10 * rel);
    }

    private SKColor RibbonColor(RenderSnapshot s, in RenderSnapshot.MemberItem m)
    {
        if (s.Status != AnalysisStatus.Solved) return SkiaPalette.NeutralMember;
        if (m.Overstressed) return SkiaPalette.Danger;
        if (s.Mode == DisplayMode.Utilization) return SkiaPalette.Utilization(m.Utilization);
        if (m.ForceKn > 1e-6) return SkiaPalette.Tension;
        if (m.ForceKn < -1e-6) return SkiaPalette.Compression;
        return SkiaPalette.NeutralMember;
    }

    private void DrawMembers(SKCanvas canvas, RenderSnapshot s)
    {
        // Selection halos first so ribbons sit on top of them.
        foreach (ref readonly var m in s.Members.AsSpan())
        {
            if (!m.Selected && !m.Hovered && s.Overlay.HighlightMember != m.ElementId) continue;
            var w = RibbonWidth(s, m);
            _stroke.StrokeWidth = w + 8;
            _stroke.Color = m.Selected ? SkiaPalette.Accent.WithAlpha(0.35) : SkiaPalette.Ink.WithAlpha(0.18);
            canvas.DrawLine(m.A, m.B, _stroke);
        }

        foreach (ref readonly var m in s.Members.AsSpan())
        {
            var w = RibbonWidth(s, m);
            var color = RibbonColor(s, m);
            if (m.Dimmed) color = color.WithAlpha(0.45);
            _stroke.StrokeWidth = w;
            _stroke.Color = color;
            canvas.DrawLine(m.A, m.B, _stroke);

            // Compression carries a dark core line: sign is never color-alone.
            if (s.Status == AnalysisStatus.Solved && m.ForceKn < -1e-6 && w >= 4)
            {
                _stroke.StrokeWidth = Math.Max(1f, w * 0.22f);
                _stroke.Color = SkiaPalette.Canvas.WithAlpha(m.Dimmed ? 0.35 : 0.7);
                canvas.DrawLine(m.A, m.B, _stroke);
            }

            // Overstressed: hatch ticks across the ribbon.
            if (m.Overstressed)
            {
                var d = new SKPoint(m.B.X - m.A.X, m.B.Y - m.A.Y);
                var len = MathF.Sqrt(d.X * d.X + d.Y * d.Y);
                if (len > 12)
                {
                    var ux = d.X / len; var uy = d.Y / len;
                    var nx = -uy * (w / 2 + 3); var ny = ux * (w / 2 + 3);
                    _stroke.StrokeWidth = 1.5f;
                    _stroke.Color = SkiaPalette.Danger.WithAlpha(0.9);
                    for (var t = 10f; t < len - 6; t += 10f)
                    {
                        var px = m.A.X + ux * t; var py = m.A.Y + uy * t;
                        canvas.DrawLine(px - nx - ux * 3, py - ny - uy * 3, px + nx + ux * 3, py + ny + uy * 3, _stroke);
                    }
                }
            }
        }
    }

    private void DrawMemberLabels(SKCanvas canvas, RenderSnapshot s)
    {
        foreach (ref readonly var m in s.Members.AsSpan())
        {
            if (Math.Abs(m.ForceKn) < 0.005 && !m.Selected) continue;
            var d = new SKPoint(m.B.X - m.A.X, m.B.Y - m.A.Y);
            var len = MathF.Sqrt(d.X * d.X + d.Y * d.Y);
            if (len < 70) continue;
            var mid = new SKPoint((m.A.X + m.B.X) / 2, (m.A.Y + m.B.Y) / 2);
            var nx = -d.Y / len; var ny = d.X / len;
            // Push the label away from the structure's center so labels fan outward instead of colliding at joints.
            var toCenterX = s.Center.X - mid.X; var toCenterY = s.Center.Y - mid.Y;
            if (nx * toCenterX + ny * toCenterY > 0) { nx = -nx; ny = -ny; }
            var off = RibbonWidth(s, m) / 2 + 9;
            var text = s.Mode == DisplayMode.Utilization
                ? $"{m.Utilization * 100:0}%"
                : (m.ForceKn >= 0 ? "+" : "−") + $"{Math.Abs(m.ForceKn):0.0}";
            var width = _monoSmall.MeasureText(text);
            var cx = mid.X + nx * off; var cy = mid.Y + ny * off;
            var rect = new SKRect(cx - width / 2 - 4, cy - 7, cx + width / 2 + 4, cy + 7);
            _fill.Color = SkiaPalette.Canvas.WithAlpha(0.82);
            canvas.DrawRoundRect(rect, 3, 3, _fill);
            _text.Color = m.Dimmed ? SkiaPalette.InkTertiary : (m.Selected ? SkiaPalette.Ink : SkiaPalette.InkSecondary);
            canvas.DrawText(text, cx - width / 2, cy + 3.5f, _monoSmall, _text);
        }
    }

    // ---- deflected ghost ----

    private void DrawDeflectedGhost(SKCanvas canvas, RenderSnapshot s)
    {
        _stroke.StrokeWidth = 1.2f;
        _stroke.Color = SkiaPalette.Ink.WithAlpha(0.28);
        _stroke.PathEffect = _ghostDash;
        foreach (ref readonly var m in s.Members.AsSpan()) canvas.DrawLine(m.DeflectedA, m.DeflectedB, _stroke);
        _stroke.PathEffect = null;
        _fill.Color = SkiaPalette.Ink.WithAlpha(0.35);
        foreach (ref readonly var n in s.Nodes.AsSpan()) canvas.DrawCircle(n.Deflected, 2.2f, _fill);
    }

    // ---- supports, loads, reactions ----

    private void DrawSupports(SKCanvas canvas, RenderSnapshot s)
    {
        foreach (ref readonly var n in s.Nodes.AsSpan())
        {
            if (n.Support == SupportKind.None) continue;
            var p = n.Screen;
            var ground = n.Dimmed ? SkiaPalette.Ground.WithAlpha(0.5) : SkiaPalette.Ground;
            var edge = n.Dimmed ? SkiaPalette.InkSecondary.WithAlpha(0.5) : SkiaPalette.InkSecondary;
            canvas.Save();
            canvas.Translate(p.X, p.Y);
            if (n.Support == SupportKind.RollerX) canvas.RotateDegrees(90);
            // Triangle under the node.
            _path.Reset();
            _path.MoveTo(0, 4);
            _path.LineTo(-9, 17);
            _path.LineTo(9, 17);
            _path.Close();
            _fill.Color = SkiaPalette.Surface;
            canvas.DrawPath(_path, _fill);
            _stroke.StrokeWidth = 1.5f;
            _stroke.Color = edge;
            canvas.DrawPath(_path, _stroke);
            var groundY = 17f;
            if (n.Support == SupportKind.RollerY || n.Support == SupportKind.RollerX)
            {
                _stroke.StrokeWidth = 1.3f;
                canvas.DrawCircle(-4.5f, 20f, 2.6f, _stroke);
                canvas.DrawCircle(4.5f, 20f, 2.6f, _stroke);
                groundY = 23.5f;
            }
            // Ground line with hatching.
            _stroke.StrokeWidth = 1.5f;
            _stroke.Color = ground;
            canvas.DrawLine(-13, groundY, 13, groundY, _stroke);
            _stroke.StrokeWidth = 1f;
            for (var x = -11f; x <= 11f; x += 5.5f) canvas.DrawLine(x, groundY, x - 3.5f, groundY + 4.5f, _stroke);
            canvas.Restore();
        }
    }

    private void DrawLoads(SKCanvas canvas, RenderSnapshot s)
    {
        foreach (ref readonly var n in s.Nodes.AsSpan())
        {
            if (n.LoadKn.LengthSquared < 1e-12) continue;
            var color = n.Dimmed ? SkiaPalette.Ink.WithAlpha(0.4) : SkiaPalette.Ink;
            DrawArrow(canvas, n.LoadTail, n.Screen, 7f, color, 1.8f, n.Selected || n.Hovered);
            var label = $"{n.LoadKn.Length:0.#} kN";
            DrawArrowLabel(canvas, n.LoadTail, n.Screen, label, color, s.Center);
        }
    }

    private void DrawReactions(SKCanvas canvas, RenderSnapshot s)
    {
        foreach (ref readonly var n in s.Nodes.AsSpan())
        {
            if (n.Support == SupportKind.None || n.ReactionKn.LengthSquared < 1e-8) continue;
            var color = SkiaPalette.Ground;
            if (n.Support.FixesY() && Math.Abs(n.ReactionKn.Y) > 1e-6)
            {
                var len = (float)HitTester.LoadArrowLengthPx(Math.Abs(n.ReactionKn.Y)) * 0.8f;
                var dir = n.ReactionKn.Y > 0 ? 1f : -1f; // positive Y reaction pushes up on screen (−y)
                var tail = new SKPoint(n.Screen.X, n.Screen.Y + dir * (len + 26));
                var head = new SKPoint(n.Screen.X, n.Screen.Y + dir * 26);
                DrawArrow(canvas, tail, head, 6f, SkiaPalette.InkSecondary, 1.5f, false);
                DrawArrowLabel(canvas, tail, head, $"{Math.Abs(n.ReactionKn.Y):0.#}", SkiaPalette.InkSecondary, s.Center);
            }
            if (n.Support.FixesX() && Math.Abs(n.ReactionKn.X) > 1e-6)
            {
                var len = (float)HitTester.LoadArrowLengthPx(Math.Abs(n.ReactionKn.X)) * 0.8f;
                var dir = n.ReactionKn.X > 0 ? -1f : 1f;
                var tail = new SKPoint(n.Screen.X + dir * (len + 14), n.Screen.Y);
                var head = new SKPoint(n.Screen.X + dir * 14, n.Screen.Y);
                DrawArrow(canvas, tail, head, 6f, SkiaPalette.InkSecondary, 1.5f, false);
                DrawArrowLabel(canvas, tail, head, $"{Math.Abs(n.ReactionKn.X):0.#}", SkiaPalette.InkSecondary, s.Center);
            }
            _ = color;
        }
    }

    private void DrawArrow(SKCanvas canvas, SKPoint tail, SKPoint head, float headSize, SKColor color, float width, bool emphasized)
    {
        var d = new SKPoint(head.X - tail.X, head.Y - tail.Y);
        var len = MathF.Sqrt(d.X * d.X + d.Y * d.Y);
        if (len < 1) return;
        var ux = d.X / len; var uy = d.Y / len;
        var gap = 7f; // stop short of the node
        var tip = new SKPoint(head.X - ux * gap, head.Y - uy * gap);
        _stroke.StrokeWidth = width;
        _stroke.Color = color;
        canvas.DrawLine(tail, new SKPoint(tip.X - ux * headSize * 0.6f, tip.Y - uy * headSize * 0.6f), _stroke);
        _path.Reset();
        _path.MoveTo(tip);
        _path.LineTo(tip.X - ux * headSize - uy * headSize * 0.55f, tip.Y - uy * headSize + ux * headSize * 0.55f);
        _path.LineTo(tip.X - ux * headSize + uy * headSize * 0.55f, tip.Y - uy * headSize - ux * headSize * 0.55f);
        _path.Close();
        _fill.Color = color;
        canvas.DrawPath(_path, _fill);
        // Tail handle.
        _fill.Color = emphasized ? SkiaPalette.Accent : color;
        canvas.DrawCircle(tail, emphasized ? 4f : 3f, _fill);
    }

    private void DrawArrowLabel(SKCanvas canvas, SKPoint tail, SKPoint head, string text, SKColor color, SKPoint center)
    {
        var d = new SKPoint(head.X - tail.X, head.Y - tail.Y);
        var len = MathF.Sqrt(d.X * d.X + d.Y * d.Y);
        if (len < 1) return;
        var ux = d.X / len; var uy = d.Y / len;
        var width = _monoSmall.MeasureText(text);
        // Beside the shaft near the head, on the side away from the structure: clear of member labels at the midpoints.
        var px = -uy; var py = ux;
        if (px * (center.X - head.X) + py * (center.Y - head.Y) > 0) { px = -px; py = -py; }
        var cx = head.X - ux * 16 + px * 8;
        var cy = head.Y - uy * 16 + py * 8;
        var lx = px > 0.3f ? cx : px < -0.3f ? cx - width : cx - width / 2;
        var ly = cy + 3.5f;
        _text.Color = color.WithAlpha(0.9);
        canvas.DrawText(text, lx, ly, _monoSmall, _text);
    }

    // ---- nodes ----

    private void DrawNodes(SKCanvas canvas, RenderSnapshot s)
    {
        foreach (ref readonly var n in s.Nodes.AsSpan())
        {
            var p = n.Screen;
            if (n.Hovered && !n.Selected)
            {
                _fill.Color = SkiaPalette.Ink.WithAlpha(0.18);
                canvas.DrawCircle(p, 10, _fill);
            }
            if (n.Selected)
            {
                _stroke.StrokeWidth = 1.5f;
                _stroke.Color = SkiaPalette.Accent;
                canvas.DrawCircle(p, 8, _stroke);
                _fill.Color = SkiaPalette.Accent;
                canvas.DrawCircle(p, 4.5f, _fill);
                _fill.Color = SkiaPalette.Ink;
                canvas.DrawCircle(p, 1.6f, _fill);
                continue;
            }
            _fill.Color = SkiaPalette.Canvas;
            canvas.DrawCircle(p, 5.5f, _fill);
            _fill.Color = n.Dimmed ? SkiaPalette.Ink.WithAlpha(0.5) : SkiaPalette.Ink;
            canvas.DrawCircle(p, 4f, _fill);
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
            _stroke.Color = SkiaPalette.Accent.WithAlpha(0.55);
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
            _stroke.StrokeWidth = 2f;
            _stroke.Color = SkiaPalette.Accent.WithAlpha(0.8);
            canvas.DrawLine(band.From, band.To, _stroke);
        }

        if (o.LoadPreview is { } lp)
        {
            DrawArrow(canvas, lp.Tail, lp.Node, 7f, SkiaPalette.Accent, 1.8f, true);
            DrawArrowLabel(canvas, lp.Tail, lp.Node, $"{lp.MagnitudeKn:0.#} kN", SkiaPalette.Accent, s.Center);
        }

        if (o.GhostNode is { } ghost)
        {
            _stroke.StrokeWidth = 1.5f;
            _stroke.Color = SkiaPalette.Accent;
            canvas.DrawCircle(ghost, 5f, _stroke);
            _fill.Color = SkiaPalette.Accent.WithAlpha(0.35);
            canvas.DrawCircle(ghost, 5f, _fill);
        }

        if (o.Marquee is { } r)
        {
            _fill.Color = SkiaPalette.Accent.WithAlpha(0.08);
            canvas.DrawRect(r, _fill);
            _stroke.StrokeWidth = 1f;
            _stroke.Color = SkiaPalette.Accent.WithAlpha(0.9);
            canvas.DrawRect(r, _stroke);
        }
    }

    public void Dispose()
    {
        _fill.Dispose();
        _stroke.Dispose();
        _hairline.Dispose();
        _text.Dispose();
        _mono.Dispose();
        _monoSmall.Dispose();
        _dash.Dispose();
        _ghostDash.Dispose();
        _path.Dispose();
    }
}
