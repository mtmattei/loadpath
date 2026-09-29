using Loadpath.Core.Editing;
using Loadpath.Core.Serialization;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace Loadpath.Services;

/// <summary>
/// Open/save through the platform pickers, with a Documents-folder fallback when a picker is unavailable on the host.
/// Pickers run on the UI thread; callers await from there.
/// </summary>
public sealed class FileService
{
    private readonly Func<Window?> _window;

    public FileService(Func<Window?> window) => _window = window;

    public string FallbackFolder
    {
        get
        {
            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (string.IsNullOrEmpty(docs) || docs == Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
                docs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents");
            var dir = Path.Combine(docs, "Loadpath");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>
    /// On Linux the pickers go through the xdg-desktop-portal over the D-Bus session bus.
    /// Without a session bus (containers, bare X sessions) the call never completes, so the fallback is used up front.
    /// </summary>
    public static bool PickersAvailable
    {
        get
        {
            if (!OperatingSystem.IsLinux()) return true;
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS"))) return true;
            var runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
            return !string.IsNullOrEmpty(runtime) && File.Exists(Path.Combine(runtime, "bus"));
        }
    }

    public async Task<(DocumentSnapshot Snapshot, string Path)?> OpenAsync()
    {
        string? path = null;
        try
        {
            if (!PickersAvailable) throw new PlatformNotSupportedException("No file chooser portal.");
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(DocumentSerializer.Extension);
            picker.FileTypeFilter.Add(".json");
            InitializeWithWindow(picker);
            var file = await picker.PickSingleFileAsync();
            path = file?.Path;
            if (file is null) return null;
        }
        catch (Exception)
        {
            // Picker unavailable on this host: open the most recent file in the fallback folder.
            path = Directory.GetFiles(FallbackFolder, "*" + DocumentSerializer.Extension).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
            if (path is null) return null;
        }
        if (string.IsNullOrEmpty(path)) return null;
        var json = await File.ReadAllTextAsync(path);
        return (DocumentSerializer.Deserialize(json), path);
    }

    /// <summary>Save to the given path, or ask for one. Returns the path written, or null when cancelled.</summary>
    public async Task<string?> SaveAsync(StructureDocument doc, string? existingPath, bool forceAsk)
    {
        var path = existingPath;
        if (path is null || forceAsk)
        {
            try
            {
                if (!PickersAvailable) throw new PlatformNotSupportedException("No file chooser portal.");
                var picker = new FileSavePicker { SuggestedFileName = Sanitize(doc.Name) };
                picker.FileTypeChoices.Add("Loadpath structure", new List<string> { DocumentSerializer.Extension });
                InitializeWithWindow(picker);
                var file = await picker.PickSaveFileAsync();
                if (file is null) return null;
                path = file.Path;
            }
            catch (Exception)
            {
                path = Path.Combine(FallbackFolder, Sanitize(doc.Name) + DocumentSerializer.Extension);
            }
        }
        if (string.IsNullOrEmpty(path)) return null;
        await File.WriteAllTextAsync(path, DocumentSerializer.Serialize(doc));
        return path;
    }

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
        return string.IsNullOrEmpty(clean) ? "Untitled" : clean;
    }

    private void InitializeWithWindow(object picker)
    {
#if WINDOWS
        var window = _window();
        if (window is not null)
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        }
#endif
    }
}
