using Loadpath.Core.Editing;
using Loadpath.Core.Geometry;
using Loadpath.Core.Model;
using Xunit;

namespace Loadpath.Tests;

public class SavePointTests
{
    private static (StructureDocument Doc, EditHistory History) Fresh()
    {
        var doc = new StructureDocument();
        return (doc, new EditHistory(doc));
    }

    [Fact]
    public void Empty_history_is_clean()
    {
        var (_, h) = Fresh();
        Assert.True(h.IsAtSavePoint);
    }

    [Fact]
    public void Undo_back_to_the_saved_state_is_clean_and_redo_away_is_dirty()
    {
        var (_, h) = Fresh();
        h.Do(new AddNodeEdit(new Vec2(0, 0)));
        h.MarkSaved();
        h.Do(new AddNodeEdit(new Vec2(1, 0)));
        Assert.False(h.IsAtSavePoint);
        h.Undo();
        Assert.True(h.IsAtSavePoint);
        h.Redo();
        Assert.False(h.IsAtSavePoint);
    }

    [Fact]
    public void Undo_past_the_save_point_is_dirty_and_redo_returns_to_clean()
    {
        var (_, h) = Fresh();
        h.Do(new AddNodeEdit(new Vec2(0, 0)));
        h.MarkSaved();
        h.Undo();
        Assert.False(h.IsAtSavePoint);
        h.Redo();
        Assert.True(h.IsAtSavePoint);
    }

    [Fact]
    public void Branching_away_from_the_save_point_stays_dirty()
    {
        var (_, h) = Fresh();
        h.Do(new AddNodeEdit(new Vec2(0, 0)));
        h.Do(new AddNodeEdit(new Vec2(1, 0)));
        h.MarkSaved();
        h.Undo();
        h.Do(new AddNodeEdit(new Vec2(2, 0))); // discards the saved entry from redo
        Assert.False(h.IsAtSavePoint);
        h.Undo();
        Assert.False(h.IsAtSavePoint);
    }

    [Fact]
    public void Saving_an_empty_document_then_editing_and_undoing_is_clean()
    {
        var (_, h) = Fresh();
        h.MarkSaved();
        h.Do(new AddNodeEdit(new Vec2(0, 0)));
        Assert.False(h.IsAtSavePoint);
        h.Undo();
        Assert.True(h.IsAtSavePoint);
    }

    [Fact]
    public void Transactions_count_as_one_step()
    {
        var (_, h) = Fresh();
        h.Do(new AddNodeEdit(new Vec2(0, 0)));
        h.Do(new AddNodeEdit(new Vec2(1, 0)));
        h.MarkSaved();
        h.BeginTransaction("Set supports");
        h.Do(new SetSupportEdit(1, SupportKind.Pin));
        h.Do(new SetSupportEdit(2, SupportKind.RollerY));
        h.CommitTransaction();
        Assert.False(h.IsAtSavePoint);
        h.Undo();
        Assert.True(h.IsAtSavePoint);
    }

    [Fact]
    public void Clear_keeps_clean_or_dirty_as_it_was()
    {
        var (_, h) = Fresh();
        h.Do(new AddNodeEdit(new Vec2(0, 0)));
        h.MarkSaved();
        h.Clear();
        Assert.True(h.IsAtSavePoint);

        h.Do(new AddNodeEdit(new Vec2(1, 0)));
        h.Clear();
        Assert.False(h.IsAtSavePoint);
    }

    [Fact]
    public void A_drag_that_continues_the_saved_entry_is_dirty_even_after_undo_redo()
    {
        var (_, h) = Fresh();
        h.Do(new AddNodeEdit(new Vec2(0, 0)));
        h.Do(new MoveNodesEdit([1], new Vec2(1, 0), gesture: 7));
        h.MarkSaved();
        h.Do(new MoveNodesEdit([1], new Vec2(1, 0), gesture: 7)); // coalesces into the saved entry
        Assert.False(h.IsAtSavePoint);
        h.Undo();
        h.Redo();
        Assert.False(h.IsAtSavePoint);
    }
}
