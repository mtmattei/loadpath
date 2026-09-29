using Loadpath.Core.Analysis;
using Loadpath.Core.Editing;
using Loadpath.Core.Geometry;
using Loadpath.Core.Model;
using Loadpath.Core.Samples;
using Xunit;

namespace Loadpath.Tests;

public class SolverTests
{
    private static StructureDocument Triangle()
    {
        // Equilateral-ish triangle: pin at A(0,0), roller at B(4,0), apex C(2,3) loaded 10 kN down.
        var doc = new StructureDocument();
        var h = new EditHistory(doc);
        h.Do(new AddNodeEdit(new Vec2(0, 0)));
        h.Do(new AddNodeEdit(new Vec2(4, 0)));
        h.Do(new AddNodeEdit(new Vec2(2, 3)));
        h.Do(new SetSupportEdit(1, SupportKind.Pin));
        h.Do(new SetSupportEdit(2, SupportKind.RollerY));
        h.Do(new SetLoadEdit(3, new Vec2(0, -10)));
        h.Do(new AddMemberEdit(1, 3, Section.Chs48));
        h.Do(new AddMemberEdit(2, 3, Section.Chs48));
        h.Do(new AddMemberEdit(1, 2, Section.Chs48));
        return doc;
    }

    [Fact]
    public void Triangle_matches_method_of_joints()
    {
        var r = TrussSolver.Solve(Triangle());
        Assert.Equal(AnalysisStatus.Solved, r.Status);
        // Legs: length 3.606 m, sin = 3/3.606. Each leg carries 5 kN vertical → F = -5 / 0.832 = -6.009 kN (compression).
        var leg = r.Members[1].AxialForceKn;
        Assert.InRange(leg, -6.02, -6.00);
        Assert.InRange(r.Members[2].AxialForceKn, -6.02, -6.00);
        // Bottom chord in tension: 6.009 * cos = 6.009 * 2/3.606 = 3.333 kN.
        Assert.InRange(r.Members[3].AxialForceKn, 3.32, 3.34);
        // Reactions: 5 kN up at each support.
        Assert.InRange(r.Nodes[1].Reaction.Y, 4.99, 5.01);
        Assert.InRange(r.Nodes[2].Reaction.Y, 4.99, 5.01);
        Assert.InRange(r.Nodes[1].Reaction.X, -0.01, 0.01);
        // Apex moves down.
        Assert.True(r.Nodes[3].Displacement.Y < 0);
    }

    [Fact]
    public void Missing_supports_is_reported()
    {
        var doc = Triangle();
        new EditHistory(doc).Do(new SetSupportEdit(2, SupportKind.None));
        var r = TrussSolver.Solve(doc);
        Assert.Equal(AnalysisStatus.Unsupported, r.Status);
    }

    [Fact]
    public void Mechanism_is_detected()
    {
        // A square without a diagonal is a mechanism.
        var doc = new StructureDocument();
        var h = new EditHistory(doc);
        h.Do(new AddNodeEdit(new Vec2(0, 0)));
        h.Do(new AddNodeEdit(new Vec2(2, 0)));
        h.Do(new AddNodeEdit(new Vec2(2, 2)));
        h.Do(new AddNodeEdit(new Vec2(0, 2)));
        h.Do(new SetSupportEdit(1, SupportKind.Pin));
        h.Do(new SetSupportEdit(2, SupportKind.RollerY));
        h.Do(new SetLoadEdit(3, new Vec2(5, 0)));
        h.Do(new AddMemberEdit(1, 2, Section.Chs48));
        h.Do(new AddMemberEdit(2, 3, Section.Chs48));
        h.Do(new AddMemberEdit(3, 4, Section.Chs48));
        h.Do(new AddMemberEdit(4, 1, Section.Chs48));
        Assert.Equal(AnalysisStatus.Mechanism, TrussSolver.Solve(doc).Status);
        h.Do(new AddMemberEdit(1, 3, Section.Chs48));
        Assert.Equal(AnalysisStatus.Solved, TrussSolver.Solve(doc).Status);
    }

    [Fact]
    public void Warren_sample_is_symmetric_and_in_equilibrium()
    {
        var doc = new StructureDocument();
        SampleStructures.Build("warren").Restore(doc);
        var r = TrussSolver.Solve(doc);
        Assert.Equal(AnalysisStatus.Solved, r.Status);
        var totalLoad = doc.Nodes.Sum(n => n.Load.Y);
        var totalReaction = r.Nodes.Values.Sum(n => n.Reaction.Y);
        Assert.InRange(totalReaction + totalLoad, -1e-6, 1e-6);
        // First and last bottom chord members mirror each other.
        Assert.InRange(r.Members[1].AxialForceKn - r.Members[6].AxialForceKn, -1e-6, 1e-6);
        Assert.True(r.MaxUtilization > 0 && r.MaxUtilization < 1);
        Assert.NotNull(r.CriticalMemberId);
    }

    [Fact]
    public void Compression_member_reports_buckling()
    {
        var doc = Triangle();
        var r = TrussSolver.Solve(doc);
        Assert.True(r.Members[1].IsCompression);
        Assert.True(r.Members[1].UtilizationBuckling > 0);
        Assert.Equal(0, r.Members[3].UtilizationBuckling);
    }
}
