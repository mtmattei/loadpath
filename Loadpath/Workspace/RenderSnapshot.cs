using Loadpath.Core.Analysis;
using Loadpath.Core.Geometry;
using Loadpath.Presentation;
using SkiaSharp;

namespace Loadpath.Workspace;

/// <summary>Plain data the renderer reads. Built on the UI thread at invalidation; never touches the live document.</summary>
public sealed class RenderSnapshot
{
    public readonly record struct NodeItem(int ElementId, SKPoint Screen, SKPoint Deflected, SupportKind Support, Vec2 LoadKn, SKPoint LoadTail, Vec2 ReactionKn, bool Selected, bool Hovered, bool Dimmed);

    public readonly record struct MemberItem(int ElementId, SKPoint A, SKPoint B, SKPoint DeflectedA, SKPoint DeflectedB, double ForceKn, double Utilization, bool Overstressed, bool BucklingGoverns, bool Selected, bool Hovered, bool Dimmed);

    public NodeItem[] Nodes { get; init; } = [];
    public MemberItem[] Members { get; init; } = [];
    public double MaxAbsForceKn { get; init; }
    public AnalysisStatus Status { get; init; }
    public double Scale { get; init; } = Core.Viewport.Viewport.BaseScale;
    public SKPoint Origin { get; init; }
    public DisplayMode Mode { get; init; }
    public bool ShowDeflection { get; init; }
    public bool ShowLabels { get; init; }
    public bool ShowReactions { get; init; }
    public bool ShowGrid { get; init; }
    public double GridMinor { get; init; } = 0.5;
    public double GridMajor { get; init; } = 1;
    public ToolOverlay Overlay { get; init; } = new();
    public float Width { get; init; }
    public float Height { get; init; }
    public bool AnySelected { get; init; }
    /// <summary>Screen-space center of the structure; labels are pushed away from it.</summary>
    public SKPoint Center { get; init; }

    public static readonly RenderSnapshot Empty = new();
}

/// <summary>Transient drawing the active tool asks for: guides, marquee, rubber band, ghosts.</summary>
public sealed class ToolOverlay
{
    public List<SnapGuide> Guides { get; } = new();
    public SKRect? Marquee { get; set; }
    public (SKPoint From, SKPoint To)? RubberBand { get; set; }
    public SKPoint? GhostNode { get; set; }
    public int? HighlightMember { get; set; }
    public (SKPoint Node, SKPoint Tail, double MagnitudeKn)? LoadPreview { get; set; }
    public int? SnapFlashNode { get; set; }

    public void Clear()
    {
        Guides.Clear();
        Marquee = null;
        RubberBand = null;
        GhostNode = null;
        HighlightMember = null;
        LoadPreview = null;
        SnapFlashNode = null;
    }
}
