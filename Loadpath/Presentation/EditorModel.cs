using System.Collections.Immutable;
using Uno.Extensions.Reactive.Commands;
using Uno.Extensions.Reactive.Config;
using Loadpath.Core.Analysis;
using Loadpath.Core.Editing;
using Loadpath.Workspace;

namespace Loadpath.Presentation;

/// <summary>
/// MVUX presentation model. Every value the views bind to is a state or feed here; every button binds a command.
/// The engine raises events; this record projects them into immutable records and pushes them into states.
/// The generated EditorViewModel is the DataContext of the page.
/// </summary>
[ImplicitCommands(false)]
public partial record EditorModel
{
    private readonly EditorEngine _engine;
    private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcher;
    private bool _syncingOptions;
    private bool _syncingFields;

    public EditorModel(EditorEngine engine)
    {
        _engine = engine;
        _dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        engine.AnalysisChanged += (_, _) => Fire(RefreshAllAsync());
        engine.SelectionChanged += (_, _) => Fire(RefreshSelectionAsync());
        engine.HistoryChanged += (_, _) => Fire(RefreshStatusAsync());
        engine.SavedStateChanged += (_, _) => Fire(RefreshStatusAsync());
        engine.OptionsChanged += (_, name) => Fire(PullOptionsAsync());
        engine.Interaction.StateChanged += (_, _) => Fire(RefreshInteractionAsync());
        engine.ViewportChanged += (_, _) => Fire(RefreshInteractionAsync());
        engine.PaletteRequested += (_, _) => Fire(TogglePalette(default));
    }

    /// <summary>
    /// Generated commands and state callbacks run off the UI thread. The engine mutates the document and raises
    /// events that reach XAML, so every mutation is posted back to the dispatcher the model was created on.
    /// </summary>
    private void Ui(Action action)
    {
        if (_dispatcher.HasThreadAccess) action();
        else _dispatcher.TryEnqueue(() => action());
    }

    private static void Fire(ValueTask task)
    {
        if (task.IsCompletedSuccessfully) return;
        _ = task.AsTask().ContinueWith(t => Trace($"async fault: {t.Exception}"), TaskContinuationOptions.OnlyOnFaulted);
    }

    internal static void Trace(string line)
    {
#if DEBUG
        if (Environment.GetEnvironmentVariable("LOADPATH_TRACE") is { } path) File.AppendAllText(path, line + "\n");
#endif
    }

    // ---- status and banners ----

    public IState<EditorStatus> Status => State.Value(this, () => EditorStatus.Initial);

    // ---- view options (two-way bound; mirrored into engine.Options for the renderer) ----

    public IState<bool> IsForces => State.Value(this, () => _engine.Options.DisplayMode == DisplayMode.Forces);
    public IState<bool> IsUtilization => State.Value(this, () => _engine.Options.DisplayMode == DisplayMode.Utilization);
    public IState<bool> ShowDeflection => State.Value(this, () => _engine.Options.ShowDeflection).ForEach(async (v, ct) => Push(o => o.ShowDeflection = v));
    public IState<double> Exaggeration => State.Value(this, () => _engine.Options.Exaggeration).ForEach(async (v, ct) => Push(o => o.Exaggeration = v));
    public IFeed<string> ExaggerationText => Exaggeration.Select(v => $"×{v:0.0}");
    public IState<bool> ShowLabels => State.Value(this, () => _engine.Options.ShowLabels).ForEach(async (v, ct) => Push(o => o.ShowLabels = v));
    public IState<bool> ShowReactions => State.Value(this, () => _engine.Options.ShowReactions).ForEach(async (v, ct) => Push(o => o.ShowReactions = v));
    public IState<bool> SnapEnabled => State.Value(this, () => _engine.Options.SnapEnabled);
    public IState<string> SnapText => State.Value(this, () => SnapLabel());
    public IState<bool> InspectorVisible => State.Value(this, () => _engine.Options.InspectorVisible);

    private void Push(Action<ViewOptions> apply)
    {
        if (_syncingOptions) return;
        Ui(() => apply(_engine.Options));
    }

