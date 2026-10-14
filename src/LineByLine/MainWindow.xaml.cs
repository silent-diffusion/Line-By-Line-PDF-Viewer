using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using LineByLine.Pdf;
using LineByLine.Services;

namespace LineByLine;

public partial class MainWindow : Window
{
    private const double PtToDip = 96.0 / 72.0;
    private const double MaxFitPageWidth = 1000;   // keeps pages readable, not giant, on wide monitors
    private const double PageGap = 24;
    private const double BandPadX = 8, BandPadY = 3;  // in PDF points
    private const double ThumbWidth = 76;

    private sealed class PageView
    {
        public required PdfPage Page { get; init; }
        public required Border Frame { get; init; }
        public required Image Image { get; init; }
        public double RenderedScale { get; set; }
    }

    private readonly SettingsStore _store;
    private readonly List<PageView> _pages = [];
    private readonly List<Border> _thumbFrames = [];
    private readonly List<Button> _railButtons = [];
    private readonly DispatcherTimer _renderTimer;
    private readonly DispatcherTimer _toastTimer;
    private readonly DispatcherTimer _scrollTimer;

    private PdfDoc? _doc;
    private HotkeyManager? _hotkeys;
    private int _pageIndex, _rowIndex;
    private double _zoom = 1;
    private bool _fitWidth = true;
    private bool _dimEnabled = true;
    private bool _portrait;
    private int _renderGeneration;

    // band drag
    private bool _dragging;
    private Point _dragStart;
    private double _dragBandTop;

    // smooth scroll
    private double _scrollFrom, _scrollTo;
    private DateTime _scrollStart;

    private AppSettings S => _store.Settings;

