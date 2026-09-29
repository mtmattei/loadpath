namespace Loadpath.Core.Geometry;

/// <summary>Adaptive grid: picks a minor step so lines stay between 8 and ~40 px apart.</summary>
public static class GridSteps
{
    private static readonly double[] Steps = [0.01, 0.02, 0.05, 0.1, 0.2, 0.5, 1, 2, 5, 10, 20, 50, 100];

    /// <summary>Returns (minor, major) world steps for the given pixels-per-meter scale.</summary>
    public static (double Minor, double Major) For(double pixelsPerMeter, double minMinorPx = 12)
    {
        foreach (var s in Steps)
        {
            if (s * pixelsPerMeter >= minMinorPx) return (s, s * (s is 0.02 or 0.2 or 2 or 20 ? 5 : s is 0.05 or 0.5 or 5 or 50 ? 2 : 10));
        }
        return (100, 1000);
    }
}
