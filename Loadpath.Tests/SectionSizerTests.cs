using Loadpath.Core.Analysis;
using Loadpath.Core.Editing;
using Loadpath.Core.Geometry;
using Loadpath.Core.Model;
using Loadpath.Core.Samples;
using Xunit;

namespace Loadpath.Tests;

public class SectionSizerTests
{
    private static StructureDocument Preset(string id, double span, int panels, double depth)
    {
        var doc = new StructureDocument();
        TrussPresets.Build(id, new PresetParameters(span, panels, depth)).Restore(doc);
        return doc;
    }

    private static void Apply(StructureDocument doc, SizingPlan plan)
    {
        var h = new EditHistory(doc);
        foreach (var g in plan.Changes.GroupBy(c => c.To)) h.Do(new SetSectionEdit(g.Select(c => c.MemberId), g.Key));
    }

    [Fact]
    public void Overloaded_pratt_is_brought_within_capacity()
    {
        var doc = Preset("pratt", 24, 6, 1.6);
        Assert.NotEmpty(TrussSolver.Solve(doc).Failures);

        var plan = SectionSizer.Plan(doc);
        Assert.NotEmpty(plan.Changes);
        Assert.Empty(plan.Unresolved);
        Assert.True(plan.AddedMassKg > 0);

        Apply(doc, plan);
        var after = TrussSolver.Solve(doc);
        Assert.Empty(after.Failures);
    }

    [Fact]
    public void Only_failing_members_change_and_each_gets_the_lightest_working_section()
    {
        var doc = Preset("pratt", 24, 6, 1.6);
        var before = TrussSolver.Solve(doc);
        var failing = before.Failures.Select(f => f.MemberId).ToHashSet();

        var plan = SectionSizer.Plan(doc);
        Assert.All(plan.Changes, c => Assert.Contains(c.MemberId, failing));

        // A Pratt is statically determinate: forces do not move, so each pick can be checked against the original force.
        foreach (var c in plan.Changes)
        {
            var r = before.Members[c.MemberId];
            Assert.True(SectionSizer.Utilization(c.To, r.AxialForceKn, r.LengthM) < SectionSizer.TargetUtilization);
            var lighterSameMaterial = Section.Library.Where(s => s.Material == c.To.Material && s.MassPerMeter < c.To.MassPerMeter);
            Assert.All(lighterSameMaterial, s => Assert.True(SectionSizer.Utilization(s, r.AxialForceKn, r.LengthM) >= SectionSizer.TargetUtilization));
        }
    }

    [Fact]
    public void Redundant_truss_converges()
    {
        // The K truss has one redundant member, so resizing moves force around.
        var doc = Preset("k", 24, 8, 3);
        Assert.NotEmpty(TrussSolver.Solve(doc).Failures);
        var plan = SectionSizer.Plan(doc);
        Apply(doc, plan);
        Assert.Equal(plan.Unresolved.Count, TrussSolver.Solve(doc).Failures.Count);
    }

    [Fact]
    public void Nothing_failing_means_an_empty_plan()
    {
        var plan = SectionSizer.Plan(Preset("warren", 12, 6, 2));
        Assert.True(plan.IsEmpty);
        Assert.Empty(plan.Unresolved);
    }

    [Fact]
    public void Load_beyond_the_library_is_reported_unresolved()
    {
        var doc = Preset("pratt", 12, 6, 2);
        var h = new EditHistory(doc);
        foreach (var n in doc.Nodes.Where(n => n.HasLoad).ToList()) h.Do(new SetLoadEdit(n.Id, new Vec2(0, -2000)));
        var plan = SectionSizer.Plan(doc);
        Assert.NotEmpty(plan.Unresolved);
    }

    [Fact]
    public void Timber_stays_timber_when_possible_and_switches_when_not()
    {
        // 45x95 timber under 20 kN tension over 1 m: 20e3 / (4275e-6 * 24e6) = 0.19, keeps timber.
        Assert.Equal(Section.Timber45x95, SectionSizer.Pick(Section.Timber45x95, 20, 1));
        // 200 kN is beyond the only timber section; the pick moves to the lightest steel or aluminum that works.
        var pick = SectionSizer.Pick(Section.Timber45x95, 200, 1);
        Assert.NotNull(pick);
        Assert.NotEqual(Material.Timber, pick!.Material);
    }
}
