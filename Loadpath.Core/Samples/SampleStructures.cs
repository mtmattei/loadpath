using Loadpath.Core.Editing;
using Loadpath.Core.Geometry;
using Loadpath.Core.Model;

namespace Loadpath.Core.Samples;

/// <summary>Built-in structures. Realistic spans and loads so the first solve reads as a real design check.</summary>
public static class SampleStructures
{
    public static readonly IReadOnlyList<(string Id, string Title, string Description)> Catalog =
    [
        ("warren", "Warren truss", "12 m span, 6 bays, 2 m deep, 15 kN at each lower node"),
        ("cantilever", "Cantilever", "4 m reach, tapered, 8 kN tip load"),
        ("roof", "Howe roof truss", "9 m span, 2.4 m rise, snow load at purlins"),
    ];

    public static DocumentSnapshot Build(string id) => id switch
    {
        "cantilever" => Cantilever(),
        "roof" => Roof(),
        _ => Warren(),
    };

    private sealed class Builder
    {
        private readonly List<(int Id, Vec2 Position, SupportKind Support, Vec2 Load)> _nodes = new();
        private readonly List<(int Id, int Start, int End, string SectionId)> _members = new();

        public int Node(double x, double y, SupportKind support = SupportKind.None, double fx = 0, double fy = 0)
        {
            var id = _nodes.Count + 1;
            _nodes.Add((id, new Vec2(x, y), support, new Vec2(fx, fy)));
            return id;
        }

        public void Member(int a, int b, Section? section = null)
        {
            _members.Add((_members.Count + 1, a, b, (section ?? Section.Chs60).Id));
        }

        public DocumentSnapshot Done(string name) => new() { Name = name, Nodes = _nodes, Members = _members };
    }

    private static DocumentSnapshot Warren()
    {
        var b = new Builder();
        const int bays = 6;
        const double bay = 2.0;
        const double depth = 2.0;
        var bottom = new int[bays + 1];
        var top = new int[bays];
        for (var i = 0; i <= bays; i++)
        {
            var support = i == 0 ? SupportKind.Pin : i == bays ? SupportKind.RollerY : SupportKind.None;
            var fy = i == 0 || i == bays ? 0 : -15;
            bottom[i] = b.Node(i * bay, 0, support, 0, fy);
        }
        for (var i = 0; i < bays; i++) top[i] = b.Node(i * bay + bay / 2, depth);
        for (var i = 0; i < bays; i++) b.Member(bottom[i], bottom[i + 1], Section.Chs76);
        for (var i = 0; i < bays - 1; i++) b.Member(top[i], top[i + 1], Section.Chs76);
        for (var i = 0; i < bays; i++)
        {
            b.Member(bottom[i], top[i], Section.Chs48);
            b.Member(top[i], bottom[i + 1], Section.Chs48);
        }
        return b.Done("Warren truss");
    }

    private static DocumentSnapshot Cantilever()
    {
        var b = new Builder();
        var wallTop = b.Node(0, 1.2, SupportKind.Pin);
        var wallBottom = b.Node(0, 0, SupportKind.Pin);
        var top1 = b.Node(1.4, 1.0);
        var bot1 = b.Node(1.4, 0);
        var top2 = b.Node(2.8, 0.75);
        var bot2 = b.Node(2.8, 0);
        var tip = b.Node(4.0, 0.3, SupportKind.None, 0, -8);
        b.Member(wallTop, top1, Section.Shs50);
        b.Member(top1, top2, Section.Shs50);
        b.Member(top2, tip, Section.Shs50);
        b.Member(wallBottom, bot1, Section.Shs50);
        b.Member(bot1, bot2, Section.Shs50);
        b.Member(bot2, tip, Section.Shs50);
        b.Member(top1, bot1, Section.Rod16);
        b.Member(top2, bot2, Section.Rod16);
        b.Member(wallBottom, top1, Section.Chs48);
        b.Member(bot1, top2, Section.Chs48);
        b.Member(bot2, top2, Section.Rod16);
        return b.Done("Cantilever");
    }

    private static DocumentSnapshot Roof()
    {
        var b = new Builder();
        const double span = 9.0;
        const double rise = 2.4;
        const int panels = 6;
        var bottom = new int[panels + 1];
        var top = new int[panels + 1];
        for (var i = 0; i <= panels; i++)
        {
            var x = i * span / panels;
            var support = i == 0 ? SupportKind.Pin : i == panels ? SupportKind.RollerY : SupportKind.None;
            bottom[i] = b.Node(x, 0, support);
        }
        for (var i = 0; i <= panels; i++)
        {
            var x = i * span / panels;
            var y = rise * (1 - Math.Abs(x - span / 2) / (span / 2));
            var fy = i == 0 || i == panels ? -3 : -6;
            top[i] = b.Node(x, y, SupportKind.None, 0, fy);
        }
        for (var i = 0; i < panels; i++) b.Member(bottom[i], bottom[i + 1], Section.Timber45x95);
        for (var i = 0; i < panels; i++) b.Member(top[i], top[i + 1], Section.Timber45x95);
        for (var i = 1; i < panels; i++) b.Member(bottom[i], top[i], Section.Timber45x95);
        // Howe diagonals slope toward the center.
        for (var i = 0; i < panels / 2; i++) b.Member(bottom[i], top[i + 1], Section.Timber45x95);
        for (var i = panels / 2; i < panels; i++) b.Member(top[i], bottom[i + 1], Section.Timber45x95);
        return b.Done("Howe roof truss");
    }
}
