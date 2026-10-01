using Loadpath.Core.Analysis;
using Loadpath.Core.Geometry;
using Loadpath.Presentation;
using Loadpath.Workspace.Tools;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using SkiaSharp;
using Uno.WinUI.Graphics2DSK;
using Windows.Foundation;

namespace Loadpath.Workspace;

/// <summary>
/// The canvas. Owns rendering (through WorkspaceRenderer), pointer input (forwarded to WorkspaceInteraction),
/// the viewport size, and the small viewport animation timer.
/// </summary>
public sealed partial class WorkspaceView : SKCanvasElement
{
    public static readonly DependencyProperty EditorProperty = DependencyProperty.Register(
        nameof(Editor), typeof(EditorEngine), typeof(WorkspaceView), new PropertyMetadata(null, (d, e) => ((WorkspaceView)d).Attach(e.OldValue as EditorEngine, e.NewValue as EditorEngine)));

    private readonly WorkspaceRenderer _renderer = new();
    private volatile RenderSnapshot _snapshot = RenderSnapshot.Empty;
    private EditorEngine? _editor;
    private DispatcherTimer? _animTimer;
    private (double Scale, Vec2 Offset) _animFrom;
    private (double Scale, Vec2 Offset) _animTo;
    private DateTime _animStart;
    private TimeSpan _animDuration;
    private bool _snapshotDirty = true;
    private bool _fitted;
    private DateTime _lastTap = DateTime.MinValue;
    private Vec2 _lastTapPos;

    public WorkspaceView()
    {
        IsTabStop = false;
        SizeChanged += (_, e) =>
        {
            _editor?.Viewport.Resize(e.NewSize.Width, e.NewSize.Height);
            // A fitted view stays fitted through window resizes until the user pans or zooms.
            if (_editor is not null && _fitted && _editor.Document.Nodes.Count > 0)
            {
                _animTimer?.Stop();
                var target = SheetFit(_editor.Document.GetBounds());
                _editor.Viewport.Set(target.Scale, target.Offset);
            }
            _editor?.Interaction.RefreshZoomText();
            MarkDirty();
        };
        Unloaded += (_, _) => { _animTimer?.Stop(); };
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerCanceled += OnPointerLost;
        PointerCaptureLost += OnPointerLost;
        PointerExited += (_, _) => _editor?.Interaction.PointerExited();
        PointerWheelChanged += OnPointerWheelChanged;
    }

    public EditorEngine? Editor
    {
        get => (EditorEngine?)GetValue(EditorProperty);
        set => SetValue(EditorProperty, value);
    }

    /// <summary>Raised on right-click with the screen point and what was hit.</summary>
    public event EventHandler<(Point Position, HitResult Hit)>? ContextMenuRequested;

    private void Attach(EditorEngine? old, EditorEngine? editor)
    {
        if (old is not null)
        {
            old.RenderRequested -= OnRenderRequested;
            old.ViewportChanged -= OnRenderRequested;
            old.FitRequested -= OnFitRequested;
            old.Interaction.CursorChanged -= OnCursorChanged;
            old.Interaction.UserNavigated -= (_, _) => _fitted = false;
        }
        _editor = editor;
        if (editor is null) return;
        editor.RenderRequested += OnRenderRequested;
        editor.ViewportChanged += OnRenderRequested;
        editor.FitRequested += OnFitRequested;
        editor.Interaction.CursorChanged += OnCursorChanged;
        editor.Interaction.UserNavigated += (_, _) => _fitted = false;
        if (ActualWidth > 0) editor.Viewport.Resize(ActualWidth, ActualHeight);
        MarkDirty();
    }

    private void OnRenderRequested(object? sender, EventArgs e)
    {
        _editor?.Interaction.RefreshZoomText();
        MarkDirty();
    }

    private void MarkDirty()
    {
        _snapshotDirty = true;
        Invalidate();
    }

    protected override void RenderOverride(SKCanvas canvas, Size area)
    {
        if (_snapshotDirty && _editor is not null)
        {
            _snapshot = BuildSnapshot(_editor, (float)area.Width, (float)area.Height);
            _snapshotDirty = false;
        }
        _renderer.Render(canvas, _snapshot);
    }

