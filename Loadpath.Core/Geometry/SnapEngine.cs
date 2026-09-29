using Loadpath.Core.Model;

namespace Loadpath.Core.Geometry;

public enum GuideAxis { Horizontal, Vertical }

/// <summary>An alignment guide: a horizontal line at Y or a vertical line at X, anchored at a reference node.</summary>
public readonly record struct SnapGuide(GuideAxis Axis, double Value, int AnchorNodeId);

public readonly record struct SnapResult(Vec2 Point, int? SnappedNodeId, IReadOnlyList<SnapGuide> Guides, bool GridSnapped)
{
    public bool HasGuides => Guides.Count > 0;
}

/// <summary>
/// Snap order: existing node (when allowed) → alignment guides to other nodes → grid.
/// Tolerances are in pixels and converted with the viewport scale.
/// </summary>
public sealed class SnapEngine
{
    public double GridStep { get; set; } = 0.5;
    public bool GridEnabled { get; set; } = true;
    public bool GuidesEnabled { get; set; } = true;
    public double NodeTolerancePx { get; set; } = 10;
    public double GuideTolerancePx { get; set; } = 6;

    public SnapResult Snap(StructureDocument doc, Viewport.Viewport vp, Vec2 world, bool allowNodeSnap, ISet<int>? exclude = null, bool constrainAxis = false, Vec2? constrainOrigin = null)
    {
        var nodeTol = vp.ToWorldLength(NodeTolerancePx);
        var guideTol = vp.ToWorldLength(GuideTolerancePx);

        if (allowNodeSnap)
        {
            Node? best = null;
            var bestD = nodeTol;
            foreach (var n in doc.Nodes)
            {
                if (exclude is not null && exclude.Contains(n.Id)) continue;
                var d = n.Position.DistanceTo(world);
                if (d <= bestD) { bestD = d; best = n; }
            }
            if (best is not null) return new SnapResult(best.Position, best.Id, [], false);
        }

        var p = world;
        if (constrainAxis && constrainOrigin is { } o)
        {
            var d = world - o;
            p = Math.Abs(d.X) >= Math.Abs(d.Y) ? new Vec2(world.X, o.Y) : new Vec2(o.X, world.Y);
        }

        var guides = new List<SnapGuide>(2);
        var snappedX = false;
        var snappedY = false;

        if (GuidesEnabled)
        {
            double bestDx = guideTol, bestDy = guideTol;
            int? gx = null, gy = null;
            double gxv = 0, gyv = 0;
            foreach (var n in doc.Nodes)
            {
                if (exclude is not null && exclude.Contains(n.Id)) continue;
                var dx = Math.Abs(n.Position.X - p.X);
                var dy = Math.Abs(n.Position.Y - p.Y);
                if (dx <= bestDx) { bestDx = dx; gx = n.Id; gxv = n.Position.X; }
                if (dy <= bestDy) { bestDy = dy; gy = n.Id; gyv = n.Position.Y; }
            }
            if (gx is { } ix && !(constrainAxis && constrainOrigin is { } ox && Math.Abs(p.X - ox.X) < 1e-9)) { p = new Vec2(gxv, p.Y); snappedX = true; guides.Add(new SnapGuide(GuideAxis.Vertical, gxv, ix)); }
            if (gy is { } iy && !(constrainAxis && constrainOrigin is { } oy && Math.Abs(p.Y - oy.Y) < 1e-9)) { p = new Vec2(p.X, gyv); snappedY = true; guides.Add(new SnapGuide(GuideAxis.Horizontal, gyv, iy)); }
        }

        var gridSnapped = false;
        if (GridEnabled && GridStep > 0)
        {
            var x = snappedX ? p.X : Math.Round(p.X / GridStep) * GridStep;
            var y = snappedY ? p.Y : Math.Round(p.Y / GridStep) * GridStep;
            gridSnapped = !snappedX || !snappedY;
            p = new Vec2(x, y);
        }

        return new SnapResult(p, null, guides, gridSnapped);
    }
}
