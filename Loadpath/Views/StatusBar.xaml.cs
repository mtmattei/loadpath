using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Loadpath.Views;

public sealed partial class StatusBar : UserControl
{
    public StatusBar()
    {
        InitializeComponent();
        SizeChanged += (_, e) => ApplyCompact(e.NewSize.Width);
    }

    /// <summary>Below 960 px the cursor readout gives way so the display toggles and the max utilization stay whole.</summary>
    private void ApplyCompact(double width)
    {
        var compact = width < 960;
        Cursor.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        CursorColumn.Width = compact ? new GridLength(0) : new GridLength(170);
    }
}
