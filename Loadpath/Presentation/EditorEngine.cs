using Loadpath.Commands;
using Loadpath.Core.Analysis;
using Loadpath.Core.Editing;
using Loadpath.Core.Samples;
using Loadpath.Core.Selection;
using Loadpath.Workspace;

namespace Loadpath.Presentation;

/// <summary>
/// The imperative editor: document, history, selection, viewport, analysis, tools and the command registry.
/// It raises events; EditorModel projects them into MVUX states for the views. Nothing here is bound to XAML.
/// </summary>
public sealed class EditorEngine
{
    public EditorEngine()
    {
        Document = new StructureDocument();
        History = new EditHistory(Document);
        Selection = new SelectionSet();
        Viewport = new Core.Viewport.Viewport();
        Snap = new Core.Geometry.SnapEngine();
        Options = new ViewOptions();
        Commands = new CommandRegistry();
        Interaction = new WorkspaceInteraction(this);
        Analysis = AnalysisResult.EmptyResult;

        Document.Changed += (_, _) => OnDocumentChanged();
        Selection.Changed += (_, _) => { SelectionChanged?.Invoke(this, EventArgs.Empty); RequestRender(); };
        History.Changed += (_, _) => HistoryChanged?.Invoke(this, EventArgs.Empty);
        Viewport.Changed += (_, _) => ViewportChanged?.Invoke(this, EventArgs.Empty);
        Options.Changed += (_, name) => OnOptionsChanged(name);
        Snap.GridStep = Options.GridStep;
        RegisterCommands();
    }

    public StructureDocument Document { get; }
    public EditHistory History { get; }
    public SelectionSet Selection { get; }
    public Core.Viewport.Viewport Viewport { get; }
    public Core.Geometry.SnapEngine Snap { get; }
    public ViewOptions Options { get; }
    public CommandRegistry Commands { get; }
    public WorkspaceInteraction Interaction { get; }
    public AnalysisResult Analysis { get; private set; }
    public string? FilePath { get; set; }
    public bool IsDirty => History.Version != _savedVersion;
    public bool HasSelection => !Selection.IsEmpty;

    /// <summary>Raised after every document change, once the analysis has been recomputed.</summary>
    public event EventHandler? AnalysisChanged;
    public event EventHandler? SelectionChanged;
    public event EventHandler? HistoryChanged;
    public event EventHandler? ViewportChanged;
    public event EventHandler<string>? OptionsChanged;
    /// <summary>The workspace should redraw (hover, overlays, options).</summary>
    public event EventHandler? RenderRequested;
    public event EventHandler<string>? ToastRequested;
    public event EventHandler? FitRequested;
    public event EventHandler? PaletteRequested;
    public event EventHandler? SavedStateChanged;

    private int _savedVersion;

    public void RequestRender() => RenderRequested?.Invoke(this, EventArgs.Empty);
    public void Toast(string message) => ToastRequested?.Invoke(this, message);
    public void RequestFit() => FitRequested?.Invoke(this, EventArgs.Empty);
    public void RequestPalette() => PaletteRequested?.Invoke(this, EventArgs.Empty);

    private void OnDocumentChanged()
    {
        Selection.Prune(Document);
        Analysis = TrussSolver.Solve(Document);
        AnalysisChanged?.Invoke(this, EventArgs.Empty);
        RequestRender();
    }

    private void OnOptionsChanged(string name)
    {
        if (name == nameof(ViewOptions.GridStep)) Snap.GridStep = Options.GridStep;
        if (name == nameof(ViewOptions.SnapEnabled)) Snap.GridEnabled = Options.SnapEnabled;
        OptionsChanged?.Invoke(this, name);
        RequestRender();
    }

    // ---- document lifecycle ----

