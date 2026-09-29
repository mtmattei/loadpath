using Loadpath.Presentation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Loadpath.Views;

public sealed partial class OutlinePanel : UserControl
{
    public static readonly DependencyProperty EditorProperty = DependencyProperty.Register(
        nameof(Editor), typeof(EditorViewModel), typeof(OutlinePanel), new PropertyMetadata(null));

    public OutlinePanel() => InitializeComponent();

    public EditorViewModel? Editor
    {
        get => (EditorViewModel?)GetValue(EditorProperty);
        set => SetValue(EditorProperty, value);
    }

    public string Chevron(bool expanded) => expanded ? "chevron-down" : "chevron-right";

    private void OnRowPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: OutlineItem item } && Editor is not null)
        {
            var shift = e.KeyModifiers.HasFlag(Windows.System.VirtualKeyModifiers.Shift) || e.KeyModifiers.HasFlag(Windows.System.VirtualKeyModifiers.Control);
            Editor.Outline.Select(item, shift);
            e.Handled = true;
        }
    }

    private void OnRowEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: OutlineItem item }) Editor?.Outline.Hover(item);
    }

    private void OnRowExited(object sender, PointerRoutedEventArgs e) => Editor?.Outline.Hover(null);

    private void OnToggleNodes(object sender, TappedRoutedEventArgs e)
    {
        if (Editor is not null) Editor.Outline.NodesExpanded = !Editor.Outline.NodesExpanded;
    }

    private void OnToggleMembers(object sender, TappedRoutedEventArgs e)
    {
        if (Editor is not null) Editor.Outline.MembersExpanded = !Editor.Outline.MembersExpanded;
    }
}
