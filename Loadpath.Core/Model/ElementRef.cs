namespace Loadpath.Core.Model;

public enum ElementKind { Node, Member }

/// <summary>A reference to a document element by kind and id. Stable across edits until the element is removed.</summary>
public readonly record struct ElementRef(ElementKind Kind, int Id)
{
    public static ElementRef Node(int id) => new(ElementKind.Node, id);
    public static ElementRef Member(int id) => new(ElementKind.Member, id);
    public bool IsNode => Kind == ElementKind.Node;
    public bool IsMember => Kind == ElementKind.Member;
    public override string ToString() => $"{Kind}#{Id}";
}
