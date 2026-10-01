using Loadpath.Controls;
using Loadpath.Presentation;
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

    /// <summary>Element keys that have already had their entrance, so a row re-prepared for a value change stays still.</summary>
    private readonly HashSet<string> _entered = new();

    // xaml-lint: allow codebehind - a new row fades and rises in once; ItemsRepeater has no per-item add transition on Uno
    private void OnRowPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        if (args.Element is FrameworkElement { Tag: string key } row && _entered.Add(key)) Motion.Enter(row);
    }

    // xaml-lint: allow codebehind - Shift/Ctrl-aware selection by element key (modifier state lives on the pointer event)
    private void OnRowPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string key } && Engine is not null)
        {
            var extend = e.KeyModifiers.HasFlag(Windows.System.VirtualKeyModifiers.Shift) || e.KeyModifiers.HasFlag(Windows.System.VirtualKeyModifiers.Control);
            Engine.SelectByKey(key, extend);
            e.Handled = true;
        }
    }

    // xaml-lint: allow codebehind - row hover highlights the element on the canvas
    private void OnRowEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string key }) Engine?.HoverByKey(key);
    }

    // xaml-lint: allow codebehind - clears the canvas hover highlight
    private void OnRowExited(object sender, PointerRoutedEventArgs e) => Engine?.HoverByKey(null);
}
