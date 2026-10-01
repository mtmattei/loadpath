using System.Globalization;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Loadpath.Controls;

/// <summary>
/// Mono numeric field with a unit suffix. Commits on Enter or blur, reverts on Esc, steps with Up/Down (×10 with Shift).
/// Value is the committed number; Committed fires once per commit.
/// </summary>
public sealed partial class NumberField : Control
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(NumberField), new PropertyMetadata(0.0, (d, _) => ((NumberField)d).RenderValue()));

    public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(
        nameof(Unit), typeof(string), typeof(NumberField), new PropertyMetadata(""));

    public static readonly DependencyProperty DecimalsProperty = DependencyProperty.Register(
        nameof(Decimals), typeof(int), typeof(NumberField), new PropertyMetadata(2, (d, _) => ((NumberField)d).RenderValue()));

    public static readonly DependencyProperty StepProperty = DependencyProperty.Register(
        nameof(Step), typeof(double), typeof(NumberField), new PropertyMetadata(0.5));

    private TextBox? _box;
    private bool _editing;

    public NumberField()
    {
        DefaultStyleKey = typeof(NumberField);
        IsTabStop = false;
    }

    public event EventHandler? Committed;

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public string Unit { get => (string)GetValue(UnitProperty); set => SetValue(UnitProperty, value); }
    public int Decimals { get => (int)GetValue(DecimalsProperty); set => SetValue(DecimalsProperty, value); }
    public double Step { get => (double)GetValue(StepProperty); set => SetValue(StepProperty, value); }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (_box is not null)
        {
            _box.KeyDown -= OnKeyDown;
            _box.LostFocus -= OnLostFocus;
            _box.GotFocus -= OnGotFocus;
        }
        _box = GetTemplateChild("PART_Box") as TextBox;
        if (_box is not null)
        {
            _box.KeyDown += OnKeyDown;
            _box.LostFocus += OnLostFocus;
            _box.GotFocus += OnGotFocus;
        }
        RenderValue();
    }

    private void RenderValue()
    {
        if (_box is null || _editing) return;
        _box.Text = Value.ToString("F" + Decimals, CultureInfo.InvariantCulture);
    }

    private void OnGotFocus(object sender, RoutedEventArgs e)
    {
        _editing = true;
        _box?.SelectAll();
    }

    private void OnLostFocus(object sender, RoutedEventArgs e)
    {
        _editing = false;
        Commit();
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Enter:
                Commit();
                e.Handled = true;
                break;
            case VirtualKey.Escape:
                _editing = false;
                RenderValue();
                e.Handled = true;
                break;
            case VirtualKey.Up:
            case VirtualKey.Down:
                var shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
                var delta = (e.Key == VirtualKey.Up ? 1 : -1) * Step * (shift ? 10 : 1);
                _editing = false;
                Value = Math.Round(Value + delta, Math.Max(Decimals, 3));
                Committed?.Invoke(this, EventArgs.Empty);
                _editing = true;
                _box?.SelectAll();
                e.Handled = true;
                break;
        }
    }

    private void Commit()
    {
        if (_box is null) return;
        var text = _box.Text.Trim().Replace(',', '.').Replace("−", "-");
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && !double.IsNaN(v) && !double.IsInfinity(v))
        {
            var rounded = Math.Round(v, Math.Max(Decimals, 3));
            var changed = Math.Abs(rounded - Value) > 1e-12;
            _editing = false;
            Value = rounded;
            RenderValue();
            if (changed) Committed?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            _editing = false;
            RenderValue();
        }
    }
}