    private string SnapLabel() => _engine.Options.SnapEnabled ? $"{_engine.Options.GridStep:0.##} m" : "off";

    private async ValueTask PullOptionsAsync()
    {
        _syncingOptions = true;
        try
        {
            var o = _engine.Options;
            await IsForces.SetAsync(o.DisplayMode == DisplayMode.Forces);
            await IsUtilization.SetAsync(o.DisplayMode == DisplayMode.Utilization);
            await ShowDeflection.SetAsync(o.ShowDeflection);
            await Exaggeration.SetAsync(o.Exaggeration);
            await ShowLabels.SetAsync(o.ShowLabels);
            await ShowReactions.SetAsync(o.ShowReactions);
            await SnapEnabled.SetAsync(o.SnapEnabled);
            await SnapText.SetAsync(SnapLabel());
            await InspectorVisible.SetAsync(o.InspectorVisible);
        }
        finally { _syncingOptions = false; }
    }

    // ---- interaction (tool, hint, cursor, zoom) ----

    public IState<bool> IsSelectActive => State.Value(this, () => true);
    public IState<bool> IsNodeActive => State.Value(this, () => false);
    public IState<bool> IsMemberActive => State.Value(this, () => false);
    public IState<bool> IsLoadActive => State.Value(this, () => false);
    public IState<bool> IsSupportActive => State.Value(this, () => false);
    public IState<bool> IsPanActive => State.Value(this, () => false);
    public IState<string> Hint => State.Value(this, () => _engine.Interaction.Hint);
    public IState<string> CursorText => State.Value(this, () => "");
    public IState<string> ZoomText => State.Value(this, () => "100%");

    private async ValueTask RefreshInteractionAsync()
    {
        var i = _engine.Interaction;
        await IsSelectActive.SetAsync(i.ActiveTool == ToolKind.Select);
        await IsNodeActive.SetAsync(i.ActiveTool == ToolKind.Node);
        await IsMemberActive.SetAsync(i.ActiveTool == ToolKind.Member);
        await IsLoadActive.SetAsync(i.ActiveTool == ToolKind.Load);
        await IsSupportActive.SetAsync(i.ActiveTool == ToolKind.Support);
        await IsPanActive.SetAsync(i.ActiveTool == ToolKind.Pan);
        await Hint.SetAsync(i.Hint);
        await CursorText.SetAsync(i.CursorWorldText);
        await ZoomText.SetAsync(i.ZoomText);
    }

    [Command] public void SelectTool() => Ui(() => _engine.Interaction.SetTool(ToolKind.Select));
    [Command] public void NodeTool() => Ui(() => _engine.Interaction.SetTool(ToolKind.Node));
    [Command] public void MemberTool() => Ui(() => _engine.Interaction.SetTool(ToolKind.Member));
    [Command] public void LoadTool() => Ui(() => _engine.Interaction.SetTool(ToolKind.Load));
    [Command] public void SupportTool() => Ui(() => _engine.Interaction.SetTool(ToolKind.Support));
    [Command] public void PanTool() => Ui(() => _engine.Interaction.SetTool(ToolKind.Pan));

    // ---- commands bound by buttons ----

