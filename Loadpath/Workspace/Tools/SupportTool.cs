using Loadpath.Core.Editing;
using Loadpath.Presentation;

namespace Loadpath.Workspace.Tools;

/// <summary>Click a node: none → pin → roller → none.</summary>
public sealed class SupportTool : Tool
{
    public override ToolKind Kind => ToolKind.Support;
    public override CursorKind IdleCursor => CursorKind.Cross;
    public override string Hint => "Click a node to cycle Pin → Roller → None · Right-click for a specific support";

    public override void UpdateHover(IToolContext ctx, PointerState p, HitResult hit)
    {
        ctx.SetCursor(hit.Kind == HitKind.Node ? CursorKind.Hand : CursorKind.Cross);
    }

    public override void OnPressed(IToolContext ctx, PointerState p)
    {
        if (!p.IsLeft) return;
        var hit = ctx.Hit(p.Screen, includeLoadHandles: false);
        if (hit.Kind != HitKind.Node) { ctx.Toast("Supports go on nodes"); return; }
        var node = ctx.Document.GetNode(hit.Id);
        ctx.Selection.Set(ElementRef.Node(hit.Id));
        ctx.History.Do(new SetSupportEdit(hit.Id, node.Support.Next()));
    }
}
