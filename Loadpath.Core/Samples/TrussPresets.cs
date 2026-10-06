using Loadpath.Core.Editing;
using Loadpath.Core.Geometry;
using Loadpath.Core.Model;

namespace Loadpath.Core.Samples;

/// <summary>Span and depth in meters; panels is the number of bays along the span. Roofs read depth as the rise.</summary>
public readonly record struct PresetParameters(double Span, int Panels, double Depth)
{
    public const double MinSpan = 2, MaxSpan = 60, MinDepth = 0.3, MaxDepth = 12;
    public const int MinPanels = 2, MaxPanels = 16;

    public PresetParameters Clamped() => new(
        Math.Clamp(double.IsFinite(Span) ? Span : MinSpan, MinSpan, MaxSpan),
        Math.Clamp(Panels, MinPanels, MaxPanels),
        Math.Clamp(double.IsFinite(Depth) ? Depth : MinDepth, MinDepth, MaxDepth));
}

/// <param name="UsesPanels">False for fixed-web roofs, where the panel count does not apply.</param>
public sealed record TrussPreset(string Id, string Title, string Description, bool UsesPanels, PresetParameters Defaults);

/// <summary>
/// Parametric truss types. Each generator returns a stable, statically sound structure with supports, panel-point
/// loads and sensible sections, so it solves the moment it lands. Bridges carry a 5 kN/m deck load on the bottom
/// chord; roofs carry 2 kN/m on the top chord.
/// </summary>
public static class TrussPresets
{
    private const double DeckLoadKnPerM = 5;
    private const double RoofLoadKnPerM = 2;

    public static readonly IReadOnlyList<TrussPreset> Catalog =
    [
        new("pratt", "Pratt", "Verticals in compression, diagonals in tension", true, new(12, 6, 2)),
        new("howe", "Howe", "Diagonals in compression, verticals in tension", true, new(12, 6, 2)),
        new("warren", "Warren", "Equilateral web, no verticals", true, new(12, 6, 2)),
        new("k", "K truss", "Split verticals keep struts short", true, new(16, 8, 3)),
        new("fink", "Fink roof", "W web, the common timber roof", false, new(9, 4, 2.4)),
        new("scissor", "Scissor roof", "Raised ceiling for a vaulted room", false, new(9, 4, 3)),
    ];

    public static TrussPreset Find(string id) => Catalog.FirstOrDefault(p => p.Id == id) ?? Catalog[0];

    public static DocumentSnapshot Build(string id, PresetParameters parameters)
    {
        var p = parameters.Clamped();
        return id switch
        {
            "howe" => Bridge("Howe truss", p, howe: true),
            "warren" => Warren(p),
            "k" => KTruss(p),
            "fink" => Fink(p),
            "scissor" => Scissor(p),
            _ => Bridge("Pratt truss", p, howe: false),
        };
    }

    private sealed class Builder
    {
        private readonly List<(int Id, Vec2 Position, SupportKind Support, Vec2 Load)> _nodes = new();
        private readonly List<(int Id, int Start, int End, string SectionId)> _members = new();

        public int Node(double x, double y, SupportKind support = SupportKind.None)
        {
            var id = _nodes.Count + 1;
            _nodes.Add((id, new Vec2(Math.Round(x, 4), Math.Round(y, 4)), support, Vec2.Zero));
            return id;
        }

        public void Load(int node, double fy)
        {
            var i = node - 1;
            var n = _nodes[i];
            _nodes[i] = n with { Load = new Vec2(0, Math.Round(n.Load.Y + fy, 2)) };
        }

        public void Member(int a, int b, Section section) => _members.Add((_members.Count + 1, a, b, section.Id));

        public DocumentSnapshot Done(string name) => new() { Name = name, Nodes = _nodes, Members = _members };
    }

    /// <summary>Bottom chord on y = 0, pin at the left end, roller at the right, deck load on interior bottom nodes.</summary>
    private static int[] BottomChord(Builder b, PresetParameters p, Section section)
    {
        var bay = p.Span / p.Panels;
        var bottom = new int[p.Panels + 1];
        for (var i = 0; i <= p.Panels; i++)
            bottom[i] = b.Node(i * bay, 0, i == 0 ? SupportKind.Pin : i == p.Panels ? SupportKind.RollerY : SupportKind.None);
        for (var i = 1; i < p.Panels; i++) b.Load(bottom[i], -DeckLoadKnPerM * bay);
        for (var i = 0; i < p.Panels; i++) b.Member(bottom[i], bottom[i + 1], section);
        return bottom;
    }

    /// <summary>Parallel chords with inclined end posts. Pratt diagonals fall toward midspan, Howe diagonals rise.</summary>
    private static DocumentSnapshot Bridge(string name, PresetParameters p, bool howe)
    {
        var b = new Builder();
        var n = p.Panels;
        var bay = p.Span / n;
        var bottom = BottomChord(b, p, Section.Chs76);
        var top = new int[n + 1];
        for (var i = 1; i < n; i++) top[i] = b.Node(i * bay, p.Depth);
        for (var i = 1; i < n - 1; i++) b.Member(top[i], top[i + 1], Section.Chs76);
        b.Member(bottom[0], top[1], Section.Chs76);
        b.Member(bottom[n], top[n - 1], Section.Chs76);
        for (var i = 1; i < n; i++) b.Member(bottom[i], top[i], Section.Chs60);
        for (var j = 1; j < n - 1; j++)
        {
            var leftHalf = j + 1 <= n / 2.0;
            // Pratt: top of the outer post to the bottom of the inner one. Howe: the mirror.
            if (leftHalf ^ howe) b.Member(top[j], bottom[j + 1], Section.Chs60);
            else b.Member(bottom[j], top[j + 1], Section.Chs60);
        }
        return b.Done(name);
    }

