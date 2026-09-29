using Loadpath.Core.Model;

namespace Loadpath.Core.Editing;

/// <summary>A reversible mutation of the document. Apply and Revert must be exact inverses.</summary>
public interface IEdit
{
    string Label { get; }
    void Apply(StructureDocument doc);
    void Revert(StructureDocument doc);
}

/// <summary>An edit that can absorb a following edit of the same kind (drag coalescing).</summary>
public interface ICoalescingEdit : IEdit
{
    /// <summary>Try to merge <paramref name="next"/> into this edit. Returns true when absorbed.</summary>
    bool TryCoalesce(IEdit next);
}
