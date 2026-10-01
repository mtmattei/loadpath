using Loadpath.Presentation;
using Loadpath.Workspace.Tools;
using Windows.System;

namespace Loadpath.Workspace;

/// <summary>
/// Owns the active tool, hover state and tool overlay; implements the tool context.
/// The view (WorkspaceView) forwards pointer and key events here.
/// </summary>
public sealed class WorkspaceInteraction : IToolContext
{
    private readonly EditorEngine _editor;
    private readonly Dictionary<ToolKind, Tool> _tools;
    private Tool _active;
    private Tool? _transientPan;
    private int _gesture;
    private bool _spaceHeld;

    public WorkspaceInteraction(EditorEngine editor)
    {
        _editor = editor;
        _tools = new Dictionary<ToolKind, Tool>
        {
            [ToolKind.Select] = new SelectTool(),
            [ToolKind.Node] = new NodeTool(),
            [ToolKind.Member] = new MemberTool(),
            [ToolKind.Load] = new LoadTool(),
            [ToolKind.Support] = new SupportTool(),
            [ToolKind.Pan] = new PanTool(),
        };
        _active = _tools[ToolKind.Select];
        _hint = _active.Hint;
    }

    public event EventHandler<CursorKind>? CursorChanged;
    public event EventHandler<ToolKind>? ToolChanged;
    /// <summary>Raised when a displayed value (tool, hint, cursor text, zoom text) changes.</summary>
    public event EventHandler? StateChanged;

    private ToolKind _activeTool = ToolKind.Select;
    private string _hint = "";
    private ElementRef? _hover;
    private string _cursorWorldText = "";
    private string _zoomText = "100%";

    public ToolKind ActiveTool { get => _activeTool; private set => Set(ref _activeTool, value); }
    public string Hint { get => _hint; set => Set(ref _hint, value); }
    public ElementRef? Hover { get => _hover; private set => Set(ref _hover, value); }
    public string CursorWorldText { get => _cursorWorldText; private set => Set(ref _cursorWorldText, value); }
    public string ZoomText { get => _zoomText; private set => Set(ref _zoomText, value); }

    private void Set<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public ToolOverlay Overlay { get; } = new();
    public bool IsSelectActive => ActiveTool == ToolKind.Select;
    public bool IsNodeActive => ActiveTool == ToolKind.Node;
    public bool IsMemberActive => ActiveTool == ToolKind.Member;
    public bool IsLoadActive => ActiveTool == ToolKind.Load;
    public bool IsSupportActive => ActiveTool == ToolKind.Support;
    public bool IsPanActive => ActiveTool == ToolKind.Pan;

    // ---- IToolContext ----
    public StructureDocument Document => _editor.Document;
    public Core.Editing.EditHistory History => _editor.History;
    public Core.Selection.SelectionSet Selection => _editor.Selection;
    public Core.Viewport.Viewport Viewport => _editor.Viewport;
    public SnapEngine Snap => _editor.Snap;
    public ViewOptions Options => _editor.Options;

    public HitResult Hit(Vec2 screen, bool includeLoadHandles = true) => HitTester.Test(Document, Viewport, screen, includeLoadHandles);

    public SnapResult SnapPoint(Vec2 world, bool allowNodeSnap, ISet<int>? exclude = null, bool constrainAxis = false, Vec2? origin = null, bool disableSnap = false)
    {
        if (disableSnap)
        {
            var p = world;
            if (constrainAxis && origin is { } o)
            {
                var d = world - o;
                p = Math.Abs(d.X) >= Math.Abs(d.Y) ? new Vec2(world.X, o.Y) : new Vec2(o.X, world.Y);
            }
            return new SnapResult(p, null, [], false);
        }
        var wasGrid = Snap.GridEnabled;
        var wasGuides = Snap.GuidesEnabled;
        Snap.GridEnabled = Options.SnapEnabled;
        Snap.GuidesEnabled = Options.SnapEnabled;
        try { return Snap.Snap(Document, Viewport, world, allowNodeSnap, exclude, constrainAxis, origin); }
        finally { Snap.GridEnabled = wasGrid; Snap.GuidesEnabled = wasGuides; }
    }

