namespace Loadpath.Presentation;

/// <summary>
/// Immutable values carried by the MVUX feeds. Plain records only (no record structs, no required members):
/// the bindable generator emits proxies for every type a feed carries.
/// Colors are resource keys, resolved in the view by a converter, so UI types stay out of the model.
/// </summary>
public record EditorStatus(
    string DocumentName,
    bool IsDirty,
    string StatusText,
    string StatusDetail,
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
    string RedoTooltip)
{
    public static readonly EditorStatus Initial = new("Untitled", false, "Empty", "Place a node to begin", "InkTertiaryBrush", false, "", false, "", "—", "OkBrush", true, false, false, "Nothing to undo", "Nothing to redo");
}

public record InspectorContent(
    bool IsSummary, bool IsNode, bool IsMember, bool IsMixed,
    string Title, string Subtitle,
    // summary
    string NodeCountText, string MemberCountText, string SupportsText, string TotalLoadText, string MassText,
    string MaxUtilText, double MaxUtil, string MaxUtilToneKey, string MaxDeflectionText, bool HasCritical, string CriticalText, string ReactionsText, string SummaryHint,
    // node
    bool IsPin, bool IsRoller, bool IsRollerX, bool IsFree, string ConnectedText, bool HasReaction, string ReactionText, string DisplacementText,
    // member
    string EndpointsText, string LengthText, string MaterialText, string AreaText, string ForceKindText, string ForceToneKey, string ForceText,
    string StressText, string UtilText, double Util, string UtilToneKey, string GovernsText, string BucklingText,
    // mixed
    string MixedText, bool MixedHasNodes, bool MixedHasMembers)
{
    public static readonly InspectorContent Empty = new(
        true, false, false, false, "Structure", "Nothing here yet",
        "0", "0", "none", "none", "—", "—", 0, "OkBrush", "—", false, "", "", "",
        false, false, false, true, "", false, "", "",
        "", "", "", "", "", "InkTertiaryBrush", "", "", "", 0, "OkBrush", "", "",
        "", false, false);
}

/// <summary>One outline row. Key is "n{id}" or "m{id}" so the view can name the element without carrying Core types.</summary>
public partial record OutlineItem(string Key, string Label, string Detail, string Value, string ValueToneKey, bool IsSelected, string RowBrushKey, bool HasBadge, string Badge);

public partial record PaletteItem(string Id, string Title, string Category, string Shortcut, string Icon, bool HasIcon, bool IsDisabled, bool IsHighlighted, string RowBrushKey);

/// <summary>A section choice for the inspector, decoupled from the Core record.</summary>
public partial record SectionChoice(string Id, string Name, string ShortName);
