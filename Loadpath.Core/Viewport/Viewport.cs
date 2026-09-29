using Loadpath.Core.Geometry;

namespace Loadpath.Core.Viewport;

/// <summary>
/// World (meters, Y up) ↔ screen (pixels, Y down) mapping.
/// Scale is pixels per meter. Offset is the screen position of the world origin.
/// </summary>
public sealed class Viewport
{
    public const double MinScale = 4;      // 5% of the 80 px/m base
    public const double MaxScale = 1600;   // 2000%
    public const double BaseScale = 80;    // "100%"

    private double _scale = BaseScale;
    private Vec2 _offset = new(120, 480);

    public event EventHandler? Changed;

    public double Scale
    {
        get => _scale;
        set
        {
            var clamped = Math.Clamp(value, MinScale, MaxScale);
            if (Math.Abs(clamped - _scale) < 1e-9) return;
            _scale = clamped;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public Vec2 Offset
    {
        get => _offset;
        set
        {
            if (_offset == value) return;
            _offset = value;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public double ScreenWidth { get; private set; }
    public double ScreenHeight { get; private set; }
    public double ZoomPercent => _scale / BaseScale * 100;

    public void Resize(double width, double height)
    {
        ScreenWidth = width;
        ScreenHeight = height;
    }

    public Vec2 ToScreen(Vec2 world) => new(world.X * _scale + _offset.X, -world.Y * _scale + _offset.Y);
    public Vec2 ToWorld(Vec2 screen) => new((screen.X - _offset.X) / _scale, -(screen.Y - _offset.Y) / _scale);
    public double ToWorldLength(double pixels) => pixels / _scale;
    public double ToScreenLength(double meters) => meters * _scale;

    public Bounds VisibleWorldBounds() => Bounds.FromPoints(ToWorld(Vec2.Zero), ToWorld(new Vec2(ScreenWidth, ScreenHeight)));

    /// <summary>Set both at once, raising one change.</summary>
    public void Set(double scale, Vec2 offset)
    {
        var s = Math.Clamp(scale, MinScale, MaxScale);
        if (Math.Abs(s - _scale) < 1e-9 && offset == _offset) return;
        _scale = s;
        _offset = offset;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Zoom so that the world point under screenAnchor stays under it.</summary>
    public void ZoomAt(Vec2 screenAnchor, double newScale)
    {
        var s = Math.Clamp(newScale, MinScale, MaxScale);
        var worldAnchor = ToWorld(screenAnchor);
        var offset = new Vec2(screenAnchor.X - worldAnchor.X * s, screenAnchor.Y + worldAnchor.Y * s);
        Set(s, offset);
    }

    public void PanBy(Vec2 screenDelta) => Offset = _offset + screenDelta;

    /// <summary>Compute the (scale, offset) that fits world bounds with padding, without applying it.</summary>
    public (double Scale, Vec2 Offset) ComputeFit(Bounds world, double paddingFraction = 0.15)
    {
        if (world.IsEmpty || ScreenWidth <= 0 || ScreenHeight <= 0) return (BaseScale, new Vec2(ScreenWidth * 0.3, ScreenHeight * 0.7));
        var w = Math.Max(world.Width, 1.0);
        var h = Math.Max(world.Height, 1.0);
        var usableW = ScreenWidth * (1 - 2 * paddingFraction);
        var usableH = ScreenHeight * (1 - 2 * paddingFraction);
        var scale = Math.Clamp(Math.Min(usableW / w, usableH / h), MinScale, MaxScale);
        var c = world.Center;
        var offset = new Vec2(ScreenWidth / 2 - c.X * scale, ScreenHeight / 2 + c.Y * scale);
        return (scale, offset);
    }

    public void Fit(Bounds world, double paddingFraction = 0.15)
    {
        var (s, o) = ComputeFit(world, paddingFraction);
        Set(s, o);
    }
}