    // ---- snapshot ----

    private static RenderSnapshot BuildSnapshot(EditorEngine ed, float width, float height)
    {
        var doc = ed.Document;
        var vp = ed.Viewport;
        var analysis = ed.Analysis;
        var sel = ed.Selection;
        var hover = ed.Interaction.Hover;
        var options = ed.Options;
        var anySelected = !sel.IsEmpty;
        var dimOthers = sel.MemberCount == 1 && sel.NodeCount == 0;
        var (minor, major) = GridSteps.For(vp.Scale);

        // Deflection exaggeration: max displacement reads as 6% of the structure diagonal, times the user factor.
        var bounds = doc.GetBounds();
        var diag = bounds.IsEmpty ? 1 : Math.Max(1, Math.Sqrt(bounds.Width * bounds.Width + bounds.Height * bounds.Height));
        var exag = analysis.IsSolved && analysis.MaxDisplacementM > 1e-12 ? diag * 0.06 * options.Exaggeration / analysis.MaxDisplacementM : 0;

        var connected = new HashSet<int>();
        if (dimOthers && doc.FindMember(sel.MemberIds.First()) is { } sm) { connected.Add(sm.StartNodeId); connected.Add(sm.EndNodeId); }
        var detached = analysis.DetachedNodeIds.Count > 0 ? new HashSet<int>(analysis.DetachedNodeIds) : null;

        var nodes = new RenderSnapshot.NodeItem[doc.Nodes.Count];
        var nodeIndex = new Dictionary<int, int>(doc.Nodes.Count);
        for (var i = 0; i < doc.Nodes.Count; i++)
        {
            var n = doc.Nodes[i];
            nodeIndex[n.Id] = i;
            var s = vp.ToScreen(n.Position);
            var r = analysis.ForNode(n.Id);
            var disp = r?.Displacement ?? Vec2.Zero;
            var deflected = vp.ToScreen(n.Position + disp * exag);
            var tail = HitTester.LoadHandleScreen(n, vp);
            var isSelected = sel.Contains(n.Ref);
            var dim = (dimOthers && !connected.Contains(n.Id)) || (detached?.Contains(n.Id) ?? false);
            nodes[i] = new RenderSnapshot.NodeItem(n.Id, P(s), P(deflected), n.Support, n.Load, P(tail), r?.Reaction ?? Vec2.Zero, isSelected, hover == n.Ref, dim, n.Position);
        }

        var members = new RenderSnapshot.MemberItem[doc.Members.Count];
        for (var i = 0; i < doc.Members.Count; i++)
        {
            var m = doc.Members[i];
            var a = nodes[nodeIndex[m.StartNodeId]];
            var b = nodes[nodeIndex[m.EndNodeId]];
            var r = analysis.For(m.Id);
            var isSelected = sel.Contains(m.Ref);
            var dim = (dimOthers && !isSelected) || (detached?.Contains(m.StartNodeId) ?? false);
            members[i] = new RenderSnapshot.MemberItem(m.Id, a.Screen, b.Screen, a.Deflected, b.Deflected,
                r?.AxialForceKn ?? 0, r?.Utilization ?? 0, r?.IsOverstressed ?? false, r?.BucklingGoverns ?? false, isSelected, hover == m.Ref, dim);
        }

        return new RenderSnapshot
        {
            Nodes = nodes,
            Members = members,
            MaxAbsForceKn = analysis.MaxAbsForceKn,
            Status = analysis.Status,
            Scale = vp.Scale,
            Origin = P(vp.ToScreen(Vec2.Zero)),
            Mode = options.DisplayMode,
            ShowDeflection = options.ShowDeflection,
            ShowLabels = options.ShowLabels,
            ShowReactions = options.ShowReactions,
            ShowGrid = options.ShowGrid,
            GridMinor = minor,
            GridMajor = major,
            Overlay = ed.Interaction.Overlay,
            Width = width,
            Height = height,
            AnySelected = anySelected,
            DocumentName = doc.Name,
            Exaggeration = options.Exaggeration,
            Center = bounds.IsEmpty ? new SKPoint(width / 2, height / 2) : P(vp.ToScreen(bounds.Center)),
        };
    }

