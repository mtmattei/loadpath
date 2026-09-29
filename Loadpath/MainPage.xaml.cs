using Loadpath.Commands;
using Loadpath.Core.Editing;
using Loadpath.Core.Geometry;
using Loadpath.Core.Serialization;
using Loadpath.Presentation;
using Loadpath.Services;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;
using Windows.System;

namespace Loadpath;

/// <summary>
/// The single page. Composes the shell, routes keyboard shortcuts, hosts the context menu, floating bar,
/// toast, section drag-and-drop, and the file/autosave lifecycle.
/// </summary>
public sealed partial class MainPage : Page
{
    private readonly FileService _files = new(() => App.MainWindow);
    private readonly SettingsService _settings = new();
    private readonly SettingsService.Model _settingsModel;
    private DispatcherTimer? _autosaveTimer;
    private DispatcherTimer? _toastTimer;
    private Section? _draggingSection;
    private int? _dropTargetMember;

    public MainPage()
    {
        Editor = new EditorViewModel();
        _settingsModel = Environment.GetEnvironmentVariable("LOADPATH_RESET") == "1" ? new SettingsService.Model() : _settings.Load();
        _settings.Apply(_settingsModel, Editor.Options);
        RegisterFileCommands();
        InitializeComponent();

        Editor.ToastRequested += (_, msg) => ShowToast(msg);
        Editor.SelectionChanged += (_, _) => { UpdateFloatingBar(); FadeInspector(); };
        Editor.ViewportChanged += (_, _) => UpdateFloatingBar();
        Editor.AnalysisChanged += (_, _) => { UpdateFloatingBar(); ScheduleAutosave(); };
        Editor.Options.PropertyChanged += (_, _) => { ApplyInspectorVisibility(); PersistSettings(); };
        Editor.Interaction.ToolChanged += (_, _) => UpdateFloatingBar();
        Workspace.ContextMenuRequested += OnContextMenuRequested;
        Workspace.FocusRequested += (_, _) => KeySink.Focus(FocusState.Programmatic);
        Inspector.SectionDragStarted += OnSectionDragStarted;
        // handledEventsToo: the canvas marks its pointer events handled, and the drag must still reach the host.
        Root.AddHandler(PointerMovedEvent, new PointerEventHandler(OnHostPointerMoved), true);
        Root.AddHandler(PointerReleasedEvent, new PointerEventHandler(OnHostPointerReleased), true);
        Root.AddHandler(PointerCaptureLostEvent, new PointerEventHandler(OnHostPointerReleased), true);

        Root.AddHandler(KeyDownEvent, new KeyEventHandler(OnRootKeyDown), true);
#if DEBUG
        if (Environment.GetEnvironmentVariable("LOADPATH_TRACE") is { } tracePath)
        {
            Root.AddHandler(PointerWheelChangedEvent, new PointerEventHandler((_, e) => File.AppendAllText(tracePath, $"page wheel {e.GetCurrentPoint(Root).Properties.MouseWheelDelta} src={e.OriginalSource?.GetType().Name}\n")), true);
            Root.AddHandler(PointerPressedEvent, new PointerEventHandler((_, e) => File.AppendAllText(tracePath, $"page press src={e.OriginalSource?.GetType().Name} pid={e.Pointer.PointerId}\n")), true);
        }
#endif
        Root.AddHandler(KeyUpEvent, new KeyEventHandler(OnRootKeyUp), true);
        Loaded += OnLoaded;
        ApplyInspectorVisibility();
    }

