using SkiaSharp;

namespace Loadpath.Workspace;

/// <summary>The token palette mirrored for Skia. Keep in sync with Themes/Tokens.xaml.</summary>
public static class SkiaPalette
{
    public static readonly SKColor Canvas = SKColor.Parse("#F7F8FB");
    public static readonly SKColor Ink = SKColor.Parse("#15171A");
    public static readonly SKColor InkSecondary = SKColor.Parse("#4D535C");
    public static readonly SKColor InkTertiary = SKColor.Parse("#8B919A");
    /// <summary>Blueprint ink: the structure, its annotations and the accent.</summary>
    public static readonly SKColor Accent = SKColor.Parse("#3446D6");
    public static readonly SKColor AccentSoft = SKColor.Parse("#E5E8FA");
    /// <summary>Construction lines: frame, grid marks, extension lines.</summary>
    public static readonly SKColor AccentFaint = SKColor.Parse("#B9C1E6");
    public static readonly SKColor GridMark = SKColor.Parse("#CDD3EC");
    public static readonly SKColor Danger = SKColor.Parse("#D42A2A");
    public static readonly SKColor DangerSoft = SKColor.Parse("#FBE2E2");
    public static readonly SKColor Warn = SKColor.Parse("#A86A12");

    public static SKColor WithAlpha(this SKColor c, double alpha) => c.WithAlpha((byte)Math.Clamp(alpha * 255, 0, 255));

    /// <summary>Utilization ramp on paper: blueprint → amber → red, saturating at 1.</summary>
    public static SKColor Utilization(double u)
    {
        u = Math.Clamp(u, 0, 1);
        return u < 0.5 ? Lerp(Accent, Warn, u / 0.5) : Lerp(Warn, Danger, (u - 0.5) / 0.5);
    }

    public static SKColor Lerp(SKColor a, SKColor b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return new SKColor(
            (byte)(a.Red + (b.Red - a.Red) * t),
            (byte)(a.Green + (b.Green - a.Green) * t),
            (byte)(a.Blue + (b.Blue - a.Blue) * t),
            (byte)(a.Alpha + (b.Alpha - a.Alpha) * t));
    }
}