    public IAsyncCommand Undo => Command.Create(b => b.Given(Status).When(s => s.CanUndo).Then(async (s, ct) => Ui(() => _engine.Commands.TryExecute("edit.undo"))));
    public IAsyncCommand Redo => Command.Create(b => b.Given(Status).When(s => s.CanRedo).Then(async (s, ct) => Ui(() => _engine.Commands.TryExecute("edit.redo"))));
    [Command] public void ShowForces() => Ui(() => _engine.Options.DisplayMode = DisplayMode.Forces);
    [Command] public void ShowUtilization() => Ui(() => _engine.Options.DisplayMode = DisplayMode.Utilization);
    [Command] public void JumpToCritical() => Ui(() => _engine.JumpToCritical());
    [Command] public void SplitMember() => Ui(() => _engine.SplitSelectedMember());
    [Command] public void DeleteSelection() => Ui(() => _engine.DeleteSelection());
    [Command] public void DuplicateSelection() => Ui(() => _engine.DuplicateSelection());
    [Command] public void CycleSupport() => Ui(() => _engine.CycleSupportOnSelection());
    [Command] public void AddLoad() => Ui(() => _engine.AddDefaultLoadOnSelection());
    [Command] public void ClearLoads() => Ui(() => _engine.ClearLoadOnSelection());
    [Command] public void SupportNone() => Ui(() => SetSupport(SupportKind.None));
    [Command] public void SupportPin() => Ui(() => SetSupport(SupportKind.Pin));
    [Command] public void SupportRoller() => Ui(() => SetSupport(SupportKind.RollerY));
    [Command] public void SupportRollerX() => Ui(() => SetSupport(SupportKind.RollerX));
    [Command] public void SampleWarren() => Ui(() => _engine.LoadSample("warren"));
    [Command] public void SampleCantilever() => Ui(() => _engine.LoadSample("cantilever"));
    [Command] public void SampleRoof() => Ui(() => _engine.LoadSample("roof"));
    [Command] public void OpenFile() => Ui(() => _engine.Commands.TryExecute("file.open"));
    [Command] public void SaveFile() => Ui(() => _engine.Commands.TryExecute("file.save"));
    [Command] public void ToggleNodes() => Fire(NodesExpanded.UpdateAsync(v => !v));
    [Command] public void ToggleMembers() => Fire(MembersExpanded.UpdateAsync(v => !v));

    /// <summary>Apply a section to the selected members, by section id (used by the bulk list).</summary>
    [Command] public void ApplySection(string sectionId) => Ui(() => _engine.SetSectionOnSelection(Section.FindById(sectionId)));

    private void SetSupport(SupportKind kind)
    {
        var sel = _engine.Selection;
        if (sel.Single is { IsNode: true } r)
        {
            if (_engine.Document.GetNode(r.Id).Support != kind) _engine.History.Do(new SetSupportEdit(r.Id, kind));
        }
        else _engine.SetSupportOnSelection(kind);
    }

    // ---- inspector ----

    public IState<InspectorContent> Inspector => State.Value(this, () => InspectorContent.Empty);
    public IState<double> NodeX => State.Value(this, () => 0.0).ForEach(async (v, ct) => CommitPosition());
    public IState<double> NodeY => State.Value(this, () => 0.0).ForEach(async (v, ct) => CommitPosition());
    public IState<double> LoadX => State.Value(this, () => 0.0).ForEach(async (v, ct) => CommitLoad());
    public IState<double> LoadY => State.Value(this, () => 0.0).ForEach(async (v, ct) => CommitLoad());
    public IState<int> SectionIndex => State.Value(this, () => 0).ForEach(async (v, ct) => CommitSection(v));
    public IListState<ConnectedItem> ConnectedMembers => ListState<ConnectedItem>.Empty(this);
    public IListFeed<SectionChoice> Sections => ListFeed.Async(async ct => (IImmutableList<SectionChoice>)Section.Library.Select(s => new SectionChoice(s.Id, s.Name, s.ShortName)).ToImmutableList());

    private int? SelectedNodeId => _engine.Selection.Single is { IsNode: true } r ? r.Id : null;
    private int? SelectedMemberId => _engine.Selection.Single is { IsMember: true } r ? r.Id : null;

    private void CommitPosition()
    {
        if (_syncingFields || SelectedNodeId is not { } id) return;
        Ui(() => Fire(CommitPositionAsync(id)));
    }

    private async ValueTask CommitPositionAsync(int id)
    {
        var node = _engine.Document.FindNode(id);
        if (node is null) return;
        var to = new Vec2(await NodeX, await NodeY);
        if (to == node.Position) return;
        Ui(() => _engine.History.Do(new SetPositionEdit(id, to)));
    }

    private void CommitLoad()
    {
        if (_syncingFields || SelectedNodeId is not { } id) return;
        Ui(() => Fire(CommitLoadAsync(id)));
    }

