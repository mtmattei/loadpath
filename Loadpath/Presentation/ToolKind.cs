namespace Loadpath.Presentation;

public enum ToolKind { Select, Node, Member, Load, Support, Pan }

public static class ToolKindExtensions
{
    public static string Title(this ToolKind k) => k switch
    {
        ToolKind.Select => "Select",
        ToolKind.Node => "Node",
        ToolKind.Member => "Member",
        ToolKind.Load => "Load",
        ToolKind.Support => "Support",
        ToolKind.Pan => "Pan",
        _ => k.ToString(),
    };

    public static string Key(this ToolKind k) => k switch
    {
        ToolKind.Select => "V",
        ToolKind.Node => "N",
        ToolKind.Member => "M",
        ToolKind.Load => "L",
        ToolKind.Support => "S",
        ToolKind.Pan => "H",
        _ => "",
    };
}