    /// <summary>Top nodes over mid-bays, alternating diagonals, no verticals.</summary>
    private static DocumentSnapshot Warren(PresetParameters p)
    {
        var b = new Builder();
        var n = p.Panels;
        var bay = p.Span / n;
        var bottom = BottomChord(b, p, Section.Chs76);
        var top = new int[n];
        for (var i = 0; i < n; i++) top[i] = b.Node(i * bay + bay / 2, p.Depth);
        for (var i = 0; i < n - 1; i++) b.Member(top[i], top[i + 1], Section.Chs76);
        for (var i = 0; i < n; i++)
        {
            b.Member(bottom[i], top[i], Section.Chs60);
            b.Member(top[i], bottom[i + 1], Section.Chs60);
        }
        return b.Done("Warren truss");
    }

    /// <summary>
    /// Rectangular panels with end verticals. Interior verticals are split at mid-height; each half-height node
    /// braces back to the top and bottom of the vertical nearer its support, so the K opens toward midspan.
    /// The center node braces both ways. Panels round up to an even count.
    /// </summary>
    private static DocumentSnapshot KTruss(PresetParameters p)
    {
        // MaxPanels is even, so rounding an odd count up stays in range.
        var n = p.Panels + p.Panels % 2;
        p = p with { Panels = n };
        var b = new Builder();
        var bay = p.Span / n;
        var bottom = BottomChord(b, p, Section.Chs76);
        var top = new int[n + 1];
        for (var i = 0; i <= n; i++) top[i] = b.Node(i * bay, p.Depth);
        for (var i = 0; i < n; i++) b.Member(top[i], top[i + 1], Section.Chs76);
        b.Member(bottom[0], top[0], Section.Chs60);
        b.Member(bottom[n], top[n], Section.Chs60);
        var center = n / 2;
        for (var i = 1; i < n; i++)
        {
            var mid = b.Node(i * bay, p.Depth / 2);
            b.Member(bottom[i], mid, Section.Chs60);
            b.Member(mid, top[i], Section.Chs60);
            if (i <= center) { b.Member(mid, top[i - 1], Section.Chs48); b.Member(mid, bottom[i - 1], Section.Chs48); }
            if (i >= center) { b.Member(mid, top[i + 1], Section.Chs48); b.Member(mid, bottom[i + 1], Section.Chs48); }
        }
        return b.Done("K truss");
    }

    /// <summary>Roof loads: half a panel share at each eave, a full share at each top-chord node between.</summary>
    private static void RoofLoads(Builder b, PresetParameters p, int eaveL, int eaveR, int[] topNodes)
    {
        var share = RoofLoadKnPerM * p.Span / (topNodes.Length + 1);
        b.Load(eaveL, -share / 2);
        b.Load(eaveR, -share / 2);
        foreach (var t in topNodes) b.Load(t, -share);
    }

    private static DocumentSnapshot Fink(PresetParameters p)
    {
        var b = new Builder();
        double s = p.Span, h = p.Depth;
        var a = b.Node(0, 0, SupportKind.Pin);
        var d = b.Node(s / 3, 0);
        var e = b.Node(2 * s / 3, 0);
        var c = b.Node(s, 0, SupportKind.RollerY);
        var f = b.Node(s / 4, h / 2);
        var apex = b.Node(s / 2, h);
        var g = b.Node(3 * s / 4, h / 2);
        RoofLoads(b, p, a, c, [f, apex, g]);
        foreach (var (x, y) in new[] { (a, f), (f, apex), (apex, g), (g, c) }) b.Member(x, y, Section.Timber45x95);
        foreach (var (x, y) in new[] { (a, d), (d, e), (e, c) }) b.Member(x, y, Section.Timber45x95);
        foreach (var (x, y) in new[] { (f, d), (d, apex), (apex, e), (e, g) }) b.Member(x, y, Section.Timber45x95);
        return b.Done("Fink roof truss");
    }

    /// <summary>Bottom chords rise to 45 % of the ridge height at midspan, giving a vaulted ceiling.</summary>
    private static DocumentSnapshot Scissor(PresetParameters p)
    {
        var b = new Builder();
        double s = p.Span, h = p.Depth;
        var a = b.Node(0, 0, SupportKind.Pin);
        var lo1 = b.Node(s / 4, h * 0.225);
        var ceiling = b.Node(s / 2, h * 0.45);
        var lo2 = b.Node(3 * s / 4, h * 0.225);
        var c = b.Node(s, 0, SupportKind.RollerY);
        var f = b.Node(s / 4, h / 2);
        var apex = b.Node(s / 2, h);
        var g = b.Node(3 * s / 4, h / 2);
        RoofLoads(b, p, a, c, [f, apex, g]);
        foreach (var (x, y) in new[] { (a, f), (f, apex), (apex, g), (g, c) }) b.Member(x, y, Section.Timber45x95);
        foreach (var (x, y) in new[] { (a, lo1), (lo1, ceiling), (ceiling, lo2), (lo2, c) }) b.Member(x, y, Section.Timber45x95);
        foreach (var (x, y) in new[] { (f, lo1), (f, ceiling), (apex, ceiling), (g, ceiling), (g, lo2) }) b.Member(x, y, Section.Timber45x95);
        return b.Done("Scissor roof truss");
    }
}
