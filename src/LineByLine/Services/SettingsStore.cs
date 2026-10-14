using System.IO;
using System.Text.Json;

namespace LineByLine.Services;

public sealed class FilePosition
{
    public int Page { get; set; }
    public int Row { get; set; }
    public DateTime LastOpened { get; set; }
}

public sealed class WindowPlacement
{
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public bool Maximized { get; set; }
}

public sealed class AppSettings
{
    public int LinesInView { get; set; } = 2;

    /// <summary>Lines moved per keypress. 0 means "same as lines in view".</summary>
    public int StepSize { get; set; }

    public double DimOpacity { get; set; } = 0.55;
    public bool AlwaysOnTop { get; set; }
    public bool RememberPosition { get; set; } = true;
    public Dictionary<string, string> Hotkeys { get; set; } = new();
    public Dictionary<string, FilePosition> Positions { get; set; } = new();
    public WindowPlacement? Window { get; set; }

    public int EffectiveStep => StepSize > 0 ? StepSize : LinesInView;

    public Hotkey GetHotkey(HotkeyAction action) =>
        Hotkeys.TryGetValue(action.ToString(), out var text) && Hotkey.TryParse(text, out var hk)
            ? hk
            : Hotkey.Defaults[action];

    public void SetHotkey(HotkeyAction action, Hotkey hotkey) => Hotkeys[action.ToString()] = hotkey.ToStorageString();

    public IReadOnlyDictionary<HotkeyAction, Hotkey> AllHotkeys() =>
        Enum.GetValues<HotkeyAction>().ToDictionary(a => a, GetHotkey);
}

/// <summary>Loads and saves settings as JSON in %AppData%\LineByLine.</summary>
public sealed class SettingsStore
{
    private const int MaxRememberedFiles = 200;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _path;

    public AppSettings Settings { get; private set; } = new();

    /// <summary>Raised after <see cref="Save"/> so open windows can pick up changes.</summary>
    public event Action? Changed;

    public SettingsStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LineByLine", "settings.json");
    }

    public void Load()
    {
        try
        {
            if (File.Exists(_path))
                Settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), JsonOptions) ?? new AppSettings();
        }
        catch (Exception)
        {
            // A corrupt settings file shouldn't stop the app from opening; start fresh.
            Settings = new AppSettings();
        }

        Settings.LinesInView = Math.Clamp(Settings.LinesInView, 1, 10);
        Settings.StepSize = Math.Clamp(Settings.StepSize, 0, 10);
        Settings.DimOpacity = Math.Clamp(Settings.DimOpacity, 0, 0.85);
    }

    public void Save(bool notify = true)
    {
        PrunePositions();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(Settings, JsonOptions));
        }
        catch (IOException)
        {
            // Settings are a convenience; failing to write them is not worth crashing over.
        }
        if (notify)
            Changed?.Invoke();
    }

    public static string PositionKey(string filePath) => Path.GetFullPath(filePath).ToLowerInvariant();

    private void PrunePositions()
    {
        if (Settings.Positions.Count <= MaxRememberedFiles)
            return;
        foreach (var key in Settings.Positions.OrderBy(p => p.Value.LastOpened)
                     .Take(Settings.Positions.Count - MaxRememberedFiles).Select(p => p.Key).ToList())
            Settings.Positions.Remove(key);
    }
}
