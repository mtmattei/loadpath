using Loadpath.Presentation;
using Microsoft.UI.Xaml;
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

    private void OnQueryKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (Model is null) return;
        switch (e.Key)
        {
            case VirtualKey.Down: _ = Model.MovePaletteHighlight(1, default); e.Handled = true; break;
            case VirtualKey.Up: _ = Model.MovePaletteHighlight(-1, default); e.Handled = true; break;
            case VirtualKey.Enter: _ = Model.RunPalette(null, default); e.Handled = true; break;
            case VirtualKey.Escape: _ = Model.ClosePalette(default); e.Handled = true; break;
        }
    }

    private void OnItemPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string id }) _ = Model?.RunPalette(id, default);
        e.Handled = true;
    }

    private void OnItemEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string id }) _ = Model?.HighlightPaletteItem(id, default);
    }

    private void OnScrimPressed(object sender, PointerRoutedEventArgs e) => _ = Model?.ClosePalette(default);
    private void OnPanelPressed(object sender, PointerRoutedEventArgs e) => e.Handled = true;
}
