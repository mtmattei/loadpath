namespace Loadpath.Presentation;

public enum DisplayMode { Forces, Utilization }

/// <summary>
/// Plain mirror of the view preferences, read synchronously by the renderer and the tools.
/// The MVUX states on EditorModel are the bindable truth; they write here and listen here.
/// </summary>
public sealed class ViewOptions
{
    private DisplayMode _displayMode = DisplayMode.Forces;
    private bool _showDeflection;
    private double _exaggeration = 1.0;
    private bool _showLabels = true;
    private bool _showReactions;
    private bool _showGrid = true;
    private bool _snapEnabled = true;
    private double _gridStep = 0.5;
    private bool _inspectorVisible = true;

    public event EventHandler<string>? Changed;

    public DisplayMode DisplayMode { get => _displayMode; set => Set(ref _displayMode, value); }
    public bool ShowDeflection { get => _showDeflection; set => Set(ref _showDeflection, value); }
    public double Exaggeration { get => _exaggeration; set => Set(ref _exaggeration, value); }
    public bool ShowLabels { get => _showLabels; set => Set(ref _showLabels, value); }
    public bool ShowReactions { get => _showReactions; set => Set(ref _showReactions, value); }
    public bool ShowGrid { get => _showGrid; set => Set(ref _showGrid, value); }
    public bool SnapEnabled { get => _snapEnabled; set => Set(ref _snapEnabled, value); }
    public double GridStep { get => _gridStep; set => Set(ref _gridStep, value); }
    public bool InspectorVisible { get => _inspectorVisible; set => Set(ref _inspectorVisible, value); }

    private void Set<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string name = "")
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        Changed?.Invoke(this, name);
    }
}