    public void MarkSaved()
    {
        _savedVersion = History.Version;
        SavedStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void LoadSnapshot(DocumentSnapshot snapshot, string label, string? filePath)
    {
        History.Do(new ReplaceDocumentEdit(snapshot, label));
        History.Clear();
        Selection.Clear();
        Interaction.CancelTool();
        FilePath = filePath;
        MarkSaved();
        RequestFit();
    }

    public void LoadSample(string id) => LoadSnapshot(SampleStructures.Build(id), "Load sample", null);

    public void NewDocument() => LoadSnapshot(new DocumentSnapshot { Name = "Untitled", Nodes = [], Members = [] }, "New", null);

    // ---- editing helpers used by commands, tools and the model ----

    public void DeleteSelection()
    {
        if (Selection.IsEmpty) { Toast("Nothing selected"); return; }
        var refs = Selection.Items.ToList();
        Selection.Clear();
        History.Do(new RemoveElementsEdit(refs));
    }

    public void DuplicateSelection()
    {
        if (Selection.IsEmpty) { Toast("Nothing selected"); return; }
        var edit = new DuplicateEdit(Selection.NodeIds, Selection.MemberIds, new Vec2(Options.GridStep, -Options.GridStep));
        History.Do(edit);
        Selection.Replace(edit.CreatedRefs);
    }

    public void NudgeSelection(Vec2 delta)
    {
        var nodes = SelectedNodeIdsIncludingMemberEnds().ToList();
        if (nodes.Count == 0) return;
        History.Do(new MoveNodesEdit(nodes, delta));
    }

    public IEnumerable<int> SelectedNodeIdsIncludingMemberEnds()
    {
        var set = new HashSet<int>(Selection.NodeIds);
        foreach (var mid in Selection.MemberIds)
        {
            var m = Document.FindMember(mid);
            if (m is null) continue;
            set.Add(m.StartNodeId);
            set.Add(m.EndNodeId);
        }
        return set;
    }

    public void SetSupportOnSelection(SupportKind kind)
    {
        var ids = Selection.NodeIds.ToList();
        if (ids.Count == 0) { Toast("Select a node first"); return; }
        History.BeginTransaction(kind == SupportKind.None ? "Remove supports" : $"Set {kind.Label().ToLowerInvariant()}");
        foreach (var id in ids) History.Do(new SetSupportEdit(id, kind));
        History.CommitTransaction();
    }

    public void CycleSupportOnSelection()
    {
        var ids = Selection.NodeIds.ToList();
        if (ids.Count == 0) return;
        SetSupportOnSelection(Document.GetNode(ids[0]).Support.Next());
    }

    public void AddDefaultLoadOnSelection()
    {
        var ids = Selection.NodeIds.ToList();
        if (ids.Count == 0) { Toast("Select a node first"); return; }
        History.BeginTransaction("Add load");
        foreach (var id in ids) History.Do(new SetLoadEdit(id, new Vec2(0, -10)));
        History.CommitTransaction();
    }

    public void ClearLoadOnSelection()
    {
        var ids = Selection.NodeIds.Where(id => Document.GetNode(id).HasLoad).ToList();
        if (ids.Count == 0) { Toast("No load on the selection"); return; }
        History.BeginTransaction("Clear loads");
        foreach (var id in ids) History.Do(new SetLoadEdit(id, Vec2.Zero));
        History.CommitTransaction();
    }

    public void SetSectionOnSelection(Section section)
    {
        var ids = Selection.MemberIds.ToList();
        if (ids.Count == 0) { Toast("Select a member first"); return; }
        History.Do(new SetSectionEdit(ids, section));
    }

    public void SplitSelectedMember()
    {
        if (Selection.Single is { IsMember: true } r && Document.FindMember(r.Id) is { } m)
        {
            var mid = Vec2.Lerp(Document.GetNode(m.StartNodeId).Position, Document.GetNode(m.EndNodeId).Position, 0.5);
            var edit = new SplitMemberEdit(m.Id, mid);
            History.Do(edit);
            Selection.Set(ElementRef.Node(edit.NewNodeId));
        }
        else Toast("Select one member to split");
    }

    public void JumpToCritical()
    {
        if (Analysis.CriticalMemberId is { } id) Selection.Set(ElementRef.Member(id));
        else Toast("No solved result yet");
    }

    /// <summary>Select an element by its outline key ("n4", "m12").</summary>
    public void SelectByKey(string key, bool extend)
    {
        if (!TryParseKey(key, out var r)) return;
        if (extend) Selection.Toggle(r); else Selection.Set(r);
    }

    public void HoverByKey(string? key)
    {
        Interaction.HoverFromOutline(key is not null && TryParseKey(key, out var r) ? r : null);
    }

    public static bool TryParseKey(string key, out ElementRef r)
    {
        r = default;
        if (key.Length < 2 || !int.TryParse(key.AsSpan(1), out var id)) return false;
        r = key[0] is 'n' or 'N' ? ElementRef.Node(id) : ElementRef.Member(id);
        return true;
    }

    private void RegisterCommands()
    {
        var c = Commands;
        c.Add(new AppCommand("tool.select", "Select tool", "Tools", "V", () => Interaction.SetTool(ToolKind.Select), icon: "select"));
        c.Add(new AppCommand("tool.node", "Node tool", "Tools", "N", () => Interaction.SetTool(ToolKind.Node), icon: "node"));
        c.Add(new AppCommand("tool.member", "Member tool", "Tools", "M", () => Interaction.SetTool(ToolKind.Member), icon: "member"));
        c.Add(new AppCommand("tool.load", "Load tool", "Tools", "L", () => Interaction.SetTool(ToolKind.Load), icon: "load"));
        c.Add(new AppCommand("tool.support", "Support tool", "Tools", "S", () => Interaction.SetTool(ToolKind.Support), icon: "support"));
        c.Add(new AppCommand("tool.pan", "Pan tool", "Tools", "H", () => Interaction.SetTool(ToolKind.Pan), icon: "pan"));

        c.Add(new AppCommand("edit.undo", "Undo", "Edit", "Ctrl+Z", () => History.Undo(), () => History.CanUndo, "undo"));
        c.Add(new AppCommand("edit.redo", "Redo", "Edit", "Ctrl+Y|Ctrl+Shift+Z", () => History.Redo(), () => History.CanRedo, "redo"));
        c.Add(new AppCommand("edit.delete", "Delete", "Edit", "Delete|Backspace", DeleteSelection, () => HasSelection, "delete"));
        c.Add(new AppCommand("edit.duplicate", "Duplicate", "Edit", "Ctrl+D", DuplicateSelection, () => HasSelection, "duplicate"));
        c.Add(new AppCommand("edit.selectAll", "Select all", "Edit", "Ctrl+A", () => Selection.Replace(Document.Nodes.Select(n => n.Ref).Concat(Document.Members.Select(m => m.Ref)))));
        c.Add(new AppCommand("edit.clearSelection", "Clear selection", "Edit", "Esc", () => { Interaction.CancelTool(); Selection.Clear(); }));
        c.Add(new AppCommand("edit.nudgeUp", "Nudge up", "Edit", "Up", () => NudgeSelection(new(0, Options.GridStep)), () => HasSelection));
        c.Add(new AppCommand("edit.nudgeDown", "Nudge down", "Edit", "Down", () => NudgeSelection(new(0, -Options.GridStep)), () => HasSelection));
        c.Add(new AppCommand("edit.nudgeLeft", "Nudge left", "Edit", "Left", () => NudgeSelection(new(-Options.GridStep, 0)), () => HasSelection));
        c.Add(new AppCommand("edit.nudgeRight", "Nudge right", "Edit", "Right", () => NudgeSelection(new(Options.GridStep, 0)), () => HasSelection));
        c.Add(new AppCommand("edit.nudgeUp5", "Nudge up ×5", "Edit", "Shift+Up", () => NudgeSelection(new(0, Options.GridStep * 5)), () => HasSelection));
        c.Add(new AppCommand("edit.nudgeDown5", "Nudge down ×5", "Edit", "Shift+Down", () => NudgeSelection(new(0, -Options.GridStep * 5)), () => HasSelection));
        c.Add(new AppCommand("edit.nudgeLeft5", "Nudge left ×5", "Edit", "Shift+Left", () => NudgeSelection(new(-Options.GridStep * 5, 0)), () => HasSelection));
        c.Add(new AppCommand("edit.nudgeRight5", "Nudge right ×5", "Edit", "Shift+Right", () => NudgeSelection(new(Options.GridStep * 5, 0)), () => HasSelection));

        c.Add(new AppCommand("structure.pin", "Set support: pin", "Structure", null, () => SetSupportOnSelection(SupportKind.Pin), () => Selection.NodeCount > 0, "support"));
        c.Add(new AppCommand("structure.roller", "Set support: roller", "Structure", null, () => SetSupportOnSelection(SupportKind.RollerY), () => Selection.NodeCount > 0, "support"));
        c.Add(new AppCommand("structure.rollerX", "Set support: vertical roller", "Structure", null, () => SetSupportOnSelection(SupportKind.RollerX), () => Selection.NodeCount > 0, "support"));
        c.Add(new AppCommand("structure.noSupport", "Remove support", "Structure", null, () => SetSupportOnSelection(SupportKind.None), () => Selection.NodeCount > 0));
        c.Add(new AppCommand("structure.clearLoad", "Clear load", "Structure", null, ClearLoadOnSelection, () => Selection.NodeCount > 0));
        c.Add(new AppCommand("structure.split", "Split member at midpoint", "Structure", null, SplitSelectedMember, () => Selection.Single is { IsMember: true }, "node"));
        c.Add(new AppCommand("structure.critical", "Jump to critical member", "Structure", "J", JumpToCritical, () => Analysis.IsSolved, "target"));
        foreach (var s in Section.Library)
        {
            var section = s;
            c.Add(new AppCommand($"section.{section.Id}", $"Set section: {section.Name}", "Sections", null, () => SetSectionOnSelection(section), () => Selection.MemberCount > 0));
        }

        c.Add(new AppCommand("view.fit", "Fit structure", "View", "F|Ctrl+0", RequestFit, icon: "fit"));
        c.Add(new AppCommand("view.zoom100", "Zoom to 100%", "View", "Ctrl+1", () => Interaction.ZoomTo(Core.Viewport.Viewport.BaseScale)));
        c.Add(new AppCommand("view.zoomIn", "Zoom in", "View", "Ctrl+=", () => Interaction.ZoomStep(1)));
        c.Add(new AppCommand("view.zoomOut", "Zoom out", "View", "Ctrl+-", () => Interaction.ZoomStep(-1)));
        c.Add(new AppCommand("view.forces", "Show forces", "View", "1", () => Options.DisplayMode = DisplayMode.Forces));
        c.Add(new AppCommand("view.utilization", "Show utilization", "View", "2", () => Options.DisplayMode = DisplayMode.Utilization));
        c.Add(new AppCommand("view.deflection", "Toggle deflected shape", "View", "3", () => Options.ShowDeflection = !Options.ShowDeflection));
        c.Add(new AppCommand("view.labels", "Toggle force labels", "View", "4", () => Options.ShowLabels = !Options.ShowLabels));
        c.Add(new AppCommand("view.reactions", "Toggle reactions", "View", "5", () => Options.ShowReactions = !Options.ShowReactions));
        c.Add(new AppCommand("view.grid", "Toggle grid", "View", "Ctrl+G", () => Options.ShowGrid = !Options.ShowGrid));
        c.Add(new AppCommand("view.snap", "Toggle snapping", "View", "G", () => Options.SnapEnabled = !Options.SnapEnabled));
        c.Add(new AppCommand("view.inspector", "Toggle inspector", "View", "Ctrl+\\", () => Options.InspectorVisible = !Options.InspectorVisible, icon: "panel"));
        c.Add(new AppCommand("view.palette", "Command palette", "View", "Ctrl+K", RequestPalette, icon: "search"));

        foreach (var (id, title, _) in SampleStructures.Catalog)
        {
            var sampleId = id;
            c.Add(new AppCommand($"sample.{sampleId}", $"Open sample: {title}", "Samples", null, () => LoadSample(sampleId), icon: "mark"));
        }
    }
}
