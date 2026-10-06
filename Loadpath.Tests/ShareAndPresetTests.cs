using Loadpath.Core.Analysis;
using Loadpath.Core.Editing;
using Loadpath.Core.Geometry;
using Loadpath.Core.Model;
using Loadpath.Core.Samples;
using Loadpath.Core.Serialization;
using Xunit;

namespace Loadpath.Tests;

public class ShareCodeTests
{
    private static StructureDocument Doc(DocumentSnapshot snapshot)
    {
        var doc = new StructureDocument();
        snapshot.Restore(doc);
        return doc;
    }

    [Theory]
    [InlineData("warren")]
    [InlineData("cantilever")]
    [InlineData("roof")]
    public void Share_code_round_trips_exactly(string sample)
    {
        var original = SampleStructures.Build(sample);
        var code = ShareCode.Encode(Doc(original));
        Assert.StartsWith(ShareCode.Prefix, code);
        Assert.DoesNotContain(code, ch => char.IsWhiteSpace(ch) || ch is '+' or '/' or '=');

        Assert.True(ShareCode.TryDecode(code, out var back, out var error), error);
        Assert.Equal(original.Name, back!.Name);
        Assert.Equal(original.Nodes, back.Nodes);
        Assert.Equal(original.Members, back.Members);
    }

    [Fact]
    public void Share_code_is_shorter_than_the_file()
    {
        var doc = Doc(SampleStructures.Build("warren"));
        Assert.True(ShareCode.Encode(doc).Length < DocumentSerializer.Serialize(doc).Length / 3);
    }

    [Fact]
    public void Code_inside_a_chat_message_decodes()
    {
        var code = ShareCode.Encode(Doc(SampleStructures.Build("roof")));
        Assert.True(ShareCode.TryDecode($"here's my roof: {code} — thoughts?", out var back, out _));
        Assert.Equal("Howe roof truss", back!.Name);
    }

    [Fact]
    public void Raw_json_document_decodes()
    {
        var json = DocumentSerializer.Serialize(Doc(SampleStructures.Build("cantilever")));
        Assert.True(ShareCode.TryDecode(json, out var back, out _));
        Assert.Equal("Cantilever", back!.Name);
    }

    [Theory]
    [InlineData(null, "Clipboard has no Loadpath design")]
    [InlineData("   ", "Clipboard has no Loadpath design")]
    [InlineData("hello world", "Clipboard has no Loadpath design")]
    [InlineData("LP1.AAAA", "That share code is damaged or incomplete")]
    [InlineData("{ not json", "That document is not valid Loadpath JSON")]
    public void Bad_input_is_refused_with_a_reason(string? text, string expected)
    {
        Assert.False(ShareCode.TryDecode(text, out var snapshot, out var error));
        Assert.Null(snapshot);
        Assert.Equal(expected, error);
    }

    [Fact]
    public void Truncated_code_is_refused()
    {
        var code = ShareCode.Encode(Doc(SampleStructures.Build("warren")));
        Assert.False(ShareCode.TryDecode(code[..(code.Length / 2)], out _, out var error));
        Assert.Equal("That share code is damaged or incomplete", error);
    }
}

