using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Loadpath.Controls;

/// <summary>An invisible focusable element. The workspace hands keyboard focus here so page shortcuts fire after a canvas click.</summary>
public sealed partial class KeySink : Control
{
    public KeySink()
    {
        IsTabStop = true;
        UseSystemFocusVisuals = false;
        Width = 1;
        Height = 1;
        Opacity = 0;
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
    }
}
