namespace Loadpath.Presentation;

public enum DisplayMode { Forces, Utilization }

/// <summary>Persisted view preferences. Changing any of them re-renders the workspace.</summary>
public sealed partial class ViewOptions : ObservableObject
{
    [ObservableProperty] private DisplayMode _displayMode = DisplayMode.Forces;
    [ObservableProperty] private bool _showDeflection;
    [ObservableProperty] private double _exaggeration = 1.0;
    [ObservableProperty] private bool _showLabels = true;
    [ObservableProperty] private bool _showReactions;
    [ObservableProperty] private bool _showGrid = true;
    [ObservableProperty] private bool _snapEnabled = true;
    [ObservableProperty] private double _gridStep = 0.5;
    [ObservableProperty] private bool _inspectorVisible = true;

    public bool IsForces => DisplayMode == DisplayMode.Forces;
    public bool IsUtilization => DisplayMode == DisplayMode.Utilization;

    partial void OnDisplayModeChanged(DisplayMode value)
    {
        OnPropertyChanged(nameof(IsForces));
        OnPropertyChanged(nameof(IsUtilization));
    }
}
