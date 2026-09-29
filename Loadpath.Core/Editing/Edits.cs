using Loadpath.Core.Geometry;
using Loadpath.Core.Model;

namespace Loadpath.Core.Editing;

public sealed class AddNodeEdit : IEdit
{
    private readonly Vec2 _position;
    private int _id;
    public AddNodeEdit(Vec2 position) => _position = position;
    public string Label => "Add node";
    public int NodeId => _id;

    public void Apply(StructureDocument doc)
    {
        var n = _id == 0 ? doc.AddNode(_position) : doc.AddNode(_position, _id);
        _id = n.Id;
    }

    public void Revert(StructureDocument doc) => doc.RemoveNode(_id);
}

public sealed class AddMemberEdit : IEdit
{
    private readonly int _start;
    private readonly int _end;
    private readonly Section _section;
    private int _id;
    public AddMemberEdit(int startNodeId, int endNodeId, Section section)
    {
        _start = startNodeId;
        _end = endNodeId;
        _section = section;
    }
    public string Label => "Add member";
    public int MemberId => _id;

    public void Apply(StructureDocument doc)
    {
        var m = _id == 0 ? doc.AddMember(_start, _end, _section) : doc.AddMember(_start, _end, _section, _id);
        _id = m.Id;
    }

    public void Revert(StructureDocument doc) => doc.RemoveMember(_id);
}

/// <summary>Translate a set of nodes. Consecutive moves of the same node set coalesce (drag).</summary>
public sealed class MoveNodesEdit : ICoalescingEdit
{
    private readonly int[] _nodeIds;
    private readonly int _gesture;
    private Vec2 _delta;

    /// <param name="gesture">Edits with the same non-zero gesture id coalesce; 0 never coalesces.</param>
    public MoveNodesEdit(IEnumerable<int> nodeIds, Vec2 delta, int gesture = 0)
    {
        _nodeIds = nodeIds.Distinct().OrderBy(i => i).ToArray();
        _delta = delta;
        _gesture = gesture;
    }
    public string Label => _nodeIds.Length == 1 ? "Move node" : $"Move {_nodeIds.Length} nodes";
    public Vec2 Delta => _delta;

    public void Apply(StructureDocument doc) => Shift(doc, _delta);
    public void Revert(StructureDocument doc) => Shift(doc, -_delta);

    private void Shift(StructureDocument doc, Vec2 d)
    {
        foreach (var id in _nodeIds) doc.SetPosition(id, doc.GetNode(id).Position + d, raise: false);
        doc.RaiseGeometry();
    }

    public bool TryCoalesce(IEdit next)
    {
        if (_gesture == 0 || next is not MoveNodesEdit m || m._gesture != _gesture || !m._nodeIds.AsSpan().SequenceEqual(_nodeIds)) return false;
        _delta += m._delta;
        return true;
    }
}

/// <summary>Set an absolute position (inspector field). Coalesces with itself for the same node.</summary>
public sealed class SetPositionEdit : IEdit
{
    private readonly int _nodeId;
    private readonly Vec2 _to;
    private Vec2 _from;
    public SetPositionEdit(int nodeId, Vec2 to) { _nodeId = nodeId; _to = to; }
    public string Label => "Set position";
    public void Apply(StructureDocument doc) { _from = doc.GetNode(_nodeId).Position; doc.SetPosition(_nodeId, _to); }
    public void Revert(StructureDocument doc) => doc.SetPosition(_nodeId, _from);
}

public sealed class SetSupportEdit : IEdit
{
    private readonly int _nodeId;
    private readonly SupportKind _to;
    private SupportKind _from;
    public SetSupportEdit(int nodeId, SupportKind to) { _nodeId = nodeId; _to = to; }
    public string Label => _to == SupportKind.None ? "Remove support" : $"Set {_to.Label().ToLowerInvariant()}";
    public void Apply(StructureDocument doc) { _from = doc.GetNode(_nodeId).Support; doc.SetSupport(_nodeId, _to); }
    public void Revert(StructureDocument doc) => doc.SetSupport(_nodeId, _from);
}

public sealed class SetLoadEdit : ICoalescingEdit
{
    private readonly int _nodeId;
    private Vec2 _to;
    private Vec2 _from;
    private readonly int _gesture;

    /// <param name="gesture">Edits with the same non-zero gesture id coalesce (vector drag); 0 never coalesces.</param>
    public SetLoadEdit(int nodeId, Vec2 to, int gesture = 0) { _nodeId = nodeId; _to = to; _gesture = gesture; }
    public string Label => _to.LengthSquared < 1e-12 ? "Clear load" : "Set load";
    public void Apply(StructureDocument doc) { _from = doc.GetNode(_nodeId).Load; doc.SetLoad(_nodeId, _to); }
    public void Revert(StructureDocument doc) => doc.SetLoad(_nodeId, _from);
    public bool TryCoalesce(IEdit next)
    {
        if (_gesture == 0 || next is not SetLoadEdit s || s._gesture != _gesture || s._nodeId != _nodeId) return false;
        _to = s._to;
        return true;
    }
}

