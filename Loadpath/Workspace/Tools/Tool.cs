using Loadpath.Presentation;
using Windows.System;

namespace Loadpath.Workspace.Tools;

public enum CursorKind { Arrow, Hand, Grab, Grabbing, Cross, Move }

/// <summary>One pointer sample, in both spaces, with modifiers.</summary>
public readonly record struct PointerState(Vec2 Screen, Vec2 World, bool Shift, bool Alt, bool Ctrl, bool IsLeft, bool IsMiddle, bool IsRight);

/// <summary>What a tool needs from the editor. Implemented by WorkspaceInteraction.</summary>
public interface IToolContext
{
    StructureDocument Document { get; }
    Core.Editing.EditHistory History { get; }
    Core.Selection.SelectionSet Selection { get; }
    Core.Viewport.Viewport Viewport { get; }
    Core.Geometry.SnapEngine Snap { get; }
    ViewOptions Options { get; }
    ToolOverlay Overlay { get; }
    HitResult Hit(Vec2 screen, bool includeLoadHandles = true);
    SnapResult SnapPoint(Vec2 world, bool allowNodeSnap, ISet<int>? exclude = null, bool constrainAxis = false, Vec2? origin = null, bool disableSnap = false);
    int NextGesture();
    void Invalidate();
    void Toast(string message);
    void SetCursor(CursorKind cursor);
    void SetTool(ToolKind kind);
    void FlashSnap(int nodeId);
}

/// <summary>A workspace tool: a small state machine over pointer and key events.</summary>
public abstract class Tool
{
    public abstract ToolKind Kind { get; }
    /// <summary>Status-bar hint for the current state.</summary>
    public abstract string Hint { get; }
    public virtual CursorKind IdleCursor => CursorKind.Arrow;
    public virtual bool IsBusy => false;

    public virtual void Activate(IToolContext ctx) { }
    public virtual void Deactivate(IToolContext ctx) => Cancel(ctx);
    public virtual void OnPressed(IToolContext ctx, PointerState p) { }
    public virtual void OnMoved(IToolContext ctx, PointerState p) { }
    public virtual void OnReleased(IToolContext ctx, PointerState p) { }
    public virtual void OnDoubleTap(IToolContext ctx, PointerState p) { }
    /// <summary>Return true when the key was consumed by the tool.</summary>
    public virtual bool OnKey(IToolContext ctx, VirtualKey key) => false;
    public virtual void Cancel(IToolContext ctx) { }
    public virtual void UpdateHover(IToolContext ctx, PointerState p, HitResult hit) { }
}