    public MainWindow(SettingsStore store)
    {
        _store = store;
        InitializeComponent();

        _renderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _renderTimer.Tick += (_, _) => { _renderTimer.Stop(); _ = RenderPagesAsync(); };
        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); Toast.Visibility = Visibility.Collapsed; };
        _scrollTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(15) };
        _scrollTimer.Tick += ScrollTimer_Tick;

        RestoreWindowPlacement();
        _store.Changed += ApplySettings;
        ApplySettings();
    }

    // ───────────────────────────── lifecycle ─────────────────────────────

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hotkeys = new HotkeyManager(new WindowInteropHelper(this).Handle);
        _hotkeys.Pressed += OnHotkey;
        RegisterHotkeys();
    }

    protected override void OnClosed(EventArgs e)
    {
        SavePosition();
        SaveWindowPlacement();
        _store.Save(notify: false);
        _hotkeys?.Dispose();
        base.OnClosed(e);
    }

    private void RegisterHotkeys()
    {
        if (_hotkeys == null)
            return;
        _hotkeys.Apply(S.AllHotkeys());
        if (_hotkeys.Failed.Count > 0)
        {
            var names = string.Join(", ", _hotkeys.Failed.Select(a => S.GetHotkey(a).ToDisplayString()));
            ShowToast($"Couldn't register {names}. Another app may be using it. You can pick a different shortcut in Settings → Hotkeys.");
        }
    }

    /// <summary>Lets the settings window pause hotkeys while it records a new shortcut.</summary>
    public HotkeyManager? Hotkeys => _hotkeys;

    private void ApplySettings()
    {
        Topmost = S.AlwaysOnTop;
        PinToggle.IsChecked = S.AlwaysOnTop;
        PinIcon.Text = S.AlwaysOnTop ? "\uE840" : "\uE718"; // pinned / pin glyphs
        PinToggle.ToolTip = S.AlwaysOnTop ? "Always on top: on" : "Always on top: off";
        LinesText.Text = S.LinesInView.ToString();
        StepText.Text = S.EffectiveStep.ToString();

        var next = S.GetHotkey(HotkeyAction.NextLines).ToDisplayString();
        var prev = S.GetHotkey(HotkeyAction.PreviousLines).ToDisplayString();
        HudHints.Text = $"{next} next · {prev} back";
        EmptyHints.Text = $"{next} next line · {prev} previous · Ctrl+O open";

        RegisterHotkeys();
        ClampRow();
        UpdateOverlay();
    }

    // ───────────────────────────── opening files ─────────────────────────────

    public async void OpenFile(string path)
    {
        if (!File.Exists(path))
        {
            ShowToast($"File not found: {path}");
            return;
        }

        PdfDoc doc;
        try
        {
            doc = await Task.Run(() => PdfDoc.Open(path));
        }
        catch (Exception ex)
        {
            ShowToast($"Couldn't open {System.IO.Path.GetFileName(path)}. It may be damaged or password-protected. ({ex.Message})");
            return;
        }

        SavePosition();
        _doc = doc;
        _renderGeneration++;
        FileNameText.Text = System.IO.Path.GetFileName(path);
        FileNameText.Foreground = (Brush)FindResource("TextBrush");
        Title = $"{System.IO.Path.GetFileName(path)} · Line by Line";
        EmptyState.Visibility = Visibility.Collapsed;
        Hud.Visibility = Visibility.Visible;

        BuildPages();
        BuildThumbnails();

        _pageIndex = 0;
        _rowIndex = 0;
        if (S.RememberPosition && S.Positions.TryGetValue(SettingsStore.PositionKey(path), out var pos))
        {
            _pageIndex = Math.Clamp(pos.Page, 0, doc.Pages.Count - 1);
            _rowIndex = pos.Row;
        }
        ClampRow();

        _fitWidth = true;
        Scroller.ScrollToVerticalOffset(0);
        _ = Dispatcher.BeginInvoke(() =>
        {
            ApplyZoom(FitZoom());
            Dispatcher.BeginInvoke(() => EnsureBandVisible(animate: false), DispatcherPriority.Loaded);
        }, DispatcherPriority.Loaded);

        if (doc.Pages.All(p => p.RowsAreFallback))
            ShowToast("No text found in this PDF (it may be a scan), so the band steps in fixed-height lines. OCR support is coming in v0.3.");

        SavePosition();
    }

    private void BuildPages()
    {
        PagesPanel.Children.Clear();
        _pages.Clear();
        foreach (var page in _doc!.Pages)
        {
            var image = new Image { Stretch = Stretch.Fill };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            var frame = new Border
            {
                Background = Brushes.White,
                Margin = new Thickness(0, 0, 0, PageGap),
                Child = image,
                Cursor = Cursors.Hand,
                Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Direction = 270, Opacity = 0.14 },
            };
            var view = new PageView { Page = page, Frame = frame, Image = image };
            frame.MouseLeftButtonUp += (_, e) => PlaceBandAt(view, e.GetPosition(image));
            _pages.Add(view);
            PagesPanel.Children.Add(frame);
        }
    }

    private void BuildThumbnails()
    {
        ThumbPanel.Children.Clear();
        RailPages.Children.Clear();
        _thumbFrames.Clear();
        _railButtons.Clear();

        foreach (var page in _doc!.Pages)
        {
            var index = page.Index;
            var thumbHeight = ThumbWidth * page.Height / page.Width;

            var image = new Image { Width = ThumbWidth, Height = thumbHeight, Stretch = Stretch.Fill };
            var frame = new Border
            {
                Background = Brushes.White,
                Width = ThumbWidth,
                Height = thumbHeight,
                BorderThickness = new Thickness(2),
                BorderBrush = Brushes.Transparent,
                CornerRadius = new CornerRadius(3),
                Child = image,
                Effect = new DropShadowEffect { BlurRadius = 4, ShadowDepth = 1, Direction = 270, Opacity = 0.15 },
            };
            var label = new TextBlock { Text = (index + 1).ToString(), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
            var thumbButton = new Button
            {
                Style = (Style)FindResource("IconButton"),
                Width = 112,
                Height = double.NaN,
                Padding = new Thickness(0, 6, 0, 6),
                Margin = new Thickness(0, 0, 0, 10),
                Content = new StackPanel { Margin = new Thickness(0, 6, 0, 6), Children = { frame, label } },
                ToolTip = $"Page {index + 1}",
            };
            thumbButton.Click += (_, _) => GoToPage(index);
            ThumbPanel.Children.Add(thumbButton);
            _thumbFrames.Add(frame);

            var railButton = new Button
            {
                Style = (Style)FindResource("IconButton"),
                Width = 40,
                Height = 40,
                Margin = new Thickness(0, 0, 0, 6),
                Content = new TextBlock { Text = (index + 1).ToString(), FontWeight = FontWeights.SemiBold },
                ToolTip = $"Page {index + 1}",
            };
            railButton.Click += (_, _) => GoToPage(index);
            RailPages.Children.Add(railButton);
            _railButtons.Add(railButton);

            var doc = _doc;
            var generation = _renderGeneration;
            var scale = ThumbWidth / page.Width * DpiScale();
            Task.Run(() => doc.Render(index, scale)).ContinueWith(t =>
            {
                if (t.IsCompletedSuccessfully && generation == _renderGeneration)
                    image.Source = t.Result;
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }
        UpdatePageIndicators();
    }

    // ───────────────────────────── zoom & rendering ─────────────────────────────

    private double DpiScale() => VisualTreeHelper.GetDpi(this).DpiScaleX;

    private double FitZoom()
    {
        if (_doc == null || _doc.Pages.Count == 0)
            return 1;
        var available = Math.Max(200, Scroller.ActualWidth - 2 * PageGap - 12);
        var widest = _doc.Pages.Max(p => p.Width) * PtToDip;
        return Math.Clamp(Math.Min(available, MaxFitPageWidth) / widest, 0.25, 4);
    }

    private void ApplyZoom(double zoom)
    {
        // Resize now with the old bitmaps stretched; re-render sharp copies once zooming settles.
        _zoom = Math.Clamp(zoom, 0.25, 4);
        foreach (var view in _pages)
        {
            view.Image.Width = view.Page.Width * PtToDip * _zoom;
            view.Image.Height = view.Page.Height * PtToDip * _zoom;
        }
        _renderTimer.Stop();
        _renderTimer.Start();
        Dispatcher.BeginInvoke(() => { UpdateOverlay(); EnsureBandVisible(animate: false); }, DispatcherPriority.Loaded);
    }

    private async Task RenderPagesAsync()
    {
        if (_doc == null)
            return;
        var doc = _doc;
        var generation = _renderGeneration;
        var scale = _zoom * PtToDip * DpiScale();

        // Current page first so the page you're reading sharpens immediately.
        var order = _pages.OrderBy(p => Math.Abs(p.Page.Index - _pageIndex)).ToList();
        foreach (var view in order)
        {
            if (Math.Abs(view.RenderedScale - scale) < 0.01)
                continue;
            var index = view.Page.Index;
            try
            {
                var bmp = await Task.Run(() => doc.Render(index, scale));
                if (generation != _renderGeneration || Math.Abs(_zoom * PtToDip * DpiScale() - scale) > 0.001)
                    return; // a newer document or zoom superseded this pass
                view.Image.Source = bmp;
                view.RenderedScale = scale;
            }
            catch (Exception ex)
            {
                ShowToast($"Couldn't draw page {index + 1}: {ex.Message}");
            }
        }
    }

    private void ZoomBy(double factor)
    {
        if (_doc == null)
            return;
        _fitWidth = false;
        ApplyZoom(_zoom * factor);
        ShowToast($"Zoom {Math.Round(_zoom * 100)}% · Ctrl+0 to fit width", seconds: 1.5);
    }

    private void ZoomToFit()
    {
        if (_doc == null)
            return;
        _fitWidth = true;
        ApplyZoom(FitZoom());
    }

    // ───────────────────────────── band navigation ─────────────────────────────

    private IReadOnlyList<TextRow> Rows(int page) => _doc?.Pages[page].Rows ?? [];

    private void ClampRow()
    {
        if (_doc == null || _doc.Pages.Count == 0)
            return;
        _pageIndex = Math.Clamp(_pageIndex, 0, _doc.Pages.Count - 1);
        _rowIndex = Math.Clamp(_rowIndex, 0, Math.Max(0, Rows(_pageIndex).Count - 1));
    }

    private int NextPageWithRows(int from, int direction)
    {
        for (var p = from + direction; p >= 0 && p < _doc!.Pages.Count; p += direction)
            if (Rows(p).Count > 0)
                return p;
        return -1;
    }

    private void MoveNext()
    {
        if (_doc == null)
            return;
        var rows = Rows(_pageIndex);
        var next = _rowIndex + S.EffectiveStep;
        if (next < rows.Count)
        {
            _rowIndex = next;
        }
        else
        {
            var page = NextPageWithRows(_pageIndex, +1);
            if (page < 0)
            {
                ShowToast("End of document", seconds: 1.5);
                return;
            }
            _pageIndex = page;
            _rowIndex = 0;
        }
        BandMoved();
    }

    private void MovePrevious()
    {
        if (_doc == null)
            return;
        if (_rowIndex > 0)
        {
            _rowIndex = Math.Max(0, _rowIndex - S.EffectiveStep);
        }
        else
        {
            var page = NextPageWithRows(_pageIndex, -1);
            if (page < 0)
            {
                ShowToast("Start of document", seconds: 1.5);
                return;
            }
            _pageIndex = page;
            _rowIndex = Math.Max(0, Rows(page).Count - S.LinesInView);
        }
        BandMoved();
    }

    private void GoToPage(int page)
    {
        if (_doc == null || page < 0 || page >= _doc.Pages.Count)
            return;
        _pageIndex = page;
        _rowIndex = 0;
        BandMoved();
    }

    private void BandMoved()
    {
        ClampRow();
        UpdateOverlay();
        UpdatePageIndicators();
        EnsureBandVisible(animate: true);
        SavePosition();
    }

    private void PlaceBandAt(PageView view, Point pointInImage)
    {
        if (_dragging)
            return;
        var y = pointInImage.Y / (_zoom * PtToDip);
        var row = NearestRow(view.Page, y);
        if (row < 0)
            return;
        _pageIndex = view.Page.Index;
        _rowIndex = row;
        BandMoved();
    }

    private static int NearestRow(PdfPage page, double yPts)
    {
        var rows = page.Rows;
        if (rows.Count == 0)
            return -1;
        for (var i = 0; i < rows.Count; i++)
            if (yPts >= rows[i].Top - BandPadY && yPts <= rows[i].Bottom + BandPadY)
                return i;
        var best = 0;
        for (var i = 1; i < rows.Count; i++)
            if (Math.Abs(rows[i].Center - yPts) < Math.Abs(rows[best].Center - yPts))
                best = i;
        return best;
    }

    /// <summary>The band's rectangle in PDF points on the current page, or null when there is nothing to show.</summary>
    private Rect? BandRectPts()
    {
        if (_doc == null)
            return null;
        var page = _doc.Pages[_pageIndex];
        var rows = page.Rows;
        if (rows.Count == 0)
            return null;
        var first = Math.Clamp(_rowIndex, 0, rows.Count - 1);
        var last = Math.Min(rows.Count - 1, first + S.LinesInView - 1);
        var top = rows[first].Top;
        var bottom = rows[first].Bottom;
        for (var i = first; i <= last; i++)
        {
            top = Math.Min(top, rows[i].Top);
            bottom = Math.Max(bottom, rows[i].Bottom);
        }
        var left = Math.Max(0, page.ContentLeft - BandPadX);
        var right = Math.Min(page.Width, page.ContentRight + BandPadX);
        return new Rect(left, top - BandPadY, right - left, bottom - top + 2 * BandPadY);
    }

    /// <summary>The band in workspace coordinates.</summary>
    private Rect? BandRectInWorkspace()
    {
        if (BandRectPts() is not { } r || _pageIndex >= _pages.Count)
            return null;
        var image = _pages[_pageIndex].Image;
        if (!image.IsLoaded || image.ActualWidth <= 0)
            return null;
        var k = _zoom * PtToDip;
        var topLeft = image.TranslatePoint(new Point(r.Left * k, r.Top * k), Workspace);
        return new Rect(topLeft.X, topLeft.Y, r.Width * k, r.Height * k);
    }

    private void UpdateOverlay()
    {
        if (_dragging)
            return;
        var rect = BandRectInWorkspace();
        if (rect is not { } r)
        {
            Band.Visibility = Grip.Visibility = DimPath.Visibility = Visibility.Collapsed;
            UpdateHud();
            return;
        }
        PositionBand(r);
        UpdateHud();
    }

    private void PositionBand(Rect r)
    {
        Band.Visibility = Grip.Visibility = Visibility.Visible;
        Canvas.SetLeft(Band, r.Left);
        Canvas.SetTop(Band, r.Top);
        Band.Width = Math.Max(4, r.Width);
        Band.Height = Math.Max(4, r.Height);
        Canvas.SetLeft(Grip, r.Left - 22);
        Canvas.SetTop(Grip, r.Top + r.Height / 2 - 9);

        if (_dimEnabled && S.DimOpacity > 0)
        {
            var whole = new RectangleGeometry(new Rect(0, 0, Workspace.ActualWidth, Workspace.ActualHeight));
            var hole = new RectangleGeometry(r, 6, 6);
            DimPath.Data = new CombinedGeometry(GeometryCombineMode.Exclude, whole, hole);
            DimPath.Opacity = S.DimOpacity;
            DimPath.Visibility = Visibility.Visible;
        }
        else
        {
            DimPath.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateHud()
    {
        if (_doc == null)
            return;
        var rows = Rows(_pageIndex);
        if (rows.Count == 0)
        {
            HudLines.Text = $"No lines on page {_pageIndex + 1}";
            return;
        }
        var first = _rowIndex + 1;
        var last = Math.Min(rows.Count, _rowIndex + S.LinesInView);
        var range = last > first ? $"{first}–{last}" : first.ToString();
        var pageText = _doc.Pages.Count > 1 ? $"  ·  Page {_pageIndex + 1} of {_doc.Pages.Count}" : "";
        HudLines.Text = $"Lines {range} of {rows.Count}{pageText}";
    }

    private void UpdatePageIndicators()
    {
        var accent = (Brush)FindResource("AccentBrush");
        for (var i = 0; i < _thumbFrames.Count; i++)
            _thumbFrames[i].BorderBrush = i == _pageIndex ? accent : Brushes.Transparent;
        for (var i = 0; i < _railButtons.Count; i++)
        {
            _railButtons[i].Background = i == _pageIndex ? (Brush)FindResource("AccentSoftBrush") : Brushes.Transparent;
            _railButtons[i].Foreground = i == _pageIndex ? (Brush)FindResource("AccentDarkBrush") : (Brush)FindResource("MutedTextBrush");
        }
    }

    // ───────────────────────────── scrolling ─────────────────────────────

    private void EnsureBandVisible(bool animate)
    {
        if (BandRectPts() is not { } r || _pageIndex >= _pages.Count)
            return;
        var image = _pages[_pageIndex].Image;
        if (!image.IsLoaded)
            return;
        var k = _zoom * PtToDip;
        var top = image.TranslatePoint(new Point(0, r.Top * k), Scroller).Y;
        var bottom = top + r.Height * k;
        var viewport = Scroller.ViewportHeight;
        const double topMargin = 48, bottomMargin = 96; // keep clear of the HUD pill
        if (top >= topMargin && bottom <= viewport - bottomMargin)
            return;

        var target = Math.Clamp(Scroller.VerticalOffset + top - viewport * 0.3, 0, Scroller.ScrollableHeight);
        if (!animate)
        {
            _scrollTimer.Stop();
            Scroller.ScrollToVerticalOffset(target);
            return;
        }
        _scrollFrom = Scroller.VerticalOffset;
        _scrollTo = target;
        _scrollStart = DateTime.UtcNow;
        _scrollTimer.Start();
    }

    private void ScrollTimer_Tick(object? sender, EventArgs e)
    {
        const double durationMs = 170;
        var t = Math.Min(1, (DateTime.UtcNow - _scrollStart).TotalMilliseconds / durationMs);
        var eased = 1 - Math.Pow(1 - t, 3);
        Scroller.ScrollToVerticalOffset(_scrollFrom + (_scrollTo - _scrollFrom) * eased);
        if (t >= 1)
            _scrollTimer.Stop();
    }

    private void Scroller_ScrollChanged(object sender, ScrollChangedEventArgs e) => UpdateOverlay();

    private void Workspace_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_fitWidth && _doc != null)
            ApplyZoom(FitZoom());
        else
            UpdateOverlay();
    }

    // ───────────────────────────── band dragging ─────────────────────────────

    private void Band_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragging = true;
        _dragStart = e.GetPosition(Workspace);
        _dragBandTop = Canvas.GetTop(Band);
        Band.CaptureMouse();
        e.Handled = true;
    }

    private void Band_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging)
            return;
        var dy = e.GetPosition(Workspace).Y - _dragStart.Y;
        PositionBand(new Rect(Canvas.GetLeft(Band), _dragBandTop + dy, Band.Width, Band.Height));
    }

    private void Band_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging)
            return;
        _dragging = false;
        Band.ReleaseMouseCapture();
        e.Handled = true;

        var dy = e.GetPosition(Workspace).Y - _dragStart.Y;
        if (Math.Abs(dy) < 3)
        {
            UpdateOverlay();
            return;
        }

        // Snap: find the page under the band's first line and the nearest row there.
        var firstRowHalf = 0.0;
        var rows = Rows(_pageIndex);
        if (_rowIndex < rows.Count)
            firstRowHalf = (rows[_rowIndex].Height / 2 + BandPadY) * _zoom * PtToDip;
        var probe = new Point(Canvas.GetLeft(Band) + Band.Width / 2, _dragBandTop + dy + firstRowHalf);

        foreach (var view in _pages)
        {
            var p = Workspace.TranslatePoint(probe, view.Image);
            if (p.Y >= 0 && p.Y <= view.Image.ActualHeight)
            {
                var row = NearestRow(view.Page, p.Y / (_zoom * PtToDip));
                if (row >= 0)
                {
                    _pageIndex = view.Page.Index;
                    _rowIndex = row;
                }
                break;
            }
        }
        BandMoved();
    }

    private void Band_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            return; // handled by the window's zoom handler
        Scroller.ScrollToVerticalOffset(Scroller.VerticalOffset - e.Delta);
        e.Handled = true;
    }

    // ───────────────────────────── input ─────────────────────────────

    private void OnHotkey(HotkeyAction action)
    {
        switch (action)
        {
            case HotkeyAction.NextLines: MoveNext(); break;
            case HotkeyAction.PreviousLines: MovePrevious(); break;
            case HotkeyAction.NextPage: GoToPage(Math.Min(_pageIndex + 1, (_doc?.Pages.Count ?? 1) - 1)); break;
            case HotkeyAction.PreviousPage: GoToPage(Math.Max(_pageIndex - 1, 0)); break;
            case HotkeyAction.ToggleDim: ToggleDim(); break;
            case HotkeyAction.ToggleAlwaysOnTop: SetAlwaysOnTop(!S.AlwaysOnTop); break;
        }
    }

    private void ToggleDim()
    {
        _dimEnabled = !_dimEnabled;
        UpdateOverlay();
        ShowToast(_dimEnabled ? "Dimming on" : "Dimming off", seconds: 1.2);
    }

    private void SetAlwaysOnTop(bool value)
    {
        S.AlwaysOnTop = value;
        _store.Save();
        ShowToast(value ? "Always on top: on" : "Always on top: off", seconds: 1.2);
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is TextBox)
            return;
        var ctrl = Keyboard.Modifiers == ModifierKeys.Control;
        switch (e.Key)
        {
            case Key.O when ctrl: ShowOpenDialog(); break;
            case Key.D0 or Key.NumPad0 when ctrl: ZoomToFit(); break;
            case Key.OemPlus or Key.Add when ctrl: ZoomBy(1.1); break;
            case Key.OemMinus or Key.Subtract when ctrl: ZoomBy(1 / 1.1); break;
            case Key.Down or Key.J when Keyboard.Modifiers == ModifierKeys.None: MoveNext(); break;
            case Key.Up or Key.K when Keyboard.Modifiers == ModifierKeys.None: MovePrevious(); break;
            case Key.PageDown when Keyboard.Modifiers == ModifierKeys.None: GoToPage(_pageIndex + 1); break;
            case Key.PageUp when Keyboard.Modifiers == ModifierKeys.None: GoToPage(_pageIndex - 1); break;
            default: return;
        }
        e.Handled = true;
    }

    private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            return;
        ZoomBy(e.Delta > 0 ? 1.1 : 1 / 1.1);
        e.Handled = true;
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = TryGetDroppedPdf(e, out _) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (TryGetDroppedPdf(e, out var path))
            OpenFile(path);
    }

    private static bool TryGetDroppedPdf(DragEventArgs e, out string path)
    {
        path = "";
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files
            && files[0].EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            path = files[0];
            return true;
        }
        return false;
    }

    private void ShowOpenDialog()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "PDF files (*.pdf)|*.pdf",
            Title = "Open an invoice",
        };
        if (dialog.ShowDialog(this) == true)
            OpenFile(dialog.FileName);
    }

    // ───────────────────────────── toolbar ─────────────────────────────

    private void OpenButton_Click(object sender, RoutedEventArgs e) => ShowOpenDialog();
    private void PrevButton_Click(object sender, RoutedEventArgs e) => MovePrevious();
    private void NextButton_Click(object sender, RoutedEventArgs e) => MoveNext();
    private void PinToggle_Click(object sender, RoutedEventArgs e) => SetAlwaysOnTop(PinToggle.IsChecked == true);

    private void LinesMinus_Click(object sender, RoutedEventArgs e) => ChangeLines(-1);
    private void LinesPlus_Click(object sender, RoutedEventArgs e) => ChangeLines(+1);
    private void StepMinus_Click(object sender, RoutedEventArgs e) => ChangeStep(-1);
    private void StepPlus_Click(object sender, RoutedEventArgs e) => ChangeStep(+1);

    private void ChangeLines(int delta)
    {
        S.LinesInView = Math.Clamp(S.LinesInView + delta, 1, 10);
        _store.Save();
    }

    private void ChangeStep(int delta)
    {
        S.StepSize = Math.Clamp(S.EffectiveStep + delta, 1, 10);
        _store.Save();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var existing = OwnedWindows.OfType<SettingsWindow>().FirstOrDefault();
        if (existing != null)
        {
            existing.Activate();
            return;
        }
        new SettingsWindow(_store, this) { Owner = this }.Show();
    }

    private void ThumbsToggle_Click(object sender, RoutedEventArgs e) => UpdateLayoutMode();

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // ───────────────────────────── layout: landscape vs portrait ─────────────────────────────

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var portrait = ActualHeight > ActualWidth;
        if (portrait != _portrait || e.PreviousSize == default)
        {
            _portrait = portrait;
            UpdateLayoutMode();
        }
    }

    /// <summary>
    /// Landscape shows the thumbnail panel. Portrait keeps the width for the page: a narrow
    /// rail of page numbers, with thumbnails available from the rail's toggle.
    /// </summary>
    private void UpdateLayoutMode()
    {
        Rail.Visibility = _portrait ? Visibility.Visible : Visibility.Collapsed;
        ThumbScroller.Visibility = !_portrait || ThumbsToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        // A maximized chrome-less window hangs over the screen edge by the resize border.
        Root.Margin = WindowState == WindowState.Maximized ? MaximizedInset() : new Thickness(0);
        MaxIcon.Text = WindowState == WindowState.Maximized ? "\uE923" : "\uE922"; // restore / maximize glyphs
        MaxButton.ToolTip = WindowState == WindowState.Maximized ? "Restore" : "Maximize";
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    private Thickness MaximizedInset()
    {
        const int smCxFrame = 32, smCxPaddedBorder = 92;
        var px = GetSystemMetrics(smCxFrame) + GetSystemMetrics(smCxPaddedBorder);
        var dip = px / DpiScale();
        return new Thickness(dip);
    }

    // ───────────────────────────── persistence ─────────────────────────────

    private void SavePosition()
    {
        if (_doc == null || !S.RememberPosition)
            return;
        S.Positions[SettingsStore.PositionKey(_doc.FilePath)] = new FilePosition
        {
            Page = _pageIndex,
            Row = _rowIndex,
            LastOpened = DateTime.UtcNow,
        };
    }

    private void SaveWindowPlacement()
    {
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        S.Window = new WindowPlacement
        {
            Left = bounds.Left,
            Top = bounds.Top,
            Width = bounds.Width,
            Height = bounds.Height,
            Maximized = WindowState == WindowState.Maximized,
        };
    }

    private void RestoreWindowPlacement()
    {
        if (S.Window is not { } w || w.Width < MinWidth || w.Height < MinHeight)
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            return;
        }
        // Only restore if the window would still be on a connected monitor.
        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        if (!screen.IntersectsWith(new Rect(w.Left + 40, w.Top + 10, Math.Max(1, w.Width - 80), 30)))
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            return;
        }
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = w.Left;
        Top = w.Top;
        Width = w.Width;
        Height = w.Height;
        if (w.Maximized)
            WindowState = WindowState.Maximized;
    }

    // ───────────────────────────── toast ─────────────────────────────

    private void ShowToast(string message, double seconds = 5)
    {
        ToastText.Text = message;
        Toast.Visibility = Visibility.Visible;
        _toastTimer.Stop();
        _toastTimer.Interval = TimeSpan.FromSeconds(seconds);
        _toastTimer.Start();
    }
}
