using Microsoft.UI.Xaml.Controls;

namespace Loadpath.Controls;

/// <summary>A tool-rail button: icon, single-key label, and an active state driven by the editor's current tool.</summary>
public sealed partial class ToolButton : Button
{
    public static readonly DependencyProperty IconKindProperty = DependencyProperty.Register(
        nameof(IconKind), typeof(string), typeof(ToolButton), new PropertyMetadata("select"));

    public static readonly DependencyProperty KeyLabelProperty = DependencyProperty.Register(
        nameof(KeyLabel), typeof(string), typeof(ToolButton), new PropertyMetadata(""));

    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
        nameof(IsActive), typeof(bool), typeof(ToolButton), new PropertyMetadata(false, (d, _) => ((ToolButton)d).UpdateActiveState(true)));

    public ToolButton()
    {
        DefaultStyleKey = typeof(ToolButton);
    }

    public string IconKind
    {
        get => (string)GetValue(IconKindProperty);
        set => SetValue(IconKindProperty, value);
    }

    public string KeyLabel
    {
        get => (string)GetValue(KeyLabelProperty);
        set => SetValue(KeyLabelProperty, value);
    }

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        UpdateActiveState(false);
    }

    private void UpdateActiveState(bool animate) => VisualStateManager.GoToState(this, IsActive ? "Active" : "Inactive", animate);
}
