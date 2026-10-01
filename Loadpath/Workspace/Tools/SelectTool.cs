using Loadpath.Core.Editing;
using Loadpath.Presentation;
using SkiaSharp;
using Windows.System;

namespace Loadpath.Workspace.Tools;

/// <summary>Click, shift-click, marquee, drag nodes (with snapping), drag load handles.</summary>
public sealed class SelectTool : Tool
{
    private enum Phase { Idle, PressedElement, PressedEmpty, DraggingNodes, Marquee, DraggingLoad }

    private const double DragThresholdPx = 4;
    private Phase _phase;
    private Vec2 _pressScreen;
    private ElementRef? _pressedRef;
    private bool _pressedWasSelected;
    private HashSet<int> _dragNodes = new();
    private int _primaryNode;
    private Vec2 _grabOffsetWorld;
    private int _gesture;
    private int _loadNode;

    public override ToolKind Kind => ToolKind.Select;
    public override bool IsBusy => _phase is Phase.DraggingNodes or Phase.Marquee or Phase.DraggingLoad;

    public override string Hint => _phase switch
    {
        Phase.DraggingNodes => "Drag to move · Shift constrains axis · Alt disables snap",
        Phase.Marquee => "Release to select what is inside · Alt selects what touches",
        Phase.DraggingLoad => "Drag to set magnitude and direction · Shift snaps to 15°",
        _ => "Click to select · Shift adds · Drag empty space for a marquee",
    };

    public override void UpdateHover(IToolContext ctx, PointerState p, HitResult hit)
    {
        ctx.SetCursor(hit.Kind switch
        {
            HitKind.Node => CursorKind.Move,
            HitKind.LoadHandle => CursorKind.Grab,
            HitKind.Member => CursorKind.Move,
            _ => CursorKind.Arrow,
        });
    }

    public override void OnPressed(IToolContext ctx, PointerState p)
    {
        if (!p.IsLeft) return;
        _pressScreen = p.Screen;
        var hit = ctx.Hit(p.Screen);
        if (hit.Kind == HitKind.LoadHandle)
        {
            _phase = Phase.DraggingLoad;
            _loadNode = hit.Id;
            _gesture = ctx.NextGesture();
            ctx.Selection.Set(ElementRef.Node(hit.Id));
            ctx.SetCursor(CursorKind.Grabbing);
            return;
        }
        if (hit.Ref is { } r)
        {
            _phase = Phase.PressedElement;
            _pressedRef = r;
            _pressedWasSelected = ctx.Selection.Contains(r);
            if (!_pressedWasSelected && !p.Shift) ctx.Selection.Set(r);
            else if (!_pressedWasSelected && p.Shift) ctx.Selection.Add([r]);
            return;
        }
        _phase = Phase.PressedEmpty;
        if (!p.Shift) ctx.Selection.Clear();
    }

    public override void OnMoved(IToolContext ctx, PointerState p)
    {
        switch (_phase)
        {
            case Phase.PressedElement:
                if ((p.Screen - _pressScreen).Length < DragThresholdPx) return;
                BeginNodeDrag(ctx, p);
                goto case Phase.DraggingNodes;
            case Phase.DraggingNodes:
                DragNodes(ctx, p);
                break;
            case Phase.PressedEmpty:
                if ((p.Screen - _pressScreen).Length < DragThresholdPx) return;
                _phase = Phase.Marquee;
                goto case Phase.Marquee;
            case Phase.Marquee:
                ctx.Overlay.Marquee = new SKRect(
                    (float)Math.Min(_pressScreen.X, p.Screen.X), (float)Math.Min(_pressScreen.Y, p.Screen.Y),
                    (float)Math.Max(_pressScreen.X, p.Screen.X), (float)Math.Max(_pressScreen.Y, p.Screen.Y));
                ctx.Invalidate();
                break;
            case Phase.DraggingLoad:
                LoadVector.Apply(ctx, _loadNode, p, _gesture);
                break;
        }
    }

    private void BeginNodeDrag(IToolContext ctx, PointerState p)
    {
        _phase = Phase.DraggingNodes;
        _gesture = ctx.NextGesture();
        var r = _pressedRef!.Value;
        // Dragging something already selected moves the whole selection; otherwise just the pressed element.
        var refs = ctx.Selection.Contains(r) ? ctx.Selection.Items : [r];
        _dragNodes = new HashSet<int>();
        foreach (var e in refs)
        {
            if (e.IsNode) _dragNodes.Add(e.Id);
            else if (ctx.Document.FindMember(e.Id) is { } m) { _dragNodes.Add(m.StartNodeId); _dragNodes.Add(m.EndNodeId); }
        }
        _primaryNode = r.IsNode ? r.Id : ctx.Document.GetMember(r.Id).StartNodeId;
        _dragStartPrimary = ctx.Document.GetNode(_primaryNode).Position;
        _grabOffsetWorld = ctx.Viewport.ToWorld(_pressScreen) - ctx.Document.GetNode(_primaryNode).Position;
        ctx.SetCursor(CursorKind.Grabbing);
    }

