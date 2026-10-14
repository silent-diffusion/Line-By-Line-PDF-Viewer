using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LineByLine.Services;

namespace LineByLine.Tests;

/// <summary>
/// Opens the real main window off-screen and saves screenshots, for eyeballing layout changes.
/// Runs only when LBL_SCREENSHOT_DIR is set, e.g.:
///   $env:LBL_SCREENSHOT_DIR="C:\temp\shots"; dotnet test --filter Visual
/// </summary>
public class VisualSmokeTests
{
    private static readonly string SamplePath = Path.Combine(AppContext.BaseDirectory, "samples", "sample-invoice.pdf");

    [Fact]
    [Trait("Category", "Visual")]
    public void Visual_LandscapeAndPortraitScreenshots()
    {
        var outDir = Environment.GetEnvironmentVariable("LBL_SCREENSHOT_DIR");
        if (string.IsNullOrEmpty(outDir))
            return;
        Directory.CreateDirectory(outDir);

        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Run(outDir);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null)
            throw failure;
    }

    private static void Run(string outDir)
    {
        // App.Run() normally installs this; without it, awaits resume on the thread pool.
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var app = new App();
        app.InitializeComponent();

        var store = new SettingsStore(Path.Combine(outDir, "settings.json"));
        var window = new MainWindow(store)
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20000,
            Top = 0,
            Width = 1280,
            Height = 800,
            ShowActivated = false,
        };
        window.Show();
        Pump(500);
        Save(window, Path.Combine(outDir, "0-empty.png"));

        window.OpenFile(SamplePath);
        Pump(2000);
        Invoke(window, "MoveNext");
        Invoke(window, "MoveNext");
        Invoke(window, "MoveNext");
        Pump(800);
        Save(window, Path.Combine(outDir, "1-landscape.png"));

        window.Width = 1080;
        window.Height = 1872;
        Pump(1500);
        Save(window, Path.Combine(outDir, "2-portrait.png"));

        var settings = new SettingsWindow(store, window) { Left = -20000, Top = 0, ShowActivated = false };
        settings.Show();
        Pump(400);
        Save(settings, Path.Combine(outDir, "3-settings.png"));
        settings.Close();

        window.Close();
    }

    private static void Invoke(object target, string method) =>
        target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, null);

    private static void Pump(int ms)
    {
        var until = DateTime.UtcNow.AddMilliseconds(ms);
        while (DateTime.UtcNow < until)
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, () => frame.Continue = false);
            Dispatcher.PushFrame(frame);
            Thread.Sleep(15);
        }
    }

    private static void Save(Window window, string path)
    {
        var root = (FrameworkElement)window.Content;
        var bmp = new RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        encoder.Save(fs);
    }
}
