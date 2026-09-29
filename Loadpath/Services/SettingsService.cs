using System.Text.Json;
using Loadpath.Presentation;

namespace Loadpath.Services;

/// <summary>View options, last file and the autosave, as JSON in the app's local folder.</summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public sealed class Model
    {
        public string DisplayMode { get; set; } = "Forces";
        public bool ShowDeflection { get; set; }
        public double Exaggeration { get; set; } = 1;
        public bool ShowLabels { get; set; } = true;
        public bool ShowReactions { get; set; }
        public bool ShowGrid { get; set; } = true;
        public bool SnapEnabled { get; set; } = true;
        public double GridStep { get; set; } = 0.5;
        public bool InspectorVisible { get; set; } = true;
        public string? LastFilePath { get; set; }
        public List<string> RecentFiles { get; set; } = new();
    }

    public string Folder
    {
        get
        {
            string? dir = null;
            try { dir = Windows.Storage.ApplicationData.Current.LocalFolder.Path; } catch { /* not available on every host */ }
            if (string.IsNullOrEmpty(dir)) dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Loadpath");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    private string SettingsPath => Path.Combine(Folder, "settings.json");
    public string AutosavePath => Path.Combine(Folder, "autosave.loadpath");

    public Model Load()
    {
        try
        {
            if (File.Exists(SettingsPath)) return JsonSerializer.Deserialize<Model>(File.ReadAllText(SettingsPath), Options) ?? new Model();
        }
        catch { /* corrupt settings are not fatal */ }
        return new Model();
    }

    public void Save(Model model)
    {
        try { File.WriteAllText(SettingsPath, JsonSerializer.Serialize(model, Options)); } catch { /* best effort */ }
    }

    public void Apply(Model m, ViewOptions o)
    {
        o.DisplayMode = Enum.TryParse<DisplayMode>(m.DisplayMode, out var mode) ? mode : DisplayMode.Forces;
        o.ShowDeflection = m.ShowDeflection;
        o.Exaggeration = Math.Clamp(m.Exaggeration, 0.2, 3);
        o.ShowLabels = m.ShowLabels;
        o.ShowReactions = m.ShowReactions;
        o.ShowGrid = m.ShowGrid;
        o.SnapEnabled = m.SnapEnabled;
        o.GridStep = m.GridStep > 0 ? m.GridStep : 0.5;
        o.InspectorVisible = m.InspectorVisible;
    }

    public void Capture(ViewOptions o, Model m)
    {
        m.DisplayMode = o.DisplayMode.ToString();
        m.ShowDeflection = o.ShowDeflection;
        m.Exaggeration = o.Exaggeration;
        m.ShowLabels = o.ShowLabels;
        m.ShowReactions = o.ShowReactions;
        m.ShowGrid = o.ShowGrid;
        m.SnapEnabled = o.SnapEnabled;
        m.GridStep = o.GridStep;
        m.InspectorVisible = o.InspectorVisible;
    }
}
