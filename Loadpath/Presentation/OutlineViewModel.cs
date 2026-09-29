using System.Collections.ObjectModel;
using Loadpath.Core.Analysis;

namespace Loadpath.Presentation;

/// <summary>One outline row. Selection and hover are synced both ways with the canvas.</summary>
public sealed partial class OutlineItem : ObservableObject
{
    public OutlineItem(ElementRef r) => Ref = r;
    public ElementRef Ref { get; }
    [ObservableProperty] private string _label = "";
    [ObservableProperty] private string _detail = "";
    [ObservableProperty] private string _value = "";
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isTension;
    [ObservableProperty] private bool _isCompression;
    [ObservableProperty] private bool _isDanger;
    [ObservableProperty] private bool _hasBadge;
    [ObservableProperty] private string _badge = "";
    public bool IsNode => Ref.IsNode;
    public bool IsMember => Ref.IsMember;
}

/// <summary>Flat list of nodes then members, rebuilt on topology changes, updated in place otherwise.</summary>
public sealed partial class OutlineViewModel : ObservableObject
{
    private readonly EditorViewModel _editor;
    private readonly Dictionary<ElementRef, OutlineItem> _index = new();

    public OutlineViewModel(EditorViewModel editor)
    {
        _editor = editor;
        editor.AnalysisChanged += (_, _) => Rebuild();
        editor.SelectionChanged += (_, _) => SyncSelection();
        Rebuild();
    }

    public ObservableCollection<OutlineItem> Nodes { get; } = new();
    public ObservableCollection<OutlineItem> Members { get; } = new();

    [ObservableProperty] private string _nodesHeader = "Nodes";
    [ObservableProperty] private string _membersHeader = "Members";
    [ObservableProperty] private bool _isEmpty = true;
    [ObservableProperty] private bool _nodesExpanded = true;
    [ObservableProperty] private bool _membersExpanded = true;

    private void Rebuild()
    {
        var doc = _editor.Document;
        var a = _editor.Analysis;
        var sel = _editor.Selection;

        Sync(Nodes, doc.Nodes.Select(n => n.Ref), r =>
        {
            var n = doc.GetNode(r.Id);
            var item = GetOrCreate(r);
            item.Label = $"N{n.Id}";
            item.Detail = $"{n.Position.X:0.##}, {n.Position.Y:0.##}";
            item.Value = n.HasLoad ? $"{n.Load.Length:0.#} kN" : "";
            item.HasBadge = n.HasSupport;
            item.Badge = n.Support switch { SupportKind.Pin => "PIN", SupportKind.RollerY => "ROL", SupportKind.RollerX => "ROL", _ => "" };
            item.IsSelected = sel.Contains(r);
            item.IsTension = item.IsCompression = item.IsDanger = false;
            return item;
        });

        Sync(Members, doc.Members.Select(m => m.Ref), r =>
        {
            var m = doc.GetMember(r.Id);
            var item = GetOrCreate(r);
            var res = a.For(m.Id);
            item.Label = $"M{m.Id}";
            item.Detail = $"N{m.StartNodeId}–N{m.EndNodeId} · {m.Section.Name}";
            if (a.IsSolved && res is { } rr)
            {
                item.Value = (rr.AxialForceKn >= 0 ? "+" : "−") + $"{Math.Abs(rr.AxialForceKn):0.0}";
                item.IsTension = rr.IsTension;
                item.IsCompression = rr.IsCompression;
                item.IsDanger = rr.IsOverstressed;
            }
            else
            {
                item.Value = "";
                item.IsTension = item.IsCompression = item.IsDanger = false;
            }
            item.HasBadge = false;
            item.IsSelected = sel.Contains(r);
            return item;
        });

        NodesHeader = $"Nodes · {doc.Nodes.Count}";
        MembersHeader = $"Members · {doc.Members.Count}";
        IsEmpty = doc.Nodes.Count == 0;
    }

    private OutlineItem GetOrCreate(ElementRef r)
    {
        if (!_index.TryGetValue(r, out var item)) { item = new OutlineItem(r); _index[r] = item; }
        return item;
    }

    private static void Sync(ObservableCollection<OutlineItem> target, IEnumerable<ElementRef> refs, Func<ElementRef, OutlineItem> update)
    {
        var wanted = refs.Select(update).ToList();
        // Cheap structural sync: replace when the ref sequence differs, otherwise items updated in place.
        if (wanted.Count != target.Count || !wanted.Select(w => w.Ref).SequenceEqual(target.Select(t => t.Ref)))
        {
            target.Clear();
            foreach (var w in wanted) target.Add(w);
        }
    }

    private void SyncSelection()
    {
        var sel = _editor.Selection;
        foreach (var item in _index.Values) item.IsSelected = sel.Contains(item.Ref);
    }

    public void Select(OutlineItem item, bool extend)
    {
        if (extend) _editor.Selection.Toggle(item.Ref);
        else _editor.Selection.Set(item.Ref);
    }

    public void Hover(OutlineItem? item) => _editor.Interaction.HoverFromOutline(item?.Ref);
}