    private async ValueTask CommitLoadAsync(int id)
    {
        var node = _engine.Document.FindNode(id);
        if (node is null) return;
        var to = new Vec2(await LoadX, await LoadY);
        if (to == node.Load) return;
        Ui(() => _engine.History.Do(new SetLoadEdit(id, to)));
    }

    private void CommitSection(int index)
    {
        if (_syncingFields || SelectedMemberId is not { } id || index < 0 || index >= Section.Library.Count) return;
        var section = Section.Library[index];
        Ui(() => { if (_engine.Document.FindMember(id) is { } m && m.Section != section) _engine.History.Do(new SetSectionEdit([id], section)); });
    }

    // ---- outline ----

    public IListState<OutlineItem> OutlineNodes => ListState<OutlineItem>.Empty(this);
    public IListState<OutlineItem> OutlineMembers => ListState<OutlineItem>.Empty(this);
    public IState<string> NodesHeader => State.Value(this, () => "Nodes");
    public IState<string> MembersHeader => State.Value(this, () => "Members");
    public IState<bool> OutlineIsEmpty => State.Value(this, () => true);
    public IState<bool> NodesExpanded => State.Value(this, () => true);
    public IState<bool> MembersExpanded => State.Value(this, () => true);

    // ---- palette ----

    public IState<bool> IsPaletteOpen => State.Value(this, () => false);
    public IState<string> PaletteQuery => State.Value(this, () => "").ForEach(async (q, ct) => await RebuildPaletteAsync(q ?? "", 0, ct));
    public IListState<PaletteItem> PaletteItems => ListState<PaletteItem>.Empty(this);
    private int _paletteHighlight;
    private List<Commands.AppCommand> _paletteMatches = new();

    [Command]
    public async ValueTask TogglePalette(CancellationToken ct)
    {
        var open = !await IsPaletteOpen;
        if (open)
        {
            await PaletteQuery.SetAsync("", ct);
            await RebuildPaletteAsync("", 0, ct);
        }
        await IsPaletteOpen.SetAsync(open, ct);
    }

    [Command] public async ValueTask ClosePalette(CancellationToken ct) => await IsPaletteOpen.SetAsync(false, ct);

    public async ValueTask MovePaletteHighlight(int delta, CancellationToken ct)
    {
        if (_paletteMatches.Count == 0) return;
        _paletteHighlight = (_paletteHighlight + delta + _paletteMatches.Count) % _paletteMatches.Count;
        await PublishPaletteAsync(ct);
    }

    public async ValueTask HighlightPaletteItem(string id, CancellationToken ct)
    {
        var i = _paletteMatches.FindIndex(c => c.Id == id);
        if (i < 0 || i == _paletteHighlight) return;
        _paletteHighlight = i;
        await PublishPaletteAsync(ct);
    }

    public async ValueTask RunPalette(string? id, CancellationToken ct)
    {
        Trace($"palette run id={id} highlight={_paletteHighlight} matches={_paletteMatches.Count}");
        var command = id is null
            ? (_paletteHighlight >= 0 && _paletteHighlight < _paletteMatches.Count ? _paletteMatches[_paletteHighlight] : null)
            : _paletteMatches.FirstOrDefault(c => c.Id == id);
        await IsPaletteOpen.SetAsync(false, ct);
        if (command is null) return;
        Ui(() => { if (!command.TryExecute()) _engine.Toast($"{command.Title} is not available right now"); });
    }

