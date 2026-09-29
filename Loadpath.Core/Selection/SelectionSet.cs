using Loadpath.Core.Model;

namespace Loadpath.Core.Selection;

/// <summary>The one selection, shared by canvas, outline, inspector and commands.</summary>
public sealed class SelectionSet
{
    private readonly HashSet<ElementRef> _items = new();

    public event EventHandler? Changed;

    public IReadOnlyCollection<ElementRef> Items => _items;
    public int Count => _items.Count;
    public bool IsEmpty => _items.Count == 0;
    public bool Contains(ElementRef r) => _items.Contains(r);

    public IEnumerable<int> NodeIds => _items.Where(r => r.IsNode).Select(r => r.Id);
    public IEnumerable<int> MemberIds => _items.Where(r => r.IsMember).Select(r => r.Id);
    public int NodeCount => _items.Count(r => r.IsNode);
    public int MemberCount => _items.Count(r => r.IsMember);

    /// <summary>The single selected element, or null when zero or many are selected.</summary>
    public ElementRef? Single => _items.Count == 1 ? _items.First() : null;

    public void Set(ElementRef r) => Replace([r]);

    public void Replace(IEnumerable<ElementRef> refs)
    {
        var next = new HashSet<ElementRef>(refs);
        if (next.SetEquals(_items)) return;
        _items.Clear();
        _items.UnionWith(next);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Add(IEnumerable<ElementRef> refs)
    {
        var before = _items.Count;
        _items.UnionWith(refs);
        if (_items.Count != before) Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Toggle(ElementRef r)
    {
        if (!_items.Remove(r)) _items.Add(r);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Remove(IEnumerable<ElementRef> refs)
    {
        var before = _items.Count;
        _items.ExceptWith(refs);
        if (_items.Count != before) Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        if (_items.Count == 0) return;
        _items.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Drop references that no longer exist in the document.</summary>
    public void Prune(StructureDocument doc)
    {
        var dead = _items.Where(r => !doc.Contains(r)).ToList();
        if (dead.Count > 0) Remove(dead);
    }
}
