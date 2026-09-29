using Loadpath.Core.Editing;
using Loadpath.Core.Geometry;
using Loadpath.Presentation;
using Windows.System;

namespace Loadpath.Workspace.Tools;

/// <summary>Click a node for a default 10 kN downward load; press and drag from a node to set the vector directly.</summary>
public sealed class LoadTool : Tool
{
    private int? _node;
    private Vec2 _pressScreen;
    private bool _dragging;
    private int _gesture;

    public override ToolKind Kind => ToolKind.Load;
    public override bool IsBusy => _dragging;
    public override CursorKind IdleCursor => CursorKind.Cross;
    public override string Hint => _dragging
        ? "Drag sets magnitude and direction · Shift snaps to 15°"
        : "Click a node for 10 kN down · Drag from a node to aim the load";

    public override void UpdateHover(IToolContext ctx, PointerState p, HitResult hit)
    {
        ctx.SetCursor(hit.Kind == HitKind.Node ? CursorKind.Grab : CursorKind.Cross);
    }

    public override void OnPressed(IToolContext ctx, PointerState p)
    {
        if (!p.IsLeft) return;
        var hit = ctx.Hit(p.Screen, includeLoadHandles: false);
        if (hit.Kind != HitKind.Node) { ctx.Toast("Loads go on nodes"); return; }
        _node = hit.Id;
        _pressScreen = p.Screen;
        _dragging = false;
        ctx.Selection.Set(ElementRef.Node(hit.Id));
    }

    public override void OnMoved(IToolContext ctx, PointerState p)
    {
        if (_node is not { } id) return;
        if (!_dragging)
        {
            if ((p.Screen - _pressScreen).Length < 6) return;
            _dragging = true;
            _gesture = ctx.NextGesture();
            ctx.SetCursor(CursorKind.Grabbing);
        }
        LoadVector.Apply(ctx, id, p, _gesture);
    }

    public override void OnReleased(IToolContext ctx, PointerState p)
    {
        if (_node is { } id)
        {
            if (!_dragging)
            {
                var node = ctx.Document.GetNode(id);
                if (!node.HasLoad) ctx.History.Do(new SetLoadEdit(id, new Vec2(0, -10)));
            }
        }
        _node = null;
        _dragging = false;
        ctx.SetCursor(CursorKind.Cross);
        ctx.Invalidate();
    }

    public override bool OnKey(IToolContext ctx, VirtualKey key)
    {
        if (key == VirtualKey.Escape && _dragging) { Cancel(ctx); return true; }
        return false;
    }

    public override void Cancel(IToolContext ctx)
    {
        if (_dragging) ctx.History.Undo();
        _node = null;
        _dragging = false;
        ctx.Overlay.Clear();
        ctx.Invalidate();
    }
}
