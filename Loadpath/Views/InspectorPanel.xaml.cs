using Loadpath.Presentation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Loadpath.Views;

public sealed partial class InspectorPanel : UserControl
{
    public static readonly DependencyProperty EditorProperty = DependencyProperty.Register(
        nameof(Editor), typeof(EditorViewModel), typeof(InspectorPanel), new PropertyMetadata(null));

    public InspectorPanel() => InitializeComponent();

    public EditorViewModel? Editor
    {
        get => (EditorViewModel?)GetValue(EditorProperty);
        set => SetValue(EditorProperty, value);
    }

    /// <summary>Raised when the user starts dragging a section chip; the page runs the drop onto the canvas.</summary>
    public event EventHandler<Section>? SectionDragStarted;

    private void OnPositionCommitted(object? sender, EventArgs e) => Editor?.Inspector.CommitPosition();
    private void OnLoadCommitted(object? sender, EventArgs e) => Editor?.Inspector.CommitLoad();
    private void OnSupportNone(object sender, RoutedEventArgs e) => SetSupport(SupportKind.None);
    private void OnSupportPin(object sender, RoutedEventArgs e) => SetSupport(SupportKind.Pin);
    private void OnSupportRoller(object sender, RoutedEventArgs e) => SetSupport(SupportKind.RollerY);
    private void OnSupportRollerX(object sender, RoutedEventArgs e) => SetSupport(SupportKind.RollerX);
    private void OnJumpCritical(object sender, RoutedEventArgs e) => Editor?.JumpToCritical();
    private void OnSplit(object sender, RoutedEventArgs e) => Editor?.SplitSelectedMember();
    private void OnClearLoads(object sender, RoutedEventArgs e) => Editor?.ClearLoadOnSelection();
    private void OnDelete(object sender, RoutedEventArgs e) => Editor?.DeleteSelection();

    private void SetSupport(SupportKind kind)
    {
        if (Editor is null) return;
        if (Editor.Inspector.IsNode) Editor.Inspector.SetSupport(kind);
        else Editor.SetSupportOnSelection(kind);
    }

    private void OnBulkSection(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: Section s }) Editor?.SetSectionOnSelection(s);
    }

    private void OnSectionChipPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: Section s })
        {
            SectionDragStarted?.Invoke(this, s);
            e.Handled = true;
        }
    }
}
