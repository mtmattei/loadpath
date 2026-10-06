using Loadpath.Core.Editing;
using Loadpath.Core.Model;

namespace Loadpath.Core.Analysis;

/// <summary>One proposed section change.</summary>
public readonly record struct SectionChange(int MemberId, Section From, Section To);

/// <param name="Changes">Section changes that bring members to capacity, in member order.</param>
/// <param name="Unresolved">Members still over capacity because no library section carries them.</param>
/// <param name="AddedMassKg">Mass added by the changes (negative if a change saves mass).</param>
public sealed record SizingPlan(IReadOnlyList<SectionChange> Changes, IReadOnlyList<int> Unresolved, double AddedMassKg)
{
    public bool IsEmpty => Changes.Count == 0;
}

/// <summary>
/// Upsizes failing members to the lightest library section that carries them. Members within capacity keep their
/// section unless the upsize itself pushes them above the target. A member stays in its material when that material has a section
/// that works, and switches to the lightest working section of any material otherwise.
/// Changing a section changes stiffness, which moves force around in a redundant truss, so the plan re-solves
/// and repeats until nothing fails or a pass changes nothing.
/// </summary>
public static class SectionSizer
{
    /// <summary>Utilization the chosen section must stay under. Below 1 so a sized member never reads "100%".</summary>
    public const double TargetUtilization = 0.95;
    private const int MaxPasses = 8;

    public static SizingPlan Plan(StructureDocument doc)
    {
        // Work on a scratch copy; the caller applies the plan as undoable edits.
        var scratch = Copy(doc);
        var original = doc.Members.ToDictionary(m => m.Id, m => m.Section);

        var result = TrussSolver.Solve(scratch);
        if (!result.IsSolved) return new SizingPlan([], [], 0);
        var before = result.Members.ToDictionary(kv => kv.Key, kv => kv.Value.Utilization);

        for (var pass = 0; pass < MaxPasses; pass++)
        {
            // Fix what fails, plus anything this plan resized or pushed upward that sits above the target.
            // A member already near its limit before the upsize, and not made worse, is left as it was.
            var targets = result.Members.Values.Where(r =>
                r.Utilization >= 1 ||
                (r.Utilization >= TargetUtilization && (scratch.GetMember(r.MemberId).Section != original[r.MemberId] || r.Utilization > before[r.MemberId] + 1e-9)));
            var changed = false;
            foreach (var r in targets.ToList())
            {
                var member = scratch.GetMember(r.MemberId);
                if (Pick(member.Section, r.AxialForceKn, r.LengthM) is { } to && to != member.Section)
                {
                    scratch.SetSection(member.Id, to);
                    changed = true;
                }
            }
            if (!changed) break;
            result = TrussSolver.Solve(scratch);
        }

        var unresolved = result.IsSolved ? result.Failures.Select(f => f.MemberId).OrderBy(i => i).ToList() : new List<int>();
        return Finish(scratch, original, unresolved);
    }

    /// <summary>
    /// The opposite move: give members the lightest same-material section that keeps every member under the target
    /// (or under its own utilization, for one already above it). Zero-force members keep their section; they often
    /// brace the frame. Members are tried heaviest first, one at a time with a re-solve, so redistribution in a
    /// redundant truss is checked on every change. Refuses (empty plan) while anything fails.
    /// </summary>
    public static SizingPlan PlanLighten(StructureDocument doc)
    {
        var scratch = Copy(doc);
        var original = doc.Members.ToDictionary(m => m.Id, m => m.Section);
        var result = TrussSolver.Solve(scratch);
        if (!result.IsSolved || result.Failures.Count > 0) return new SizingPlan([], [], 0);
        var limit = result.Members.ToDictionary(kv => kv.Key, kv => Math.Max(TargetUtilization, kv.Value.Utilization));

        var order = scratch.Members
            .Where(m => Math.Abs(result.Members[m.Id].AxialForceKn) >= ZeroForceKn)
            .OrderByDescending(m => m.Section.MassPerMeter * scratch.MemberLength(m))
            .Select(m => m.Id)
            .ToList();
        foreach (var id in order)
        {
            var current = scratch.GetMember(id).Section;
            foreach (var candidate in Section.Library.Where(s => s.Material == current.Material && s.MassPerMeter < current.MassPerMeter).OrderBy(s => s.MassPerMeter))
            {
                scratch.SetSection(id, candidate);
                var trial = TrussSolver.Solve(scratch);
                if (trial.IsSolved && trial.Members.All(kv => kv.Value.Utilization <= limit[kv.Key])) { result = trial; current = candidate; break; }
                scratch.SetSection(id, current);
            }
        }
        return Finish(scratch, original, []);
    }

    /// <summary>Forces below this are zero-force members for sizing purposes (kN).</summary>
    public const double ZeroForceKn = 0.05;

    private static StructureDocument Copy(StructureDocument doc)
    {
        var scratch = new StructureDocument();
        DocumentSnapshot.Capture(doc).Restore(scratch);
        return scratch;
    }

    private static SizingPlan Finish(StructureDocument scratch, Dictionary<int, Section> original, IReadOnlyList<int> unresolved)
    {
        var changes = scratch.Members
            .Where(m => m.Section != original[m.Id])
            .Select(m => new SectionChange(m.Id, original[m.Id], m.Section))
            .ToList();
        var added = changes.Sum(c => (c.To.MassPerMeter - c.From.MassPerMeter) * scratch.MemberLength(scratch.GetMember(c.MemberId)));
        return new SizingPlan(changes, unresolved, added);
    }

    /// <summary>Lightest section carrying the force, same material first. Null when nothing in the library does.</summary>
    public static Section? Pick(Section current, double axialForceKn, double lengthM)
    {
        var working = Section.Library.Where(s => Utilization(s, axialForceKn, lengthM) < TargetUtilization).OrderBy(s => s.MassPerMeter).ToList();
        return working.FirstOrDefault(s => s.Material == current.Material) ?? working.FirstOrDefault();
    }

    /// <summary>Governing utilization of a section under an axial force, as the solver computes it.</summary>
    public static double Utilization(Section s, double axialForceKn, double lengthM)
    {
        var n = Math.Abs(axialForceKn) * 1000;
        var yield = n / (s.AreaM2 * s.YieldStrengthPa);
        var buckling = axialForceKn < 0 ? n / s.CriticalBucklingLoadN(lengthM) : 0;
        return Math.Max(yield, buckling);
    }
}
