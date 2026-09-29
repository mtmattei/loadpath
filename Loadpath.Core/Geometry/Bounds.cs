namespace Loadpath.Core.Geometry;

/// <summary>Axis-aligned bounding box. Empty when Min > Max on any axis.</summary>
public readonly record struct Bounds(Vec2 Min, Vec2 Max)
{
    public static readonly Bounds Empty = new(new Vec2(double.PositiveInfinity, double.PositiveInfinity), new Vec2(double.NegativeInfinity, double.NegativeInfinity));

    public bool IsEmpty => Min.X > Max.X || Min.Y > Max.Y;
    public double Width => Max.X - Min.X;
    public double Height => Max.Y - Min.Y;
    public Vec2 Center => (Min + Max) / 2;

    public static Bounds FromPoints(Vec2 a, Vec2 b) => new(
        new Vec2(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y)),
        new Vec2(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y)));

    public Bounds Include(Vec2 p) => new(
        new Vec2(Math.Min(Min.X, p.X), Math.Min(Min.Y, p.Y)),
        new Vec2(Math.Max(Max.X, p.X), Math.Max(Max.Y, p.Y)));

    public Bounds Inflate(double amount) => new(Min - new Vec2(amount, amount), Max + new Vec2(amount, amount));

    public bool Contains(Vec2 p) => p.X >= Min.X && p.X <= Max.X && p.Y >= Min.Y && p.Y <= Max.Y;

    /// <summary>True when the segment a-b touches the box (either endpoint inside, or it crosses an edge).</summary>
    public bool IntersectsSegment(Vec2 a, Vec2 b)
    {
        if (Contains(a) || Contains(b)) return true;
        // Liang–Barsky clipping.
        double t0 = 0, t1 = 1;
        var d = b - a;
        double[] p = [-d.X, d.X, -d.Y, d.Y];
        double[] q = [a.X - Min.X, Max.X - a.X, a.Y - Min.Y, Max.Y - a.Y];
        for (var i = 0; i < 4; i++)
        {
            if (Math.Abs(p[i]) < 1e-12)
            {
                if (q[i] < 0) return false;
                continue;
            }
            var r = q[i] / p[i];
            if (p[i] < 0) { if (r > t1) return false; if (r > t0) t0 = r; }
            else { if (r < t0) return false; if (r < t1) t1 = r; }
        }
        return t0 <= t1;
    }
}
