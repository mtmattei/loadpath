using Loadpath.Core.Editing;
using Loadpath.Presentation;
using SkiaSharp;
using Windows.System;

namespace Loadpath.Workspace.Tools;

/// <summary>Click a node to start, click another to connect; clicking empty space creates a node and keeps chaining.</summary>
public sealed class MemberTool : Tool
{
    private int? _start;
    private Section _section = Section.Chs60;

    public override ToolKind Kind => ToolKind.Member;
    public override bool IsBusy => _start is not null;
    public override CursorKind IdleCursor => CursorKind.Cross;
    public override string Hint => _start is null
        ? "Click a node to start a member · Click empty space to start with a new node"
        : "Click a node or empty space to connect · Esc to finish";

    public override void UpdateHover(IToolContext ctx, PointerState p, HitResult hit) => OnMoved(ctx, p);

    public override void OnMoved(IToolContext ctx, PointerState p)
    {
        ctx.Overlay.Clear();
        var end = ResolveEnd(ctx, p, out var snappedNode, out var guides);
        var s = ctx.Viewport.ToScreen(end);
        var sp = new SKPoint((float)s.X, (float)s.Y);
        if (snappedNode is null) ctx.Overlay.GhostNode = sp;
        ctx.Overlay.Guides.AddRange(guides);
        if (_start is { } startId && ctx.Document.FindNode(startId) is { } start)
        {
            var a = ctx.Viewport.ToScreen(start.Position);
            ctx.Overlay.RubberBand = (new SKPoint((float)a.X, (float)a.Y), sp);
        }
        ctx.SetCursor(CursorKind.Cross);
        ctx.Invalidate();
    }

    private Vec2 ResolveEnd(IToolContext ctx, PointerState p, out int? snappedNode, out IReadOnlyList<SnapGuide> guides)
    {
        var hit = ctx.Hit(p.Screen, includeLoadHandles: false);
        if (hit.Kind == HitKind.Node)
        {
            snappedNode = hit.Id;
            guides = [];
            return ctx.Document.GetNode(hit.Id).Position;
        }
        var origin = _start is { } s && ctx.Document.FindNode(s) is { } n ? n.Position : (Vec2?)null;
        var snap = ctx.SnapPoint(p.World, allowNodeSnap: true, constrainAxis: p.Shift && origin is not null, origin: origin, disableSnap: p.Alt);
        snappedNode = snap.SnappedNodeId;
        guides = snap.Guides;
        return snap.Point;
    }

    public override void OnPressed(IToolContext ctx, PointerState p)
    {
        if (p.IsRight) { Cancel(ctx); return; }
        if (!p.IsLeft) return;
        var end = ResolveEnd(ctx, p, out var snappedNode, out _);

        if (_start is null)
        {
            if (snappedNode is { } existing)
            {
                _start = existing;
            }
            else
            {
                var add = new AddNodeEdit(end);
                ctx.History.Do(add);
                _start = add.NodeId;
            }
            ctx.Selection.Set(ElementRef.Node(_start.Value));
            OnMoved(ctx, p);
            return;
        }

        var startId = _start.Value;
        if (snappedNode == startId) { ctx.Toast("A member needs two different nodes"); return; }
        if (snappedNode is { } endId)
        {
            if (ctx.Document.FindMemberBetween(startId, endId) is not null) { ctx.Toast("Those nodes are already connected"); return; }
            var add = new AddMemberEdit(startId, endId, _section);
            ctx.History.Do(add);
            ctx.Selection.Set(ElementRef.Member(add.MemberId));
            _start = endId;
        }
        else
        {
            if (ctx.Document.GetNode(startId).Position.DistanceTo(end) < 1e-6) { ctx.Toast("Zero-length member"); return; }
            ctx.History.BeginTransaction("Add member");
            var node = new AddNodeEdit(end);
            ctx.History.Do(node);
            var add = new AddMemberEdit(startId, node.NodeId, _section);
            ctx.History.Do(add);
            ctx.History.CommitTransaction();
            ctx.Selection.Set(ElementRef.Member(add.MemberId));
            _start = node.NodeId;
        }
        OnMoved(ctx, p);
    }

    public override void OnDoubleTap(IToolContext ctx, PointerState p) => Cancel(ctx);

    public override bool OnKey(IToolContext ctx, VirtualKey key)
    {
        if (key == VirtualKey.Escape && _start is not null) { Cancel(ctx); return true; }
        return false;
    }

    public override void Cancel(IToolContext ctx)
    {
        _start = null;
        ctx.Overlay.Clear();
        ctx.SetCursor(CursorKind.Cross);
        ctx.Invalidate();
    }
}
