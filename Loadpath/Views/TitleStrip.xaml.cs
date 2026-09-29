using Loadpath.Presentation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Loadpath.Views;

public sealed partial class TitleStrip : UserControl
{
    public static readonly DependencyProperty EditorProperty = DependencyProperty.Register(
        nameof(Editor), typeof(EditorViewModel), typeof(TitleStrip), new PropertyMetadata(null));

    public TitleStrip() => InitializeComponent();

    public EditorViewModel? Editor
    {
        get => (EditorViewModel?)GetValue(EditorProperty);
        set => SetValue(EditorProperty, value);
    }

    private void OnUndo(object sender, RoutedEventArgs e) => Editor?.Commands.TryExecute("edit.undo");
    private void OnRedo(object sender, RoutedEventArgs e) => Editor?.Commands.TryExecute("edit.redo");
    private void OnPalette(object sender, RoutedEventArgs e) => Editor?.Commands.TryExecute("view.palette");
    private void OnOpen(object sender, RoutedEventArgs e) => Editor?.Commands.TryExecute("file.open");
    private void OnSave(object sender, RoutedEventArgs e) => Editor?.Commands.TryExecute("file.save");
}