    public EditorViewModel Editor { get; }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        KeySink.Focus(FocusState.Programmatic);
        RestoreOrSeed();
        // The window is sized through PreferredLaunchViewSize at launch. The X11 host can still miss a configure
        // event and lay out at the old size, so the size is re-asserted until the layout agrees with the frame.
        if (App.MainWindow is { } window && Environment.GetEnvironmentVariable("LOADPATH_NO_RESIZE") != "1")
        {
            var attempts = 0;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            timer.Tick += (_, _) =>
            {
                attempts++;
                try
                {
                    var size = window.AppWindow.Size;
                    var layoutAgrees = Math.Abs(Root.ActualWidth - size.Width) < 4 && Math.Abs(Root.ActualHeight - size.Height) < 4;
                    if (size.Width >= App.LaunchWidth - 2 && size.Height >= App.LaunchHeight - 2 && layoutAgrees) { timer.Stop(); return; }
                    if (size.Width >= App.LaunchWidth - 2 && size.Height >= App.LaunchHeight - 2)
                    {
                        // Frame is right but the layout is stale: nudge by a pixel so the host re-reads its size.
                        window.AppWindow.Resize(new Windows.Graphics.SizeInt32 { Width = size.Width + (attempts % 2 == 0 ? 1 : -1), Height = size.Height });
                    }
                    else
                    {
                        window.AppWindow.Resize(new Windows.Graphics.SizeInt32 { Width = App.LaunchWidth, Height = App.LaunchHeight });
                    }
                }
                catch { timer.Stop(); }
                if (attempts >= 10) timer.Stop();
                timer.Interval = TimeSpan.FromMilliseconds(400);
            };
            timer.Start();
        }
        Root.SizeChanged += (_, args) => ApplyResponsiveLayout(args.NewSize.Width);
        ApplyResponsiveLayout(ActualWidth);
    }

    private bool _autoCollapsedInspector;

    /// <summary>Below 1100 px the inspector column gives way to the canvas; it comes back when there is room.</summary>
    private void ApplyResponsiveLayout(double width)
    {
        if (width <= 0) return;
        if (width < 1100 && Editor.Options.InspectorVisible)
        {
            _autoCollapsedInspector = true;
            Editor.Options.InspectorVisible = false;
        }
        else if (width >= 1100 && _autoCollapsedInspector && !Editor.Options.InspectorVisible)
        {
            _autoCollapsedInspector = false;
            Editor.Options.InspectorVisible = true;
        }
        Rail.Visibility = width < 640 ? Visibility.Collapsed : Visibility.Visible;
    }

    // ---- startup: autosave restore, DEBUG fixture hooks ----

    private void RestoreOrSeed()
    {
        var sample = Environment.GetEnvironmentVariable("LOADPATH_SAMPLE");
        if (!string.IsNullOrEmpty(sample))
        {
            Editor.LoadSample(sample);
        }
        else if (Environment.GetEnvironmentVariable("LOADPATH_RESET") != "1" && File.Exists(_settings.AutosavePath))
        {
            try
            {
                var snap = DocumentSerializer.Deserialize(File.ReadAllText(_settings.AutosavePath));
                if (snap.Nodes.Count > 0) Editor.LoadSnapshot(snap, "Restore", _settingsModel.LastFilePath);
            }
            catch { /* a bad autosave is discarded */ }
        }
#if DEBUG
        ApplyFixtureHooks();
#endif
    }

