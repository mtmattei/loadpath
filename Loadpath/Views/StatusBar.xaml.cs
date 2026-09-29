using Loadpath.Presentation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Loadpath.Views;

public sealed partial class StatusBar : UserControl
{
    public static readonly DependencyProperty EditorProperty = DependencyProperty.Register(
        nameof(Editor), typeof(EditorViewModel), typeof(StatusBar), new PropertyMetadata(null));

    public StatusBar() => InitializeComponent();

    public EditorViewModel? Editor
    {
        get => (EditorViewModel?)GetValue(EditorProperty);
        set => SetValue(EditorProperty, value);
    }

    public string SnapText(bool enabled, double step) => enabled ? $"{step:0.##} m" : "off";

    private void OnForces(object sender, RoutedEventArgs e) => Editor?.Commands.TryExecute("view.forces");
    private void OnUtilization(object sender, RoutedEventArgs e) => Editor?.Commands.TryExecute("view.utilization");
}
