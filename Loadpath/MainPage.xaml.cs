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
        Engine = new EditorEngine();
        _settingsModel = Environment.GetEnvironmentVariable("LOADPATH_RESET") == "1" ? new SettingsService.Model() : _settings.Load();
        _settings.Apply(_settingsModel, Engine.Options);
        RegisterFileCommands();
        InitializeComponent();

        // MVUX: the generated view model is the binding surface; the engine stays reachable for pointer-level code.
        var viewModel = new EditorViewModel(Engine);
        Model = viewModel.Model;
        DataContext = viewModel;
        Workspace.Editor = Engine;
        Outline.Engine = Engine;
        Palette.Model = Model;
        Model.IsPaletteOpen.ForEach(async (open, ct) =>
        {
            if (open) Palette.OnOpened();
            else KeySink.Focus(FocusState.Programmatic);
        });

        Engine.ToastRequested += (_, msg) => ShowToast(msg);
        Engine.SelectionChanged += (_, _) => { UpdateFloatingBar(); FadeInspector(); EnsureKeyboardTarget(); };
        Engine.ViewportChanged += (_, _) => UpdateFloatingBar();
        Engine.AnalysisChanged += (_, _) => { UpdateFloatingBar(); ScheduleAutosave(); };
        Engine.OptionsChanged += (_, _) => { ApplyInspectorVisibility(); PersistSettings(); };
        Engine.Interaction.ToolChanged += (_, _) => UpdateFloatingBar();
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
            Root.GotFocus += (_, e) => File.AppendAllText(tracePath, $"focus -> {e.OriginalSource?.GetType().Name}\n");
        }
