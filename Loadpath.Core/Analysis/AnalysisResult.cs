using Loadpath.Core.Geometry;

namespace Loadpath.Core.Analysis;

public enum AnalysisStatus
{
    /// <summary>No nodes or members.</summary>
    Empty,
    /// <summary>Members exist but nothing is loaded; nothing to solve.</summary>
    NoLoads,
    /// <summary>Not enough supports to prevent rigid-body motion.</summary>
    Unsupported,
    /// <summary>Stiffness matrix is singular: the structure can move freely somewhere.</summary>
    Mechanism,
    Solved,
}

/// <summary>Per-member outcome. Forces in kN (positive = tension), stress in MPa.</summary>
public readonly record struct MemberResult(
    int MemberId,
    double AxialForceKn,
    double StressMPa,
    double LengthM,
    double UtilizationYield,
    double UtilizationBuckling)
{
    public bool IsTension => AxialForceKn > 1e-9;
    public bool IsCompression => AxialForceKn < -1e-9;
    /// <summary>Governing utilization (0..∞). ≥ 1 means failure.</summary>
    public double Utilization => Math.Max(UtilizationYield, UtilizationBuckling);
    public bool BucklingGoverns => UtilizationBuckling > UtilizationYield;
    public bool IsOverstressed => Utilization >= 1.0;
}

/// <summary>Per-node outcome. Displacement in meters; reaction in kN (only nonzero at supports).</summary>
public readonly record struct NodeResult(int NodeId, Vec2 Displacement, Vec2 Reaction);

public sealed class AnalysisResult
{
    public static readonly AnalysisResult EmptyResult = new(AnalysisStatus.Empty, new Dictionary<int, MemberResult>(), new Dictionary<int, NodeResult>(), 0, 0, 0, null, 0);

    public AnalysisResult(
        AnalysisStatus status,
        IReadOnlyDictionary<int, MemberResult> members,
        IReadOnlyDictionary<int, NodeResult> nodes,
        double maxAbsForceKn,
        double maxUtilization,
        double maxDisplacementM,
        int? criticalMemberId,
        double solveMilliseconds)
    {
        Status = status;
        Members = members;
        Nodes = nodes;
        MaxAbsForceKn = maxAbsForceKn;
        MaxUtilization = maxUtilization;
        MaxDisplacementM = maxDisplacementM;
        CriticalMemberId = criticalMemberId;
        SolveMilliseconds = solveMilliseconds;
    }

    public AnalysisStatus Status { get; }
    public bool IsSolved => Status == AnalysisStatus.Solved;
    public IReadOnlyDictionary<int, MemberResult> Members { get; }
    public IReadOnlyDictionary<int, NodeResult> Nodes { get; }
    public double MaxAbsForceKn { get; }
    public double MaxUtilization { get; }
    public double MaxDisplacementM { get; }
    public int? CriticalMemberId { get; }
    public double SolveMilliseconds { get; }

    public MemberResult? For(int memberId) => Members.TryGetValue(memberId, out var r) ? r : null;
    public NodeResult? ForNode(int nodeId) => Nodes.TryGetValue(nodeId, out var r) ? r : null;
}