public sealed class SetSectionEdit : IEdit
{
    private readonly int[] _memberIds;
    private readonly Section _to;
    private readonly Dictionary<int, Section> _from = new();
    public SetSectionEdit(IEnumerable<int> memberIds, Section to) { _memberIds = memberIds.Distinct().ToArray(); _to = to; }
    public string Label => _memberIds.Length == 1 ? $"Set section {_to.Name}" : $"Set section on {_memberIds.Length} members";
    public void Apply(StructureDocument doc)
    {
        foreach (var id in _memberIds) { _from[id] = doc.GetMember(id).Section; doc.SetSection(id, _to); }
    }
    public void Revert(StructureDocument doc)
    {
        foreach (var id in _memberIds) doc.SetSection(id, _from[id]);
    }
}

/// <summary>Remove nodes and members. Removing a node also removes the members that touch it.</summary>
public sealed class RemoveElementsEdit : IEdit
{
    private readonly List<ElementRef> _requested;
    private readonly List<(int Id, int Start, int End, Section Section)> _members = new();
    private readonly List<(int Id, Vec2 Position, SupportKind Support, Vec2 Load)> _nodes = new();
    private bool _captured;

    public RemoveElementsEdit(IEnumerable<ElementRef> refs) => _requested = refs.Distinct().ToList();

    public string Label
    {
        get
        {
            var n = _requested.Count(r => r.IsNode);
            var m = _requested.Count(r => r.IsMember);
            return (n, m) switch
            {
                (1, 0) => "Delete node",
                (0, 1) => "Delete member",
                (_, 0) => $"Delete {n} nodes",
                (0, _) => $"Delete {m} members",
                _ => $"Delete {n + m} elements",
            };
        }
    }

    public void Apply(StructureDocument doc)
    {
        if (!_captured)
        {
            var nodeIds = _requested.Where(r => r.IsNode).Select(r => r.Id).ToHashSet();
            var memberIds = _requested.Where(r => r.IsMember).Select(r => r.Id).ToHashSet();
            foreach (var m in doc.Members)
            {
                if (memberIds.Contains(m.Id) || nodeIds.Contains(m.StartNodeId) || nodeIds.Contains(m.EndNodeId))
                    _members.Add((m.Id, m.StartNodeId, m.EndNodeId, m.Section));
            }
            foreach (var id in nodeIds)
            {
                var n = doc.GetNode(id);
                _nodes.Add((n.Id, n.Position, n.Support, n.Load));
            }
            _captured = true;
        }
        foreach (var m in _members) doc.RemoveMember(m.Id);
        foreach (var n in _nodes) doc.RemoveNode(n.Id);
    }

    public void Revert(StructureDocument doc)
    {
        foreach (var n in _nodes)
        {
            doc.AddNode(n.Position, n.Id);
            if (n.Support != SupportKind.None) doc.SetSupport(n.Id, n.Support);
            if (n.Load.LengthSquared > 0) doc.SetLoad(n.Id, n.Load);
        }
        foreach (var m in _members) doc.AddMember(m.Start, m.End, m.Section, m.Id);
    }
}

/// <summary>Insert a node on a member at parameter t and replace the member with two.</summary>
public sealed class SplitMemberEdit : IEdit
{
    private readonly int _memberId;
    private readonly Vec2 _at;
    private int _newNodeId;
    private int _newMemberId;
    private int _start, _end;
    private Section _section = Section.Chs48;

    public SplitMemberEdit(int memberId, Vec2 at) { _memberId = memberId; _at = at; }
    public string Label => "Split member";
    public int NewNodeId => _newNodeId;

    public void Apply(StructureDocument doc)
    {
        var m = doc.GetMember(_memberId);
        _start = m.StartNodeId;
        _end = m.EndNodeId;
        _section = m.Section;
        var node = _newNodeId == 0 ? doc.AddNode(_at) : doc.AddNode(_at, _newNodeId);
        _newNodeId = node.Id;
        doc.SetEndpoints(_memberId, _start, _newNodeId);
        var second = _newMemberId == 0 ? doc.AddMember(_newNodeId, _end, _section) : doc.AddMember(_newNodeId, _end, _section, _newMemberId);
        _newMemberId = second.Id;
    }

    public void Revert(StructureDocument doc)
    {
        doc.RemoveMember(_newMemberId);
        doc.SetEndpoints(_memberId, _start, _end);
        doc.RemoveNode(_newNodeId);
    }
}