#if DEBUG
    /// <summary>Env-driven start states so headless captures are deterministic without a pointer.</summary>
    private void ApplyFixtureHooks()
    {
        var select = Environment.GetEnvironmentVariable("LOADPATH_SELECT");
        if (!string.IsNullOrEmpty(select))
        {
            var refs = select.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim())
                .Select(s => s[0] is 'n' or 'N' ? ElementRef.Node(int.Parse(s[1..])) : ElementRef.Member(int.Parse(s[1..])));
            Editor.Selection.Replace(refs);
        }
        var mode = Environment.GetEnvironmentVariable("LOADPATH_MODE");
        if (!string.IsNullOrEmpty(mode))
        {
            foreach (var m in mode.Split(','))
            {
                switch (m.Trim().ToLowerInvariant())
                {
                    case "utilization": Editor.Options.DisplayMode = DisplayMode.Utilization; break;
                    case "forces": Editor.Options.DisplayMode = DisplayMode.Forces; break;
                    case "deflection": Editor.Options.ShowDeflection = true; break;
                    case "reactions": Editor.Options.ShowReactions = true; break;
                    case "nolabels": Editor.Options.ShowLabels = false; break;
                }
            }
        }
        var tool = Environment.GetEnvironmentVariable("LOADPATH_TOOL");
        if (!string.IsNullOrEmpty(tool) && Enum.TryParse<ToolKind>(tool, true, out var kind)) Editor.Interaction.SetTool(kind);
        if (Environment.GetEnvironmentVariable("LOADPATH_PALETTE") == "1") Editor.IsPaletteOpen = true;
        var toast = Environment.GetEnvironmentVariable("LOADPATH_TOAST");
        if (!string.IsNullOrEmpty(toast)) ShowToast(toast);
        var edit = Environment.GetEnvironmentVariable("LOADPATH_EDIT");
        if (edit == "unsupported" && Editor.Document.Nodes.Count > 0)
        {
            foreach (var n in Editor.Document.Nodes.Where(n => n.HasSupport).ToList()) Editor.History.Do(new SetSupportEdit(n.Id, SupportKind.None));
        }
        if (edit == "overload" && Editor.Document.Nodes.Count > 0)
        {
            foreach (var n in Editor.Document.Nodes.Where(n => n.HasLoad).ToList()) Editor.History.Do(new SetLoadEdit(n.Id, n.Load * 6));
        }
    }
#endif

    // ---- keyboard ----

    private static bool IsTextInputFocused(XamlRoot root)
    {
        var focused = FocusManager.GetFocusedElement(root);
        return focused is TextBox or PasswordBox or RichEditBox;
    }

    private void OnRootKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var ctrl = IsDown(VirtualKey.Control);
        var shift = IsDown(VirtualKey.Shift);
        var alt = IsDown(VirtualKey.Menu);
        var key = e.Key;
#if DEBUG
        if (Environment.GetEnvironmentVariable("LOADPATH_TRACE") is { } tracePath)
            File.AppendAllText(tracePath, $"key {key} ctrl={ctrl} focused={(XamlRoot is { } fr ? FocusManager.GetFocusedElement(fr)?.GetType().Name : "?")} src={e.OriginalSource?.GetType().Name}\n");
