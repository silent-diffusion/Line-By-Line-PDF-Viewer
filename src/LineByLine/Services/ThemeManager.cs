using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace LineByLine.Services;

public enum AppTheme
{
    /// <summary>Follow Windows' "Choose your app mode" setting, and change with it.</summary>
    System,
    Light,
    Dark,
}

/// <summary>
/// Swaps the app's colour dictionary (Themes/Light.xaml or Themes/Dark.xaml) and keeps window title
/// bars in step. Every colour in the UI is a DynamicResource, so a swap restyles open windows live.
/// </summary>
public static class ThemeManager
{
    private const int DwmUseImmersiveDarkMode = 20;
    private static AppTheme _mode = AppTheme.System;
    private static bool _watching;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static bool IsDark { get; private set; }

    /// <summary>Raised after the colours change, for anything drawn in code that needs a refresh.</summary>
    public static event Action? Changed;

    public static void SetMode(AppTheme mode)
    {
        _mode = mode;
        if (!_watching && Application.Current != null)
        {
            // Windows tells running apps when the user flips light/dark mode.
            SystemEvents.UserPreferenceChanged += (_, e) =>
            {
                if (e.Category == UserPreferenceCategory.General && _mode == AppTheme.System)
                    Application.Current?.Dispatcher.BeginInvoke(Apply);
            };
            _watching = true;
        }
        Apply();
    }

    public static bool SystemPrefersDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void Apply()
    {
        var app = Application.Current;
        if (app == null)
            return;
        var dark = _mode == AppTheme.Dark || (_mode == AppTheme.System && SystemPrefersDark());
        var dictionaries = app.Resources.MergedDictionaries;
        // Full pack URI: a relative one resolves against whichever program started the process.
        var wanted = new Uri($"pack://application:,,,/LineByLine;component/Themes/{(dark ? "Dark" : "Light")}.xaml");
        var current = dictionaries.Count > 0 ? dictionaries[0].Source?.OriginalString ?? "" : "";
        if (current.EndsWith($"/{(dark ? "Dark" : "Light")}.xaml", StringComparison.OrdinalIgnoreCase))
        {
            IsDark = dark;
            return;
        }

        var theme = new ResourceDictionary { Source = wanted };
        if (dictionaries.Count > 0)
            dictionaries[0] = theme;
        else
            dictionaries.Insert(0, theme);
        IsDark = dark;
        foreach (Window window in app.Windows)
            ApplyTitleBar(window);
        Changed?.Invoke();
    }

    /// <summary>Dark or light title bar for windows that use the standard Windows frame.</summary>
    public static void ApplyTitleBar(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return;
        var value = IsDark ? 1 : 0;
        _ = DwmSetWindowAttribute(hwnd, DwmUseImmersiveDarkMode, ref value, sizeof(int));
    }
}
