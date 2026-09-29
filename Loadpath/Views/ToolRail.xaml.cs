using Loadpath.Presentation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Loadpath.Views;

public sealed partial class ToolRail : UserControl
{
    public static readonly DependencyProperty EditorProperty = DependencyProperty.Register(
        nameof(Editor), typeof(EditorViewModel), typeof(ToolRail), new PropertyMetadata(null));

    public ToolRail() => InitializeComponent();

    public EditorViewModel? Editor
    {
        get => (EditorViewModel?)GetValue(EditorProperty);
        set => SetValue(EditorProperty, value);
    }

    private void OnSelect(object sender, RoutedEventArgs e) => Editor?.Interaction.SetTool(ToolKind.Select);
    private void OnNode(object sender, RoutedEventArgs e) => Editor?.Interaction.SetTool(ToolKind.Node);
    private void OnMember(object sender, RoutedEventArgs e) => Editor?.Interaction.SetTool(ToolKind.Member);
    private void OnLoad(object sender, RoutedEventArgs e) => Editor?.Interaction.SetTool(ToolKind.Load);
    private void OnSupport(object sender, RoutedEventArgs e) => Editor?.Interaction.SetTool(ToolKind.Support);
    private void OnPan(object sender, RoutedEventArgs e) => Editor?.Interaction.SetTool(ToolKind.Pan);
}
