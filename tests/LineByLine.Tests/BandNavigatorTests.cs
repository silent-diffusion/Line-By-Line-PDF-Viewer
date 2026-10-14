using System.IO;
using LineByLine.Documents;

namespace LineByLine.Tests;

public class BandNavigatorTests
{
    private static readonly PdfDoc Doc = PdfDoc.Open(Path.Combine(AppContext.BaseDirectory, "samples", "sample-invoice.pdf"));

    // Sample invoice columns (PDF points): Qty right edge 400, Unit 476, Amount 558.
    private const double QtyToAmountLeft = 370, QtyToAmountRight = 566;

    private static int RowOf(int page, string text) =>
        Doc.Pages[page].Rows.ToList().FindIndex(r => r.Text.Contains(text));

    private static string BandText(BandNavigator nav) =>
        Doc.Pages[nav.Page].Rows[nav.Row].Text;

    [Fact]
    public void Cells_SplitAtColumnGaps_AndKnowNumbers()
    {
        var row = Doc.Pages[0].Rows[RowOf(0, "PA-2011")];

        Assert.Equal(["PA-2011", "Copy paper, 8.5 x 11, 20 lb (case)", "10", "42.50", "425.00"], row.Cells.Select(c => c.Text));
        Assert.Equal([false, false, true, true, true], row.Cells.Select(c => c.IsNumeric));
    }

    [Fact]
    public void WithoutColumnFocus_EveryLineIsAStop()
    {
        var nav = new BandNavigator(Doc) { LinesInView = 1, Step = 1 };
        nav.PlaceAt(0, RowOf(0, "LB-4600"));

        Assert.True(nav.Next());
        Assert.Contains("500 labels per roll", BandText(nav)); // the wrapped description line
    }

    [Fact]
    public void ColumnFocus_SkipsWrappedDescriptionsAndHeaders()
    {
        var nav = new BandNavigator(Doc) { LinesInView = 1, Step = 1 };
        nav.PlaceAt(0, RowOf(0, "PA-2011"));
        nav.SetSpan(QtyToAmountLeft, QtyToAmountRight);

        var stops = nav.Stops(0).Select(i => Doc.Pages[0].Rows[i].Text).ToList();
        Assert.Equal(18, stops.Count); // the 18 items on page 1, nothing else
        Assert.DoesNotContain(stops, t => t.Contains("500 labels per roll"));
        Assert.DoesNotContain(stops, t => t.Contains("QTY"));

        nav.PlaceAt(0, RowOf(0, "LB-4600"));
        Assert.True(nav.Next());
        Assert.Contains("TP-0200", BandText(nav)); // jumped over the wrapped line
    }

    [Fact]
    public void ColumnFocus_ContinuesIntoTheSameTableOnTheNextPage()
    {
        var nav = new BandNavigator(Doc) { LinesInView = 1, Step = 1 };
        nav.PlaceAt(0, RowOf(0, "SN-HD08")); // last item on page 1
        nav.SetSpan(QtyToAmountLeft, QtyToAmountRight);

        Assert.True(nav.Next());
        Assert.Equal(1, nav.Page);
        Assert.Contains("BT-AA24", BandText(nav)); // first item on page 2, not its header

        var page2 = nav.Stops(1).Select(i => Doc.Pages[1].Rows[i].Text).ToList();
        Assert.Equal(10, page2.Count);
        Assert.DoesNotContain(page2, t => t.Contains("Subtotal"));
    }

    [Fact]
    public void StepAndLinesInView_CountStopsNotRawLines()
    {
        var nav = new BandNavigator(Doc) { LinesInView = 2, Step = 2 };
        nav.PlaceAt(0, RowOf(0, "TN-0458"));
        nav.SetSpan(QtyToAmountLeft, QtyToAmountRight);

        // Band covers TN-0458 and LB-4600 (2 stops), then steps 2 stops to TP-0200.
        var band = nav.BandRows()!.Value;
        Assert.Contains("LB-4600", Doc.Pages[0].Rows[band.Last].Text);
        Assert.Equal((2, 3, 18), nav.Progress());

        Assert.True(nav.Next());
        Assert.Contains("TP-0200", BandText(nav));

        Assert.True(nav.Previous());
        Assert.Contains("TN-0458", BandText(nav));
    }

    [Fact]
    public void ClearingFocus_RestoresEveryLine()
    {
        var nav = new BandNavigator(Doc);
        nav.PlaceAt(0, RowOf(0, "PA-2011"));
        nav.SetSpan(QtyToAmountLeft, QtyToAmountRight);
        nav.ClearSpan();

        Assert.False(nav.HasColumnFocus);
        Assert.Equal(Doc.Pages[0].Rows.Count, nav.Stops(0).Count);
    }

    [Fact]
    public void SnapEdge_PullsToNearbyColumnEdges()
    {
        var nav = new BandNavigator(Doc);
        nav.PlaceAt(0, RowOf(0, "PA-2011"));

        // A rough drag near the Amount column lands exactly on a cell's right edge (+ band padding).
        var snapped = nav.SnapEdge(563, isLeftEdge: false);
        var cellEdges = Doc.Pages[0].Rows.SelectMany(r => r.Cells).Select(c => c.Right + BandNavigator.PadX);
        Assert.Contains(cellEdges, e => Math.Abs(e - snapped) < 0.001);
        Assert.InRange(snapped, 551, 575);

        Assert.Equal(20, nav.SnapEdge(20, isLeftEdge: false), 0); // nothing near the page margin: unchanged
    }

    [Fact]
    public void EndsOfDocument_ReportFalse()
    {
        var nav = new BandNavigator(Doc) { LinesInView = 1, Step = 1 };
        nav.PlaceAt(0, 0);
        Assert.False(nav.Previous());

        nav.PlaceAt(1, Doc.Pages[1].Rows.Count - 1);
        Assert.False(nav.Next());
    }
}
