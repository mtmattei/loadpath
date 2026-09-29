using System.Collections.ObjectModel;
using Loadpath.Core.Analysis;
using Loadpath.Core.Editing;
using Loadpath.Core.Geometry;

namespace Loadpath.Presentation;

public enum InspectorMode { Summary, Node, Member, Mixed }

/// <summary>Contextual inspector: what is selected decides which panel shows. Edits go through the history.</summary>
public sealed partial class InspectorViewModel : ObservableObject
{
    private readonly EditorViewModel _editor;
    private bool _suppress;

    public InspectorViewModel(EditorViewModel editor)
    {
        _editor = editor;
        editor.SelectionChanged += (_, _) => Refresh();
        editor.AnalysisChanged += (_, _) => Refresh();
        Sections = new ObservableCollection<Section>(Section.Library);
        Refresh();
    }

    public ObservableCollection<Section> Sections { get; }

    [ObservableProperty] private InspectorMode _mode = InspectorMode.Summary;
    [ObservableProperty] private string _title = "Structure";
    [ObservableProperty] private string _subtitle = "";

    // Summary
    [ObservableProperty] private string _nodeCountText = "0";
    [ObservableProperty] private string _memberCountText = "0";
    [ObservableProperty] private string _supportsText = "—";
    [ObservableProperty] private string _totalLoadText = "—";
    [ObservableProperty] private string _massText = "—";
    [ObservableProperty] private string _maxUtilText = "—";
    [ObservableProperty] private double _maxUtil;
    [ObservableProperty] private string _maxDeflectionText = "—";
    [ObservableProperty] private string _criticalText = "";
    [ObservableProperty] private string _reactionsText = "";
    [ObservableProperty] private bool _hasCritical;
    [ObservableProperty] private string _summaryHint = "";

    // Node
    [ObservableProperty] private double _nodeX;
    [ObservableProperty] private double _nodeY;
    [ObservableProperty] private double _loadX;
    [ObservableProperty] private double _loadY;
    [ObservableProperty] private bool _isPin;
    [ObservableProperty] private bool _isRoller;
    [ObservableProperty] private bool _isRollerX;
    [ObservableProperty] private bool _isFree;
    [ObservableProperty] private string _connectedText = "";
    [ObservableProperty] private string _reactionText = "";
    [ObservableProperty] private bool _hasReaction;
    [ObservableProperty] private string _displacementText = "";

    // Member
    [ObservableProperty] private string _endpointsText = "";
    [ObservableProperty] private string _lengthText = "";
    [ObservableProperty] private Section? _memberSection;
    [ObservableProperty] private string _materialText = "";
    [ObservableProperty] private string _areaText = "";
    [ObservableProperty] private string _forceText = "";
    [ObservableProperty] private string _forceKindText = "";
    [ObservableProperty] private bool _isTension;
    [ObservableProperty] private bool _isCompression;
    [ObservableProperty] private string _stressText = "";
    [ObservableProperty] private string _utilText = "";
    [ObservableProperty] private double _util;
    [ObservableProperty] private bool _isOverstressed;
    [ObservableProperty] private string _bucklingText = "";
    [ObservableProperty] private string _governsText = "";

    // Mixed
    [ObservableProperty] private string _mixedText = "";
    [ObservableProperty] private bool _mixedHasNodes;
    [ObservableProperty] private bool _mixedHasMembers;

    public bool IsSummary => Mode == InspectorMode.Summary;
    public bool IsNode => Mode == InspectorMode.Node;
    public bool IsMember => Mode == InspectorMode.Member;
    public bool IsMixed => Mode == InspectorMode.Mixed;

    partial void OnModeChanged(InspectorMode value)
    {
        OnPropertyChanged(nameof(IsSummary));
        OnPropertyChanged(nameof(IsNode));
        OnPropertyChanged(nameof(IsMember));
        OnPropertyChanged(nameof(IsMixed));
    }

    private int? SelectedNodeId => _editor.Selection.Single is { IsNode: true } r ? r.Id : null;
    private int? SelectedMemberId => _editor.Selection.Single is { IsMember: true } r ? r.Id : null;

