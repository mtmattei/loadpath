using Loadpath.Presentation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Loadpath.Views;

public sealed partial class OutlinePanel : UserControl
{
    public static readonly DependencyProperty EngineProperty = DependencyProperty.Register(
        nameof(Engine), typeof(EditorEngine), typeof(OutlinePanel), new PropertyMetadata(null));

    public OutlinePanel() => InitializeComponent();

    /// <summary>Pointer gestures on rows (press, hover) talk to the engine directly; rows carry their element key in Tag.</summary>
    public EditorEngine? Engine
    {
        get => (EditorEngine?)GetValue(EngineProperty);
        set => SetValue(EngineProperty, value);
    }

    private void OnRowPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string key } && Engine is not null)
        {
            var extend = e.KeyModifiers.HasFlag(Windows.System.VirtualKeyModifiers.Shift) || e.KeyModifiers.HasFlag(Windows.System.VirtualKeyModifiers.Control);
            Engine.SelectByKey(key, extend);
            e.Handled = true;
        }
    }

    private void OnRowEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string key }) Engine?.HoverByKey(key);
    }

    private void OnRowExited(object sender, PointerRoutedEventArgs e) => Engine?.HoverByKey(null);
}
