namespace Loadpath.Views;

public sealed partial class TitleStrip : UserControl
{
    public TitleStrip() => InitializeComponent();

    // xaml-lint: allow codebehind - Flyout has no bindable IsOpen; Done closes it (light dismiss also does)
    private void OnNoiseDone(object sender, RoutedEventArgs e) => NoiseFlyout.Hide();

    // xaml-lint: allow codebehind - a chosen preset closes its flyout; the bound command does the work
    private void OnPresetClick(object sender, RoutedEventArgs e) => PresetsFlyout.Hide();
}