#endif
        Root.AddHandler(KeyUpEvent, new KeyEventHandler(OnRootKeyUp), true);
        Loaded += OnLoaded;
        ApplyInspectorVisibility();
    }

    public EditorEngine Engine { get; }
    public EditorModel Model { get; }

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
        if (width < 1100 && Engine.Options.InspectorVisible)
        {
            _autoCollapsedInspector = true;
            Engine.Options.InspectorVisible = false;
        }
        else if (width >= 1100 && _autoCollapsedInspector && !Engine.Options.InspectorVisible)
        {
            _autoCollapsedInspector = false;
            Engine.Options.InspectorVisible = true;
        }
        // The structure outline yields next, below 1240 px; the floating tool palette only on very narrow windows.
        var showStructure = width >= 1240;
        StructureColumn.Width = showStructure ? new GridLength(240) : new GridLength(0);
        StructureHost.Visibility = showStructure ? Visibility.Visible : Visibility.Collapsed;
        Rail.Visibility = width < 640 ? Visibility.Collapsed : Visibility.Visible;
    }

    // ---- startup: autosave restore, DEBUG fixture hooks ----

    private void RestoreOrSeed()
    {
        var sample = Environment.GetEnvironmentVariable("LOADPATH_SAMPLE");
        var fixtureFile = Environment.GetEnvironmentVariable("LOADPATH_FILE");
        if (!string.IsNullOrEmpty(sample))
        {
            Engine.LoadSample(sample);
        }
#if DEBUG
        else if (!string.IsNullOrEmpty(fixtureFile) && File.Exists(fixtureFile))
        {
            Engine.LoadSnapshot(DocumentSerializer.Deserialize(File.ReadAllText(fixtureFile)), "Open", null);
        }
#endif
        else if (Environment.GetEnvironmentVariable("LOADPATH_RESET") != "1" && File.Exists(_settings.AutosavePath))
        {
            try
            {
                var snap = DocumentSerializer.Deserialize(File.ReadAllText(_settings.AutosavePath));
                if (snap.Nodes.Count > 0) Engine.LoadSnapshot(snap, "Restore", _settingsModel.LastFilePath);
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
            Engine.Selection.Replace(refs);
        }
        var mode = Environment.GetEnvironmentVariable("LOADPATH_MODE");
        if (!string.IsNullOrEmpty(mode))
        {
            foreach (var m in mode.Split(','))
            {
                switch (m.Trim().ToLowerInvariant())
                {
                    case "utilization": Engine.Options.DisplayMode = DisplayMode.Utilization; break;
                    case "forces": Engine.Options.DisplayMode = DisplayMode.Forces; break;
                    case "deflection": Engine.Options.ShowDeflection = true; break;
                    case "reactions": Engine.Options.ShowReactions = true; break;
                    case "nolabels": Engine.Options.ShowLabels = false; break;
                    case "labels": Engine.Options.ShowLabels = true; break;
                }
            }
        }
        var tool = Environment.GetEnvironmentVariable("LOADPATH_TOOL");
        if (!string.IsNullOrEmpty(tool) && Enum.TryParse<ToolKind>(tool, true, out var kind)) Engine.Interaction.SetTool(kind);
        if (Environment.GetEnvironmentVariable("LOADPATH_PALETTE") == "1") _ = Model.TogglePalette(default);
        var toast = Environment.GetEnvironmentVariable("LOADPATH_TOAST");
        if (!string.IsNullOrEmpty(toast)) ShowToast(toast);
        var edit = Environment.GetEnvironmentVariable("LOADPATH_EDIT");
        if (edit == "unsupported" && Engine.Document.Nodes.Count > 0)
        {
            foreach (var n in Engine.Document.Nodes.Where(n => n.HasSupport).ToList()) Engine.History.Do(new SetSupportEdit(n.Id, SupportKind.None));
        }
        if (edit == "overload" && Engine.Document.Nodes.Count > 0)
        {
            foreach (var n in Engine.Document.Nodes.Where(n => n.HasLoad).ToList()) Engine.History.Do(new SetLoadEdit(n.Id, n.Load * 6));
        }
    }
#endif

    // ---- keyboard ----

    /// <summary>
    /// When the focused element leaves the tree (an inspector panel collapses, the palette closes), keyboard events
    /// have no target. Focus returns to the key sink so shortcuts keep working.
    /// </summary>
    private void EnsureKeyboardTarget()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (XamlRoot is not { } root) return;
            var focused = FocusManager.GetFocusedElement(root);
            var detached = focused is FrameworkElement fe && (fe.XamlRoot is null || !fe.IsLoaded || fe.Visibility == Visibility.Collapsed);
            if (focused is null || detached) KeySink.Focus(FocusState.Programmatic);
        });
    }

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
            // The palette shortcut works from anywhere, including a focused number field.
            if (ctrl && key == VirtualKey.K && Palette.Visibility != Visibility.Visible) { _ = Model.TogglePalette(default); e.Handled = true; return; }
            if (key == VirtualKey.Escape && Palette.Visibility != Visibility.Visible) { KeySink.Focus(FocusState.Programmatic); e.Handled = true; }
            return;
        }
        if (Palette.Visibility == Visibility.Visible)
        {
            if (key == VirtualKey.Escape) { _ = Model.ClosePalette(default); e.Handled = true; }
            return;
        }

        // Tools get first refusal (Esc cancels a chain or drag).
        if (!ctrl && !alt && Engine.Interaction.KeyDown(key)) { e.Handled = true; return; }

        var command = Engine.Commands.Resolve(key, ctrl, shift, alt);
        if (command is null) return;
        // Tool switches wait for a gesture to end.
        if (command.Id.StartsWith("tool.") && Engine.Interaction.IsBusy) return;
        if (command.TryExecute()) e.Handled = true;
    }

    private void OnRootKeyUp(object sender, KeyRoutedEventArgs e) => Engine.Interaction.KeyUp(e.Key);

    private static bool IsDown(VirtualKey key) =>
        Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    // ---- context menu ----

    private void OnContextMenuRequested(object? sender, (Point Position, HitResult Hit) args)
    {
        var menu = new MenuFlyout { Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft };
        void Add(string id, string? title = null)
        {
            var c = Engine.Commands.Find(id);
            if (c is null) return;
            var item = new MenuFlyoutItem { Text = title ?? c.Title, IsEnabled = c.CanExecute, KeyboardAcceleratorTextOverride = c.ShortcutDisplay.Split('|')[0] };
            item.Click += (_, _) => { if (!c.TryExecute()) Engine.Toast($"{c.Title} is not available"); };
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
                    var c = Engine.Commands[$"section.{s.Id}"];
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
                var world = Engine.Interaction.SnapPoint(Engine.Viewport.ToWorld(new Vec2(args.Position.X, args.Position.Y)), false).Point;
                addNode.Click += (_, _) => { var add = new AddNodeEdit(world); Engine.History.Do(add); Engine.Selection.Set(ElementRef.Node(add.NodeId)); };
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
        var sel = Engine.Selection;
        if (sel.IsEmpty || Engine.Interaction.ActiveTool != ToolKind.Select || Engine.Interaction.IsBusy)
        {
            FloatingBar.Visibility = Visibility.Collapsed;
            return;
        }
        // Anchor above the selection's screen bounds.
        var bounds = Bounds.Empty;
        foreach (var id in Engine.SelectedNodeIdsIncludingMemberEnds())
        {
            if (Engine.Document.FindNode(id) is { } n) bounds = bounds.Include(Engine.Viewport.ToScreen(n.Position));
        }
        if (bounds.IsEmpty) { FloatingBar.Visibility = Visibility.Collapsed; return; }
        FloatSupport.Visibility = sel.NodeCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        FloatLoad.Visibility = sel.NodeCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        FloatSplit.Visibility = sel.Single is { IsMember: true } ? Visibility.Visible : Visibility.Collapsed;
        FloatLabel.Text = sel.Single is { } one ? (one.IsNode ? $"N{one.Id}" : $"M{one.Id}") : $"{sel.Count} sel";
        FloatingBar.Visibility = Visibility.Visible;
        FloatingBar.UpdateLayout();
        var w = FloatingBar.ActualWidth > 0 ? FloatingBar.ActualWidth : 150;
        // Up and to the right of the selection, clear of its load arrow; flip left when it would leave the sheet.
        var x = bounds.Max.X + 24;
        if (x + w > WorkspaceHost.ActualWidth - 8) x = bounds.Min.X - 24 - w;
        x = Math.Clamp(x, 32, Math.Max(32, WorkspaceHost.ActualWidth - w - 8));
        var y = bounds.Min.Y - 80;
        if (y < 32) y = bounds.Max.Y + 28;
        FloatingBar.Margin = new Thickness(x, y, 0, 0);
    }




    // ---- section drag-and-drop (inspector chip → member on canvas) ----

    private void OnSectionDragStarted(object? sender, Section section)
    {
        _draggingSection = section;
        DragGhostText.Text = section.Name;
        DragGhost.Margin = new Thickness(WorkspaceHost.ActualWidth - 120, 12, 0, 0);
        DragGhost.Visibility = Visibility.Visible;
        Engine.Interaction.Hint = "Drop on a member to apply the section · Esc cancels";
    }

    private void OnHostPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_draggingSection is null) return;
        var p = e.GetCurrentPoint(WorkspaceHost).Position;
        DragGhost.Margin = new Thickness(p.X + 12, p.Y + 12, 0, 0);
        var canvasPoint = e.GetCurrentPoint(Workspace).Position;
        var hit = Engine.Interaction.Hit(new Vec2(canvasPoint.X, canvasPoint.Y), includeLoadHandles: false);
        var target = hit.Kind == HitKind.Member ? hit.Id : (int?)null;
        if (target != _dropTargetMember)
        {
            _dropTargetMember = target;
            Engine.Interaction.Overlay.HighlightMember = target;
            Engine.RequestRender();
        }
    }

    private void OnHostPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_draggingSection is null) return;
        var section = _draggingSection;
        _draggingSection = null;
        DragGhost.Visibility = Visibility.Collapsed;
        Engine.Interaction.Overlay.HighlightMember = null;
        if (_dropTargetMember is { } id)
        {
            Engine.History.Do(new SetSectionEdit([id], section));
            Engine.Selection.Set(ElementRef.Member(id));
            ShowToast($"{section.Name} applied to member {id}");
        }
        _dropTargetMember = null;
        Engine.Interaction.Hint = "";
        Engine.RequestRender();
    }

    // ---- inspector swap: a quick fade so the eye reads a change, not a flash ----

    private int _lastInspectorShape;

    private void FadeInspector()
    {
        var sel = Engine.Selection;
        var shape = sel.IsEmpty ? 0 : sel.Single is { IsNode: true } ? 1 : sel.Single is { IsMember: true } ? 2 : 3;
        if (shape == _lastInspectorShape) return;
        _lastInspectorShape = shape;
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
        var visible = Engine.Options.InspectorVisible;
        InspectorColumn.Width = visible ? new GridLength(336) : new GridLength(0);
        InspectorHost.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---- samples ----


    // ---- files, autosave, settings ----

    private void RegisterFileCommands()
    {
        Engine.Commands.Add(new AppCommand("file.new", "New structure", "File", "Ctrl+N", () => _ = NewAsync(), icon: "new"));
        Engine.Commands.Add(new AppCommand("file.open", "Open…", "File", "Ctrl+O", () => _ = OpenAsync(), icon: "folder"));
        Engine.Commands.Add(new AppCommand("file.save", "Save", "File", "Ctrl+S", () => _ = SaveAsync(false), icon: "save"));
        Engine.Commands.Add(new AppCommand("file.saveAs", "Save as…", "File", "Ctrl+Shift+S", () => _ = SaveAsync(true), icon: "save"));
    }

    private async Task NewAsync()
    {
        if (Engine.IsDirty && !await ConfirmDiscardAsync()) return;
        Engine.NewDocument();
    }

    private async Task OpenAsync()
    {
        if (Engine.IsDirty && !await ConfirmDiscardAsync()) return;
        try
        {
            var result = await _files.OpenAsync();
            if (result is null) { ShowToast("Nothing opened"); return; }
            Engine.LoadSnapshot(result.Value.Snapshot, "Open", result.Value.Path);
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
            var path = await _files.SaveAsync(Engine.Document, Engine.FilePath, ask);
            if (path is null) return;
            Engine.FilePath = path;
            Engine.Document.Name = Path.GetFileNameWithoutExtension(path);
            Engine.MarkSaved();
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
            Content = $"“{Engine.Document.Name}” has changes that are not saved.",
            PrimaryButtonText = "Save",
            SecondaryButtonText = "Discard",
            CloseButtonText = "Cancel",
            XamlRoot = XamlRoot,
        };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary) { await SaveAsync(false); return !Engine.IsDirty; }
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
        try { File.WriteAllText(_settings.AutosavePath, DocumentSerializer.Serialize(Engine.Document)); } catch { /* best effort */ }
    }

    private void PersistSettings()
    {
        _settings.Capture(Engine.Options, _settingsModel);
        _settings.Save(_settingsModel);
    }
}
