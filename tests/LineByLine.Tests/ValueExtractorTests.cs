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
    public void Amount_IsNullForLinesWithoutMoney()
    {
        Assert.Null(ValueExtractor.Amount(RowWith(0, "500 labels per roll")));
        Assert.Null(ValueExtractor.Amount(RowWith(0, "BILL TO")));
    }

    [Fact]
    public void Row_IsTabSeparatedCells()
    {
        var value = ValueExtractor.Row(RowWith(0, "PA-2011"));

        Assert.Equal("PA-2011\tCopy paper, 8.5 x 11, 20 lb (case)\t10\t42.50\t425.00", value?.Clipboard);
    }
}
