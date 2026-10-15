using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Docnet.Core;
using Docnet.Core.Converters;
using Docnet.Core.Models;

namespace LineByLine.Documents;

/// <summary>
/// A loaded PDF. PDFium is not thread-safe, so every native call goes through one lock.
/// Pages without embedded text (scans) start as fallback stripes until OCR fills them in.
/// </summary>
public sealed class PdfDoc : Document
{
    // Characters are extracted at this scale because PDFium's char boxes are integers.
    private const double TextScale = 4.0;
    private static readonly object Gate = new();

    private readonly byte[] _bytes;

    private PdfDoc(string path, byte[] bytes, IReadOnlyList<DocumentPage> pages, IReadOnlyList<LineMark> highlights)
        : base(path, pages)
    {
        _bytes = bytes;
        EmbeddedHighlights = highlights;
    }

    public static PdfDoc Open(string path)
    {
        var bytes = File.ReadAllBytes(path);

        // Highlights this app saved into the file come out of the page and into the app, which
        // draws (and toggles) them itself; otherwise they'd show twice and couldn't be removed.
        List<LineMark> highlights = [];
        if (PdfHighlights.ContainsOurs(bytes))
        {
            try
            {
                (bytes, highlights) = PdfHighlights.Extract(bytes);
            }
            catch (Exception)
            {
                // Unreadable for PDFsharp (e.g. unusual encryption): show the file as it is.
            }
        }

        var pages = new List<DocumentPage>();
        lock (Gate)
        {
            using var reader = DocLib.Instance.GetDocReader(bytes, new PageDimensions(TextScale));
            var count = reader.GetPageCount();
            for (var i = 0; i < count; i++)
            {
                using var page = reader.GetPageReader(i);
                var docPage = new DocumentPage
                {
                    Index = i,
                    Width = page.GetPageWidth() / TextScale,
                    Height = page.GetPageHeight() / TextScale,
                };
                docPage.SetRows(LineDetector.Detect(page.GetCharacters(), TextScale), RowSource.Text);
                pages.Add(docPage);
            }
        }
        return new PdfDoc(path, bytes, pages, highlights);
    }

    public override BitmapSource Render(int pageIndex, double scale)
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
}