    private static SKPoint P(Vec2 v) => new((float)v.X, (float)v.Y);

    // ---- pointer ----

    private PointerState State(PointerRoutedEventArgs e)
    {
        var pt = e.GetCurrentPoint(this);
        var screen = new Vec2(pt.Position.X, pt.Position.Y);
        var mods = e.KeyModifiers;
        var props = pt.Properties;
        return new PointerState(
            screen,
            _editor?.Viewport.ToWorld(screen) ?? Vec2.Zero,
            mods.HasFlag(Windows.System.VirtualKeyModifiers.Shift),
            mods.HasFlag(Windows.System.VirtualKeyModifiers.Menu),
            mods.HasFlag(Windows.System.VirtualKeyModifiers.Control),
            props.IsLeftButtonPressed,
            props.IsMiddleButtonPressed,
            props.IsRightButtonPressed);
    }

    private PointerState _lastState;

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_editor is null) return;
        var s = State(e);
        _lastState = s;
        FocusRequested?.Invoke(this, EventArgs.Empty);
#if DEBUG
        if (Environment.GetEnvironmentVariable("LOADPATH_TRACE") is { } tr)
        {
            var pp = e.GetCurrentPoint(this).Properties;
            File.AppendAllText(tr, $"press kind={pp.PointerUpdateKind} l={pp.IsLeftButtonPressed} m={pp.IsMiddleButtonPressed} r={pp.IsRightButtonPressed} x1={pp.IsXButton1Pressed} x2={pp.IsXButton2Pressed} wheel={pp.MouseWheelDelta}\n");
        }
#endif
        if (s.IsRight)
        {
            _editor.Interaction.CancelTool();
            var hit = _editor.Interaction.Hit(s.Screen);
            if (hit.Ref is { } r && !_editor.Selection.Contains(r)) _editor.Selection.Set(r);
            ContextMenuRequested?.Invoke(this, (new Point(s.Screen.X, s.Screen.Y), hit));
            e.Handled = true;
            return;
        }
        CapturePointer(e.Pointer);
        if (s.IsMiddle || _editor.Interaction.ActiveTool == ToolKind.Pan) _fitted = false;
        var now = DateTime.UtcNow;
        if ((now - _lastTap).TotalMilliseconds < 350 && (s.Screen - _lastTapPos).Length < 6)
        {
            _editor.Interaction.DoubleTap(s);
            _lastTap = DateTime.MinValue;
        }
        else
        {
            _lastTap = now;
            _lastTapPos = s.Screen;
            _editor.Interaction.PointerPressed(s);
        }
        e.Handled = true;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_editor is null) return;
        var s = State(e);
        _lastState = s;
        _editor.Interaction.PointerMoved(s);
        e.Handled = true;
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_editor is null) return;
        var s = State(e);
        _editor.Interaction.PointerReleased(s);
        ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    private void OnPointerLost(object sender, PointerRoutedEventArgs e)
    {
        _editor?.Interaction.PointerReleased(_lastState with { IsLeft = false, IsMiddle = false });
    }

    private void OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (_editor is null) return;
        var pt = e.GetCurrentPoint(this);
        var delta = pt.Properties.MouseWheelDelta;
#if DEBUG
        if (Environment.GetEnvironmentVariable("LOADPATH_TRACE") is { } trace) File.AppendAllText(trace, $"wheel {delta} h={pt.Properties.IsHorizontalMouseWheel}\n");
