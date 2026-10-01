using Loadpath.Presentation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.System;

namespace Loadpath.Views;

/// <summary>Ctrl+K palette. Items and the highlight live on the model; this view handles keys and the entrance motion.</summary>
public sealed partial class CommandPalette : UserControl
{
    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(
        nameof(Model), typeof(EditorModel), typeof(CommandPalette), new PropertyMetadata(null));

    public CommandPalette() => InitializeComponent();

    public EditorModel? Model
    {
        get => (EditorModel?)GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    /// <summary>Called by the page when the palette becomes visible: focus the query and play the entrance.</summary>
    public void OnOpened()
    {
        DispatcherQueue.TryEnqueue(() => Query.Focus(FocusState.Programmatic));
        if (!Loadpath.Workspace.MotionSettings.AnimationsEnabled) return;
        var sb = new Storyboard();
        var spline = new KeySpline { ControlPoint1 = new Windows.Foundation.Point(0.17, 1), ControlPoint2 = new Windows.Foundation.Point(0.32, 1) };
        var fade = new DoubleAnimationUsingKeyFrames();
        fade.KeyFrames.Add(new DiscreteDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero), Value = 0 });
        fade.KeyFrames.Add(new SplineDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(200)), Value = 1, KeySpline = spline });
        Storyboard.SetTarget(fade, Panel);
        Storyboard.SetTargetProperty(fade, "Opacity");
        var rise = new DoubleAnimationUsingKeyFrames();
        rise.KeyFrames.Add(new DiscreteDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero), Value = 6 });
        rise.KeyFrames.Add(new SplineDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(200)), Value = 0, KeySpline = spline });
        Storyboard.SetTarget(rise, PanelTranslate);
        Storyboard.SetTargetProperty(rise, "Y");
        sb.Children.Add(fade);
        sb.Children.Add(rise);
        sb.Begin();
    }

    // Handled is set before the first await: routing reads it synchronously.

    // xaml-lint: allow codebehind - arrow/Enter/Esc routing inside the search box
    private async void OnQueryKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (Model is null) return;
        switch (e.Key)
        {
            case VirtualKey.Down: e.Handled = true; await Model.MovePaletteHighlight(1, default); break;
            case VirtualKey.Up: e.Handled = true; await Model.MovePaletteHighlight(-1, default); break;
            case VirtualKey.Enter: e.Handled = true; await Model.RunPalette(null, default); break;
            case VirtualKey.Escape: e.Handled = true; await Model.ClosePalette(default); break;
        }
    }

    // xaml-lint: allow codebehind - rows are not Buttons so arrow keys keep focus in the query; a press runs the row
    private async void OnItemPressed(object sender, PointerRoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement { Tag: string id } && Model is { } model) await model.RunPalette(id, default);
    }

    // xaml-lint: allow codebehind - hover moves the keyboard highlight
    private async void OnItemEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string id } && Model is { } model) await model.HighlightPaletteItem(id, default);
    }

    // xaml-lint: allow codebehind - a press outside the panel closes it
    private async void OnScrimPressed(object sender, PointerRoutedEventArgs e)
    {
        if (Model is { } model) await model.ClosePalette(default);
    }

    // xaml-lint: allow codebehind - a press inside the panel must not reach the scrim
    private void OnPanelPressed(object sender, PointerRoutedEventArgs e) => e.Handled = true;
}
