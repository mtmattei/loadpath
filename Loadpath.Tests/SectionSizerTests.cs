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
    public void In_a_determinate_truss_only_failing_members_change_and_each_gets_the_lightest_working_section()
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

    [Fact]
    public void Upsize_leaves_no_member_it_touched_or_pushed_above_the_target()
    {
        // The 30 m K truss is redundant: upsizing the failures moves force onto members that were passing.
        var doc = Preset("k", 30, 6, 2);
        var before = TrussSolver.Solve(doc).Members.ToDictionary(kv => kv.Key, kv => kv.Value.Utilization);
        var plan = SectionSizer.Plan(doc);
        Apply(doc, plan);
        var after = TrussSolver.Solve(doc);
        Assert.Empty(after.Failures);
        foreach (var (id, r) in after.Members)
            Assert.True(r.Utilization < SectionSizer.TargetUtilization || r.Utilization <= before[id] + 1e-9,
                $"M{id} ends at {r.Utilization:P1} (was {before[id]:P1})");
    }

    [Fact]
    public void Lighten_saves_mass_and_keeps_every_member_within_its_limit()
    {
        // A short, lightly loaded Pratt with heavy default sections has room to lose.
        var doc = Preset("pratt", 8, 4, 1.5);
        var before = TrussSolver.Solve(doc);
        Assert.Empty(before.Failures);
        var plan = SectionSizer.PlanLighten(doc);
        Assert.NotEmpty(plan.Changes);
        Assert.True(plan.AddedMassKg < 0);
        Assert.All(plan.Changes, c => Assert.Equal(c.From.Material, c.To.Material));
        Apply(doc, plan);
        var after = TrussSolver.Solve(doc);
        Assert.All(after.Members, kv => Assert.True(kv.Value.Utilization <= Math.Max(SectionSizer.TargetUtilization, before.Members[kv.Key].Utilization) + 1e-9));
    }

    [Fact]
    public void Lighten_keeps_zero_force_members()
    {
        var doc = Preset("pratt", 8, 4, 1.5);
        var r = TrussSolver.Solve(doc);
        var zero = r.Members.Values.Where(m => Math.Abs(m.AxialForceKn) < SectionSizer.ZeroForceKn).Select(m => m.MemberId).ToHashSet();
        Assert.NotEmpty(zero); // the premise: this Pratt has zero-force members
        Assert.DoesNotContain(SectionSizer.PlanLighten(doc).Changes, c => zero.Contains(c.MemberId));
    }

    [Fact]
    public void Lighten_refuses_while_something_fails()
    {
        Assert.True(SectionSizer.PlanLighten(Preset("pratt", 24, 6, 1.6)).IsEmpty);
    }

    [Fact]
    public void Lighten_then_lighten_again_changes_nothing()
    {
        var doc = Preset("k", 16, 8, 3);
        Apply(doc, SectionSizer.PlanLighten(doc));
        Assert.True(SectionSizer.PlanLighten(doc).IsEmpty);
    }

    [Fact]
    public void Redundant_truss_lightens_without_new_overloads()
    {
        var doc = Preset("k", 16, 8, 3);
        var before = TrussSolver.Solve(doc);
        Apply(doc, SectionSizer.PlanLighten(doc));
        var after = TrussSolver.Solve(doc);
        Assert.Empty(after.Failures);
        Assert.All(after.Members, kv => Assert.True(kv.Value.Utilization <= Math.Max(SectionSizer.TargetUtilization, before.Members[kv.Key].Utilization) + 1e-9));
    }
}
