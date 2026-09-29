namespace Loadpath.Core.Model;

/// <summary>A two-force bar between two nodes.</summary>
public sealed class Member
{
    public Member(int id, int startNodeId, int endNodeId, Section section)
    {
        Id = id;
        StartNodeId = startNodeId;
        EndNodeId = endNodeId;
        Section = section;
    }

    public int Id { get; }
    public int StartNodeId { get; internal set; }
    public int EndNodeId { get; internal set; }
    public Section Section { get; internal set; }
    public ElementRef Ref => ElementRef.Member(Id);

    public bool Connects(int nodeId) => StartNodeId == nodeId || EndNodeId == nodeId;
    public int OtherEnd(int nodeId) => StartNodeId == nodeId ? EndNodeId : StartNodeId;
}
