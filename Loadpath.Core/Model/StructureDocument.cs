using Loadpath.Core.Geometry;

namespace Loadpath.Core.Model;

public enum DocumentChangeKind { Geometry, Topology, Loads, Supports, Sections, Reset }

public readonly record struct DocumentChange(DocumentChangeKind Kind);

/// <summary>
/// The truth. Nodes and members with stable integer ids.
/// Mutations go through the internal Set*/Add*/Remove* methods, which edits call; the UI never mutates directly.
/// </summary>
public sealed class StructureDocument
{
    private readonly Dictionary<int, Node> _nodes = new();
    private readonly Dictionary<int, Member> _members = new();
    private readonly List<Node> _nodeOrder = new();
    private readonly List<Member> _memberOrder = new();
    private int _nextNodeId = 1;
    private int _nextMemberId = 1;

    public event EventHandler<DocumentChange>? Changed;

    public string Name { get; set; } = "Untitled";
    public IReadOnlyList<Node> Nodes => _nodeOrder;
    public IReadOnlyList<Member> Members => _memberOrder;

    public Node? FindNode(int id) => _nodes.GetValueOrDefault(id);
    public Member? FindMember(int id) => _members.GetValueOrDefault(id);
    public Node GetNode(int id) => _nodes[id];
    public Member GetMember(int id) => _members[id];
    public bool Contains(ElementRef r) => r.IsNode ? _nodes.ContainsKey(r.Id) : _members.ContainsKey(r.Id);

    public IEnumerable<Member> MembersAt(int nodeId) => _memberOrder.Where(m => m.Connects(nodeId));

    public Member? FindMemberBetween(int a, int b) =>
        _memberOrder.FirstOrDefault(m => (m.StartNodeId == a && m.EndNodeId == b) || (m.StartNodeId == b && m.EndNodeId == a));

    public double MemberLength(Member m) => GetNode(m.StartNodeId).Position.DistanceTo(GetNode(m.EndNodeId).Position);

    public Bounds GetBounds()
    {
        var b = Bounds.Empty;
        foreach (var n in _nodeOrder) b = b.Include(n.Position);
        return b;
    }

    public int PeekNextNodeId() => _nextNodeId;
    public int PeekNextMemberId() => _nextMemberId;

    // ---- mutation surface (used by edits and the serializer) ----

    internal Node AddNode(Vec2 position, int? id = null)
    {
        var nodeId = id ?? _nextNodeId;
        if (_nodes.ContainsKey(nodeId)) throw new InvalidOperationException($"Node {nodeId} already exists.");
        var node = new Node(nodeId, position);
        _nodes[nodeId] = node;
        _nodeOrder.Add(node);
        _nodeOrder.Sort((a, b) => a.Id.CompareTo(b.Id));
        _nextNodeId = Math.Max(_nextNodeId, nodeId + 1);
        Raise(DocumentChangeKind.Topology);
        return node;
    }

    internal void RemoveNode(int id)
    {
        if (!_nodes.Remove(id, out var node)) return;
        _nodeOrder.Remove(node);
        Raise(DocumentChangeKind.Topology);
    }

    internal Member AddMember(int startId, int endId, Section section, int? id = null)
    {
        if (!_nodes.ContainsKey(startId) || !_nodes.ContainsKey(endId)) throw new InvalidOperationException("Member endpoints must exist.");
        var memberId = id ?? _nextMemberId;
        if (_members.ContainsKey(memberId)) throw new InvalidOperationException($"Member {memberId} already exists.");
        var member = new Member(memberId, startId, endId, section);
        _members[memberId] = member;
        _memberOrder.Add(member);
        _memberOrder.Sort((a, b) => a.Id.CompareTo(b.Id));
        _nextMemberId = Math.Max(_nextMemberId, memberId + 1);
        Raise(DocumentChangeKind.Topology);
        return member;
    }

    internal void RemoveMember(int id)
    {
        if (!_members.Remove(id, out var member)) return;
        _memberOrder.Remove(member);
        Raise(DocumentChangeKind.Topology);
    }

    internal void SetPosition(int nodeId, Vec2 position, bool raise = true)
    {
        _nodes[nodeId].Position = position;
        if (raise) Raise(DocumentChangeKind.Geometry);
    }

    internal void SetSupport(int nodeId, SupportKind kind)
    {
        _nodes[nodeId].Support = kind;
        Raise(DocumentChangeKind.Supports);
    }

    internal void SetLoad(int nodeId, Vec2 load)
    {
        _nodes[nodeId].Load = load;
        Raise(DocumentChangeKind.Loads);
    }

    internal void SetSection(int memberId, Section section)
    {
        _members[memberId].Section = section;
        Raise(DocumentChangeKind.Sections);
    }

    internal void SetEndpoints(int memberId, int startId, int endId)
    {
        var m = _members[memberId];
        m.StartNodeId = startId;
        m.EndNodeId = endId;
        Raise(DocumentChangeKind.Topology);
    }

    internal void Clear()
    {
        _nodes.Clear();
        _members.Clear();
        _nodeOrder.Clear();
        _memberOrder.Clear();
        _nextNodeId = 1;
        _nextMemberId = 1;
        Raise(DocumentChangeKind.Reset);
    }

    internal void RaiseGeometry() => Raise(DocumentChangeKind.Geometry);
    internal void RaiseReset() => Raise(DocumentChangeKind.Reset);

    private void Raise(DocumentChangeKind kind) => Changed?.Invoke(this, new DocumentChange(kind));
}
