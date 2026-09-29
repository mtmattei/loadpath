using Windows.UI.ViewManagement;

namespace Loadpath.Workspace;

/// <summary>Reduced-motion gate and the house curves as functions, for code-driven animation.</summary>
public static class MotionSettings
{
    private static bool? _enabled;

    public static bool AnimationsEnabled
    {
        get
        {
            if (_enabled is null)
            {
                try { _enabled = new UISettings().AnimationsEnabled; } catch { _enabled = true; }
            }
            return _enabled.Value;
        }
    }

    /// <summary>Cubic bezier (0.66,0 0.34,1): EaseInOut.</summary>
    public static double EaseInOut(double t) => Bezier(t, 0.66, 0, 0.34, 1);

    /// <summary>Cubic bezier (0.22,1 0.36,1): EaseSmooth.</summary>
    public static double EaseSmooth(double t) => Bezier(t, 0.22, 1, 0.36, 1);

    private static double Bezier(double t, double x1, double y1, double x2, double y2)
    {
        if (t <= 0) return 0;
        if (t >= 1) return 1;
        // Solve x(u) = t by bisection, then return y(u).
        double lo = 0, hi = 1, u = t;
        for (var i = 0; i < 24; i++)
        {
            u = (lo + hi) / 2;
            var x = 3 * (1 - u) * (1 - u) * u * x1 + 3 * (1 - u) * u * u * x2 + u * u * u;
            if (x < t) lo = u; else hi = u;
        }
        return 3 * (1 - u) * (1 - u) * u * y1 + 3 * (1 - u) * u * u * y2 + u * u * u;
    }
}