    private void DragNodes(IToolContext ctx, PointerState p)
    {
        var primary = ctx.Document.GetNode(_primaryNode);
        var target = p.World - _grabOffsetWorld;
        var snap = ctx.SnapPoint(target, allowNodeSnap: false, exclude: _dragNodes, constrainAxis: p.Shift, origin: primary.Position - CurrentDelta(ctx), disableSnap: p.Alt);
        var delta = snap.Point - primary.Position;
        ctx.Overlay.Guides.Clear();
        ctx.Overlay.Guides.AddRange(snap.Guides);
        if (delta.LengthSquared > 1e-18) ctx.History.Do(new MoveNodesEdit(_dragNodes, delta, _gesture));
        ctx.Invalidate();
    }

    private Vec2 _dragStartPrimary;
    private Vec2 CurrentDelta(IToolContext ctx) => ctx.Document.GetNode(_primaryNode).Position - _dragStartPrimary;

    public override void OnReleased(IToolContext ctx, PointerState p)
    {
        switch (_phase)
        {
            case Phase.PressedElement:
                // A click on an already-selected element with Shift toggles it off; without Shift, isolates it.
                if (_pressedRef is { } r && _pressedWasSelected)
                {
                    if (p.Shift) ctx.Selection.Toggle(r);
                    else ctx.Selection.Set(r);
                }
                break;
            case Phase.Marquee:
                if (ctx.Overlay.Marquee is { } rect)
                {
                    var bounds = Bounds.FromPoints(new Vec2(rect.Left, rect.Top), new Vec2(rect.Right, rect.Bottom));
                    var hits = HitTester.Marquee(ctx.Document, ctx.Viewport, bounds, contained: !p.Alt).ToList();
                    if (p.Shift) ctx.Selection.Add(hits); else ctx.Selection.Replace(hits);
                }
                break;
        }
        Reset(ctx);
    }

    public override bool OnKey(IToolContext ctx, VirtualKey key)
    {
        if (key == VirtualKey.Escape && _phase != Phase.Idle) { Cancel(ctx); return true; }
        return false;
    }

    public override void Cancel(IToolContext ctx)
    {
        if (_phase == Phase.DraggingNodes) ctx.History.Undo();
        else if (_phase == Phase.DraggingLoad) ctx.History.Undo();
        Reset(ctx);
    }

    private void Reset(IToolContext ctx)
    {
        _phase = Phase.Idle;
        _pressedRef = null;
        ctx.Overlay.Clear();
        ctx.SetCursor(CursorKind.Arrow);
        ctx.Invalidate();
    }
}

/// <summary>Shared vector-drag logic for loads: tail under the pointer, arrow into the node.</summary>
internal static class LoadVector
{
    public static Vec2 FromPointer(IToolContext ctx, int nodeId, PointerState p)
    {
        var node = ctx.Document.GetNode(nodeId);
        var nodeScreen = ctx.Viewport.ToScreen(node.Position);
        var tail = p.Screen - nodeScreen; // screen-space vector from node to pointer
        var len = tail.Length;
        if (len < 6) return Vec2.Zero;
        // Load direction points from the tail into the node: −tail, flipped to world Y.
        var dir = new Vec2(-tail.X, tail.Y).Normalized();
        if (p.Shift)
        {
            var angle = Math.Round(Math.Atan2(dir.Y, dir.X) / (Math.PI / 12)) * (Math.PI / 12);
            dir = new Vec2(Math.Cos(angle), Math.Sin(angle));
        }
        var magnitude = Math.Max(0.5, Math.Round(((len - 28) / 12.0) * ((len - 28) / 12.0) * 2) / 2);
        return dir * magnitude;
    }

    public static void Apply(IToolContext ctx, int nodeId, PointerState p, int gesture)
    {
        var load = FromPointer(ctx, nodeId, p);
        if (load.LengthSquared < 1e-12) return;
        ctx.History.Do(new SetLoadEdit(nodeId, load, gesture));
        ctx.Invalidate();
    }
}
