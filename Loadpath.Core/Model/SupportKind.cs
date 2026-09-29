namespace Loadpath.Core.Model;

/// <summary>Boundary condition on a node. Pin fixes both axes; rollers fix one.</summary>
public enum SupportKind
{
    None = 0,
    /// <summary>Fixed in X and Y.</summary>
    Pin = 1,
    /// <summary>Fixed in Y only (rolls horizontally). The common roller.</summary>
    RollerY = 2,
    /// <summary>Fixed in X only (rolls vertically).</summary>
    RollerX = 3,
}

public static class SupportKindExtensions
{
    public static bool FixesX(this SupportKind kind) => kind is SupportKind.Pin or SupportKind.RollerX;
    public static bool FixesY(this SupportKind kind) => kind is SupportKind.Pin or SupportKind.RollerY;

    public static string Label(this SupportKind kind) => kind switch
    {
        SupportKind.Pin => "Pin",
        SupportKind.RollerY => "Roller",
        SupportKind.RollerX => "Roller (vertical)",
        _ => "None",
    };

    /// <summary>Cycle used by the Support tool: None → Pin → Roller → None.</summary>
    public static SupportKind Next(this SupportKind kind) => kind switch
    {
        SupportKind.None => SupportKind.Pin,
        SupportKind.Pin => SupportKind.RollerY,
        _ => SupportKind.None,
    };
}
