using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Docnet.Core;
using Docnet.Core.Converters;
using Docnet.Core.Models;

namespace LineByLine.Pdf;

/// <summary>One text line on a page, in PDF points (1/72 in) measured from the page's top-left.</summary>
public sealed record TextRow(double Left, double Top, double Right, double Bottom, string Text)
{
    public double Height => Bottom - Top;
    public double Center => (Top + Bottom) / 2;
}

public sealed class PdfPage
{
    public required int Index { get; init; }
    public required double Width { get; init; }
    public required double Height { get; init; }
    public required IReadOnlyList<TextRow> Rows { get; init; }

    /// <summary>True when the page had no extractable text and rows are evenly spaced stripes.</summary>
    public required bool RowsAreFallback { get; init; }

    public double ContentLeft => Rows.Count == 0 ? 0 : Rows.Min(r => r.Left);
    public double ContentRight => Rows.Count == 0 ? Width : Rows.Max(r => r.Right);
}

/// <summary>
/// A loaded PDF. PDFium is not thread-safe, so every native call goes through one lock.
/// </summary>
public sealed class PdfDoc
{
    // Characters are extracted at this scale because PDFium's char boxes are integers.
    private const double TextScale = 4.0;
    private const double FallbackRowHeight = 18.0;
    private static readonly object Gate = new();

    private readonly byte[] _bytes;

    public string FilePath { get; }
    public IReadOnlyList<PdfPage> Pages { get; }

    private PdfDoc(string path, byte[] bytes, IReadOnlyList<PdfPage> pages)
    {
        FilePath = path;
        _bytes = bytes;
        Pages = pages;
    }

    public static PdfDoc Open(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var pages = new List<PdfPage>();
        lock (Gate)
        {
            using var reader = DocLib.Instance.GetDocReader(bytes, new PageDimensions(TextScale));
            var count = reader.GetPageCount();
            for (var i = 0; i < count; i++)
            {
                using var page = reader.GetPageReader(i);
                var width = page.GetPageWidth() / TextScale;
                var height = page.GetPageHeight() / TextScale;
                var rows = LineDetector.Detect(page.GetCharacters(), TextScale);
                var fallback = rows.Count == 0;
                if (fallback)
                    rows = FallbackRows(width, height);
                pages.Add(new PdfPage { Index = i, Width = width, Height = height, Rows = rows, RowsAreFallback = fallback });
            }
        }
        return new PdfDoc(path, bytes, pages);
    }

    /// <summary>Renders a page to a frozen bitmap. <paramref name="scale"/> is pixels per PDF point.</summary>
    public BitmapSource Render(int pageIndex, double scale)
    {
        lock (Gate)
        {
            using var reader = DocLib.Instance.GetDocReader(_bytes, new PageDimensions(scale));
            using var page = reader.GetPageReader(pageIndex);
            var w = page.GetPageWidth();
            var h = page.GetPageHeight();
            var raw = page.GetImage(new NaiveTransparencyRemover(255, 255, 255), RenderFlags.RenderAnnotations);
            var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, raw, w * 4);
            bmp.Freeze();
            return bmp;
        }
    }

    private static List<TextRow> FallbackRows(double width, double height)
    {
        var rows = new List<TextRow>();
        const double margin = 36;
        for (var y = margin; y + FallbackRowHeight <= height - margin; y += FallbackRowHeight)
            rows.Add(new TextRow(margin, y, width - margin, y + FallbackRowHeight, ""));
        return rows;
    }
}