#endif

        if (XamlRoot is { } xr && IsTextInputFocused(xr))
        {
            if (key == VirtualKey.Escape && !Editor.IsPaletteOpen) { KeySink.Focus(FocusState.Programmatic); e.Handled = true; }
            return;
        }
        if (Editor.IsPaletteOpen)
        {
            if (key == VirtualKey.Escape) { Editor.IsPaletteOpen = false; e.Handled = true; }
            return;
        }

        // Tools get first refusal (Esc cancels a chain or drag).
        if (!ctrl && !alt && Editor.Interaction.KeyDown(key)) { e.Handled = true; return; }

        var command = Editor.Commands.Resolve(key, ctrl, shift, alt);
        if (command is null) return;
        // Tool switches wait for a gesture to end.
        if (command.Id.StartsWith("tool.") && Editor.Interaction.IsBusy) return;
        if (command.TryExecute()) e.Handled = true;
    }

    private void OnRootKeyUp(object sender, KeyRoutedEventArgs e) => Editor.Interaction.KeyUp(e.Key);

    private static bool IsDown(VirtualKey key) =>
        Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    // ---- context menu ----

    private void OnContextMenuRequested(object? sender, (Point Position, HitResult Hit) args)
    {
        var menu = new MenuFlyout { Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft };
        void Add(string id, string? title = null)
        {
            var c = Editor.Commands.Find(id);
            if (c is null) return;
            var item = new MenuFlyoutItem { Text = title ?? c.Title, IsEnabled = c.CanExecute, KeyboardAcceleratorTextOverride = c.ShortcutDisplay.Split('|')[0] };
            item.Click += (_, _) => { if (!c.TryExecute()) Editor.Toast($"{c.Title} is not available"); };
            menu.Items.Add(item);
        }
        void Sep() => menu.Items.Add(new MenuFlyoutSeparator());

        switch (args.Hit.Kind)
        {
            case HitKind.Node:
            case HitKind.LoadHandle:
                Add("structure.pin", "Pin support");
                Add("structure.roller", "Roller support");
                Add("structure.rollerX", "Vertical roller");
                Add("structure.noSupport", "No support");
                Sep();
                Add("structure.clearLoad");
                Add("edit.duplicate");
                Add("edit.delete");
                break;
            case HitKind.Member:
                var sub = new MenuFlyoutSubItem { Text = "Set section" };
                foreach (var s in Section.Library)
                {
                    var c = Editor.Commands[$"section.{s.Id}"];
                    var item = new MenuFlyoutItem { Text = s.Name };
                    item.Click += (_, _) => c.TryExecute();
                    sub.Items.Add(item);
                }
                menu.Items.Add(sub);
                Add("structure.split");
                Sep();
                Add("edit.duplicate");
                Add("edit.delete");
                break;
            default:
                var addNode = new MenuFlyoutItem { Text = "Add node here", KeyboardAcceleratorTextOverride = "N" };
                var world = Editor.Interaction.SnapPoint(Editor.Viewport.ToWorld(new Vec2(args.Position.X, args.Position.Y)), false).Point;
                addNode.Click += (_, _) => { var add = new AddNodeEdit(world); Editor.History.Do(add); Editor.Selection.Set(ElementRef.Node(add.NodeId)); };
                menu.Items.Add(addNode);
                Add("edit.selectAll");
                Sep();
                Add("view.fit");
                Add("view.grid");
                Add("view.snap");
                Sep();
                Add("view.palette");
                break;
        }
        menu.ShowAt(Workspace, args.Position);
    }

    // ---- floating selection bar ----

    private void UpdateFloatingBar()
    {
        var sel = Editor.Selection;
        if (sel.IsEmpty || Editor.Interaction.ActiveTool != ToolKind.Select || Editor.Interaction.IsBusy)
        {
            FloatingBar.Visibility = Visibility.Collapsed;
            return;
        }
        // Anchor above the selection's screen bounds.
        var bounds = Bounds.Empty;
        foreach (var id in Editor.SelectedNodeIdsIncludingMemberEnds())
        {
            if (Editor.Document.FindNode(id) is { } n) bounds = bounds.Include(Editor.Viewport.ToScreen(n.Position));
        }
        if (bounds.IsEmpty) { FloatingBar.Visibility = Visibility.Collapsed; return; }
        FloatSupport.Visibility = sel.NodeCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        FloatLoad.Visibility = sel.NodeCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        FloatSplit.Visibility = sel.Single is { IsMember: true } ? Visibility.Visible : Visibility.Collapsed;
        FloatingBar.Visibility = Visibility.Visible;
        FloatingBar.UpdateLayout();
        var w = FloatingBar.ActualWidth > 0 ? FloatingBar.ActualWidth : 150;
        var x = Math.Clamp(bounds.Center.X - w / 2, 8, Math.Max(8, WorkspaceHost.ActualWidth - w - 8));
        var y = bounds.Min.Y - 52;
        if (y < 8) y = bounds.Max.Y + 28;
        FloatingBar.Margin = new Thickness(x, y, 0, 0);
    }

    private void OnFloatSupport(object sender, RoutedEventArgs e)
    {
        var ids = Editor.Selection.NodeIds.ToList();
        if (ids.Count == 0) return;
        var next = Editor.Document.GetNode(ids[0]).Support.Next();
        Editor.SetSupportOnSelection(next);
    }

    private void OnFloatLoad(object sender, RoutedEventArgs e)
    {
        var ids = Editor.Selection.NodeIds.ToList();
        if (ids.Count == 0) return;
        Editor.History.BeginTransaction("Add load");
        foreach (var id in ids) Editor.History.Do(new SetLoadEdit(id, new Vec2(0, -10)));
        Editor.History.CommitTransaction();
    }

    private void OnFloatSplit(object sender, RoutedEventArgs e) => Editor.SplitSelectedMember();
    private void OnFloatDuplicate(object sender, RoutedEventArgs e) => Editor.DuplicateSelection();
    private void OnFloatDelete(object sender, RoutedEventArgs e) => Editor.DeleteSelection();

    // ---- section drag-and-drop (inspector chip → member on canvas) ----

    private void OnSectionDragStarted(object? sender, Section section)
    {
        _draggingSection = section;
        DragGhostText.Text = section.Name;
        DragGhost.Margin = new Thickness(WorkspaceHost.ActualWidth - 120, 12, 0, 0);
        DragGhost.Visibility = Visibility.Visible;
        Editor.Interaction.Hint = "Drop on a member to apply the section · Esc cancels";
    }

    private void OnHostPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_draggingSection is null) return;
        var p = e.GetCurrentPoint(WorkspaceHost).Position;
        DragGhost.Margin = new Thickness(p.X + 12, p.Y + 12, 0, 0);
        var canvasPoint = e.GetCurrentPoint(Workspace).Position;
        var hit = Editor.Interaction.Hit(new Vec2(canvasPoint.X, canvasPoint.Y), includeLoadHandles: false);
        var target = hit.Kind == HitKind.Member ? hit.Id : (int?)null;
        if (target != _dropTargetMember)
        {
            _dropTargetMember = target;
            Editor.Interaction.Overlay.HighlightMember = target;
            Editor.RequestRender();
        }
    }

    private void OnHostPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_draggingSection is null) return;
        var section = _draggingSection;
        _draggingSection = null;
        DragGhost.Visibility = Visibility.Collapsed;
        Editor.Interaction.Overlay.HighlightMember = null;
        if (_dropTargetMember is { } id)
        {
            Editor.History.Do(new SetSectionEdit([id], section));
            Editor.Selection.Set(ElementRef.Member(id));
            ShowToast($"{section.Name} applied to member {id}");
        }
        _dropTargetMember = null;
        Editor.Interaction.Hint = "";
        Editor.RequestRender();
    }

    // ---- inspector swap: a quick fade so the eye reads a change, not a flash ----

    private InspectorMode _lastInspectorMode = InspectorMode.Summary;

    private void FadeInspector()
    {
        var mode = Editor.Inspector.Mode;
        if (mode == _lastInspectorMode) return;
        _lastInspectorMode = mode;
        if (!Loadpath.Workspace.MotionSettings.AnimationsEnabled) return;
        var sb = new Storyboard();
        var fade = new DoubleAnimationUsingKeyFrames();
        fade.KeyFrames.Add(new DiscreteDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero), Value = 0.45 });
        fade.KeyFrames.Add(new SplineDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(150)), Value = 1, KeySpline = new KeySpline { ControlPoint1 = new Point(0.22, 1), ControlPoint2 = new Point(0.36, 1) } });
        Storyboard.SetTarget(fade, Inspector);
        Storyboard.SetTargetProperty(fade, "Opacity");
        sb.Children.Add(fade);
        sb.Begin();
    }

    // ---- toast ----

    private void ShowToast(string message)
    {
        ToastText.Text = message;
        Toast.Opacity = 1;
        ToastTranslate.Y = 0;
        _toastTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2400) };
        _toastTimer.Stop();
        _toastTimer.Tick -= OnToastTick;
        _toastTimer.Tick += OnToastTick;
        _toastTimer.Start();
        if (Loadpath.Workspace.MotionSettings.AnimationsEnabled)
        {
            var sb = new Storyboard();
            var fade = new DoubleAnimation { From = 0, To = 1, Duration = new Duration(TimeSpan.FromMilliseconds(200)) };
            Storyboard.SetTarget(fade, Toast);
            Storyboard.SetTargetProperty(fade, "Opacity");
            var rise = new DoubleAnimation { From = 6, To = 0, Duration = new Duration(TimeSpan.FromMilliseconds(200)) };
            Storyboard.SetTarget(rise, ToastTranslate);
            Storyboard.SetTargetProperty(rise, "Y");
            sb.Children.Add(fade);
            sb.Children.Add(rise);
            sb.Begin();
        }
    }

    private void OnToastTick(object? sender, object e)
    {
        _toastTimer?.Stop();
        Toast.Opacity = 0;
    }

    // ---- inspector visibility ----

    private void ApplyInspectorVisibility()
    {
        if (InspectorColumn is null) return;
        var visible = Editor.Options.InspectorVisible;
        InspectorColumn.Width = visible ? new GridLength(280) : new GridLength(0);
        InspectorHost.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---- samples ----

    private void OnSampleWarren(object sender, RoutedEventArgs e) => Editor.LoadSample("warren");
    private void OnSampleCantilever(object sender, RoutedEventArgs e) => Editor.LoadSample("cantilever");
    private void OnSampleRoof(object sender, RoutedEventArgs e) => Editor.LoadSample("roof");

    // ---- files, autosave, settings ----

    private void RegisterFileCommands()
    {
        Editor.Commands.Add(new AppCommand("file.new", "New structure", "File", "Ctrl+N", () => _ = NewAsync(), icon: "new"));
        Editor.Commands.Add(new AppCommand("file.open", "Open…", "File", "Ctrl+O", () => _ = OpenAsync(), icon: "folder"));
        Editor.Commands.Add(new AppCommand("file.save", "Save", "File", "Ctrl+S", () => _ = SaveAsync(false), icon: "save"));
        Editor.Commands.Add(new AppCommand("file.saveAs", "Save as…", "File", "Ctrl+Shift+S", () => _ = SaveAsync(true), icon: "save"));
    }

    private async Task NewAsync()
    {
        if (Editor.IsDirty && !await ConfirmDiscardAsync()) return;
        Editor.NewDocument();
    }

    private async Task OpenAsync()
    {
        if (Editor.IsDirty && !await ConfirmDiscardAsync()) return;
        try
        {
            var result = await _files.OpenAsync();
            if (result is null) { ShowToast("Nothing opened"); return; }
            Editor.LoadSnapshot(result.Value.Snapshot, "Open", result.Value.Path);
            _settingsModel.LastFilePath = result.Value.Path;
            PersistSettings();
            ShowToast($"Opened {Path.GetFileName(result.Value.Path)}");
        }
        catch (Exception ex)
        {
            ShowToast($"Could not open: {ex.Message}");
        }
    }

    private async Task SaveAsync(bool ask)
    {
        try
        {
            var path = await _files.SaveAsync(Editor.Document, Editor.FilePath, ask);
            if (path is null) return;
            Editor.FilePath = path;
            Editor.Document.Name = Path.GetFileNameWithoutExtension(path);
            Editor.DocumentName = Editor.Document.Name;
            Editor.MarkSaved();
            _settingsModel.LastFilePath = path;
            PersistSettings();
            ShowToast($"Saved to {path}");
        }
        catch (Exception ex)
        {
            ShowToast($"Could not save: {ex.Message}");
        }
    }

    private async Task<bool> ConfirmDiscardAsync()
    {
        var dialog = new ContentDialog
        {
            Title = "Unsaved changes",
            Content = $"“{Editor.DocumentName}” has changes that are not saved.",
            PrimaryButtonText = "Save",
            SecondaryButtonText = "Discard",
            CloseButtonText = "Cancel",
            XamlRoot = XamlRoot,
        };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary) { await SaveAsync(false); return !Editor.IsDirty; }
        return result == ContentDialogResult.Secondary;
    }

    private void ScheduleAutosave()
    {
        _autosaveTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
        _autosaveTimer.Stop();
        _autosaveTimer.Tick -= OnAutosaveTick;
        _autosaveTimer.Tick += OnAutosaveTick;
        _autosaveTimer.Start();
    }

    private void OnAutosaveTick(object? sender, object e)
    {
        _autosaveTimer?.Stop();
        try { File.WriteAllText(_settings.AutosavePath, DocumentSerializer.Serialize(Editor.Document)); } catch { /* best effort */ }
    }

    private void PersistSettings()
    {
        _settings.Capture(Editor.Options, _settingsModel);
        _settings.Save(_settingsModel);
    }
}
