using Loadpath.Core.Model;

namespace Loadpath.Core.Editing;

/// <summary>Linear undo/redo. The only path from UI to document mutation.</summary>
public sealed class EditHistory
{
    private readonly StructureDocument _doc;
    private readonly List<IEdit> _undo = new();
    private readonly List<IEdit> _redo = new();
    private CompositeEdit? _openTransaction;

    public EditHistory(StructureDocument doc) => _doc = doc;

    public event EventHandler? Changed;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public string? UndoLabel => _undo.Count > 0 ? _undo[^1].Label : null;
    public string? RedoLabel => _redo.Count > 0 ? _redo[^1].Label : null;
    public int Version { get; private set; }

    /// <summary>Apply an edit and push it. Coalesces into the previous edit when that edit accepts it.</summary>
    public void Do(IEdit edit)
    {
        edit.Apply(_doc);
        if (_openTransaction is not null)
        {
            _openTransaction.Add(edit);
            return;
        }
        _redo.Clear();
        if (_undo.Count > 0 && _undo[^1] is ICoalescingEdit prev && prev.TryCoalesce(edit))
        {
            Version++;
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }
        _undo.Add(edit);
        Version++;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Group the edits made until Commit into one undo entry.</summary>
    public void BeginTransaction(string label)
    {
        if (_openTransaction is not null) throw new InvalidOperationException("Transaction already open.");
        _openTransaction = new CompositeEdit(label);
    }

    public void CommitTransaction()
    {
        var t = _openTransaction ?? throw new InvalidOperationException("No open transaction.");
        _openTransaction = null;
        if (t.Count == 0) return;
        _redo.Clear();
        _undo.Add(t);
        Version++;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool IsInTransaction => _openTransaction is not null;

    public void Undo()
    {
        if (_undo.Count == 0) return;
        var e = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        e.Revert(_doc);
        _redo.Add(e);
        Version++;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Redo()
    {
        if (_redo.Count == 0) return;
        var e = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        e.Apply(_doc);
        _undo.Add(e);
        Version++;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        _openTransaction = null;
        Version++;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>Several edits as one undo entry, reverted in reverse order.</summary>
public sealed class CompositeEdit : IEdit
{
    private readonly List<IEdit> _edits = new();
    public CompositeEdit(string label) => Label = label;
    public string Label { get; }
    public int Count => _edits.Count;
    public void Add(IEdit e) => _edits.Add(e);

    public void Apply(StructureDocument doc)
    {
        foreach (var e in _edits) e.Apply(doc);
    }

    public void Revert(StructureDocument doc)
    {
        for (var i = _edits.Count - 1; i >= 0; i--) _edits[i].Revert(doc);
    }
}
