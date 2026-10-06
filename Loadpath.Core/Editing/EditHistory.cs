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

    // The save point is the undo entry on top when the document was saved (null: empty history).
    // Undo and redo move the top, so returning to that entry means the document matches the file again.
    // A discarded entry never comes back, so a branch away from the save point stays dirty for good.
    private IEdit? _savedTop;
    private bool _saveValid = true;

    private IEdit? Top => _undo.Count > 0 ? _undo[^1] : null;

    /// <summary>True when the document is back at the state recorded by MarkSaved.</summary>
    public bool IsAtSavePoint => _saveValid && ReferenceEquals(Top, _savedTop);

    public void MarkSaved()
    {
        _savedTop = Top;
        _saveValid = true;
        Changed?.Invoke(this, EventArgs.Empty);
    }

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
            // The saved entry just grew past what was saved; it can no longer mark the save point.
            if (ReferenceEquals(prev, _savedTop)) _saveValid = false;
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
        var wasClean = IsAtSavePoint;
        _undo.Clear();
        _redo.Clear();
        _openTransaction = null;
        // Clearing keeps the content, so it stays clean only if it was clean.
        _savedTop = null;
        _saveValid = wasClean;
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
