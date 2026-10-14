using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace LineByLine.Documents;

/// <summary>
/// Reads text lines from page images with the OCR engine built into Windows 10/11.
/// It runs offline and uses the recognition languages installed with Windows.
/// </summary>
public static class OcrReader
{
    /// <summary>
    /// Windows OCR sometimes drops short, isolated words (a quantity alone in its column), and
    /// which ones it drops changes with resolution. Reading at a few resolutions and merging the
    /// words recovers almost all of them.
    /// </summary>
    public static readonly IReadOnlyList<double> DefaultPassDpis = [250, 150, 300];

    // Two words are the same word when their boxes overlap by this much of the smaller box.
    private const double SameWordOverlap = 0.3;

    private static readonly Lazy<OcrEngine?> Engine = new(CreateEngine);

    /// <summary>The engine's language, e.g. "English (United States)", or null when OCR isn't available.</summary>
    public static string? LanguageName => Engine.Value?.RecognizerLanguage.DisplayName;

    public static bool IsAvailable => Engine.Value != null;

    private static OcrEngine? CreateEngine()
    {
        try
        {
            return OcrEngine.TryCreateFromUserProfileLanguages()
                   ?? OcrEngine.TryCreateFromLanguage(new Language("en-US"));
        }
        catch (Exception)
        {
            return null; // e.g. OCR components missing on a stripped-down Windows install
        }
    }

    /// <summary>
    /// Reads a page's lines, in PDF points like embedded PDF text. Call from a background thread:
    /// it renders the page several times.
    /// </summary>
    public static async Task<List<TextRow>> ReadPageAsync(Document doc, int pageIndex, IReadOnlyList<double>? passDpis = null)
    {
        var page = doc.Pages[pageIndex];
        var merged = new List<LineDetector.Glyph>();
        foreach (var dpi in passDpis ?? DefaultPassDpis)
        {
            var scale = ScaleFor(page, dpi);
            var words = await RecognizeWordsAsync(doc.Render(pageIndex, scale), scale);
            merged.AddRange(words.Where(w => !merged.Any(m => SameWord(m, w))));
        }

        // Windows OCR splits a table row into several "lines" when columns are far apart, so
        // regroup its words with the same line detector the PDF text uses.
        return LineDetector.Detect(merged);
    }

    /// <summary>Pixels per point for a pass at <paramref name="dpi"/>, kept within the engine's size limit.</summary>
    private static double ScaleFor(DocumentPage page, double dpi) =>
        Math.Min(dpi / 72, OcrEngine.MaxImageDimension / Math.Max(page.Width, page.Height));

    private static bool SameWord(LineDetector.Glyph a, LineDetector.Glyph b)
    {
        var w = Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left);
        var h = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top);
        if (w <= 0 || h <= 0)
            return false;
        var smaller = Math.Min((a.Right - a.Left) * a.Height, (b.Right - b.Left) * b.Height);
        return w * h >= SameWordOverlap * smaller;
    }

    /// <summary>One OCR pass. <paramref name="scale"/> is the image's pixels per PDF point.</summary>
    private static async Task<List<LineDetector.Glyph>> RecognizeWordsAsync(BitmapSource image, double scale)
    {
        var engine = Engine.Value ?? throw new InvalidOperationException("Windows OCR isn't available on this PC.");

        var bgra = image.Format == PixelFormats.Bgra32 ? image : new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var stride = bgra.PixelWidth * 4;
        var pixels = new byte[stride * bgra.PixelHeight];
        bgra.CopyPixels(pixels, stride, 0);

        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(
            pixels.AsBuffer(), BitmapPixelFormat.Bgra8, bgra.PixelWidth, bgra.PixelHeight, BitmapAlphaMode.Premultiplied);
        var result = await engine.RecognizeAsync(bitmap);

        return result.Lines
            .SelectMany(line => line.Words)
            .Select(w => new LineDetector.Glyph(
                w.Text,
                w.BoundingRect.X / scale,
                w.BoundingRect.Y / scale,
                (w.BoundingRect.X + w.BoundingRect.Width) / scale,
                (w.BoundingRect.Y + w.BoundingRect.Height) / scale,
                AfterSpace: true))
            .ToList();
    }
}
