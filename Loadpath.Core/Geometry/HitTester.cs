using Loadpath.Core.Model;
using Loadpath.Core.Viewport;

namespace Loadpath.Core.Geometry;

public enum HitKind { None, Node, Member, LoadHandle }

public readonly record struct HitResult(HitKind Kind, int Id)
{
    public static readonly HitResult None = new(HitKind.None, 0);
    public bool IsNone => Kind == HitKind.None;
    public ElementRef? Ref => Kind switch
    {
        HitKind.Node => ElementRef.Node(Id),
        HitKind.Member => ElementRef.Member(Id),
        _ => null,
    };
}

/// <summary>Pixel-tolerance hit testing against the document through a viewport.</summary>
public static class HitTester
{
    public const double NodeRadiusPx = 9;
    public const double LoadHandleRadiusPx = 8;
    public const double MemberTolerancePx = 6;
    public const double LoadArrowPxPerKn = 4; // 10 kN = 40 px at 100%

    /// <summary>
    /// Screen position of the tail of a node's load arrow (the drag handle).
    /// The arrow points into the node along the load direction, so the tail sits opposite to it.
    /// </summary>
    public static Vec2 LoadHandleScreen(Node node, Viewport.Viewport vp)
    {
        var origin = vp.ToScreen(node.Position);
        var dir = new Vec2(node.Load.X, -node.Load.Y).Normalized();
        var len = LoadArrowLengthPx(node.Load.Length);
        return origin - dir * len;
    }

    public static double LoadArrowLengthPx(double magnitudeKn) => 22 + Math.Sqrt(Math.Max(0, magnitudeKn)) * 9;

    public static HitResult Test(StructureDocument doc, Viewport.Viewport vp, Vec2 screen, bool includeLoadHandles = true)
    {
        // Nodes first: they are on top.
        Node? bestNode = null;
        var bestD = double.MaxValue;
        foreach (var n in doc.Nodes)
        {
            var d = vp.ToScreen(n.Position).DistanceTo(screen);
            if (d <= NodeRadiusPx && d < bestD) { bestD = d; bestNode = n; }
        }
        if (bestNode is not null) return new HitResult(HitKind.Node, bestNode.Id);

        if (includeLoadHandles)
        {
            foreach (var n in doc.Nodes)
            {
                if (!n.HasLoad) continue;
                if (LoadHandleScreen(n, vp).DistanceTo(screen) <= LoadHandleRadiusPx) return new HitResult(HitKind.LoadHandle, n.Id);
            }
        }

        Member? bestMember = null;
        bestD = double.MaxValue;
        foreach (var m in doc.Members)
        {
            var a = vp.ToScreen(doc.GetNode(m.StartNodeId).Position);
            var b = vp.ToScreen(doc.GetNode(m.EndNodeId).Position);
            var d = Vec2.DistanceToSegment(screen, a, b, out _);
            if (d <= MemberTolerancePx && d < bestD) { bestD = d; bestMember = m; }
        }
        return bestMember is null ? HitResult.None : new HitResult(HitKind.Member, bestMember.Id);
    }

    /// <summary>Elements inside a screen-space marquee. Contained: fully inside. Otherwise: touching.</summary>
    public static IEnumerable<ElementRef> Marquee(StructureDocument doc, Viewport.Viewport vp, Bounds screenRect, bool contained)
    {
        var world = Bounds.FromPoints(vp.ToWorld(screenRect.Min), vp.ToWorld(screenRect.Max));
        foreach (var n in doc.Nodes)
        {
            if (world.Contains(n.Position)) yield return n.Ref;
        }
        foreach (var m in doc.Members)
        {
            var a = doc.GetNode(m.StartNodeId).Position;
            var b = doc.GetNode(m.EndNodeId).Position;
            var hit = contained ? world.Contains(a) && world.Contains(b) : world.IntersectsSegment(a, b);
            if (hit) yield return m.Ref;
        }
    }
}
