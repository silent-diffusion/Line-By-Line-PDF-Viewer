using System.Windows.Input;

namespace LineByLine.Services;

public enum HotkeyAction
{
    NextLines,
    PreviousLines,
    NextPage,
    PreviousPage,
    ToggleDim,
    ToggleAlwaysOnTop,
}

/// <summary>A key plus modifiers, stored in settings as text like "Ctrl+Alt+Down".</summary>
public readonly record struct Hotkey(ModifierKeys Modifiers, Key Key)
{
    public static readonly IReadOnlyDictionary<HotkeyAction, Hotkey> Defaults = new Dictionary<HotkeyAction, Hotkey>
    {
        [HotkeyAction.NextLines] = new(ModifierKeys.Control | ModifierKeys.Alt, Key.Down),
        [HotkeyAction.PreviousLines] = new(ModifierKeys.Control | ModifierKeys.Alt, Key.Up),
        [HotkeyAction.NextPage] = new(ModifierKeys.Control | ModifierKeys.Alt, Key.PageDown),
        [HotkeyAction.PreviousPage] = new(ModifierKeys.Control | ModifierKeys.Alt, Key.PageUp),
        [HotkeyAction.ToggleDim] = new(ModifierKeys.Control | ModifierKeys.Alt, Key.D),
        [HotkeyAction.ToggleAlwaysOnTop] = new(ModifierKeys.Control | ModifierKeys.Alt, Key.T),
    };

    public static string Describe(HotkeyAction action) => action switch
    {
        HotkeyAction.NextLines => "Next line(s)",
        HotkeyAction.PreviousLines => "Previous line(s)",
        HotkeyAction.NextPage => "Next page",
        HotkeyAction.PreviousPage => "Previous page",
        HotkeyAction.ToggleDim => "Toggle dimming",
        HotkeyAction.ToggleAlwaysOnTop => "Toggle always on top",
        _ => action.ToString(),
    };

    /// <summary>Global hotkeys need Ctrl, Alt or Win, or they would swallow ordinary typing everywhere.</summary>
    public bool IsValidGlobal =>
        (Modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) != 0 && !IsModifierKey(Key);

    public static bool IsModifierKey(Key key) => key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
        or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System or Key.None;

    public string ToStorageString() => string.Join("+", ModifierNames().Append(KeyName(Key)));

    public string ToDisplayString() => string.Join("+", ModifierNames().Append(KeyDisplay(Key)));

    public override string ToString() => ToDisplayString();

    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var mods = ModifierKeys.None;
        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return false;

        foreach (var part in parts[..^1])
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl": mods |= ModifierKeys.Control; break;
                case "alt": mods |= ModifierKeys.Alt; break;
                case "shift": mods |= ModifierKeys.Shift; break;
                case "win": mods |= ModifierKeys.Windows; break;
                default: return false;
            }
        }

        if (!Enum.TryParse(parts[^1], ignoreCase: true, out Key key))
            return false;

        hotkey = new Hotkey(mods, key);
        return true;
    }

    private IEnumerable<string> ModifierNames()
    {
        if (Modifiers.HasFlag(ModifierKeys.Control)) yield return "Ctrl";
        if (Modifiers.HasFlag(ModifierKeys.Alt)) yield return "Alt";
        if (Modifiers.HasFlag(ModifierKeys.Shift)) yield return "Shift";
        if (Modifiers.HasFlag(ModifierKeys.Windows)) yield return "Win";
    }

    // Key.PageDown/Key.Next and Key.PageUp/Key.Prior share values, so name them explicitly.
    private static string KeyName(Key key) => key switch
    {
        Key.PageDown => "PageDown",
        Key.PageUp => "PageUp",
        _ => key.ToString(),
    };

    private static string KeyDisplay(Key key) => key switch
    {
        Key.Down => "↓",
        Key.Up => "↑",
        Key.Left => "←",
        Key.Right => "→",
        Key.PageDown => "PgDn",
        Key.PageUp => "PgUp",
        >= Key.D0 and <= Key.D9 => ((int)(key - Key.D0)).ToString(),
        _ => key.ToString(),
    };
}
