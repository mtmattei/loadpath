using System.Collections.ObjectModel;
using Loadpath.Presentation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Microsoft.UI.Xaml.Media.Animation;

namespace Loadpath.Views;

/// <summary>Ctrl+K palette: every command, fuzzy-filtered, keyboard-driven. The discoverability surface.</summary>
public sealed partial class CommandPalette : UserControl
{
    public static readonly DependencyProperty EditorProperty = DependencyProperty.Register(
        nameof(Editor), typeof(EditorViewModel), typeof(CommandPalette), new PropertyMetadata(null, (d, e) => ((CommandPalette)d).Attach(e.NewValue as EditorViewModel)));

    private int _highlight;

    public CommandPalette() => InitializeComponent();

    public EditorViewModel? Editor
    {
        get => (EditorViewModel?)GetValue(EditorProperty);
        set => SetValue(EditorProperty, value);
    }

    public ObservableCollection<PaletteItem> Items { get; } = new();

    private void Attach(EditorViewModel? editor)
    {
        if (editor is null) return;
        editor.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(EditorViewModel.IsPaletteOpen) && editor.IsPaletteOpen) Open();
        };
    }

    private void Open()
    {
        Query.Text = "";
        Rebuild("");
        DispatcherQueue.TryEnqueue(() => Query.Focus(FocusState.Programmatic));
        if (!Loadpath.Workspace.MotionSettings.AnimationsEnabled) return;
        var sb = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        var spline = new Microsoft.UI.Xaml.Media.Animation.KeySpline { ControlPoint1 = new Windows.Foundation.Point(0.17, 1), ControlPoint2 = new Windows.Foundation.Point(0.32, 1) };
        var fade = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimationUsingKeyFrames();
        fade.KeyFrames.Add(new Microsoft.UI.Xaml.Media.Animation.DiscreteDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero), Value = 0 });
        fade.KeyFrames.Add(new Microsoft.UI.Xaml.Media.Animation.SplineDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(200)), Value = 1, KeySpline = spline });
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(fade, Panel);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(fade, "Opacity");
        var rise = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimationUsingKeyFrames();
        rise.KeyFrames.Add(new Microsoft.UI.Xaml.Media.Animation.DiscreteDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero), Value = 6 });
        rise.KeyFrames.Add(new Microsoft.UI.Xaml.Media.Animation.SplineDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(200)), Value = 0, KeySpline = spline });
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(rise, PanelTranslate);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(rise, "Y");
        sb.Children.Add(fade);
        sb.Children.Add(rise);
        sb.Begin();
    }

    private void Close()
    {
        if (Editor is not null) Editor.IsPaletteOpen = false;
    }

    private void Rebuild(string query)
    {
        Items.Clear();
        if (Editor is null) return;
        var q = query.Trim();
        var ranked = Editor.Commands.All
            .Select(c => (c, score: Score(c.Title, c.Category, q)))
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score)
            .ThenBy(x => x.c.Category)
            .ThenBy(x => x.c.Title)
            .Take(40);
        foreach (var (c, _) in ranked) Items.Add(new PaletteItem(c));
        _highlight = 0;
        Highlight();
    }

    private static int Score(string title, string category, string q)
    {
        if (q.Length == 0) return 1;
        var t = title.ToLowerInvariant();
        var ql = q.ToLowerInvariant();
        if (t.StartsWith(ql)) return 100;
        if (t.Contains(ql)) return 60;
        if (category.ToLowerInvariant().Contains(ql)) return 30;
        // Subsequence match.
        var i = 0;
        foreach (var ch in t) { if (i < ql.Length && ch == ql[i]) i++; }
        return i == ql.Length ? 10 : 0;
    }

    private void Highlight()
    {
        for (var i = 0; i < Items.Count; i++) Items[i].IsHighlighted = i == _highlight;
    }

    private void OnQueryChanged(object sender, TextChangedEventArgs e) => Rebuild(Query.Text);

    private void OnQueryKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Down:
                if (Items.Count > 0) { _highlight = (_highlight + 1) % Items.Count; Highlight(); }
                e.Handled = true;
                break;
            case VirtualKey.Up:
                if (Items.Count > 0) { _highlight = (_highlight - 1 + Items.Count) % Items.Count; Highlight(); }
                e.Handled = true;
                break;
            case VirtualKey.Enter:
                Run(_highlight);
                e.Handled = true;
                break;
            case VirtualKey.Escape:
                Close();
                e.Handled = true;
                break;
        }
    }

    private void Run(int index)
    {
        if (index < 0 || index >= Items.Count) return;
        var item = Items[index];
        Close();
        if (!item.Command.TryExecute()) Editor?.Toast($"{item.Command.Title} is not available right now");
    }

    private void OnItemPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: PaletteItem item }) Run(Items.IndexOf(item));
        e.Handled = true;
    }

    private void OnItemEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: PaletteItem item }) { _highlight = Items.IndexOf(item); Highlight(); }
    }

    private void OnScrimPressed(object sender, PointerRoutedEventArgs e) => Close();
    private void OnPanelPressed(object sender, PointerRoutedEventArgs e) => e.Handled = true;
}
