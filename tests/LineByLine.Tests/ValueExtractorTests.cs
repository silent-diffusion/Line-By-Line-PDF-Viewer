using System.IO;
using LineByLine.Documents;

namespace LineByLine.Tests;

public class ValueExtractorTests
{
    private static readonly PdfDoc Doc = PdfDoc.Open(Path.Combine(AppContext.BaseDirectory, "samples", "sample-invoice.pdf"));

    private static TextRow RowWith(int page, string text) => Doc.Pages[page].Rows.First(r => r.Text.Contains(text));

    [Theory]
    [InlineData("425.00", true)]
    [InlineData("1,825.59", true)]
    [InlineData("$1,825.59", true)]
    [InlineData("$ 9.99", true)]
    [InlineData("(45.00)", true)]
    [InlineData("-12.50", true)]
    [InlineData("120.00 CR", true)]
    [InlineData("$1,825", true)]
    [InlineData("10", false)]          // quantity
    [InlineData("PA-2011", false)]     // SKU
    [InlineData("10/02/2026", false)]  // date
    [InlineData("7%", false)]
    [InlineData("8.5", false)]         // a size, not money
    public void RecognizesMoney(string text, bool expected) => Assert.Equal(expected, ValueExtractor.IsMoney(text));

    [Theory]
    [InlineData("$1,825.59", "1825.59")]
    [InlineData("(45.00)", "-45.00")]
    [InlineData("−12.50", "-12.50")]
    [InlineData("120.00 CR", "-120.00")]
    public void NormalizesForExcel(string money, string expected) => Assert.Equal(expected, ValueExtractor.Normalize(money));

    [Fact]
    public void Amount_IsTheLineTotal_NotTheUnitPrice()
    {
        var value = ValueExtractor.Amount(RowWith(0, "TN-0458"));

        Assert.NotNull(value);
        Assert.Equal("359.96", value.Display);
        Assert.Equal("359.96", value.Clipboard);
    }

    [Fact]
    public void Amount_FindsTotalsWithLabels()
    {
        var total = ValueExtractor.Amount(RowWith(1, "Total due"));

        Assert.NotNull(total);
        Assert.StartsWith("$", total.Display);
        Assert.DoesNotContain(",", total.Clipboard);
        Assert.DoesNotContain("$", total.Clipboard);
    }

    [Fact]
    public void Amount_RespectsColumnFocus()
    {
        // Focused on the Unit column only (Unit right edge ≈ 476): the unit price, not the amount.
        var value = ValueExtractor.Amount(RowWith(0, "TN-0458"), 420, 482);
        Assert.Equal("89.99", value?.Clipboard);
    }

    [Fact]
    public void Amount_IsNullForLinesWithoutAValue()
    {
        Assert.Null(ValueExtractor.Amount(RowWith(0, "BILL TO")));
        // "500 labels per roll" has a number but no money: only the strict pick skips it.
        Assert.Null(ValueExtractor.Amount(RowWith(0, "500 labels per roll"), pick: ValuePick.RightmostMoney));
        Assert.Equal("500", ValueExtractor.Amount(RowWith(0, "500 labels per roll"))?.Clipboard);
    }

    private static TextRow OcrLine(params (double Left, double Right, string Text)[] cells) =>
        new(cells[0].Left, 100, cells[^1].Right, 110, string.Join(" ", cells.Select(c => c.Text)))
        {
            Cells = cells.Select(c => TextCell.Create(c.Left, c.Right, c.Text)).ToList(),
        };

    [Theory]
    [InlineData("425. 00", "425.00")]     // stray space after the point
    [InlineData("1, 825.59", "1825.59")]  // stray space after the comma
    [InlineData("$ 1,825.59", "1825.59")] // currency sign split off
    [InlineData("1O5.5O", "105.50")]      // letter O read for zero
    [InlineData("l2.00", "12.00")]        // letter l read for one
    [InlineData("425.00.", "425.00")]     // trailing speck
    [InlineData("120.00 CR", "-120.00")]
    public void Amount_RepairsOcrNumbers(string ocrAmount, string expected)
    {
        // Qty 4, unit 89.99 in the middle, mangled total on the right: the total must win.
        var line = OcrLine((54, 200, "Toner cartridge"), (395, 400, "4"), (447, 475, "89.99"), (520, 560, ocrAmount));

        Assert.Equal(expected, ValueExtractor.Amount(line)?.Clipboard);
        Assert.Equal(expected, ValueExtractor.Amount(line, pick: ValuePick.RightmostMoney)?.Clipboard);
    }

    [Fact]
    public void RightmostValue_TakesTheRightmostNumber_EvenWithoutCents()
    {
        // A total OCR'd as "1825" (decimal point lost) is still the rightmost value; the strict
        // money pick would fall back to the unit price in the middle.
        var line = OcrLine((54, 200, "Widget"), (395, 400, "2"), (447, 475, "912.50"), (520, 560, "1825"));

        Assert.Equal("1825", ValueExtractor.Amount(line)?.Clipboard);
        Assert.Equal("912.50", ValueExtractor.Amount(line, pick: ValuePick.RightmostMoney)?.Clipboard);
    }

    [Theory]
    [InlineData("7%")]
    [InlineData("10/02/2026")]
    [InlineData("PA-2011")]
    [InlineData("INV-20418")]
    public void RightmostValue_SkipsPercentagesDatesAndCodes(string rightmost)
    {
        var line = OcrLine((54, 200, "Item"), (447, 475, "89.99"), (520, 560, rightmost));
        Assert.Equal("89.99", ValueExtractor.Amount(line)?.Clipboard);
    }

    [Fact]
    public void Row_IsTabSeparatedCells()
    {
        var value = ValueExtractor.Row(RowWith(0, "PA-2011"));

        Assert.Equal("PA-2011\tCopy paper, 8.5 x 11, 20 lb (case)\t10\t42.50\t425.00", value?.Clipboard);
    }
}
