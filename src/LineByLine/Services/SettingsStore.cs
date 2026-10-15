using System.IO;
using System.Text.Json;
using LineByLine.Documents;

namespace LineByLine.Services;

public sealed class FilePosition
{
    public int Page { get; set; }
    public int Row { get; set; }

    /// <summary>Column focus span in PDF points, when the band was narrowed.</summary>
    public double? SpanLeft { get; set; }
    public double? SpanRight { get; set; }
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

public enum ValueGrabMode
{
    /// <summary>Just the line's dollar amount, as a plain number.</summary>
    Amount,

    /// <summary>The whole line, tab-separated so it fills Excel columns.</summary>
    Row,
}

public sealed class AppSettings
{
    public int LinesInView { get; set; } = 2;

    /// <summary>Lines moved per keypress. 0 means "same as lines in view".</summary>
    public int StepSize { get; set; }

    public double DimOpacity { get; set; } = 0.55;
    public bool AlwaysOnTop { get; set; }
    public bool RememberPosition { get; set; } = true;

    /// <summary>Read scanned pages and images with Windows OCR.</summary>
    public bool OcrEnabled { get; set; } = true;

    /// <summary>Value Grab: amount chips beside the band and copy hotkeys. Off by default.</summary>
    public bool ValueGrabEnabled { get; set; }
    public ValueGrabMode ValueGrabMode { get; set; } = ValueGrabMode.Amount;
    public bool ShowValueGrabButton { get; set; } = true;

    /// <summary>Which number counts as the line's value.</summary>
    public ValuePick ValuePick { get; set; } = ValuePick.RightmostValue;

    /// <summary>Copy automatically every time the band moves, so moving is all it takes.</summary>
    public bool AutoGrab { get; set; }

    /// <summary>Auto grab copies every line in the band instead of just the top one.</summary>
    public bool AutoGrabAllLines { get; set; }

    /// <summary>Highlighter colour, "#RRGGBB". Green by default.</summary>
    public string HighlightColor { get; set; } = "#3DDC84";
    public double HighlightOpacity { get; set; } = 0.35;

    /// <summary>Highlight every line in the band instead of just the top one.</summary>
    public bool HighlightAllLines { get; set; }

    /// <summary>Highlight just the line's value (what Value Grab would copy) instead of the whole line.</summary>
    public bool HighlightValueOnly { get; set; }

    /// <summary>Highlight lines automatically as the band moves forward past them.</summary>
    public bool AutoHighlight { get; set; }

    /// <summary>Also write highlights into the PDF so other viewers show them. Off: they live in the app only.</summary>
    public bool SaveHighlightsToPdf { get; set; }

    /// <summary>Highlights per file (same keys as <see cref="Positions"/>).</summary>
    public Dictionary<string, List<LineMark>> Highlights { get; set; } = new();

    /// <summary>Check GitHub on every start and install new versions automatically.</summary>
    public bool AutoUpdate { get; set; }
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
        Settings.HighlightOpacity = Math.Clamp(Settings.HighlightOpacity, 0.1, 0.8);
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
