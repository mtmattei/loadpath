using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Loadpath.Views;

public sealed partial class InspectorPanel : UserControl
{
    public InspectorPanel() => InitializeComponent();

    /// <summary>Raised when the user presses a section chip; the page runs the pointer drag onto the canvas.</summary>
    public event EventHandler<Section>? SectionDragStarted;

    // xaml-lint: allow codebehind - starts the section drag that MainPage tracks onto the canvas
    private void OnSectionChipPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string id })
        {
            SectionDragStarted?.Invoke(this, Section.FindById(id));
            e.Handled = true;
        }
    }
}
