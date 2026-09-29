using Loadpath.Core.Geometry;

namespace Loadpath.Core.Model;

/// <summary>A pin joint. Owns its support and its (single) point load, in kN.</summary>
public sealed class Node
{
    public Node(int id, Vec2 position)
    {
        Id = id;
        Position = position;
    }

    public int Id { get; }
    public Vec2 Position { get; internal set; }
    public SupportKind Support { get; internal set; }
    /// <summary>Point load in kN, world axes (Y up, so a gravity load has negative Y).</summary>
    public Vec2 Load { get; internal set; }

    public bool HasLoad => Load.LengthSquared > 1e-12;
    public bool HasSupport => Support != SupportKind.None;
    public ElementRef Ref => ElementRef.Node(Id);
}
