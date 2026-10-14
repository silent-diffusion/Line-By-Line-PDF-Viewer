using System.IO;
using System.Windows.Media.Imaging;
using LineByLine.Documents;

namespace LineByLine.Tests;

/// <summary>
/// Turns the sample PDF into a "scan" (a PNG with no text layer), then reads it back with
/// Windows OCR. Skipped automatically on a PC without an OCR language installed.
/// </summary>
public class OcrTests
{
    private static readonly string SamplePdf = Path.Combine(AppContext.BaseDirectory, "samples", "sample-invoice.pdf");

    /// <summary>Writes page 1 of the sample as a 200 DPI PNG and returns its path.</summary>
    internal static string MakeScan()
    {
        var path = Path.Combine(Path.GetTempPath(), "linebyline-sample-scan.png");
        var pdf = PdfDoc.Open(SamplePdf);
        var bmp = pdf.Render(0, 200.0 / 72);
        var withDpi = BitmapSource.Create(bmp.PixelWidth, bmp.PixelHeight, 200, 200, bmp.Format, null,
            CopyPixels(bmp), bmp.PixelWidth * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(withDpi));
        using var fs = File.Create(path);
        encoder.Save(fs);
        return path;
    }

    private static byte[] CopyPixels(BitmapSource bmp)
    {
        var pixels = new byte[bmp.PixelWidth * 4 * bmp.PixelHeight];
        bmp.CopyPixels(pixels, bmp.PixelWidth * 4, 0);
        return pixels;
    }

    [Fact]
    public void ImageDocument_UsesDpiForPageSize_AndStartsWithoutText()
    {
        var doc = (ImageDocument)Document.Load(MakeScan());

        Assert.Single(doc.Pages);
        Assert.Equal(612, doc.Pages[0].Width, 0);
        Assert.Equal(792, doc.Pages[0].Height, 0);
        Assert.Equal(RowSource.Fallback, doc.Pages[0].Source);
    }

    [Fact]
    public async Task Ocr_FindsLineItemsAsWholeRows()
    {
        if (!OcrReader.IsAvailable)
            return;

        var doc = Document.Load(MakeScan());
        var page = doc.Pages[0];
        var rows = await OcrReader.ReadPageAsync(doc, 0);
        var dump = string.Join(" | ", rows.Select(r => r.Text));

        // Each item comes back as one line holding its description and amount, even though
        // Windows OCR reports far-apart columns as separate lines.
        var toner = rows.FirstOrDefault(r => r.Text.Contains("Toner"));
        Assert.True(toner != null, dump);
        Assert.Contains("359.96", toner.Text);
        Assert.True(toner.Cells.Count >= 3, $"expected separate cells: {string.Join(" / ", toner.Cells.Select(c => c.Text))}");
        Assert.Contains(toner.Cells, c => c.IsNumeric && c.Text.Contains("359.96"));

        // Rows land where the text is: same coordinates as the PDF's own text layer.
        var pdfToner = PdfDoc.Open(SamplePdf).Pages[0].Rows.First(r => r.Text.Contains("Toner"));
        Assert.InRange(toner.Center, pdfToner.Center - 3, pdfToner.Center + 3);

        Assert.Equal(18, rows.Count(r => r.Cells.Count(c => c.IsNumeric) >= 3)); // every item row, qty + unit + amount
    }

    [Fact]
    public async Task ColumnFocus_WorksOnOcrText()
    {
        if (!OcrReader.IsAvailable)
            return;

        var doc = Document.Load(MakeScan());
        var page = doc.Pages[0];
        page.SetRows(await OcrReader.ReadPageAsync(doc, 0), RowSource.Ocr);

        var nav = new BandNavigator(doc) { LinesInView = 1, Step = 1 };
        nav.PlaceAt(0, page.Rows.ToList().FindIndex(r => r.Text.Contains("Shipping labels")));
        nav.SetSpan(370, 566);

        string Cells(string text) => string.Join(" / ", page.Rows.First(r => r.Text.Contains(text)).Cells
            .Select(c => $"{c.Text}@{c.Left:F0}-{c.Right:F0}{(c.IsNumeric ? "#" : "")}"));
        var debug = $"labels: {Cells("Shipping labels")} || tape: {Cells("Packing tape")}";

        Assert.True(nav.Next());
        Assert.True(page.Rows[nav.Row].Text.Contains("Packing tape"), debug); // skipped the wrapped "500 labels" line
    }
}
