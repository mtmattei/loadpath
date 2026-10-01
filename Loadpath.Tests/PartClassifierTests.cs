using Loadpath.Core.Geometry;
using Xunit;

namespace Loadpath.Tests;

public class PartClassifierTests
{
    private static (Vec2, Vec2) M(double ax, double ay, double bx, double by) => (new Vec2(ax, ay), new Vec2(bx, by));

    // The four-panel roof truss from the design mock: bottom chord, two rafters each side, king post, posts, webs.
    private static readonly (Vec2 A, Vec2 B)[] Roof =
    [
        M(8, 0, 6, 0), M(6, 0, 4, 0), M(4, 0, 2, 0), M(2, 0, 0, 0),   // 0-3 bottom chord
        M(0, 0, 2, 2), M(2, 2, 4, 3.5), M(4, 3.5, 6, 2), M(6, 2, 8, 0), // 4-7 rafters
        M(2, 0, 2, 2), M(2, 2, 4, 0), M(4, 0, 4, 3.5), M(4, 0, 6, 2), M(6, 2, 6, 0), // 8-12 web, posts
    ];

    [Fact]
    public void Roof_truss_parts_are_named()
    {
        var kinds = PartClassifier.Classify(Roof);
        for (var i = 0; i <= 3; i++) Assert.Equal(PartKind.BottomChord, kinds[i]);
        for (var i = 4; i <= 7; i++) Assert.Equal(PartKind.TopChord, kinds[i]);
        Assert.Equal(PartKind.Post, kinds[8]);
        Assert.Equal(PartKind.Web, kinds[9]);
        Assert.Equal(PartKind.KingPost, kinds[10]);
        Assert.Equal(PartKind.Web, kinds[11]);
        Assert.Equal(PartKind.Post, kinds[12]);
    }

    [Fact]
    public void Pitched_top_chord_reads_as_roof_truss()
    {
        var kinds = PartClassifier.Classify(Roof);
        Assert.Equal("Roof truss", PartClassifier.Describe(Roof, kinds, [new Vec2(0, 0), new Vec2(8, 0)]));
    }

    [Fact]
    public void Flat_truss_with_end_supports_reads_as_truss()
    {
        // Warren: level top chord, diagonals between. The end diagonals have nothing above them either.
        (Vec2, Vec2)[] warren = [M(0, 0, 4, 0), M(4, 0, 8, 0), M(2, 2, 6, 2), M(0, 0, 2, 2), M(2, 2, 4, 0), M(4, 0, 6, 2), M(6, 2, 8, 0)];
        var kinds = PartClassifier.Classify(warren);
        Assert.Equal(PartKind.TopChord, kinds[2]);
        Assert.Equal(PartKind.Web, kinds[4]);
        Assert.Equal("Truss", PartClassifier.Describe(warren, kinds, [new Vec2(0, 0), new Vec2(8, 0)]));
    }

    [Fact]
    public void Verticals_under_a_level_top_chord_are_posts_not_king_posts()
    {
        // Pratt bridge: level top chord, three verticals.
        (Vec2, Vec2)[] pratt =
        [
            M(0, 0, 2, 0), M(2, 0, 4, 0), M(4, 0, 6, 0), M(6, 0, 8, 0),
            M(0, 0, 2, 2), M(2, 2, 4, 2), M(4, 2, 6, 2), M(6, 2, 8, 0),
            M(2, 0, 2, 2), M(4, 0, 4, 2), M(6, 0, 6, 2), M(2, 2, 4, 0), M(4, 0, 6, 2),
        ];
        var kinds = PartClassifier.Classify(pratt);
        Assert.Equal(PartKind.Post, kinds[8]);
        Assert.Equal(PartKind.Post, kinds[9]);
        Assert.Equal(PartKind.Post, kinds[10]);
    }

    [Fact]
    public void Supports_bunched_at_one_end_read_as_cantilever()
    {
        (Vec2, Vec2)[] arm = [M(0, 0, 4, 0), M(0, 1, 4, 0), M(0, 0, 0, 1)];
        var kinds = PartClassifier.Classify(arm);
        Assert.Equal("Cantilever truss", PartClassifier.Describe(arm, kinds, [new Vec2(0, 0), new Vec2(0, 1)]));
    }
}