    public void Refresh()
    {
        _suppress = true;
        try
        {
            var sel = _editor.Selection;
            if (sel.IsEmpty) { Mode = InspectorMode.Summary; RefreshSummary(); }
            else if (SelectedNodeId is { } nid && _editor.Document.FindNode(nid) is { } node) { Mode = InspectorMode.Node; RefreshNode(node); }
            else if (SelectedMemberId is { } mid && _editor.Document.FindMember(mid) is { } member) { Mode = InspectorMode.Member; RefreshMember(member); }
            else { Mode = InspectorMode.Mixed; RefreshMixed(); }
        }
        finally { _suppress = false; }
    }

    private void RefreshSummary()
    {
        var doc = _editor.Document;
        var a = _editor.Analysis;
        Title = doc.Nodes.Count == 0 ? "Structure" : doc.Name;
        Subtitle = doc.Nodes.Count == 0 ? "Nothing here yet" : "Nothing selected";
        NodeCountText = doc.Nodes.Count.ToString();
        MemberCountText = doc.Members.Count.ToString();
        var pins = doc.Nodes.Count(n => n.Support == SupportKind.Pin);
        var rollers = doc.Nodes.Count(n => n.Support is SupportKind.RollerY or SupportKind.RollerX);
        SupportsText = pins + rollers == 0 ? "none" : $"{pins} pin · {rollers} roller";
        var total = doc.Nodes.Aggregate(Vec2.Zero, (acc, n) => acc + n.Load);
        TotalLoadText = total.LengthSquared < 1e-12 ? "none" : $"{total.Length:0.#} kN";
        var mass = doc.Members.Sum(m => m.Section.MassPerMeter * doc.MemberLength(m));
        MassText = doc.Members.Count == 0 ? "—" : $"{mass:0} kg";
        if (a.IsSolved)
        {
            MaxUtil = Math.Clamp(a.MaxUtilization, 0, 1);
            MaxUtilText = $"{a.MaxUtilization * 100:0}%";
            MaxDeflectionText = $"{a.MaxDisplacementM * 1000:0.0} mm";
            HasCritical = a.CriticalMemberId is not null;
            CriticalText = a.CriticalMemberId is { } cid ? $"Member {cid} governs" : "";
            var reactions = doc.Nodes.Where(n => n.HasSupport).Select(n => (n, r: a.ForNode(n.Id)?.Reaction ?? Vec2.Zero));
            ReactionsText = string.Join("\n", reactions.Select(x => $"N{x.n.Id}  {Fmt(x.r.X)}  {Fmt(x.r.Y)} kN"));
            SummaryHint = a.MaxUtilization >= 1 ? "A member is over its limit. Select it to change its section." : "Drag any node to watch the load path change.";
        }
        else
        {
            MaxUtil = 0;
            MaxUtilText = "—";
            MaxDeflectionText = "—";
            HasCritical = false;
            CriticalText = "";
            ReactionsText = "";
            SummaryHint = a.Status switch
            {
                AnalysisStatus.Empty => "Press N and click to place nodes. M connects them.",
                AnalysisStatus.NoLoads => "Add a load with L to see forces.",
                AnalysisStatus.Unsupported => "Add a pin and a roller with S.",
                AnalysisStatus.Mechanism => "Triangulate the structure or add a support.",
                _ => "",
            };
        }
    }

    private static string Fmt(double v) => (v >= 0 ? "+" : "−") + $"{Math.Abs(v),6:0.0}";

    private void RefreshNode(Node node)
    {
        var doc = _editor.Document;
        var a = _editor.Analysis;
        Title = $"Node {node.Id}";
        Subtitle = node.Support.Label() + (node.HasLoad ? $" · {node.Load.Length:0.#} kN" : "");
        NodeX = node.Position.X;
        NodeY = node.Position.Y;
        LoadX = node.Load.X;
        LoadY = node.Load.Y;
        IsPin = node.Support == SupportKind.Pin;
        IsRoller = node.Support == SupportKind.RollerY;
        IsRollerX = node.Support == SupportKind.RollerX;
        IsFree = node.Support == SupportKind.None;
        var members = doc.MembersAt(node.Id).ToList();
        ConnectedText = members.Count == 0 ? "No members" : string.Join(", ", members.Select(m => $"M{m.Id}"));
        var r = a.ForNode(node.Id);
        HasReaction = a.IsSolved && node.HasSupport && r is not null;
        ReactionText = HasReaction ? $"{Fmt(r!.Value.Reaction.X)}  {Fmt(r.Value.Reaction.Y)} kN" : "";
        DisplacementText = a.IsSolved && r is not null ? $"{r.Value.Displacement.X * 1000:+0.00;−0.00}  {r.Value.Displacement.Y * 1000:+0.00;−0.00} mm" : "—";
    }

