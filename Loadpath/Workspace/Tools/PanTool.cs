using Loadpath.Presentation;

namespace Loadpath.Workspace.Tools;

/// <summary>Drag to pan. Also used transiently for Space+drag and middle-button drag from any tool.</summary>
public sealed class PanTool : Tool
{
    private Vec2? _last;
    public override ToolKind Kind => ToolKind.Pan;
    public override CursorKind IdleCursor => CursorKind.Grab;
    public override bool IsBusy => _last is not null;
    public override string Hint => "Drag to pan · Wheel zooms toward the cursor · F fits the structure";

    public override void UpdateHover(IToolContext ctx, PointerState p, HitResult hit) => ctx.SetCursor(CursorKind.Grab);

    public override void OnPressed(IToolContext ctx, PointerState p)
    {
        _last = p.Screen;
        ctx.SetCursor(CursorKind.Grabbing);
    }

    public override void OnMoved(IToolContext ctx, PointerState p)
    {
        if (_last is not { } last) return;
        ctx.Viewport.PanBy(p.Screen - last);
        _last = p.Screen;
    }

    public override void OnReleased(IToolContext ctx, PointerState p)
    {
        _last = null;
        ctx.SetCursor(CursorKind.Grab);
    }

    public override void Cancel(IToolContext ctx) => _last = null;
}
