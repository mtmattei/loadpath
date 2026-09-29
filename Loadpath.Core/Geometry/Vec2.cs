namespace Loadpath.Core.Geometry;

/// <summary>Immutable 2D vector in world units (meters) or screen units (pixels), by context.</summary>
public readonly record struct Vec2(double X, double Y)
{
    public static readonly Vec2 Zero = new(0, 0);

    public double Length => Math.Sqrt(X * X + Y * Y);
    public double LengthSquared => X * X + Y * Y;

    public Vec2 Normalized()
    {
        var len = Length;
        return len < 1e-12 ? Zero : new Vec2(X / len, Y / len);
    }

    public double Dot(Vec2 other) => X * other.X + Y * other.Y;
    public double Cross(Vec2 other) => X * other.Y - Y * other.X;
    public Vec2 Perp() => new(-Y, X);
    public double DistanceTo(Vec2 other) => (this - other).Length;

    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec2 operator -(Vec2 a) => new(-a.X, -a.Y);
    public static Vec2 operator *(Vec2 a, double s) => new(a.X * s, a.Y * s);
    public static Vec2 operator *(double s, Vec2 a) => new(a.X * s, a.Y * s);
    public static Vec2 operator /(Vec2 a, double s) => new(a.X / s, a.Y / s);

    public static Vec2 Lerp(Vec2 a, Vec2 b, double t) => a + (b - a) * t;

    /// <summary>Distance from a point to a line segment.</summary>
    public static double DistanceToSegment(Vec2 p, Vec2 a, Vec2 b, out double t)
    {
        var ab = b - a;
        var lenSq = ab.LengthSquared;
        if (lenSq < 1e-18)
        {
            t = 0;
            return p.DistanceTo(a);
        }
        t = Math.Clamp((p - a).Dot(ab) / lenSq, 0, 1);
        return p.DistanceTo(a + ab * t);
    }

    public override string ToString() => $"({X:0.###}, {Y:0.###})";
}