/// <summary>Duplicate nodes (with supports/loads) and the members among them, offset by a delta.</summary>
public sealed class DuplicateEdit : IEdit
{
    private readonly int[] _nodeIds;
    private readonly int[] _memberIds;
    private readonly Vec2 _offset;
    private readonly Dictionary<int, int> _nodeMap = new();
    private readonly List<int> _newMembers = new();
    private bool _idsFixed;
    private readonly List<(int Id, Vec2 Pos, SupportKind Support, Vec2 Load)> _newNodes = new();
    private readonly List<(int Id, int Start, int End, Section Section)> _newMemberDefs = new();

    public DuplicateEdit(IEnumerable<int> nodeIds, IEnumerable<int> memberIds, Vec2 offset)
    {
        _nodeIds = nodeIds.Distinct().ToArray();
        _memberIds = memberIds.Distinct().ToArray();
        _offset = offset;
    }

    public string Label => "Duplicate";
    public IEnumerable<ElementRef> CreatedRefs => _newNodes.Select(n => ElementRef.Node(n.Id)).Concat(_newMemberDefs.Select(m => ElementRef.Member(m.Id)));

    public void Apply(StructureDocument doc)
    {
        if (!_idsFixed)
        {
            // Members whose both ends are duplicated come along; a selected member brings its nodes.
            var nodeSet = _nodeIds.ToHashSet();
            foreach (var mid in _memberIds)
            {
                var m = doc.GetMember(mid);
                nodeSet.Add(m.StartNodeId);
                nodeSet.Add(m.EndNodeId);
            }
            foreach (var id in nodeSet.OrderBy(i => i))
            {
                var n = doc.GetNode(id);
                var created = doc.AddNode(n.Position + _offset);
                _nodeMap[id] = created.Id;
                _newNodes.Add((created.Id, created.Position, n.Support, n.Load));
                if (n.Support != SupportKind.None) doc.SetSupport(created.Id, n.Support);
                if (n.HasLoad) doc.SetLoad(created.Id, n.Load);
            }
            foreach (var m in doc.Members.ToList())
            {
                if (_nodeMap.ContainsKey(m.StartNodeId) && _nodeMap.ContainsKey(m.EndNodeId) && (nodeSet.Contains(m.StartNodeId) && nodeSet.Contains(m.EndNodeId)))
                {
                    if (_nodeMap.ContainsValue(m.Id)) { }
                    var created = doc.AddMember(_nodeMap[m.StartNodeId], _nodeMap[m.EndNodeId], m.Section);
                    _newMemberDefs.Add((created.Id, created.StartNodeId, created.EndNodeId, m.Section));
                }
            }
            _idsFixed = true;
            return;
        }
        foreach (var n in _newNodes)
        {
            doc.AddNode(n.Pos, n.Id);
            if (n.Support != SupportKind.None) doc.SetSupport(n.Id, n.Support);
            if (n.Load.LengthSquared > 0) doc.SetLoad(n.Id, n.Load);
        }
        foreach (var m in _newMemberDefs) doc.AddMember(m.Start, m.End, m.Section, m.Id);
    }

    public void Revert(StructureDocument doc)
    {
        foreach (var m in _newMemberDefs) doc.RemoveMember(m.Id);
        foreach (var n in _newNodes) doc.RemoveNode(n.Id);
    }
}

/// <summary>Replace the whole document (open, new, sample). Snapshots before and after.</summary>
public sealed class ReplaceDocumentEdit : IEdit
{
    private readonly DocumentSnapshot _to;
    private DocumentSnapshot? _from;
    public ReplaceDocumentEdit(DocumentSnapshot to, string label) { _to = to; Label = label; }
    public string Label { get; }
    public void Apply(StructureDocument doc) { _from ??= DocumentSnapshot.Capture(doc); _to.Restore(doc); }
    public void Revert(StructureDocument doc) => _from?.Restore(doc);
}

/// <summary>Immutable copy of a document's content.</summary>
public sealed class DocumentSnapshot
{
    public required string Name { get; init; }
    public required IReadOnlyList<(int Id, Vec2 Position, SupportKind Support, Vec2 Load)> Nodes { get; init; }
    public required IReadOnlyList<(int Id, int Start, int End, string SectionId)> Members { get; init; }

    public static DocumentSnapshot Capture(StructureDocument doc) => new()
    {
        Name = doc.Name,
        Nodes = doc.Nodes.Select(n => (n.Id, n.Position, n.Support, n.Load)).ToList(),
        Members = doc.Members.Select(m => (m.Id, m.StartNodeId, m.EndNodeId, m.Section.Id)).ToList(),
    };

    public void Restore(StructureDocument doc)
    {
        doc.Clear();
        doc.Name = Name;
        foreach (var n in Nodes)
        {
            doc.AddNode(n.Position, n.Id);
            if (n.Support != SupportKind.None) doc.SetSupport(n.Id, n.Support);
            if (n.Load.LengthSquared > 0) doc.SetLoad(n.Id, n.Load);
        }
        foreach (var m in Members) doc.AddMember(m.Start, m.End, Section.FindById(m.SectionId), m.Id);
        doc.RaiseReset();
    }
}
