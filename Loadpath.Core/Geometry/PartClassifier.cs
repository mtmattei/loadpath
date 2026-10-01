namespace Loadpath.Core.Geometry;

public enum PartKind { Web, BottomChord, TopChord, Post, KingPost }

/// <summary>
/// Names truss parts from geometry alone, for drawing annotations. A heuristic, not structural analysis:
/// bottom chord = members on the lowest level; top chord = non-vertical members with nothing above them;
/// posts = vertical members, and the one reaching the apex is the king post; everything else is web.
/// </summary>
public static class PartClassifier
{
    private const double Eps = 1e-6;

    public static PartKind[] Classify(IReadOnlyList<(Vec2 A, Vec2 B)> members)
    {
        var kinds = new PartKind[members.Count];
        if (members.Count == 0) return kinds;

        double minY = double.MaxValue, maxY = double.MinValue;
        foreach (var (a, b) in members)
        {
            minY = Math.Min(minY, Math.Min(a.Y, b.Y));
            maxY = Math.Max(maxY, Math.Max(a.Y, b.Y));
        }

        var apexes = new HashSet<(long, long)>();
        foreach (var (a, b) in members)
        {
            if (Math.Abs(a.Y - maxY) < Eps) apexes.Add((Key(a.X), Key(a.Y)));
            if (Math.Abs(b.Y - maxY) < Eps) apexes.Add((Key(b.X), Key(b.Y)));
        }
        var apexCount = apexes.Count;

        for (var i = 0; i < members.Count; i++)
        {
            var (a, b) = members[i];
            if (Math.Abs(a.Y - minY) < Eps && Math.Abs(b.Y - minY) < Eps && Math.Abs(a.X - b.X) > Eps)
            {
                kinds[i] = PartKind.BottomChord;
            }
            else if (Math.Abs(a.X - b.X) < Eps)
            {
                // A king post rises to a single apex; under a level top chord every vertical is just a post.
                kinds[i] = apexCount == 1 && Math.Abs(Math.Max(a.Y, b.Y) - maxY) < Eps && maxY - minY > Eps ? PartKind.KingPost : PartKind.Post;
            }
            else if (NothingAbove(members, i))
            {
                kinds[i] = PartKind.TopChord;
            }
        }

        // A flat structure has no top chord: everything on one level is bottom chord already.
        return kinds;
    }

    /// <summary>"Roof truss" when the top chord pitches both ways, "Cantilever truss" when the supports bunch at one end.</summary>
    public static string Describe(IReadOnlyList<(Vec2 A, Vec2 B)> members, IReadOnlyList<PartKind> kinds, IReadOnlyList<Vec2> supports)
    {
        if (members.Count == 0) return "Structure";
        bool rises = false, falls = false, flatTop = false;
        double minX = double.MaxValue, maxX = double.MinValue;
        for (var i = 0; i < members.Count; i++)
        {
            var (a, b) = members[i];
            minX = Math.Min(minX, Math.Min(a.X, b.X));
            maxX = Math.Max(maxX, Math.Max(a.X, b.X));
            if (kinds[i] != PartKind.TopChord) continue;
            var (l, r) = a.X <= b.X ? (a, b) : (b, a);
            if (r.Y - l.Y > Eps) rises = true;
            if (l.Y - r.Y > Eps) falls = true;
            if (Math.Abs(r.Y - l.Y) < Eps) flatTop = true;
        }
        // A Warren's end diagonals also rise and fall; a roof has no level top chord.
        if (rises && falls && !flatTop) return "Roof truss";
        if (supports.Count > 0 && maxX - minX > Eps)
        {
            var sMin = supports.Min(p => p.X);
            var sMax = supports.Max(p => p.X);
            if ((sMax - sMin) / (maxX - minX) < 0.25) return "Cantilever truss";
        }
        return "Truss";
    }

    private static long Key(double v) => (long)Math.Round(v / Eps);

    private static bool NothingAbove(IReadOnlyList<(Vec2 A, Vec2 B)> members, int index)
    {
        var (a, b) = members[index];
        var x = (a.X + b.X) / 2;
        var y = (a.Y + b.Y) / 2;
        for (var j = 0; j < members.Count; j++)
        {
            if (j == index) continue;
            var (c, d) = members[j];
            var lo = Math.Min(c.X, d.X); var hi = Math.Max(c.X, d.X);
            if (hi - lo < Eps || x < lo - Eps || x > hi + Eps) continue;
            var t = (x - c.X) / (d.X - c.X);
            if (c.Y + (d.Y - c.Y) * t > y + Eps) return false;
        }
        return true;
    }
}
