using Loadpath.Workspace;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;

namespace Loadpath.Controls;

/// <summary>
/// House motion for show/hide. <c>Motion.Reveal="True"</c> plays the entrance (opacity 0→1 plus a 6 px rise,
/// 200 ms on EaseSmooth) every time the element turns Visible, so binding-driven panels fade in instead of popping.
/// Collapse stays instant: an exit would need a delayed collapse that fights the binding. Honours reduced motion.
/// </summary>
public static class Motion
{
    public static readonly TimeSpan Fast = TimeSpan.FromMilliseconds(150);
    public static readonly TimeSpan Normal = TimeSpan.FromMilliseconds(200);
    public const double Rise = 6;

    public static readonly DependencyProperty RevealProperty = DependencyProperty.RegisterAttached(
        "Reveal", typeof(bool), typeof(Motion), new PropertyMetadata(false, OnRevealChanged));

    public static bool GetReveal(DependencyObject d) => (bool)d.GetValue(RevealProperty);
    public static void SetReveal(DependencyObject d, bool value) => d.SetValue(RevealProperty, value);

    /// <summary>The house curves as KeySplines (a KeySpline instance cannot be shared between keyframes).</summary>
    public static KeySpline EaseSmooth() => new() { ControlPoint1 = new Point(0.22, 1), ControlPoint2 = new Point(0.36, 1) };
    public static KeySpline EaseOut() => new() { ControlPoint1 = new Point(0.17, 1), ControlPoint2 = new Point(0.32, 1) };

    private static void OnRevealChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element || e.NewValue is not true) return;
        element.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, (s, _) =>
        {
            if (s is UIElement el && el.Visibility == Visibility.Visible && GetReveal(el)) Enter(el);
        });
    }

    /// <summary>Fade in and rise into place. Safe to call on any element; reuses a TranslateTransform if present.</summary>
    public static void Enter(UIElement element, double rise = Rise, bool decorative = false)
    {
        if (!MotionSettings.AnimationsEnabled) { element.Opacity = 1; return; }
        var shift = EnsureTranslate(element);
        var sb = new Storyboard();
        sb.Children.Add(Tween(element, "Opacity", 0, 1, Normal, decorative ? EaseOut() : EaseSmooth()));
        sb.Children.Add(Tween(shift, "Y", rise, 0, Normal, decorative ? EaseOut() : EaseSmooth()));
        sb.Begin();
    }

    /// <summary>
    /// FLIP glide: the caller has already moved the element to its new layout position; this offsets it back by
    /// (<paramref name="dx"/>, <paramref name="dy"/>) and eases the offset to zero, so it travels instead of jumping.
    /// </summary>
    public static void Glide(UIElement element, double dx, double dy)
    {
        if (!MotionSettings.AnimationsEnabled || (Math.Abs(dx) < 1 && Math.Abs(dy) < 1)) return;
        var shift = EnsureTranslate(element);
        var sb = new Storyboard();
        sb.Children.Add(Tween(shift, "X", dx, 0, Normal, EaseSmooth()));
        sb.Children.Add(Tween(shift, "Y", dy, 0, Normal, EaseSmooth()));
        sb.Begin();
    }

    /// <summary>Fade out and drop slightly; <paramref name="done"/> runs when it finishes (or at once when motion is off).</summary>
    public static void Exit(UIElement element, Action? done = null, double drop = 4)
    {
        if (!MotionSettings.AnimationsEnabled) { element.Opacity = 0; done?.Invoke(); return; }
        var shift = EnsureTranslate(element);
        var sb = new Storyboard();
        sb.Children.Add(Tween(element, "Opacity", element.Opacity, 0, Fast, EaseSmooth()));
        sb.Children.Add(Tween(shift, "Y", shift.Y, drop, Fast, EaseSmooth()));
        if (done is not null) sb.Completed += (_, _) => done();
        sb.Begin();
    }

    /// <summary>One spline-eased keyframe tween from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public static DoubleAnimationUsingKeyFrames Tween(DependencyObject target, string property, double from, double to, TimeSpan duration, KeySpline spline)
    {
        var anim = new DoubleAnimationUsingKeyFrames();
        anim.KeyFrames.Add(new DiscreteDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero), Value = from });
        anim.KeyFrames.Add(new SplineDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(duration), Value = to, KeySpline = spline });
        Storyboard.SetTarget(anim, target);
        Storyboard.SetTargetProperty(anim, property);
        return anim;
    }

    private static TranslateTransform EnsureTranslate(UIElement element)
    {
        if (element.RenderTransform is TranslateTransform t) return t;
        var shift = new TranslateTransform();
        element.RenderTransform = shift;
        return shift;
    }
}