public class PresetTests
{
    public static TheoryData<string, double, int, double> Cases()
    {
        var data = new TheoryData<string, double, int, double>();
        foreach (var p in TrussPresets.Catalog)
        {
            data.Add(p.Id, p.Defaults.Span, p.Defaults.Panels, p.Defaults.Depth);
            data.Add(p.Id, 6, 2, 1.5);
            data.Add(p.Id, 20, 7, 2.5);
            data.Add(p.Id, 30, 16, 4);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Every_preset_is_stable_and_loaded(string id, double span, int panels, double depth)
    {
        var doc = new StructureDocument();
        TrussPresets.Build(id, new PresetParameters(span, panels, depth)).Restore(doc);
        var r = TrussSolver.Solve(doc);
        Assert.Equal(AnalysisStatus.Solved, r.Status);
        Assert.False(r.HasDetached);
        var b = doc.GetBounds();
        Assert.InRange(b.Max.X - b.Min.X, span - 1e-6, span + 1e-6);
        // Self-checking statics: reactions balance the applied load.
        var applied = doc.Nodes.Sum(n => n.Load.Y);
        var reacted = r.Nodes.Values.Sum(n => n.Reaction.Y);
        Assert.InRange(applied + reacted, -1e-6, 1e-6);
    }

    [Fact]
    public void Defaults_are_within_capacity()
    {
        foreach (var p in TrussPresets.Catalog)
        {
            var doc = new StructureDocument();
            TrussPresets.Build(p.Id, p.Defaults).Restore(doc);
            var r = TrussSolver.Solve(doc);
            Assert.True(r.MaxUtilization < 1, $"{p.Id} at defaults is {r.MaxUtilization:P0}");
        }
    }

    [Fact]
    public void Pratt_and_Howe_put_the_web_in_opposite_signs()
    {
        static double EndDiagonal(string id)
        {
            var doc = new StructureDocument();
            TrussPresets.Build(id, new PresetParameters(12, 6, 2)).Restore(doc);
            var r = TrussSolver.Solve(doc);
            // Diagonals are generated last.
            var diagonal = doc.Members[^1];
            return r.Members[diagonal.Id].AxialForceKn;
        }
        Assert.True(EndDiagonal("pratt") > 0, "Pratt diagonals carry tension");
        Assert.True(EndDiagonal("howe") < 0, "Howe diagonals carry compression");
    }

    [Fact]
    public void Parameters_clamp()
    {
        var p = new PresetParameters(1000, 99, -3).Clamped();
        Assert.Equal(new PresetParameters(PresetParameters.MaxSpan, PresetParameters.MaxPanels, PresetParameters.MinDepth), p);
        Assert.Equal(PresetParameters.MinSpan, new PresetParameters(double.NaN, 4, 1).Clamped().Span);
    }
}

public class FailureModeTests
{
    /// <summary>A single horizontal strut between a pin and a roller, pushed (or pulled) along its axis.</summary>
    private static AnalysisResult Bar(double length, double fx, Section section)
    {
        var doc = new StructureDocument();
        var h = new EditHistory(doc);
        h.Do(new AddNodeEdit(new Vec2(0, 0)));
        h.Do(new AddNodeEdit(new Vec2(length, 0)));
        h.Do(new AddNodeEdit(new Vec2(length / 2, 1)));
        h.Do(new SetSupportEdit(1, SupportKind.Pin));
        h.Do(new SetSupportEdit(2, SupportKind.RollerY));
        h.Do(new AddMemberEdit(1, 2, section));
        h.Do(new AddMemberEdit(1, 3, Section.Shs80));
        h.Do(new AddMemberEdit(2, 3, Section.Shs80));
        // The roller is free in x, so a horizontal load at node 2 goes straight into member 1.
        h.Do(new SetLoadEdit(2, new Vec2(fx, 0)));
        return TrussSolver.Solve(doc);
    }

    [Fact]
    public void Long_slender_strut_fails_by_buckling()
    {
        // Rod Ø16 over 3 m: Pcr = π²·210e9·3217e-12 / 9 ≈ 0.74 kN, so 5 kN of compression buckles it.
        var r = Bar(3, -5, Section.Rod16);
        Assert.Equal(FailureMode.Buckling, r.Members[1].Failure);
        Assert.Equal(1, r.Failures[0].MemberId);
    }

    [Fact]
    public void Overloaded_tie_fails_by_yield()
    {
        // Rod Ø16 at 355 MPa yields at 71.4 kN.
        var r = Bar(3, 90, Section.Rod16);
        Assert.Equal(FailureMode.Yield, r.Members[1].Failure);
    }

    [Fact]
    public void Within_capacity_has_no_failures()
    {
        var r = Bar(3, 10, Section.Rod16);
        Assert.Equal(FailureMode.None, r.Members[1].Failure);
        Assert.Empty(r.Failures);
    }

    [Fact]
    public void Failures_are_sorted_worst_first()
    {
        var doc = new StructureDocument();
        TrussPresets.Build("pratt", new PresetParameters(40, 6, 2)).Restore(doc);
        var r = TrussSolver.Solve(doc);
        Assert.True(r.Failures.Count > 1);
        for (var i = 1; i < r.Failures.Count; i++) Assert.True(r.Failures[i - 1].Utilization >= r.Failures[i].Utilization);
        Assert.Equal(r.CriticalMemberId, r.Failures[0].MemberId);
    }
}
