namespace Loadpath.Presentation;

/// <summary>
/// Immutable values carried by the MVUX feeds. Plain records only (no record structs, no required members):
/// the bindable generator emits proxies for every type a feed carries.
/// Colors are resource keys, resolved in the view by a converter, so UI types stay out of the model.
/// </summary>
public record EditorStatus(
    string DocumentName,
    bool IsDirty,
    string StatusToneKey,
    bool IsMechanism,
    string MechanismText,
    bool HasDetached,
    string DetachedText,
    string MaxUtilizationText,
    string MaxUtilizationToneKey,
    bool IsEmpty,
    bool CanUndo,
    bool CanRedo,
    string UndoTooltip,
    string RedoTooltip,
    // title-strip status pill and status-bar solve readout
    string PillText,
    bool PillIsDanger,
    string SolveText,
    bool IsOverCapacity)
{
    public static readonly EditorStatus Initial = new("Untitled", false, "InkTertiaryBrush", false, "", false, "", "—", "OkBrush", true, false, false, "Nothing to undo", "Nothing to redo", "EMPTY · PLACE A NODE TO BEGIN", false, "—", false);
}

public record InspectorContent(
    bool IsSummary, bool IsNode, bool IsMember, bool IsMixed,
    string Title, string Subtitle,
    // summary
    string NodeCountText, string MemberCountText, string SupportsText, string TotalLoadText, string MassText,
    string MaxUtilText, double MaxUtil, string MaxUtilToneKey, string MaxDeflectionText, bool HasCritical, string CriticalText, string ReactionsText, string SummaryHint,
    // node
    bool IsPin, bool IsRoller, bool IsRollerX, bool IsFree, bool HasReaction, string ReactionText,
    string NodeDescription, string DispXText, string DispYText, string DispMagText, double DispDotX, double DispDotY, string ConnectedCountText,
    // member
    string EndpointsText, string LengthText, string MaterialText, string AreaText, string ForceKindText, string ForceToneKey, string ForceText,
    string StressText, string UtilText, double Util, string UtilToneKey, string GovernsText, string BucklingText,
    // mixed
    string MixedText, bool MixedHasNodes, bool MixedHasMembers,
    // floating selection bar
    bool SelectionHasNodes, string SelectionLabel)
{
    public static readonly InspectorContent Empty = new(
        true, false, false, false, "Structure", "Nothing here yet",
        "0", "0", "none", "none", "—", "—", 0, "OkBrush", "—", false, "", "", "",
        false, false, false, true, false, "",
        "", "—", "—", "—", 36, 36, "0",
        "", "", "", "", "", "InkTertiaryBrush", "", "", "", 0, "OkBrush", "", "",
        "", false, false,
        false, "");
}

/// <summary>One outline row. Key is "n{id}" or "m{id}" so the view can name the element without carrying Core types.</summary>
public partial record OutlineItem(string Key, string Label, string Detail, string Value, string ValueToneKey, string RowBrushKey, bool HasBadge, string Badge, bool HasPill, string PillText);

/// <summary>A member connected to the selected node, as listed in the node inspector.</summary>
public partial record ConnectedItem(string Key, string Label, string Endpoints, string Force, string ForceToneKey, string Util, bool UtilIsDanger);

public partial record PaletteItem(string Id, string Title, string Category, string Shortcut, string Icon, bool HasIcon, bool IsDisabled, string RowBrushKey);

/// <summary>Counts beside each layer in the Reduce noise flyout.</summary>
public record LayerCounts(string Grid, string Supports, string Loads, string OverCapacity, string MemberForces, string Reactions)
{
    public static readonly LayerCounts Empty = new("0.5 m", "0", "0", "0", "0", "0");
}

/// <summary>A section choice for the inspector, decoupled from the Core record.</summary>
public partial record SectionChoice(string Id, string Name, string ShortName);

/// <summary>A member over capacity, as listed in the summary inspector. Mode is the failure verb ("BUCKLES", "YIELDS").</summary>
public partial record FailureItem(string Key, string Label, string Mode, string Util, string Detail, string AutomationName);

/// <summary>A preset tile. Sketch is the generated geometry as pixel-space segments "x1,y1,x2,y2;…" for the thumbnail.</summary>
public partial record PresetItem(string Id, string Title, string Description, string Sketch, string Size, string AutomationName);
