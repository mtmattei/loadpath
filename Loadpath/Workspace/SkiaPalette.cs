using SkiaSharp;

namespace Loadpath.Workspace;

/// <summary>The token palette mirrored for Skia. Keep in sync with Themes/Tokens.xaml.</summary>
public static class SkiaPalette
{
    public static readonly SKColor Canvas = SKColor.Parse("#0F1216");
    public static readonly SKColor Surface = SKColor.Parse("#151A20");
    public static readonly SKColor SurfaceRaised = SKColor.Parse("#1B212A");
    public static readonly SKColor Ink = SKColor.Parse("#E7EBF0");
    public static readonly SKColor InkSecondary = SKColor.Parse("#98A2B3");
    public static readonly SKColor InkTertiary = SKColor.Parse("#5F6B7A");
    public static readonly SKColor Accent = SKColor.Parse("#7DB1FF");
    public static readonly SKColor Tension = SKColor.Parse("#D9924A");
    public static readonly SKColor Compression = SKColor.Parse("#5D9AD6");
    public static readonly SKColor NeutralMember = SKColor.Parse("#8791A0");
    public static readonly SKColor Danger = SKColor.Parse("#F25F5C");
    public static readonly SKColor Ok = SKColor.Parse("#4FB286");
    public static readonly SKColor Warn = SKColor.Parse("#D9C24A");
    public static readonly SKColor Ground = SKColor.Parse("#3A424D");
    public static readonly SKColor White = SKColors.White;

    public static SKColor WithAlpha(this SKColor c, double alpha) => c.WithAlpha((byte)Math.Clamp(alpha * 255, 0, 255));

    /// <summary>Utilization ramp: ok → warn → danger, saturating at 1.</summary>
    public static SKColor Utilization(double u)
    {
        u = Math.Clamp(u, 0, 1);
        return u < 0.5 ? Lerp(Ok, Warn, u / 0.5) : Lerp(Warn, Danger, (u - 0.5) / 0.5);
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