    public int NextGesture() => ++_gesture;
    public void Invalidate() => _editor.RequestRender();
    public void Toast(string message) => _editor.Toast(message);
    public void SetCursor(CursorKind cursor) => CursorChanged?.Invoke(this, cursor);
    public void FlashSnap(int nodeId) { Overlay.SnapFlashNode = nodeId; Invalidate(); }

    public void SetTool(ToolKind kind)
    {
        if (kind == ActiveTool) return;
        _active.Deactivate(this);
        Overlay.Clear();
        _active = _tools[kind];
        ActiveTool = kind;
        _active.Activate(this);
        Hint = _active.Hint;
        SetCursor(_active.IdleCursor);
        ToolChanged?.Invoke(this, kind);
        Invalidate();
    }

    public void CancelTool()
    {
        _active.Cancel(this);
        Hint = _active.Hint;
    }

    /// <summary>True while a tool is mid-gesture (drag, chain); shortcuts that change tools are deferred.</summary>
    public bool IsBusy => _active.IsBusy || _transientPan is not null;

    // ---- pointer routing ----

    public void PointerPressed(PointerState p)
    {
        if (p.IsMiddle || (_spaceHeld && p.IsLeft))
        {
            _transientPan = _tools[ToolKind.Pan];
            _transientPan.OnPressed(this, p with { IsLeft = true });
            return;
        }
        _active.OnPressed(this, p);
        Hint = _active.Hint;
    }

    public void PointerMoved(PointerState p)
    {
        CursorWorldText = $"X {p.World.X,6:0.00}  Y {p.World.Y,6:0.00}";
        if (_transientPan is not null) { _transientPan.OnMoved(this, p); return; }
        if (!_active.IsBusy)
        {
            var hit = Hit(p.Screen, includeLoadHandles: ActiveTool == ToolKind.Select);
            var newHover = hit.Ref;
            if (newHover != Hover) { Hover = newHover; Invalidate(); }
            _active.UpdateHover(this, p, hit);
        }
        _active.OnMoved(this, p);
        Hint = _active.Hint;
    }

    public void PointerReleased(PointerState p)
    {
        if (_transientPan is not null)
        {
            _transientPan.OnReleased(this, p);
            _transientPan = null;
            SetCursor(_active.IdleCursor);
            return;
        }
        _active.OnReleased(this, p);
        Hint = _active.Hint;
    }

    public void PointerExited()
    {
        if (Hover is not null) { Hover = null; Invalidate(); }
        CursorWorldText = "";
    }

    public void DoubleTap(PointerState p) => _active.OnDoubleTap(this, p);

    public void HoverFromOutline(ElementRef? r)
    {
        if (r != Hover) { Hover = r; Invalidate(); }
    }

    // ---- keys ----

    public bool KeyDown(VirtualKey key)
    {
        if (key == VirtualKey.Space) { _spaceHeld = true; SetCursor(CursorKind.Grab); return true; }
        var consumed = _active.OnKey(this, key);
        Hint = _active.Hint;
        return consumed;
    }

    public void KeyUp(VirtualKey key)
    {
        if (key == VirtualKey.Space) { _spaceHeld = false; SetCursor(_active.IdleCursor); }
    }

    // ---- zoom ----

    public event EventHandler? UserNavigated;

    public void ZoomStep(int direction, Vec2? anchor = null)
    {
        UserNavigated?.Invoke(this, EventArgs.Empty);
        var factor = Math.Pow(1.1, direction);
        var a = anchor ?? new Vec2(Viewport.ScreenWidth / 2, Viewport.ScreenHeight / 2);
        Viewport.ZoomAt(a, Viewport.Scale * factor);
        ZoomText = $"{Viewport.ZoomPercent:0}%";
    }

    public void ZoomTo(double scale)
    {
        UserNavigated?.Invoke(this, EventArgs.Empty);
        var a = new Vec2(Viewport.ScreenWidth / 2, Viewport.ScreenHeight / 2);
        Viewport.ZoomAt(a, scale);
        ZoomText = $"{Viewport.ZoomPercent:0}%";
    }

    public void RefreshZoomText() => ZoomText = $"{Viewport.ZoomPercent:0}%";
}
