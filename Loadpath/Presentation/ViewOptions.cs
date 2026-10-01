namespace Loadpath.Presentation;

public enum DisplayMode { Forces, Utilization }

/// <summary>Everything "Reduce noise" can hide. The structure, the selection and the inspector inputs always stay.</summary>
[Flags]
public enum NoiseLayer
{
    None = 0,
    Grid = 1 << 0,
    Dimensions = 1 << 1,
    Supports = 1 << 2,
    Loads = 1 << 3,
    OverCapacity = 1 << 4,
    MemberForces = 1 << 5,
    Reactions = 1 << 6,
    Deflection = 1 << 7,
    PartNames = 1 << 8,
    Legend = 1 << 9,
    StructureList = 1 << 10,
    ResultDetails = 1 << 11,
    All = (1 << 12) - 1,
}

/// <summary>
/// Plain mirror of the view preferences, read synchronously by the renderer and the tools.
/// The MVUX states on EditorModel are the bindable truth; they write here and listen here.
/// </summary>
public sealed class ViewOptions
{
    public const int LayerCount = 12;
    public const NoiseLayer DefaultKeep = NoiseLayer.Supports | NoiseLayer.Loads | NoiseLayer.OverCapacity;

    private DisplayMode _displayMode = DisplayMode.Forces;
    private bool _showDeflection = true;
    private double _exaggeration = 1.0;
    private bool _showLabels = true;
    private bool _showReactions = true;
    private bool _showGrid = true;
    private bool _snapEnabled = true;
    private double _gridStep = 0.5;
    private bool _inspectorVisible = true;
    private bool _reduceNoise;
    private NoiseLayer _keep = DefaultKeep;

    public event EventHandler<string>? Changed;

    public DisplayMode DisplayMode { get => _displayMode; set => Set(ref _displayMode, value); }
    public bool ShowDeflection { get => _showDeflection; set => Set(ref _showDeflection, value); }
    public double Exaggeration { get => _exaggeration; set => Set(ref _exaggeration, Math.Clamp(Math.Round(value, 1), 0.2, 3)); }
    public bool ShowLabels { get => _showLabels; set => Set(ref _showLabels, value); }
    public bool ShowReactions { get => _showReactions; set => Set(ref _showReactions, value); }
    public bool ShowGrid { get => _showGrid; set => Set(ref _showGrid, value); }
    public bool SnapEnabled { get => _snapEnabled; set => Set(ref _snapEnabled, value); }
    public double GridStep { get => _gridStep; set => Set(ref _gridStep, value); }
    public bool InspectorVisible { get => _inspectorVisible; set => Set(ref _inspectorVisible, value); }
    public bool ReduceNoise { get => _reduceNoise; set => Set(ref _reduceNoise, value); }
    /// <summary>The layers that stay on screen while noise is reduced.</summary>
    public NoiseLayer Keep { get => _keep; set => Set(ref _keep, value & NoiseLayer.All); }

    public void SetKeep(NoiseLayer layer, bool keep) => Keep = keep ? Keep | layer : Keep & ~layer;

    public int HiddenLayerCount => ReduceNoise ? LayerCount - System.Numerics.BitOperations.PopCount((uint)Keep) : 0;

    /// <summary>A layer shows when its own toggle is on (grid, keys 3/4/5) and noise reduction keeps it, or is off.</summary>
    public bool IsVisible(NoiseLayer layer)
    {
        var baseOn = layer switch
        {
            NoiseLayer.Grid => ShowGrid,
            NoiseLayer.Deflection => ShowDeflection,
            NoiseLayer.MemberForces => ShowLabels,
            NoiseLayer.Reactions => ShowReactions,
            _ => true,
        };
        return baseOn && (!ReduceNoise || (Keep & layer) != 0);
    }

    private void Set<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string name = "")
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        Changed?.Invoke(this, name);
    }
}
