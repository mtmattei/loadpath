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
/// Upsizes failing members to the lightest library section that carries them. Only failing members change;
/// members within capacity keep their section. A member stays in its material when that material has a section
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
        var scratch = new StructureDocument();
        DocumentSnapshot.Capture(doc).Restore(scratch);
        var original = doc.Members.ToDictionary(m => m.Id, m => m.Section);

        var result = TrussSolver.Solve(scratch);
        for (var pass = 0; pass < MaxPasses && result.IsSolved && result.Failures.Count > 0; pass++)
        {
            var changed = false;
            foreach (var f in result.Failures)
            {
                var member = scratch.GetMember(f.MemberId);
                if (Pick(member.Section, f.AxialForceKn, f.LengthM) is { } to && to != member.Section)
                {
                    scratch.SetSection(member.Id, to);
                    changed = true;
                }
            }
            if (!changed) break;
            result = TrussSolver.Solve(scratch);
        }

        var changes = scratch.Members
            .Where(m => m.Section != original[m.Id])
            .Select(m => new SectionChange(m.Id, original[m.Id], m.Section))
            .ToList();
        var added = changes.Sum(c => (c.To.MassPerMeter - c.From.MassPerMeter) * scratch.MemberLength(scratch.GetMember(c.MemberId)));
        var unresolved = result.IsSolved ? result.Failures.Select(f => f.MemberId).OrderBy(i => i).ToList() : new List<int>();
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
