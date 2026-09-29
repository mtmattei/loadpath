using Loadpath.Core.Editing;
using Loadpath.Core.Geometry;
using Loadpath.Core.Model;
using Loadpath.Core.Serialization;
using Loadpath.Core.Samples;
using Xunit;

namespace Loadpath.Tests;

public class EditingTests
{
    [Fact]
    public void Undo_redo_round_trips_topology()
    {
        var doc = new StructureDocument();
        var h = new EditHistory(doc);
        h.Do(new AddNodeEdit(new Vec2(0, 0)));
        h.Do(new AddNodeEdit(new Vec2(1, 0)));
        h.Do(new AddMemberEdit(1, 2, Section.Chs48));
        Assert.Single(doc.Members);
        h.Undo();
        Assert.Empty(doc.Members);
        h.Undo();
        Assert.Single(doc.Nodes);
        h.Redo();
        h.Redo();
        Assert.Single(doc.Members);
        Assert.Equal(2, doc.Members[0].EndNodeId);
    }

    [Fact]
    public void Drag_moves_coalesce_into_one_entry()
    {
        var doc = new StructureDocument();
        var h = new EditHistory(doc);
        h.Do(new AddNodeEdit(new Vec2(0, 0)));
        h.Do(new MoveNodesEdit([1], new Vec2(0.5, 0), gesture: 7));
        h.Do(new MoveNodesEdit([1], new Vec2(0.5, 0), gesture: 7));
        h.Do(new MoveNodesEdit([1], new Vec2(0, 1), gesture: 7));
        Assert.Equal(new Vec2(1, 1), doc.GetNode(1).Position);
        h.Undo();
        Assert.Equal(new Vec2(0, 0), doc.GetNode(1).Position);
        Assert.True(h.CanRedo);
        Assert.Equal("Add node", h.UndoLabel);
    }

    [Fact]
    public void Deleting_a_node_removes_its_members_and_undo_restores_them()
    {
        var doc = new StructureDocument();
        var h = new EditHistory(doc);
        h.Do(new AddNodeEdit(new Vec2(0, 0)));
        h.Do(new AddNodeEdit(new Vec2(1, 0)));
        h.Do(new AddNodeEdit(new Vec2(1, 1)));
        h.Do(new AddMemberEdit(1, 2, Section.Chs48));
        h.Do(new AddMemberEdit(2, 3, Section.Chs60));
        h.Do(new SetSupportEdit(2, SupportKind.Pin));
        h.Do(new RemoveElementsEdit([ElementRef.Node(2)]));
        Assert.Equal(2, doc.Nodes.Count);
        Assert.Empty(doc.Members);
        h.Undo();
        Assert.Equal(3, doc.Nodes.Count);
        Assert.Equal(2, doc.Members.Count);
        Assert.Equal(SupportKind.Pin, doc.GetNode(2).Support);
        Assert.Equal(Section.Chs60, doc.GetMember(2).Section);
    }

    [Fact]
    public void Split_member_inserts_a_node_and_is_reversible()
    {
        var doc = new StructureDocument();
        var h = new EditHistory(doc);
        h.Do(new AddNodeEdit(new Vec2(0, 0)));
        h.Do(new AddNodeEdit(new Vec2(2, 0)));
        h.Do(new AddMemberEdit(1, 2, Section.Chs48));
        h.Do(new SplitMemberEdit(1, new Vec2(1, 0)));
        Assert.Equal(3, doc.Nodes.Count);
        Assert.Equal(2, doc.Members.Count);
        Assert.Equal(3, doc.GetMember(1).EndNodeId);
        h.Undo();
        Assert.Equal(2, doc.Nodes.Count);
        Assert.Single(doc.Members);
        Assert.Equal(2, doc.GetMember(1).EndNodeId);
        h.Redo();
        Assert.Equal(2, doc.Members.Count);
    }

    [Fact]
    public void Transaction_groups_edits()
    {
        var doc = new StructureDocument();
        var h = new EditHistory(doc);
        h.BeginTransaction("Chain");
        h.Do(new AddNodeEdit(new Vec2(0, 0)));
        h.Do(new AddNodeEdit(new Vec2(1, 0)));
        h.Do(new AddMemberEdit(1, 2, Section.Chs48));
        h.CommitTransaction();
        Assert.Equal("Chain", h.UndoLabel);
        h.Undo();
        Assert.Empty(doc.Nodes);
    }

    [Fact]
    public void Duplicate_copies_nodes_members_supports_and_loads()
    {
        var doc = new StructureDocument();
        var h = new EditHistory(doc);
        h.Do(new AddNodeEdit(new Vec2(0, 0)));
        h.Do(new AddNodeEdit(new Vec2(1, 0)));
        h.Do(new AddMemberEdit(1, 2, Section.Rod16));
        h.Do(new SetLoadEdit(2, new Vec2(0, -3)));
        h.Do(new DuplicateEdit([1, 2], [1], new Vec2(0, 1)));
        Assert.Equal(4, doc.Nodes.Count);
        Assert.Equal(2, doc.Members.Count);
        Assert.Equal(new Vec2(1, 1), doc.GetNode(4).Position);
        Assert.Equal(new Vec2(0, -3), doc.GetNode(4).Load);
        h.Undo();
        Assert.Equal(2, doc.Nodes.Count);
        h.Redo();
        Assert.Equal(4, doc.Nodes.Count);
    }

    [Fact]
    public void Serializer_round_trips_samples()
    {
        foreach (var (id, _, _) in SampleStructures.Catalog)
        {
            var doc = new StructureDocument();
            SampleStructures.Build(id).Restore(doc);
            var json = DocumentSerializer.Serialize(doc);
            var back = new StructureDocument();
            DocumentSerializer.Deserialize(json).Restore(back);
            Assert.Equal(doc.Nodes.Count, back.Nodes.Count);
            Assert.Equal(doc.Members.Count, back.Members.Count);
            Assert.Equal(doc.Name, back.Name);
            for (var i = 0; i < doc.Nodes.Count; i++)
            {
                Assert.Equal(doc.Nodes[i].Position, back.Nodes[i].Position);
                Assert.Equal(doc.Nodes[i].Support, back.Nodes[i].Support);
                Assert.Equal(doc.Nodes[i].Load, back.Nodes[i].Load);
            }
            for (var i = 0; i < doc.Members.Count; i++) Assert.Equal(doc.Members[i].Section.Id, back.Members[i].Section.Id);
        }
    }
}
