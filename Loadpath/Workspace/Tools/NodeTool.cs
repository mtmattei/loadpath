using Loadpath.Core.Editing;
using Loadpath.Core.Geometry;
using Loadpath.Presentation;
using SkiaSharp;
using Windows.System;

namespace Loadpath.Workspace.Tools;

/// <summary>Click places a node on the snapped point. Clicking a member splits it there.</summary>
public sealed class NodeTool : Tool
{
    public override ToolKind Kind => ToolKind.Node;
    public override string Hint => "Click to place a node · Click a member to split it · V to select";
    public override CursorKind IdleCursor => CursorKind.Cross;

    public override void UpdateHover(IToolContext ctx, PointerState p, HitResult hit)
    {
        ctx.Overlay.Clear();
        if (hit.Kind == HitKind.Member)
        {
            ctx.Overlay.HighlightMember = hit.Id;
            var at = ProjectOnMember(ctx, hit.Id, p.World);
            var s = ctx.Viewport.ToScreen(at);
            ctx.Overlay.GhostNode = new SKPoint((float)s.X, (float)s.Y);
        }
        else if (hit.Kind != HitKind.Node)
        {
            var snap = ctx.SnapPoint(p.World, allowNodeSnap: false, disableSnap: p.Alt);
            var s = ctx.Viewport.ToScreen(snap.Point);
            ctx.Overlay.GhostNode = new SKPoint((float)s.X, (float)s.Y);
            ctx.Overlay.Guides.AddRange(snap.Guides);
        }
        ctx.SetCursor(CursorKind.Cross);
        ctx.Invalidate();
    }

    public override void OnMoved(IToolContext ctx, PointerState p) => UpdateHover(ctx, p, ctx.Hit(p.Screen, includeLoadHandles: false));

    public override void OnPressed(IToolContext ctx, PointerState p)
    {
        if (!p.IsLeft) return;
        var hit = ctx.Hit(p.Screen, includeLoadHandles: false);
        if (hit.Kind == HitKind.Node) { ctx.Selection.Set(ElementRef.Node(hit.Id)); return; }
        if (hit.Kind == HitKind.Member)
        {
            var at = ProjectOnMember(ctx, hit.Id, p.World);
            var edit = new SplitMemberEdit(hit.Id, at);
            ctx.History.Do(edit);
            ctx.Selection.Set(ElementRef.Node(edit.NewNodeId));
            return;
        }
        var snap = ctx.SnapPoint(p.World, allowNodeSnap: false, disableSnap: p.Alt);
        var add = new AddNodeEdit(snap.Point);
        ctx.History.Do(add);
        ctx.Selection.Set(ElementRef.Node(add.NodeId));
    }

    private static Vec2 ProjectOnMember(IToolContext ctx, int memberId, Vec2 world)
    {
        var m = ctx.Document.GetMember(memberId);
        var a = ctx.Document.GetNode(m.StartNodeId).Position;
        var b = ctx.Document.GetNode(m.EndNodeId).Position;
        Vec2.DistanceToSegment(world, a, b, out var t);
        t = Math.Clamp(t, 0.05, 0.95);
        // Snap the split parameter to the grid along the member when that lands inside it.
        var raw = a + (b - a) * t;
        if (ctx.Options.SnapEnabled)
        {
            var snapped = ctx.SnapPoint(raw, allowNodeSnap: false).Point;
            Vec2.DistanceToSegment(snapped, a, b, out var ts);
            var onSeg = a + (b - a) * ts;
            if (onSeg.DistanceTo(snapped) < ctx.Viewport.ToWorldLength(3) && ts is > 0.02 and < 0.98) return onSeg;
        }
        return raw;
    }

    public override bool OnKey(IToolContext ctx, VirtualKey key) => false;

    public override void Cancel(IToolContext ctx)
    {
        ctx.Overlay.Clear();
        ctx.Invalidate();
    }
}