    private void RefreshMember(Member member)
    {
        var doc = _editor.Document;
        var a = _editor.Analysis;
        Title = $"Member {member.Id}";
        EndpointsText = $"N{member.StartNodeId} → N{member.EndNodeId}";
        LengthText = $"{doc.MemberLength(member):0.000} m";
        MemberSection = member.Section;
        Subtitle = member.Section.Name;
        MaterialText = $"{member.Section.Material.Name} · E {member.Section.Material.ElasticModulusGPa:0} GPa · fy {member.Section.Material.YieldStrengthMPa:0} MPa";
        AreaText = $"A {member.Section.AreaMm2:0} mm²  ·  I {member.Section.SecondMomentMm4 / 1e4:0.#} cm⁴";
        var r = a.For(member.Id);
        if (a.IsSolved && r is { } res)
        {
            IsTension = res.IsTension;
            IsCompression = res.IsCompression;
            ForceKindText = res.IsTension ? "Tension" : res.IsCompression ? "Compression" : "Zero force";
            ForceText = $"{Math.Abs(res.AxialForceKn):0.00} kN";
            StressText = $"{Math.Abs(res.StressMPa):0.0} MPa";
            Util = Math.Clamp(res.Utilization, 0, 1);
            UtilText = $"{res.Utilization * 100:0}%";
            IsOverstressed = res.IsOverstressed;
            BucklingText = res.IsCompression ? $"Pcr {member.Section.CriticalBucklingLoadN(res.LengthM) / 1000:0.0} kN · {res.UtilizationBuckling * 100:0}%" : "n/a (tension)";
            GovernsText = res.IsOverstressed
                ? (res.BucklingGoverns ? "Fails by buckling" : "Fails by yield")
                : (res.BucklingGoverns ? "Buckling governs" : "Yield governs");
        }
        else
        {
            IsTension = IsCompression = false;
            ForceKindText = "Not solved";
            ForceText = "—"; StressText = "—"; UtilText = "—"; Util = 0; IsOverstressed = false; BucklingText = "—"; GovernsText = "";
        }
    }

    private void RefreshMixed()
    {
        var sel = _editor.Selection;
        Title = $"{sel.Count} selected";
        Subtitle = $"{sel.NodeCount} nodes · {sel.MemberCount} members";
        MixedHasNodes = sel.NodeCount > 0;
        MixedHasMembers = sel.MemberCount > 0;
        MixedText = "Bulk actions apply to every selected element.";
    }

    // ---- commits from fields ----

    public void CommitPosition()
    {
        if (_suppress || SelectedNodeId is not { } id) return;
        var node = _editor.Document.GetNode(id);
        var to = new Vec2(NodeX, NodeY);
        if (to == node.Position) return;
        _editor.History.Do(new SetPositionEdit(id, to));
    }

    public void CommitLoad()
    {
        if (_suppress || SelectedNodeId is not { } id) return;
        var node = _editor.Document.GetNode(id);
        var to = new Vec2(LoadX, LoadY);
        if (to == node.Load) return;
        _editor.History.Do(new SetLoadEdit(id, to));
    }

    public void SetSupport(SupportKind kind)
    {
        if (_suppress || SelectedNodeId is not { } id) return;
        if (_editor.Document.GetNode(id).Support == kind) return;
        _editor.History.Do(new SetSupportEdit(id, kind));
    }

    partial void OnMemberSectionChanged(Section? value)
    {
        if (_suppress || value is null || SelectedMemberId is not { } id) return;
        if (_editor.Document.GetMember(id).Section == value) return;
        _editor.History.Do(new SetSectionEdit([id], value));
    }

    public void ApplySectionToSelection(Section section) => _editor.SetSectionOnSelection(section);
}