    private async ValueTask RebuildPaletteAsync(string query, int highlight, CancellationToken ct)
    {
        var q = query.Trim();
        _paletteMatches = _engine.Commands.All
            .Select(c => (c, score: Score(c.Title, c.Category, q)))
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score).ThenBy(x => x.c.Category).ThenBy(x => x.c.Title)
            .Take(40)
            .Select(x => x.c)
            .ToList();
        _paletteHighlight = highlight;
        await PublishPaletteAsync(ct);
    }

    private async ValueTask PublishPaletteAsync(CancellationToken ct)
    {
        var items = _paletteMatches.Select((c, i) => new PaletteItem(
            c.Id, c.Title, c.Category, c.ShortcutDisplay.Replace("|", " · "), c.Icon ?? "chevron-right", c.Icon is not null,
            !c.CanExecute, i == _paletteHighlight, i == _paletteHighlight ? "SurfaceHoverBrush" : "TransparentBrush")).ToImmutableList();
        await PaletteItems.UpdateAsync(_ => items, ct);
    }

    private static int Score(string title, string category, string q)
    {
        if (q.Length == 0) return 1;
        var t = title.ToLowerInvariant();
        var ql = q.ToLowerInvariant();
        if (t.StartsWith(ql)) return 100;
        if (t.Contains(ql)) return 60;
        if (category.ToLowerInvariant().Contains(ql)) return 30;
        var i = 0;
        foreach (var ch in t) { if (i < ql.Length && ch == ql[i]) i++; }
        return i == ql.Length ? 10 : 0;
    }

    // ---- projections ----

    private async ValueTask RefreshAllAsync()
    {
        await RefreshStatusAsync();
        await RefreshInspectorAsync();
        await RefreshOutlineAsync();
    }

    private async ValueTask RefreshSelectionAsync()
    {
        await RefreshInspectorAsync();
        await RefreshOutlineAsync();
    }

    private async ValueTask RefreshStatusAsync()
    {
        var a = _engine.Analysis;
        var h = _engine.History;
        string text, detail, tone, mechanismText = "";
        var mechanism = false;
        switch (a.Status)
        {
            case AnalysisStatus.Empty: text = "Empty"; detail = "Place a node to begin"; tone = "InkTertiaryBrush"; break;
            case AnalysisStatus.NoLoads: text = "No loads"; detail = "Add a load (L) to see forces"; tone = "InkTertiaryBrush"; break;
            case AnalysisStatus.Unsupported:
                text = "Unsupported"; detail = "A pin and a roller are needed"; tone = "DangerBrush"; mechanism = true;
                mechanismText = "Unsupported — the structure can move freely. Add a pin and a roller (S)."; break;
            case AnalysisStatus.Mechanism:
                text = "Mechanism"; detail = "The structure can move freely"; tone = "DangerBrush"; mechanism = true;
                mechanismText = "Mechanism — the structure can move freely. Triangulate it or add a support."; break;
            default:
                text = a.MaxUtilization >= 1 ? "Overstressed" : "Solved";
                detail = $"{a.SolveMilliseconds:0.0} ms";
                tone = a.MaxUtilization >= 1 ? "DangerBrush" : "OkBrush";
                break;
        }
        var d = a.DetachedNodeIds.Count;
        var over = a.IsSolved ? a.Members.Values.Count(m => m.IsOverstressed) : 0;
        var pillText = over > 0 ? (over == 1 ? "1 member over capacity" : $"{over} members over capacity")
            : a.IsSolved ? "Within capacity" : $"{text} · {detail.ToLowerInvariant()}";
        var status = new EditorStatus(
            _engine.Document.Name, _engine.IsDirty, text, detail, tone, mechanism, mechanismText,
            a.HasDetached && !mechanism,
            d == 1 ? "1 node is not connected to a support and carries nothing." : $"{d} nodes are not connected to a support and carry nothing.",
            a.IsSolved ? $"{a.MaxUtilization * 100:0}%" : "—", UtilTone(a.MaxUtilization),
            _engine.Document.Nodes.Count == 0, h.CanUndo, h.CanRedo,
            h.UndoLabel is { } u ? $"Undo {u.ToLowerInvariant()}" : "Nothing to undo",
            h.RedoLabel is { } r ? $"Redo {r.ToLowerInvariant()}" : "Nothing to redo",
            pillText, over > 0 || mechanism,
            a.IsSolved ? $"{a.SolveMilliseconds:0.0} ms" : "—", over > 0);
        await Status.UpdateAsync(_ => status);
    }

    private static string UtilTone(double util) => util >= 1 ? "DangerBrush" : util >= 0.75 ? "WarnBrush" : "InkBrush";
    private static string ForceTone(bool tension, bool compression, bool danger) => danger ? "DangerBrush" : tension ? "TensionBrush" : compression ? "CompressionBrush" : "InkTertiaryBrush";
    private static string SignedKn(double v) => (v >= 0 ? "+" : "−") + $"{Math.Abs(v):0.0}";

    /// <summary>Nearest of eight arrows for a load direction (screen convention: +y is up).</summary>
    private static string Arrow(Vec2 v)
    {
        if (v.LengthSquared < 1e-12) return "";
        var deg = Math.Atan2(v.Y, v.X) * 180 / Math.PI;
        var i = (int)Math.Round(((deg % 360) + 360) % 360 / 45) % 8;
        return "→↗↑↖←↙↓↘"[i].ToString();
    }

    private static string SupportWord(SupportKind k) => k switch
    {
        SupportKind.Pin => "Pin",
        SupportKind.RollerY => "Roller",
        SupportKind.RollerX => "Roller y",
        _ => "",
    };
    private static string Fmt(double v) => (v >= 0 ? "+" : "−") + $"{Math.Abs(v),6:0.0}";

    private async ValueTask RefreshInspectorAsync()
    {
        var doc = _engine.Document;
        var a = _engine.Analysis;
        var sel = _engine.Selection;
        var c = InspectorContent.Empty;

        _syncingFields = true;
        try
        {
            if (sel.IsEmpty)
            {
                var pins = doc.Nodes.Count(n => n.Support == SupportKind.Pin);
                var rollers = doc.Nodes.Count(n => n.Support is SupportKind.RollerY or SupportKind.RollerX);
                var total = doc.Nodes.Aggregate(Vec2.Zero, (acc, n) => acc + n.Load);
                var mass = doc.Members.Sum(m => m.Section.MassPerMeter * doc.MemberLength(m));
                var reactions = a.IsSolved ? string.Join("\n", doc.Nodes.Where(n => n.HasSupport).Select(n => { var r = a.ForNode(n.Id)?.Reaction ?? Vec2.Zero; return $"N{n.Id}  {Fmt(r.X)}  {Fmt(r.Y)} kN"; })) : "";
                c = c with
                {
                    IsSummary = true,
                    Title = doc.Nodes.Count == 0 ? "Structure" : doc.Name,
                    Subtitle = doc.Nodes.Count == 0 ? "Nothing here yet" : "Nothing selected",
                    NodeCountText = doc.Nodes.Count.ToString(),
                    MemberCountText = doc.Members.Count.ToString(),
                    SupportsText = pins + rollers == 0 ? "none" : $"{pins} pin · {rollers} roller",
                    TotalLoadText = total.LengthSquared < 1e-12 ? "none" : $"{total.Length:0.#} kN",
                    MassText = doc.Members.Count == 0 ? "—" : $"{mass:0} kg",
                    MaxUtil = a.IsSolved ? Math.Clamp(a.MaxUtilization, 0, 1) : 0,
                    MaxUtilText = a.IsSolved ? $"{a.MaxUtilization * 100:0}%" : "—",
                    MaxUtilToneKey = UtilTone(a.MaxUtilization),
                    MaxDeflectionText = a.IsSolved ? $"{a.MaxDisplacementM * 1000:0.0} mm" : "—",
                    HasCritical = a.IsSolved && a.CriticalMemberId is not null,
                    CriticalText = a.CriticalMemberId is { } cid ? $"Member {cid} governs" : "",
                    ReactionsText = reactions,
                    SummaryHint = a.Status switch
                    {
                        AnalysisStatus.Empty => "Press N and click to place nodes. M connects them.",
                        AnalysisStatus.NoLoads => "Add a load with L to see forces.",
                        AnalysisStatus.Unsupported => "Add a pin and a roller with S.",
                        AnalysisStatus.Mechanism => "Triangulate the structure or add a support.",
                        _ => a.MaxUtilization >= 1 ? "A member is over its limit. Select it to change its section." : "Drag any node to watch the load path change.",
                    },
                };
            }
            else if (SelectedNodeId is { } nid && doc.FindNode(nid) is { } node)
            {
                var r = a.ForNode(node.Id);
                var members = doc.MembersAt(node.Id).ToList();
                var disp = a.IsSolved && r is { } dn ? dn.Displacement * 1000 : Vec2.Zero;
                var hasDisp = a.IsSolved && r is not null;
                // Mini diagram: 72 px box, direction of travel drawn at a fixed 24 px so small motions still read.
                var dotX = 36.0; var dotY = 36.0;
                if (hasDisp && disp.Length > 1e-6) { dotX = 36 + disp.X / disp.Length * 24; dotY = 36 - disp.Y / disp.Length * 24; }
                var kindText = node.Support switch
                {
                    SupportKind.Pin => "Pinned support",
                    SupportKind.RollerY => "Roller support, free in x",
                    SupportKind.RollerX => "Roller support, free in y",
                    _ => "Free node",
                };
                c = c with
                {
                    NodeDescription = kindText + (node.HasLoad ? $" with a {node.Load.Length:0.#} kN point load" : ""),
                    DispXText = hasDisp ? $"{disp.X:+0.00;−0.00} mm" : "—",
                    DispYText = hasDisp ? $"{disp.Y:+0.00;−0.00} mm" : "—",
                    DispMagText = hasDisp ? $"{disp.Length:0.00} mm" : "—",
                    DispDotX = dotX, DispDotY = dotY,
                    ConnectedCountText = members.Count.ToString(),
                    IsSummary = false, IsNode = true,
                    Title = $"Node {node.Id}",
                    Subtitle = node.Support.Label() + (node.HasLoad ? $" · {node.Load.Length:0.#} kN" : ""),
                    IsPin = node.Support == SupportKind.Pin, IsRoller = node.Support == SupportKind.RollerY,
                    IsRollerX = node.Support == SupportKind.RollerX, IsFree = node.Support == SupportKind.None,
                    ConnectedText = members.Count == 0 ? "No members" : string.Join(", ", members.Select(m => $"M{m.Id}")),
                    HasReaction = a.IsSolved && node.HasSupport && r is not null,
                    ReactionText = a.IsSolved && node.HasSupport && r is { } rr ? $"{Fmt(rr.Reaction.X)}  {Fmt(rr.Reaction.Y)} kN" : "",
                    DisplacementText = a.IsSolved && r is { } dr ? $"{dr.Displacement.X * 1000:+0.00;−0.00}  {dr.Displacement.Y * 1000:+0.00;−0.00} mm" : "—",
                };
                var connected = members.Select(m =>
                {
                    var mr = a.IsSolved ? a.For(m.Id) : null;
                    return new ConnectedItem($"m{m.Id}", $"M{m.Id}", $"N{m.StartNodeId}–N{m.EndNodeId}",
                        mr is { } f ? SignedKn(f.AxialForceKn) : "—",
                        mr is { } t && t.IsOverstressed ? "DangerBrush" : "InkBrush",
                        mr is { } u ? $"{u.Utilization * 100:0}%" : "",
                        mr is { IsOverstressed: true });
                }).ToImmutableList();
                await ConnectedMembers.UpdateAsync(_ => connected);
                await NodeX.SetAsync(node.Position.X);
                await NodeY.SetAsync(node.Position.Y);
                await LoadX.SetAsync(node.Load.X);
                await LoadY.SetAsync(node.Load.Y);
            }
            else if (SelectedMemberId is { } mid && doc.FindMember(mid) is { } member)
            {
                var res = a.For(member.Id);
                var solved = a.IsSolved && res is not null;
                var mr = res ?? default;
                c = c with
                {
                    IsSummary = false, IsMember = true,
                    Title = $"Member {member.Id}",
                    Subtitle = member.Section.Name,
                    EndpointsText = $"N{member.StartNodeId} → N{member.EndNodeId}",
                    LengthText = $"{doc.MemberLength(member):0.000} m",
                    MaterialText = $"{member.Section.Material.Name} · E {member.Section.Material.ElasticModulusGPa:0} GPa · fy {member.Section.Material.YieldStrengthMPa:0} MPa",
                    AreaText = $"A {member.Section.AreaMm2:0} mm²  ·  I {member.Section.SecondMomentMm4 / 1e4:0.#} cm⁴",
                    ForceKindText = solved ? (mr.IsTension ? "Tension" : mr.IsCompression ? "Compression" : "Zero force") : "Not solved",
                    ForceToneKey = solved ? ForceTone(mr.IsTension, mr.IsCompression, mr.IsOverstressed) : "InkTertiaryBrush",
                    ForceText = solved ? $"{Math.Abs(mr.AxialForceKn):0.00} kN" : "—",
                    StressText = solved ? $"{Math.Abs(mr.StressMPa):0.0} MPa" : "—",
                    Util = solved ? Math.Clamp(mr.Utilization, 0, 1) : 0,
                    UtilText = solved ? $"{mr.Utilization * 100:0}%" : "—",
                    UtilToneKey = solved ? UtilTone(mr.Utilization) : "OkBrush",
                    GovernsText = solved ? (mr.IsOverstressed ? (mr.BucklingGoverns ? "Fails by buckling" : "Fails by yield") : (mr.BucklingGoverns ? "Buckling governs" : "Yield governs")) : "",
                    BucklingText = solved ? (mr.IsCompression ? $"Pcr {member.Section.CriticalBucklingLoadN(mr.LengthM) / 1000:0.0} kN · {mr.UtilizationBuckling * 100:0}%" : "n/a (tension)") : "—",
                };
                await SectionIndex.SetAsync(Math.Max(0, Section.Library.ToList().IndexOf(member.Section)));
            }
            else
            {
                c = c with
                {
                    IsSummary = false, IsMixed = true,
                    Title = $"{sel.Count} selected",
                    Subtitle = $"{sel.NodeCount} nodes · {sel.MemberCount} members",
                    MixedHasNodes = sel.NodeCount > 0,
                    MixedHasMembers = sel.MemberCount > 0,
                    MixedText = "Bulk actions apply to every selected element.",
                };
            }
            await Inspector.UpdateAsync(_ => c);
        }
        finally { _syncingFields = false; }
    }

    private async ValueTask RefreshOutlineAsync()
    {
        var doc = _engine.Document;
        var a = _engine.Analysis;
        var sel = _engine.Selection;
        var nodes = doc.Nodes.Select(n =>
        {
            var selected = sel.Contains(n.Ref);
            // Round, then add +0.0: -0.0 + 0.0 is +0.0, so a node at x = -1e-12 reads "0" instead of "-0".
            return new OutlineItem($"n{n.Id}", $"N{n.Id}", $"{Math.Round(n.Position.X, 2) + 0.0:0.##}, {Math.Round(n.Position.Y, 2) + 0.0:0.##}",
                n.HasLoad ? $"{Arrow(n.Load)}{n.Load.Length:0.#} kN" : "",
                "InkBrush", selected, selected ? "SelectionBrush" : "TransparentBrush", n.HasSupport, SupportWord(n.Support), false, "");
        }).ToImmutableList();
        var members = doc.Members.Select(m =>
        {
            var selected = sel.Contains(m.Ref);
            var r = a.For(m.Id);
            var solved = a.IsSolved && r is { } rr;
            var res = r ?? default;
            return new OutlineItem($"m{m.Id}", $"M{m.Id}", $"N{m.StartNodeId}-N{m.EndNodeId}",
                solved ? (Math.Abs(res.AxialForceKn) < 0.05 ? "0.0" : SignedKn(res.AxialForceKn)) : "",
                solved ? ForceTone(res.IsTension, res.IsCompression, res.IsOverstressed) : "InkTertiaryBrush",
                selected, selected ? "SelectionBrush" : "TransparentBrush", false, "",
                solved && res.IsOverstressed, solved ? $"{res.Utilization * 100:0}%" : "");
        }).ToImmutableList();
        await OutlineNodes.UpdateAsync(_ => nodes);
        await OutlineMembers.UpdateAsync(_ => members);
        await NodesHeader.SetAsync(doc.Nodes.Count.ToString());
        await MembersHeader.SetAsync(doc.Members.Count.ToString());
        await OutlineIsEmpty.SetAsync(doc.Nodes.Count == 0);
    }
}