#endif
        if (delta == 0) return;
        var mods = e.KeyModifiers;
        var screen = new Vec2(pt.Position.X, pt.Position.Y);
        _fitted = false;
        if (mods.HasFlag(Windows.System.VirtualKeyModifiers.Shift) || pt.Properties.IsHorizontalMouseWheel)
        {
            _editor.Viewport.PanBy(new Vec2(delta > 0 ? 60 : -60, 0));
        }
        else
        {
            var steps = Math.Sign(delta) * Math.Max(1, Math.Abs(delta) / 120.0);
            AnimateZoom(screen, _editor.Viewport.Scale * Math.Pow(1.1, steps));
        }
        e.Handled = true;
    }

    /// <summary>The page focuses its key sink when the canvas is pressed, so shortcuts work after a click.</summary>
    public event EventHandler? FocusRequested;

    private void OnCursorChanged(object? sender, CursorKind cursor)
    {
        var shape = cursor switch
        {
            CursorKind.Hand => InputSystemCursorShape.Hand,
            CursorKind.Grab => InputSystemCursorShape.Hand,
            CursorKind.Grabbing => InputSystemCursorShape.SizeAll,
            CursorKind.Cross => InputSystemCursorShape.Cross,
            CursorKind.Move => InputSystemCursorShape.SizeAll,
            _ => InputSystemCursorShape.Arrow,
        };
        try { ProtectedCursor = InputSystemCursor.Create(shape); } catch { /* cursor shapes are cosmetic */ }
    }

    // ---- viewport animation (timer only while animating; no Rendering pump) ----

    private void OnFitRequested(object? sender, EventArgs e)
    {
        if (_editor is null) return;
        _fitted = true;
        var bounds = _editor.Document.GetBounds();
        var target = SheetFit(bounds);
        AnimateViewport(target.Scale, target.Offset, TimeSpan.FromMilliseconds(280));
    }

    /// <summary>
    /// Fit into the drawing area of the sheet: inside the rulers, clear of the floating tool palette on the left,
    /// and above the band that holds dimension lines and the figure legend.
    /// </summary>
    private (double Scale, Vec2 Offset) SheetFit(Bounds world)
    {
        var vp = _editor!.Viewport;
        if (world.IsEmpty || vp.ScreenWidth <= 0 || vp.ScreenHeight <= 0) return vp.ComputeFit(world);
        const double left = 104, top = 120, right = 72, bottom = 200;
        var areaW = Math.Max(120, vp.ScreenWidth - left - right);
        var areaH = Math.Max(120, vp.ScreenHeight - top - bottom);
        var w = Math.Max(world.Width, 1.0);
        var h = Math.Max(world.Height, 1.0);
        var scale = Math.Clamp(Math.Min(areaW * 0.9 / w, areaH * 0.9 / h), Core.Viewport.Viewport.MinScale, Core.Viewport.Viewport.MaxScale);
        var c = world.Center;
        return (scale, new Vec2(left + areaW / 2 - c.X * scale, top + areaH / 2 + c.Y * scale));
    }

    private void AnimateZoom(Vec2 anchor, double newScale)
    {
        if (_editor is null) return;
        var vp = _editor.Viewport;
        var s = Math.Clamp(newScale, Core.Viewport.Viewport.MinScale, Core.Viewport.Viewport.MaxScale);
        // Compute the target offset that keeps the anchor fixed, then ease to it.
        var worldAnchor = vp.ToWorld(anchor);
        var offset = new Vec2(anchor.X - worldAnchor.X * s, anchor.Y + worldAnchor.Y * s);
        AnimateViewport(s, offset, TimeSpan.FromMilliseconds(120));
    }

    private void AnimateViewport(double scale, Vec2 offset, TimeSpan duration)
    {
        if (_editor is null) return;
        var vp = _editor.Viewport;
        if (!MotionSettings.AnimationsEnabled || duration == TimeSpan.Zero)
        {
            vp.Set(scale, offset);
            return;
        }
        _animFrom = (vp.Scale, vp.Offset);
        _animTo = (scale, offset);
        _animStart = DateTime.UtcNow;
        _animDuration = duration;
        _animTimer ??= CreateTimer();
        if (!_animTimer.IsEnabled) _animTimer.Start();
    }

    private DispatcherTimer CreateTimer()
    {
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        t.Tick += (_, _) =>
        {
            if (_editor is null) { t.Stop(); return; }
            var k = Math.Clamp((DateTime.UtcNow - _animStart).TotalMilliseconds / _animDuration.TotalMilliseconds, 0, 1);
            var e = MotionSettings.EaseInOut(k);
            // Interpolate scale geometrically so zoom feels linear.
            var scale = _animFrom.Scale * Math.Pow(_animTo.Scale / _animFrom.Scale, e);
            var offset = Vec2.Lerp(_animFrom.Offset, _animTo.Offset, e);
            _editor.Viewport.Set(scale, offset);
            if (k >= 1) t.Stop();
        };
        return t;
    }
}
