using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace LineByLine.Services;

/// <summary>
/// System-wide hotkeys via Win32 RegisterHotKey, so the band moves while Excel has focus.
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x1, ModControl = 0x2, ModShift = 0x4, ModWin = 0x8;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly IntPtr _hwnd;
    private readonly HwndSource _source;
    private readonly Dictionary<int, HotkeyAction> _registered = new();
    private IReadOnlyList<(HotkeyAction Action, Hotkey Hotkey)> _current = [];
    private int _suspendCount;

    public event Action<HotkeyAction>? Pressed;

    /// <summary>Shortcuts that Windows refused, usually because another app already owns them.</summary>
    public IReadOnlyList<(HotkeyAction Action, Hotkey Hotkey)> Failed { get; private set; } = [];

    public HotkeyManager(IntPtr hwnd)
    {
        _hwnd = hwnd;
        _source = HwndSource.FromHwnd(hwnd) ?? throw new InvalidOperationException("No HwndSource for window.");
        _source.AddHook(WndProc);
    }

    public void Apply(IEnumerable<(HotkeyAction Action, Hotkey Hotkey)> hotkeys)
    {
        _current = hotkeys.ToList();
        if (_suspendCount == 0)
            RegisterCurrent();
    }

    /// <summary>Temporarily releases every hotkey, e.g. while the user records a new shortcut.</summary>
    public void Suspend()
    {
        if (_suspendCount++ == 0)
            UnregisterAll();
    }

    public void Resume()
    {
        if (_suspendCount > 0 && --_suspendCount == 0)
            RegisterCurrent();
    }

    private void RegisterCurrent()
    {
        UnregisterAll();
        var failed = new List<(HotkeyAction, Hotkey)>();
        var id = 1;
        foreach (var (action, hotkey) in _current)
        {
            if (!hotkey.IsValidGlobal)
            {
                failed.Add((action, hotkey));
                continue;
            }

            var vk = (uint)KeyInterop.VirtualKeyFromKey(hotkey.Key);
            if (RegisterHotKey(_hwnd, id, ToNative(hotkey.Modifiers), vk))
                _registered[id] = action;
            else
                failed.Add((action, hotkey));
            id++;
        }
        Failed = failed;
    }

    private void UnregisterAll()
    {
        foreach (var id in _registered.Keys)
            UnregisterHotKey(_hwnd, id);
        _registered.Clear();
    }

    private static uint ToNative(ModifierKeys mods)
    {
        uint result = 0;
        if (mods.HasFlag(ModifierKeys.Alt)) result |= ModAlt;
        if (mods.HasFlag(ModifierKeys.Control)) result |= ModControl;
        if (mods.HasFlag(ModifierKeys.Shift)) result |= ModShift;
        if (mods.HasFlag(ModifierKeys.Windows)) result |= ModWin;
        return result;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && _registered.TryGetValue(wParam.ToInt32(), out var action))
        {
            handled = true;
            Pressed?.Invoke(action);
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterAll();
        _source.RemoveHook(WndProc);
    }
}
