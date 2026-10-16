using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LineByLine.Documents;
using LineByLine.Services;

namespace LineByLine;

public partial class SettingsWindow : Window
{
    private readonly SettingsStore _store;
    private readonly MainWindow _main;
    private readonly Dictionary<HotkeyAction, (TextBox Box, TextBox Second, TextBlock Status)> _hotkeyRows = new();
    // True until the first Refresh: building the window fires change events (a slider snapping to
    // its minimum, say) that must not be saved as if the user had made them.
    private bool _loading = true;

    private AppSettings S => _store.Settings;

    public SettingsWindow(SettingsStore store, MainWindow main)
    {
        _store = store;
        _main = main;
        InitializeComponent();

        var versionText = App.Updates.CurrentVersion;
        VersionText.Text = $"Line by Line v{versionText}";
        InstalledVersionText.Text = App.Updates.IsInstalled ? $"Installed version {versionText}" : $"Version {versionText} (run from source)";
        ShowUpdateIdle();

        BuildHotkeyRows();
        BuildSwatches();
        Refresh();
        _store.Changed += Refresh;
        ThemeManager.Changed += Refresh;
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);
        Closed += (_, _) =>
        {
            _store.Changed -= Refresh;
            ThemeManager.Changed -= Refresh;
        };
    }

    private void Refresh()
    {
        _loading = true;
        LinesValue.Text = S.LinesInView.ToString();
        StepValue.Text = S.EffectiveStep.ToString();
        StepMatchesLines.IsChecked = S.StepSize == 0;
        StepMinusButton.IsEnabled = StepPlusButton.IsEnabled = S.StepSize != 0;
        DimSlider.Value = S.DimOpacity;
        DimValue.Text = $"{Math.Round(S.DimOpacity * 100)}%";
        TopmostSwitch.IsChecked = S.AlwaysOnTop;
        RememberSwitch.IsChecked = S.RememberPosition;
        AutoUpdateSwitch.IsChecked = S.AutoUpdate;
        AutoUpdateSwitch.IsEnabled = App.Updates.IsInstalled;
        OcrSwitch.IsChecked = S.OcrEnabled && OcrReader.IsAvailable;
        OcrSwitch.IsEnabled = OcrReader.IsAvailable;
        OcrDescription.Text = OcrReader.IsAvailable
            ? $"Finds the lines in scanned PDFs and images (PNG, JPG, TIFF) with the OCR built into Windows, offline. Language: {OcrReader.LanguageName}."
            : "Windows OCR isn't available on this PC. Adding a language in Windows Settings → Time & language installs it.";
        ValueGrabSwitch.IsChecked = S.ValueGrabEnabled;
        ModeAmount.IsChecked = S.ValueGrabMode == ValueGrabMode.Amount;
        ModeRow.IsChecked = S.ValueGrabMode == ValueGrabMode.Row;
        ValueGrabButtonSwitch.IsChecked = S.ShowValueGrabButton;
        PickRightmost.IsChecked = S.ValuePick == ValuePick.RightmostValue;
        PickMoney.IsChecked = S.ValuePick == ValuePick.RightmostMoney;
        PickPanel.IsEnabled = S.ValueGrabMode == ValueGrabMode.Amount;
        AutoGrabSwitch.IsChecked = S.AutoGrab;
        AutoGrabSwitch.IsEnabled = S.ValueGrabEnabled;
        AutoGrabTop.IsChecked = !S.AutoGrabAllLines;
        AutoGrabAll.IsChecked = S.AutoGrabAllLines;
        AutoGrabScopePanel.IsEnabled = S.ValueGrabEnabled && S.AutoGrab;
        RefreshHighlights();
        ThemeSystem.IsChecked = S.Theme == AppTheme.System;
        ThemeLight.IsChecked = S.Theme == AppTheme.Light;
        ThemeDark.IsChecked = S.Theme == AppTheme.Dark;
        ThemeSystemNow.Text = $" (currently {(ThemeManager.SystemPrefersDark() ? "dark" : "light")})";
        ValueGrabHint.Text = S.ValueGrabEnabled
            ? $"On. {S.GetHotkey(HotkeyAction.CopyValue).ToDisplayString()} copies the top line's value; {S.GetHotkey(HotkeyAction.CopyAllValues).ToDisplayString()} copies every line in the band, one per row. You can also click a chip."
            : "Off. No chips, and the copy hotkeys aren't registered, so they're free for other apps.";
        RefreshHotkeyRows();
        _loading = false;
    }

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (BandPage == null)
            return; // fires during InitializeComponent
        BandPage.Visibility = NavBand.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        HotkeysPage.Visibility = NavHotkeys.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        ValueGrabPage.Visibility = NavValueGrab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        HighlightsPage.Visibility = NavHighlights.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        AppearancePage.Visibility = NavAppearance.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        WindowPage.Visibility = NavWindow.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        UpdatesPage.Visibility = NavUpdates.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    // ───────────────────────────── focus band ─────────────────────────────

    private void LinesMinus_Click(object sender, RoutedEventArgs e) => SetLines(S.LinesInView - 1);
    private void LinesPlus_Click(object sender, RoutedEventArgs e) => SetLines(S.LinesInView + 1);

    private void SetLines(int value)
    {
        S.LinesInView = Math.Clamp(value, 1, 10);
        _store.Save();
    }

    private void StepMinus_Click(object sender, RoutedEventArgs e) => SetStep(S.EffectiveStep - 1);
    private void StepPlus_Click(object sender, RoutedEventArgs e) => SetStep(S.EffectiveStep + 1);

    private void SetStep(int value)
    {
        S.StepSize = Math.Clamp(value, 1, 10);
        _store.Save();
    }

    private void StepMatches_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;
        S.StepSize = StepMatchesLines.IsChecked == true ? 0 : S.LinesInView;
        _store.Save();
    }

    private void DimSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading || DimValue == null)
            return;
        S.DimOpacity = Math.Round(e.NewValue, 2);
        _store.Save();
    }

    private void OcrSwitch_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;
        S.OcrEnabled = OcrSwitch.IsChecked == true;
        _store.Save();
    }

    // ───────────────────────────── value grab ─────────────────────────────

    private void ValueGrabSwitch_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;
        S.ValueGrabEnabled = ValueGrabSwitch.IsChecked == true;
        _store.Save();
    }

    private void Mode_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading || ModeRow == null)
            return;
        S.ValueGrabMode = ModeRow.IsChecked == true ? ValueGrabMode.Row : ValueGrabMode.Amount;
        _store.Save();
    }

    private void Pick_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading || PickMoney == null)
            return;
        S.ValuePick = PickMoney.IsChecked == true ? ValuePick.RightmostMoney : ValuePick.RightmostValue;
        _store.Save();
    }

    private void AutoGrabSwitch_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;
        S.AutoGrab = AutoGrabSwitch.IsChecked == true;
        _store.Save();
    }

    private void AutoScope_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading || AutoGrabAll == null)
            return;
        S.AutoGrabAllLines = AutoGrabAll.IsChecked == true;
        _store.Save();
    }

    private void ValueGrabButtonSwitch_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;
        S.ShowValueGrabButton = ValueGrabButtonSwitch.IsChecked == true;
        _store.Save();
    }

    // ───────────────────────────── updates ─────────────────────────────

    private Velopack.UpdateInfo? _update;

    private void ShowUpdateIdle()
    {
        var updates = App.Updates;
        CheckUpdatesButton.IsEnabled = updates.IsInstalled;
        if (!updates.IsInstalled)
        {
            UpdateStatusText.Text = "Updates work in the installed app. Get the installer from GitHub Releases.";
            return;
        }
        if (updates.Downloaded is { } ready)
        {
            ShowUpdateCard(ready, downloaded: true);
            UpdateStatusText.Text = $"Version {UpdateService.VersionOf(ready)} is downloaded and installs when you close Line by Line.";
            return;
        }
        UpdateStatusText.Text = "Checks the Line by Line releases on GitHub.";
    }

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdatesButton.IsEnabled = false;
        UpdateStatusText.Text = "Checking GitHub…";
        UpdateCard.Visibility = Visibility.Collapsed;
        try
        {
            _update = await App.Updates.CheckAsync();
            if (_update == null)
            {
                UpdateStatusText.Text = $"You're on the latest version. Checked {DateTime.Now:t}.";
                return;
            }
            UpdateStatusText.Text = "A new version is available.";
            ShowUpdateCard(_update, downloaded: App.Updates.Downloaded != null);
        }
        catch (Exception ex)
        {
            UpdateStatusText.Text = $"Couldn't reach GitHub ({ex.Message}). Check your connection and try again.";
        }
        finally
        {
            CheckUpdatesButton.IsEnabled = true;
        }
    }

    private void ShowUpdateCard(Velopack.UpdateInfo update, bool downloaded)
    {
        _update = update;
        UpdateTitle.Text = $"Version {UpdateService.VersionOf(update)} is available";
        UpdateNotes.Text = UpdateService.NotesOf(update);
        UpdateNowButton.Content = downloaded ? "Restart now to update" : "Update & restart now";
        UpdateNowButton.IsEnabled = LaterButton.IsEnabled = true;
        UpdateCard.Visibility = Visibility.Visible;
    }

    private async void UpdateNow_Click(object sender, RoutedEventArgs e)
    {
        if (_update == null)
            return;
        UpdateNowButton.IsEnabled = LaterButton.IsEnabled = false;
        try
        {
            if (App.Updates.Downloaded == null)
            {
                await App.Updates.DownloadAsync(_update, percent =>
                    Dispatcher.BeginInvoke(() => UpdateNowButton.Content = $"Downloading… {percent}%"));
            }
            UpdateNowButton.Content = "Restarting…";
            App.Updates.ApplyAndRestart(_update);
        }
        catch (Exception ex)
        {
            UpdateStatusText.Text = $"The update didn't finish ({ex.Message}). Try again in a moment.";
            UpdateNowButton.Content = "Update & restart now";
            UpdateNowButton.IsEnabled = LaterButton.IsEnabled = true;
        }
    }

    private void Later_Click(object sender, RoutedEventArgs e)
    {
        UpdateCard.Visibility = Visibility.Collapsed;
        UpdateStatusText.Text = "Update postponed. Check again anytime.";
    }

    private void ReleaseLink_Click(object sender, RoutedEventArgs e) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(UpdateService.ReleasesUrl) { UseShellExecute = true });

    private void AutoUpdateSwitch_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;
        S.AutoUpdate = AutoUpdateSwitch.IsChecked == true;
        _store.Save();
    }

    // ───────────────────────────── appearance ─────────────────────────────

    private void Theme_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading || ThemeDark == null)
            return;
        S.Theme = ThemeDark.IsChecked == true ? AppTheme.Dark : ThemeLight.IsChecked == true ? AppTheme.Light : AppTheme.System;
        _store.Save();
    }

    // ───────────────────────────── highlights ─────────────────────────────

    private static readonly (string Name, string Hex)[] Swatches =
    [
        ("Green", "#3DDC84"), ("Yellow", "#FFD60A"), ("Blue", "#4DA3FF"), ("Pink", "#FF6FB5"), ("Orange", "#FF9F43"),
    ];

    private readonly List<(Border Ring, string Hex)> _swatchRings = [];

    private void BuildSwatches()
    {
        foreach (var (name, hex) in Swatches)
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            var ring = new Border
            {
                Width = 34, Height = 34, CornerRadius = new CornerRadius(17), BorderThickness = new Thickness(2),
                Child = new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Background = new SolidColorBrush(color) },
            };
            var button = new Button
            {
                Content = ring,
                ToolTip = name,
                Margin = new Thickness(0, 0, 8, 0),
                Cursor = Cursors.Hand,
                Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(
                    "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'><ContentPresenter/></ControlTemplate>"),
            };
            System.Windows.Automation.AutomationProperties.SetName(button, $"{name} highlight");
            button.Click += (_, _) => SetHighlightColor(hex);
            SwatchPanel.Children.Add(button);
            _swatchRings.Add((ring, hex));
        }
    }

    private void RefreshHighlights()
    {
        var hotkey = S.GetHotkey(HotkeyAction.HighlightLine).ToDisplayString();
        HighlightIntro.Text = $"Press {hotkey} to highlight the line the band is on, like a highlighter pen; press it again to remove it. Highlights stay as you move on, so you can see which lines are done.";
        foreach (var (ring, hex) in _swatchRings)
            ring.BorderBrush = string.Equals(hex, S.HighlightColor, StringComparison.OrdinalIgnoreCase)
                ? (Brush)FindResource("TextBrush") : Brushes.Transparent;
        if (!CustomColorBox.IsKeyboardFocused)
            CustomColorBox.Text = S.HighlightColor;
        CustomColorError.Text = "";
        try
        {
            HighlightPreview.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(S.HighlightColor)) { Opacity = S.HighlightOpacity };
        }
        catch (FormatException)
        {
            HighlightPreview.Background = Brushes.Transparent;
        }
        HighlightStrength.Value = S.HighlightOpacity;
        HighlightStrengthValue.Text = $"{Math.Round(S.HighlightOpacity * 100)}%";
        HighlightWholeLine.IsChecked = !S.HighlightValueOnly;
        HighlightValueOnly.IsChecked = S.HighlightValueOnly;
        HighlightTopLine.IsChecked = !S.HighlightAllLines;
        HighlightAllLines.IsChecked = S.HighlightAllLines;
        AutoHighlightSwitch.IsChecked = S.AutoHighlight;
        SaveToPdfSwitch.IsChecked = S.SaveHighlightsToPdf;

        var count = _main.HighlightCount;
        ClearHighlightsButton.IsEnabled = count > 0;
        HighlightCountText.Text = !_main.HasDocument ? "Open a document to see its highlights."
            : count == 0 ? "No highlights in this document."
            : $"{count} {(count == 1 ? "highlight" : "highlights")} in this document.";
    }

    private void SetHighlightColor(string hex)
    {
        S.HighlightColor = hex.ToUpperInvariant();
        _store.Save();
    }

    private void CustomColor_Click(object sender, RoutedEventArgs e) => ApplyCustomColor();

    private void CustomColorBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            ApplyCustomColor();
    }

    private void ApplyCustomColor()
    {
        var text = CustomColorBox.Text.Trim();
        if (!text.StartsWith('#'))
            text = "#" + text;
        if (!System.Text.RegularExpressions.Regex.IsMatch(text, "^#[0-9A-Fa-f]{6}$"))
        {
            CustomColorError.Text = "Use the form #RRGGBB, e.g. #3DDC84";
            return;
        }
        SetHighlightColor(text);
    }

    private void HighlightStrength_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading || HighlightStrengthValue == null)
            return;
        S.HighlightOpacity = Math.Round(e.NewValue, 2);
        _store.Save();
    }

    private void HighlightWhat_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading || HighlightValueOnly == null)
            return;
        S.HighlightValueOnly = HighlightValueOnly.IsChecked == true;
        _store.Save();
    }

    private void HighlightWhich_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading || HighlightAllLines == null)
            return;
        S.HighlightAllLines = HighlightAllLines.IsChecked == true;
        _store.Save();
    }

    private void AutoHighlightSwitch_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;
        S.AutoHighlight = AutoHighlightSwitch.IsChecked == true;
        _store.Save();
    }

    private void SaveToPdfSwitch_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;
        S.SaveHighlightsToPdf = SaveToPdfSwitch.IsChecked == true;
        _store.Save(); // the main window writes the open PDF's highlights right away
    }

    private void ClearHighlights_Click(object sender, RoutedEventArgs e)
    {
        var count = _main.HighlightCount;
        var inPdf = _main.DocumentIsPdf ? " Any of them saved inside the PDF are removed from it too." : "";
        if (MessageBox.Show(this, $"Remove all {count} highlights from this document?{inPdf}", "Clear highlights",
                MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            return;
        _main.ClearHighlights();
        RefreshHighlights();
    }

    // ───────────────────────────── window ─────────────────────────────

    private void TopmostSwitch_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;
        S.AlwaysOnTop = TopmostSwitch.IsChecked == true;
        _store.Save();
    }

    private void RememberSwitch_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;
        S.RememberPosition = RememberSwitch.IsChecked == true;
        if (!S.RememberPosition)
            S.Positions.Clear();
        _store.Save();
    }

    // ───────────────────────────── hotkeys ─────────────────────────────

    private static Grid HotkeyGrid(Thickness margin)
    {
        var grid = new Grid { Margin = margin };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        return grid;
    }

    private TextBox HotkeyBox(HotkeyAction action, bool second)
    {
        var box = new TextBox
        {
            IsReadOnly = true,
            IsReadOnlyCaretVisible = false,
            Cursor = Cursors.Hand,
            Height = 32,
            Padding = new Thickness(8, 0, 8, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            FontWeight = second ? FontWeights.Normal : FontWeights.SemiBold,
            ToolTip = second ? "Click, then press a second shortcut. Backspace removes it." : "Click, then press a new shortcut",
            Tag = (action, second),
        };
        System.Windows.Automation.AutomationProperties.SetName(box, Hotkey.Describe(action) + (second ? ", second shortcut" : ""));
        box.GotKeyboardFocus += HotkeyBox_GotFocus;
        box.LostKeyboardFocus += HotkeyBox_LostFocus;
        box.PreviewKeyDown += HotkeyBox_PreviewKeyDown;
        return box;
    }

    private void BuildHotkeyRows()
    {
        var header = HotkeyGrid(new Thickness(0, 4, 0, 2));
        foreach (var (text, column) in new[] { ("Shortcut", 1), ("Second shortcut", 3) })
        {
            var label = new TextBlock { Text = text, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center };
            label.SetResourceReference(TextBlock.ForegroundProperty, "SubtleTextBrush");
            Grid.SetColumn(label, column);
            header.Children.Add(label);
        }
        HotkeyRows.Children.Add(header);

        foreach (var action in Enum.GetValues<HotkeyAction>())
        {
            var grid = HotkeyGrid(new Thickness(0, 6, 0, 6));

            var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            labels.Children.Add(new TextBlock { Text = Hotkey.Describe(action), FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 12, 0) });
            var status = new TextBlock
            {
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(0, 2, 12, 0),
            };
            status.SetResourceReference(TextBlock.ForegroundProperty, "DangerBrush");
            labels.Children.Add(status);

            var box = HotkeyBox(action, second: false);
            var secondBox = HotkeyBox(action, second: true);
            Grid.SetColumn(box, 1);
            Grid.SetColumn(secondBox, 3);

            grid.Children.Add(labels);
            grid.Children.Add(box);
            grid.Children.Add(secondBox);
            HotkeyRows.Children.Add(grid);
            _hotkeyRows[action] = (box, secondBox, status);
        }
    }

    private void RefreshHotkeyRows()
    {
        var failed = _main.Hotkeys?.Failed ?? [];
        foreach (var (action, (box, secondBox, status)) in _hotkeyRows)
        {
            if (!box.IsKeyboardFocused)
                box.Text = S.GetHotkey(action).ToDisplayString();
            if (!secondBox.IsKeyboardFocused)
                secondBox.Text = S.GetSecondHotkey(action)?.ToDisplayString() ?? "—";
            var refused = failed.Where(f => f.Action == action).Select(f => f.Hotkey.ToDisplayString()).ToList();
            var isFailed = refused.Count > 0 && (S.ValueGrabEnabled || !Hotkey.IsValueGrabAction(action));
            status.Text = isFailed ? $"Windows wouldn't register {string.Join(" or ", refused)}. Another app may be using it." : "";
            status.Visibility = isFailed ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void HotkeyBox_GotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        // Release global hotkeys so the combination reaches this box instead of moving the band.
        _main.Hotkeys?.Suspend();
        var box = (TextBox)sender;
        box.Text = "Press keys…";
        box.BorderBrush = (Brush)FindResource("AccentBrush");
    }

    private void HotkeyBox_LostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        var box = (TextBox)sender;
        box.ClearValue(BorderBrushProperty);
        _main.Hotkeys?.Resume();
        RefreshHotkeyRows();
    }

    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var box = (TextBox)sender;
        var (action, second) = ((HotkeyAction, bool))box.Tag;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Escape)
        {
            NavHotkeys.Focus();
            return;
        }
        if (second && key is Key.Back or Key.Delete && Keyboard.Modifiers == ModifierKeys.None)
        {
            S.SetSecondHotkey(action, null);
            _store.Save();
            NavHotkeys.Focus();
            return;
        }

        var mods = Keyboard.Modifiers;
        if (Hotkey.IsModifierKey(key))
        {
            box.Text = new Hotkey(mods, Key.None).ToDisplayString().Replace("None", "…");
            return;
        }

        var hotkey = new Hotkey(mods, key);
        var (_, _, status) = _hotkeyRows[action];
        if (!hotkey.IsValidGlobal)
        {
            ShowRowError(status, "Include Ctrl or Alt so the shortcut doesn't block normal typing.");
            return;
        }

        if (key == Key.Delete && mods.HasFlag(ModifierKeys.Control) && mods.HasFlag(ModifierKeys.Alt))
        {
            ShowRowError(status, "Windows keeps Ctrl+Alt+Delete for itself (the number-pad period is the same key).");
            return;
        }

        var current = second ? S.GetSecondHotkey(action) : S.GetHotkey(action);
        var clash = S.AllBindings().FirstOrDefault(b => b.Hotkey == hotkey && !(b.Action == action && b.Hotkey == current));
        if (clash != default)
        {
            ShowRowError(status, clash.Action == action
                ? "That's already this action's other shortcut."
                : $"Already used for \"{Hotkey.Describe(clash.Action)}\".");
            return;
        }

        if (second)
            S.SetSecondHotkey(action, hotkey);
        else
            S.SetHotkey(action, hotkey);
        _store.Save();
        NavHotkeys.Focus();
    }

    private static void ShowRowError(TextBlock status, string message)
    {
        status.Text = message;
        status.Visibility = Visibility.Visible;
    }

    private void ResetHotkeys_Click(object sender, RoutedEventArgs e)
    {
        S.Hotkeys.Clear();
        S.SecondHotkeys.Clear();
        _store.Save();
    }
}

