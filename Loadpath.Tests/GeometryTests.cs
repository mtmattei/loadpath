using Loadpath.Core.Editing;
using Loadpath.Core.Geometry;
using Loadpath.Core.Model;
using Loadpath.Core.Viewport;
using Xunit;

namespace Loadpath.Tests;

public class GeometryTests
{
    [Fact]
    public void ZoomAt_keeps_the_anchor_world_point_fixed()
    {
        var vp = new Viewport();
        vp.Resize(1000, 800);
        var anchor = new Vec2(300, 200);
        var before = vp.ToWorld(anchor);
        vp.ZoomAt(anchor, vp.Scale * 1.7);
        var after = vp.ToWorld(anchor);
        Assert.InRange((before - after).Length, 0, 1e-9);
    }

    [Fact]
    public void Fit_centers_bounds()
    {
        var vp = new Viewport();
        vp.Resize(1000, 800);
        vp.Fit(new Bounds(new Vec2(0, 0), new Vec2(12, 2)));
        var c = vp.ToScreen(new Vec2(6, 1));
        Assert.InRange(c.X, 499, 501);
        Assert.InRange(c.Y, 399, 401);
        Assert.True(vp.ToScreen(new Vec2(0, 0)).X > 0);
    }

    [Fact]
    public void Hit_test_prefers_nodes_then_members()
    {
        var doc = new StructureDocument();
        var h = new EditHistory(doc);
        h.Do(new AddNodeEdit(new Vec2(0, 0)));
        h.Do(new AddNodeEdit(new Vec2(2, 0)));
        h.Do(new AddMemberEdit(1, 2, Section.Chs48));
        var vp = new Viewport();
        vp.Resize(1000, 800);
        var onNode = vp.ToScreen(new Vec2(0, 0)) + new Vec2(4, 3);
        Assert.Equal(HitKind.Node, HitTester.Test(doc, vp, onNode).Kind);
        var onMember = vp.ToScreen(new Vec2(1, 0)) + new Vec2(0, 3);
        Assert.Equal(HitKind.Member, HitTester.Test(doc, vp, onMember).Kind);
        var far = vp.ToScreen(new Vec2(1, 1));
        Assert.True(HitTester.Test(doc, vp, far).IsNone);
    }

    [Fact]
    public void Snap_yields_guides_and_grid()
    {
        var doc = new StructureDocument();
        var h = new EditHistory(doc);
        h.Do(new AddNodeEdit(new Vec2(0, 0)));
        h.Do(new AddNodeEdit(new Vec2(3, 1.5)));
        var vp = new Viewport();
        vp.Resize(1000, 800);
        var snap = new SnapEngine { GridStep = 0.5 };
        // Near x = 3 (guide) and y ≈ 0.7 (grid → 0.5).
        var r = snap.Snap(doc, vp, new Vec2(3.02, 0.7), allowNodeSnap: false);
        Assert.Equal(new Vec2(3, 0.5), r.Point);
        Assert.Contains(r.Guides, g => g.Axis == GuideAxis.Vertical && g.AnchorNodeId == 2);
        // Close to node 1: node snap when allowed.
        var n = snap.Snap(doc, vp, new Vec2(0.05, 0.02), allowNodeSnap: true);
        Assert.Equal(1, n.SnappedNodeId);
    }

    [Fact]
    public void Marquee_contained_vs_touching()
    {
        var doc = new StructureDocument();
        var h = new EditHistory(doc);
        h.Do(new AddNodeEdit(new Vec2(0, 0)));
        h.Do(new AddNodeEdit(new Vec2(4, 0)));
        h.Do(new AddMemberEdit(1, 2, Section.Chs48));
        var vp = new Viewport();
        vp.Resize(1000, 800);
        var rect = Bounds.FromPoints(vp.ToScreen(new Vec2(-0.5, 0.5)), vp.ToScreen(new Vec2(2, -0.5)));
        var contained = HitTester.Marquee(doc, vp, rect, contained: true).ToList();
        Assert.Contains(ElementRef.Node(1), contained);
        Assert.DoesNotContain(ElementRef.Member(1), contained);
        var touching = HitTester.Marquee(doc, vp, rect, contained: false).ToList();
        Assert.Contains(ElementRef.Member(1), touching);
    }
}
