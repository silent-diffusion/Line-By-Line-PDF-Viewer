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
    private const double ThumbWidth = 76;
    private const double EdgeGrab = 8;             // px inside the band edge that start a resize

    private sealed class PageView
    {
        public required PdfPage Page { get; init; }
        public required Border Frame { get; init; }
        public required Image Image { get; init; }
        public double RenderedScale { get; set; }
    }

    private enum DragMode { None, Move, Left, Right, Top, Bottom }

    private readonly SettingsStore _store;
    private readonly List<PageView> _pages = [];
    private readonly List<Border> _thumbFrames = [];
    private readonly List<Button> _railButtons = [];
    private readonly DispatcherTimer _renderTimer;
    private readonly DispatcherTimer _toastTimer;
    private readonly DispatcherTimer _scrollTimer;

    private PdfDoc? _doc;
    private BandNavigator? _nav;
    private HotkeyManager? _hotkeys;
    private double _zoom = 1;
    private bool _fitWidth = true;
    private bool _dimEnabled = true;
    private bool _portrait;
    private int _renderGeneration;

    // band drag / resize
    private DragMode _drag;
    private Point _dragStart;
    private Rect _dragOrigin;
    private bool _bandHovered;

    // smooth scroll
    private double _scrollFrom, _scrollTo;
    private DateTime _scrollStart;

    private AppSettings S => _store.Settings;
    private int PageIndex => _nav?.Page ?? 0;
    private double Scale => _zoom * PtToDip;

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
        PinIcon.Text = S.AlwaysOnTop ? "" : ""; // pinned / pin glyphs
        PinToggle.ToolTip = S.AlwaysOnTop ? "Always on top: on" : "Always on top: off";
        LinesText.Text = S.LinesInView.ToString();
        StepText.Text = S.EffectiveStep.ToString();

        var next = S.GetHotkey(HotkeyAction.NextLines).ToDisplayString();
        var prev = S.GetHotkey(HotkeyAction.PreviousLines).ToDisplayString();
        HudHints.Text = $"{next} next · {prev} back";
        EmptyHints.Text = $"{next} next line · {prev} previous · Ctrl+O open";

        if (_nav != null)
        {
            _nav.LinesInView = S.LinesInView;
            _nav.Step = S.EffectiveStep;
        }

        RegisterHotkeys();
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
        _nav = new BandNavigator(doc) { LinesInView = S.LinesInView, Step = S.EffectiveStep };
        _renderGeneration++;
        FileNameText.Text = System.IO.Path.GetFileName(path);
        FileNameText.Foreground = (Brush)FindResource("TextBrush");
        Title = $"{System.IO.Path.GetFileName(path)} · Line by Line";
        EmptyState.Visibility = Visibility.Collapsed;
        Hud.Visibility = Visibility.Visible;

        BuildPages();
        BuildThumbnails();

        if (S.RememberPosition && S.Positions.TryGetValue(SettingsStore.PositionKey(path), out var pos))
        {
            _nav.PlaceAt(pos.Page, pos.Row);
            if (pos.SpanLeft is { } left && pos.SpanRight is { } right)
                _nav.SetSpan(left, right);
        }
        else
        {
            _nav.PlaceAt(0, 0);
        }

        _fitWidth = true;
        Scroller.ScrollToVerticalOffset(0);
        _ = Dispatcher.BeginInvoke(() =>
        {
            ApplyZoom(FitZoom());
            Dispatcher.BeginInvoke(() => EnsureBandVisible(animate: false), DispatcherPriority.Loaded);
        }, DispatcherPriority.Loaded);

        if (doc.Pages.All(p => p.RowsAreFallback))
            ShowToast("No text found in this PDF (it may be a scan), so the band steps in fixed-height lines. OCR support is coming in v0.3.");

        UpdatePageIndicators();
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
            view.Image.Width = view.Page.Width * Scale;
            view.Image.Height = view.Page.Height * Scale;
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
        var scale = Scale * DpiScale();

        // Current page first so the page you're reading sharpens immediately.
        var order = _pages.OrderBy(p => Math.Abs(p.Page.Index - PageIndex)).ToList();
        foreach (var view in order)
        {
            if (Math.Abs(view.RenderedScale - scale) < 0.01)
                continue;
            var index = view.Page.Index;
            try
            {
                var bmp = await Task.Run(() => doc.Render(index, scale));
                if (generation != _renderGeneration || Math.Abs(Scale * DpiScale() - scale) > 0.001)
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

    private void MoveNext()
    {
        if (_nav == null)
            return;
        if (!_nav.Next())
        {
            ShowToast("End of document", seconds: 1.5);
            return;
        }
        BandMoved();
    }

    private void MovePrevious()
    {
        if (_nav == null)
            return;
        if (!_nav.Previous())
        {
            ShowToast("Start of document", seconds: 1.5);
            return;
        }
        BandMoved();
    }

    private void GoToPage(int page)
    {
        if (_nav == null || page < 0 || page >= _nav.Document.Pages.Count)
            return;
        _nav.GoToPage(page);
        BandMoved();
    }

    private void BandMoved()
    {
        UpdateOverlay();
        UpdatePageIndicators();
        EnsureBandVisible(animate: true);
        SavePosition();
    }

    private void PlaceBandAt(PageView view, Point pointInImage)
    {
        if (_nav == null || _drag != DragMode.None)
            return;
        var row = BandNavigator.NearestRow(view.Page, pointInImage.Y / Scale);
        if (row < 0)
            return;
        _nav.PlaceAt(view.Page.Index, row);
        BandMoved();
    }

    private void SetColumnFocus(double left, double right)
    {
        if (_nav == null)
            return;
        if (_nav.IsFullWidth(left + BandNavigator.PadX, right - BandNavigator.PadX))
        {
            ClearColumnFocus();
            return;
        }
        var hadFocus = _nav.HasColumnFocus;
        _nav.SetSpan(left, right);
        if (!hadFocus)
            ShowToast("Column focus on: the band now stops only on lines shaped like this one. Double-click the band or press Esc to go back to full width.", seconds: 6);
        BandMoved();
    }

    private void ClearColumnFocus()
    {
        if (_nav is not { HasColumnFocus: true })
            return;
        _nav.ClearSpan();
        BandMoved();
    }

    /// <summary>Converts a rectangle in PDF points on the current page to workspace coordinates.</summary>
    private Rect? ToWorkspace(Rect pts)
    {
        if (PageIndex >= _pages.Count)
            return null;
        var image = _pages[PageIndex].Image;
        if (!image.IsLoaded || image.ActualWidth <= 0)
            return null;
        var topLeft = image.TranslatePoint(new Point(pts.Left * Scale, pts.Top * Scale), Workspace);
        return new Rect(topLeft.X, topLeft.Y, pts.Width * Scale, pts.Height * Scale);
    }

    /// <summary>Converts a workspace point to PDF points on the current page.</summary>
    private Point ToPagePoints(Point workspacePoint)
    {
        var p = Workspace.TranslatePoint(workspacePoint, _pages[PageIndex].Image);
        return new Point(p.X / Scale, p.Y / Scale);
    }

    private void UpdateOverlay()
    {
        if (_drag != DragMode.None)
            return;
        if (_nav?.BandRect() is not { } pts || ToWorkspace(pts) is not { } r)
        {
            Band.Visibility = Grip.Visibility = DimPath.Visibility = Visibility.Collapsed;
            SetHandlesVisible(false);
            Region.Visibility = RegionLabel.Visibility = Visibility.Collapsed;
            UpdateHud();
            return;
        }
        PositionBand(r);
        PositionRegion();
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

        PlaceHandle(HandleLeft, r.Left, r.Top + r.Height / 2);
        PlaceHandle(HandleRight, r.Right, r.Top + r.Height / 2);
        PlaceHandle(HandleTop, r.Left + r.Width / 2, r.Top);
        PlaceHandle(HandleBottom, r.Left + r.Width / 2, r.Bottom);
        SetHandlesVisible(_bandHovered || _drag != DragMode.None);

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

    private static void PlaceHandle(FrameworkElement handle, double centerX, double centerY)
    {
        Canvas.SetLeft(handle, centerX - handle.Width / 2);
        Canvas.SetTop(handle, centerY - handle.Height / 2);
    }

    private void SetHandlesVisible(bool visible)
    {
        var v = visible ? Visibility.Visible : Visibility.Collapsed;
        HandleLeft.Visibility = HandleRight.Visibility = HandleTop.Visibility = HandleBottom.Visibility = v;
    }

    /// <summary>The dashed outline around every line the band will stop on while column focus is on.</summary>
    private void PositionRegion()
    {
        if (_nav?.RegionRect() is not { } pts || ToWorkspace(pts) is not { } r)
        {
            Region.Visibility = RegionLabel.Visibility = Visibility.Collapsed;
            return;
        }
        Region.Visibility = RegionLabel.Visibility = Visibility.Visible;
        Canvas.SetLeft(Region, r.Left);
        Canvas.SetTop(Region, r.Top);
        Region.Width = r.Width;
        Region.Height = r.Height;
        var stops = _nav.Stops(PageIndex).Count;
        RegionLabelText.Text = $"Column focus · {stops} {(stops == 1 ? "line" : "lines")} on this page";
        Canvas.SetLeft(RegionLabel, r.Left + 8);
        Canvas.SetTop(RegionLabel, r.Top - 11);
    }

    private void UpdateHud()
    {
        if (_nav == null)
            return;
        ColumnChip.Visibility = _nav.HasColumnFocus ? Visibility.Visible : Visibility.Collapsed;
        if (_nav.Progress() is not { } p)
        {
            HudLines.Text = $"No lines on page {PageIndex + 1}";
            return;
        }
        var range = p.To > p.From ? $"{p.From}–{p.To}" : p.From.ToString();
        var pages = _nav.Document.Pages.Count;
        var pageText = pages > 1 ? $"  ·  Page {PageIndex + 1} of {pages}" : "";
        HudLines.Text = $"Lines {range} of {p.Of}{pageText}";
    }

    private void UpdatePageIndicators()
    {
        var accent = (Brush)FindResource("AccentBrush");
        for (var i = 0; i < _thumbFrames.Count; i++)
            _thumbFrames[i].BorderBrush = i == PageIndex ? accent : Brushes.Transparent;
        for (var i = 0; i < _railButtons.Count; i++)
        {
            _railButtons[i].Background = i == PageIndex ? (Brush)FindResource("AccentSoftBrush") : Brushes.Transparent;
            _railButtons[i].Foreground = i == PageIndex ? (Brush)FindResource("AccentDarkBrush") : (Brush)FindResource("MutedTextBrush");
        }
    }

    // ───────────────────────────── scrolling ─────────────────────────────

    private void EnsureBandVisible(bool animate)
    {
        if (_nav?.BandRect() is not { } r || PageIndex >= _pages.Count)
            return;
        var image = _pages[PageIndex].Image;
        if (!image.IsLoaded)
            return;
        var top = image.TranslatePoint(new Point(0, r.Top * Scale), Scroller).Y;
        var bottom = top + r.Height * Scale;
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

    // ───────────────────────────── band drag & resize ─────────────────────────────

    /// <summary>Which edge (if any) the pointer is on. Side edges win over top/bottom on thin bands.</summary>
    private DragMode HitZone(Point inBand)
    {
        if (inBand.X <= EdgeGrab) return DragMode.Left;
        if (inBand.X >= Band.ActualWidth - EdgeGrab) return DragMode.Right;
        var edgeY = Math.Min(EdgeGrab, Band.ActualHeight / 4);
        if (inBand.Y <= edgeY) return DragMode.Top;
        if (inBand.Y >= Band.ActualHeight - edgeY) return DragMode.Bottom;
        return DragMode.Move;
    }

    private static Cursor CursorFor(DragMode mode) => mode switch
    {
        DragMode.Left or DragMode.Right => Cursors.SizeWE,
        DragMode.Top or DragMode.Bottom => Cursors.SizeNS,
        _ => Cursors.SizeAll,
    };

    private void Band_MouseEnter(object sender, MouseEventArgs e)
    {
        _bandHovered = true;
        SetHandlesVisible(true);
    }

    private void Band_MouseLeave(object sender, MouseEventArgs e)
    {
        _bandHovered = false;
        if (_drag == DragMode.None)
            SetHandlesVisible(false);
    }

    private void Band_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (e.ClickCount == 2)
        {
            ClearColumnFocus();
            return;
        }
        _drag = HitZone(e.GetPosition(Band));
        _dragStart = e.GetPosition(Workspace);
        _dragOrigin = new Rect(Canvas.GetLeft(Band), Canvas.GetTop(Band), Band.Width, Band.Height);
        Band.CaptureMouse();
    }

    private void Band_MouseMove(object sender, MouseEventArgs e)
    {
        if (_drag == DragMode.None)
        {
            Band.Cursor = CursorFor(HitZone(e.GetPosition(Band)));
            return;
        }
        var d = e.GetPosition(Workspace) - _dragStart;
        var o = _dragOrigin;
        const double min = 16;
        var r = _drag switch
        {
            DragMode.Move => new Rect(o.Left, o.Top + d.Y, o.Width, o.Height),
            DragMode.Left => new Rect(Math.Min(o.Left + d.X, o.Right - min), o.Top, Math.Max(min, o.Width - d.X), o.Height),
            DragMode.Right => new Rect(o.Left, o.Top, Math.Max(min, o.Width + d.X), o.Height),
            DragMode.Top => new Rect(o.Left, Math.Min(o.Top + d.Y, o.Bottom - min), o.Width, Math.Max(min, o.Height - d.Y)),
            DragMode.Bottom => new Rect(o.Left, o.Top, o.Width, Math.Max(min, o.Height + d.Y)),
            _ => o,
        };
        PositionBand(r);
    }

    private void Band_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_drag == DragMode.None)
            return;
        var mode = _drag;
        var moved = e.GetPosition(Workspace) - _dragStart;
        _drag = DragMode.None;
        Band.ReleaseMouseCapture();
        e.Handled = true;

        if (_nav == null || Math.Abs(moved.X) + Math.Abs(moved.Y) < 3)
        {
            UpdateOverlay();
            return;
        }

        var r = new Rect(Canvas.GetLeft(Band), Canvas.GetTop(Band), Band.Width, Band.Height);
        switch (mode)
        {
            case DragMode.Move:
                FinishMove(r);
                break;
            case DragMode.Left:
            case DragMode.Right:
                FinishWidth(r, mode);
                break;
            case DragMode.Top:
                FinishTop(r);
                break;
            case DragMode.Bottom:
                FinishBottom(r);
                break;
        }
    }

    private void FinishMove(Rect r)
    {
        // Snap to the line under the band's first row, on whichever page that is.
        var firstRowHalf = 0.0;
        if (_nav!.BandRows() is { } band)
            firstRowHalf = (_nav.Document.Pages[PageIndex].Rows[band.First].Height / 2 + BandNavigator.PadY) * Scale;
        var probe = new Point(r.Left + r.Width / 2, r.Top + firstRowHalf);

        foreach (var view in _pages)
        {
            var p = Workspace.TranslatePoint(probe, view.Image);
            if (p.Y < 0 || p.Y > view.Image.ActualHeight)
                continue;
            var row = BandNavigator.NearestRow(view.Page, p.Y / Scale);
            if (row >= 0)
                _nav.PlaceAt(view.Page.Index, row); // keeps any column focus; this line becomes the reference shape
            break;
        }
        BandMoved();
    }

    private void FinishWidth(Rect r, DragMode mode)
    {
        var left = ToPagePoints(new Point(r.Left, r.Top)).X;
        var right = ToPagePoints(new Point(r.Right, r.Top)).X;
        if (mode == DragMode.Left)
            left = _nav!.SnapEdge(left, isLeftEdge: true);
        else
            right = _nav!.SnapEdge(right, isLeftEdge: false);
        SetColumnFocus(left, right);
    }

    private void FinishTop(Rect r)
    {
        var nav = _nav!;
        if (nav.BandRows() is not { } band)
            return;
        var top = ToPagePoints(new Point(r.Left, r.Top)).Y;
        var newFirst = nav.FirstStopBelow(top);
        if (newFirst < 0 || newFirst > band.Last)
            newFirst = band.Last;
        var stops = nav.Stops(PageIndex);
        var lines = stops.Count(i => i >= newFirst && i <= band.Last);
        nav.PlaceAt(PageIndex, newFirst);
        SetLines(Math.Max(1, lines));
    }

    private void FinishBottom(Rect r)
    {
        var bottom = ToPagePoints(new Point(r.Left, r.Bottom)).Y;
        SetLines(Math.Max(1, _nav!.StopsAbove(bottom)));
    }

    private void SetLines(int lines)
    {
        S.LinesInView = Math.Clamp(lines, 1, 10);
        _store.Save(); // ApplySettings pushes the new size into the navigator and redraws
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
            case HotkeyAction.NextPage: GoToPage(PageIndex + 1); break;
            case HotkeyAction.PreviousPage: GoToPage(PageIndex - 1); break;
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
        var none = Keyboard.Modifiers == ModifierKeys.None;
        switch (e.Key)
        {
            case Key.O when ctrl: ShowOpenDialog(); break;
            case Key.D0 or Key.NumPad0 when ctrl: ZoomToFit(); break;
            case Key.OemPlus or Key.Add when ctrl: ZoomBy(1.1); break;
            case Key.OemMinus or Key.Subtract when ctrl: ZoomBy(1 / 1.1); break;
            case Key.Down or Key.J when none: MoveNext(); break;
            case Key.Up or Key.K when none: MovePrevious(); break;
            case Key.PageDown when none: GoToPage(PageIndex + 1); break;
            case Key.PageUp when none: GoToPage(PageIndex - 1); break;
            case Key.Escape when none && _nav is { HasColumnFocus: true }: ClearColumnFocus(); break;
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
    private void ColumnChip_Click(object sender, RoutedEventArgs e) => ClearColumnFocus();
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
        MaxIcon.Text = WindowState == WindowState.Maximized ? "" : ""; // restore / maximize glyphs
        MaxButton.ToolTip = WindowState == WindowState.Maximized ? "Restore" : "Maximize";
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    private Thickness MaximizedInset()
    {
        const int smCxFrame = 32, smCxPaddedBorder = 92;
        var px = GetSystemMetrics(smCxFrame) + GetSystemMetrics(smCxPaddedBorder);
        return new Thickness(px / DpiScale());
    }

    // ───────────────────────────── persistence ─────────────────────────────

    private void SavePosition()
    {
        if (_nav == null || _doc == null || !S.RememberPosition)
            return;
        S.Positions[SettingsStore.PositionKey(_doc.FilePath)] = new FilePosition
        {
            Page = _nav.Page,
            Row = _nav.Row,
            SpanLeft = _nav.SpanLeft,
            SpanRight = _nav.SpanRight,
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
