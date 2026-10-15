using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Docnet.Core;
using Docnet.Core.Converters;
using Docnet.Core.Models;
using LineByLine.Documents;

namespace LineByLine.Tests;

public class HighlightTests
{
    private static readonly string SamplePdf = Path.Combine(AppContext.BaseDirectory, "samples", "sample-invoice.pdf");

    private static LineMark Mark(int page, double top) => new(page, 50, top, 560, top + 12);

    [Fact]
    public void Toggle_AddsThenRemoves()
    {
        var set = new HighlightSet();

        Assert.True(set.Toggle([Mark(0, 100)]));
        Assert.Single(set.All);
        Assert.False(set.Toggle([Mark(0, 100.5)])); // same spot, slightly off: still a removal
        Assert.Empty(set.All);
    }

    [Fact]
    public void Toggle_WithSeveralLines_AddsMissingUntilAllAreMarked()
    {
        var set = new HighlightSet([Mark(0, 100)]);

        Assert.True(set.Toggle([Mark(0, 100), Mark(0, 120)])); // one was missing: add it
        Assert.Equal(2, set.All.Count);
        Assert.False(set.Toggle([Mark(0, 100), Mark(0, 120)])); // both marked: remove both
        Assert.Empty(set.All);
    }

    [Fact]
    public void Add_NeverDuplicatesOrRemoves()
    {
        var set = new HighlightSet([Mark(0, 100)]);

        Assert.False(set.Add([Mark(0, 100)]));
        Assert.True(set.Add([Mark(1, 100)])); // same spot on another page is a different mark
        Assert.Equal(2, set.All.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public void PageFrame_RoundTripsForEveryRotation(int rotate)
    {
        var frame = new PdfHighlights.PageFrame(10, 20, 622, 812, rotate);
        var pdf = frame.ToPdf(123.4, 56.7);
        var back = frame.ToPoints(pdf.X, pdf.Y);

        Assert.Equal(123.4, back.X, 6);
        Assert.Equal(56.7, back.Y, 6);
    }

    [Fact]
    public void PageFrame_TopLeftOfAnUnrotatedPageIsTheBoxTop()
    {
        var frame = new PdfHighlights.PageFrame(0, 0, 612, 792, 0);
        Assert.Equal((0.0, 792.0), frame.ToPdf(0, 0));
    }

    [Fact]
    public void SavedHighlights_ShowInOtherViewers_AndComeBackIntoTheApp()
    {
        var copy = Path.Combine(Path.GetTempPath(), $"linebyline-highlight-{Guid.NewGuid():N}.pdf");
        File.Copy(SamplePdf, copy);
        try
        {
            var row = PdfDoc.Open(copy).Pages[0].Rows.First(r => r.Text.Contains("TN-0458"));
            var mark = new LineMark(0, row.Left - 2, row.Top - 1.5, row.Right + 2, row.Bottom + 1.5);
            var green = Color.FromRgb(0x3D, 0xDC, 0x84);

            PdfHighlights.Write(copy, [mark], green, 0.4);

            // PDFium (the engine in Chrome and Edge) draws the highlight from the file itself.
            var probe = (X: (int)((row.Left + 40) * 2), Y: (int)(row.Center * 2));
            var other = PixelAt(RenderRaw(File.ReadAllBytes(copy), 2), probe);
            Assert.True(other.G > other.R + 30 && other.G > other.B + 30, $"expected green in other viewers, got {other}");

            // Reopening in Line by Line: the highlight comes back as a mark, and isn't also baked into the page.
            var reopened = PdfDoc.Open(copy);
            var back = Assert.Single(reopened.EmbeddedHighlights);
            Assert.True(back.SameSpotAs(mark), $"{back} vs {mark}");
            Assert.Equal(mark.Left, back.Left, 1);
            Assert.Equal(mark.Bottom, back.Bottom, 1);
            var inApp = PixelAt(reopened.Render(0, 2), probe);
            Assert.True(Math.Abs(inApp.G - inApp.R) < 20, $"highlight should be stripped from the app's render, got {inApp}");

            // Clearing writes the file back without our annotations.
            PdfHighlights.Write(copy, [], green, 0.4);
            Assert.False(PdfHighlights.ContainsOurs(File.ReadAllBytes(copy)));
            Assert.Empty(PdfDoc.Open(copy).EmbeddedHighlights);
        }
        finally
        {
            File.Delete(copy);
        }
    }

    [Fact]
    public void ValueOnly_KnowsWhereTheValueSits()
    {
        var row = PdfDoc.Open(SamplePdf).Pages[0].Rows.First(r => r.Text.Contains("TN-0458"));
        var amountCell = row.Cells[^1];
        var value = ValueExtractor.Amount(row);

        Assert.Equal(amountCell.Left, value!.Left!.Value, 3);
        Assert.Equal(amountCell.Right, value.Right!.Value, 3);
    }

    private static BitmapSource RenderRaw(byte[] pdf, double scale)
    {
        using var reader = DocLib.Instance.GetDocReader(pdf, new PageDimensions(scale));
        using var page = reader.GetPageReader(0);
        var w = page.GetPageWidth();
        var h = page.GetPageHeight();
        var raw = page.GetImage(new NaiveTransparencyRemover(255, 255, 255), RenderFlags.RenderAnnotations);
        return BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, raw, w * 4);
    }

    private static Color PixelAt(BitmapSource bmp, (int X, int Y) p)
    {
        var px = new byte[4];
        bmp.CopyPixels(new System.Windows.Int32Rect(p.X, p.Y, 1, 1), px, 4, 0);
        return Color.FromRgb(px[2], px[1], px[0]);
    }
}
