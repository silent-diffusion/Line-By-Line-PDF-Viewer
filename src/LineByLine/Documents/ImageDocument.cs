using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LineByLine.Documents;

/// <summary>
/// An image file opened as a document: one page per frame (multi-page TIFFs have several).
/// Page sizes come from the image's DPI so a 300 DPI letter scan is 612 × 792 points, like the PDF.
/// Lines start as fallback stripes until OCR reads them.
/// </summary>
public sealed class ImageDocument : Document
{
    private readonly IReadOnlyList<BitmapSource> _frames;

    private ImageDocument(string path, IReadOnlyList<DocumentPage> pages, IReadOnlyList<BitmapSource> frames) : base(path, pages)
    {
        _frames = frames;
    }

    public static ImageDocument Open(string path)
    {
        // Load fully into memory so the file isn't kept locked while it's open here.
        using var stream = new MemoryStream(File.ReadAllBytes(path));
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);

        var frames = new List<BitmapSource>();
        var pages = new List<DocumentPage>();
        foreach (var frame in decoder.Frames)
        {
            // Copy into a plain BGRA pixel buffer. Decoder frames stay tied to the thread that
            // decoded them, but OCR renders pages on a background thread.
            var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            var stride = converted.PixelWidth * 4;
            var pixels = new byte[stride * converted.PixelHeight];
            converted.CopyPixels(pixels, stride, 0);
            var bgra = BitmapSource.Create(converted.PixelWidth, converted.PixelHeight, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
            bgra.Freeze();
            frames.Add(bgra);

            var dpiX = frame.DpiX > 1 ? frame.DpiX : 96;
            var dpiY = frame.DpiY > 1 ? frame.DpiY : 96;
            var page = new DocumentPage
            {
                Index = pages.Count,
                Width = frame.PixelWidth / dpiX * 72,
                Height = frame.PixelHeight / dpiY * 72,
            };
            page.SetRows([], RowSource.Fallback);
            pages.Add(page);
        }
        return new ImageDocument(path, pages, frames);
    }

    public override BitmapSource Render(int pageIndex, double scale)
    {
        var frame = _frames[pageIndex];
        var page = Pages[pageIndex];
        var sx = page.Width * scale / frame.PixelWidth;
        var sy = page.Height * scale / frame.PixelHeight;
        if (Math.Abs(sx - 1) < 0.01 && Math.Abs(sy - 1) < 0.01)
            return frame;
        var scaled = new TransformedBitmap(frame, new ScaleTransform(sx, sy));
        scaled.Freeze();
        return scaled;
    }
}
