using System.Windows;

namespace LineByLine.Documents;

/// <summary>
/// Where the focus band is and how it moves. Without column focus every text line is a stop.
/// With column focus (the band narrowed to some columns) only lines shaped like the one the
/// band was placed on are stops: e.g. narrow it to Qty→Amount on an item line and it skips
/// wrapped descriptions, headers and notes, carrying on into the same table on the next page.
/// All coordinates are PDF points.
/// </summary>
public sealed class BandNavigator(Document doc)
{
    public const double PadX = 8, PadY = 3;

    // A line belongs to the focused table when at least this share of the anchor cells
    // (the reference line's cells inside the band) have a matching cell in the same place.
    private const double MatchShare = 0.6;
    private const double AnchorTolerance = 2;

    private IReadOnlyList<TextCell> _anchors = [];
    private readonly Dictionary<int, IReadOnlyList<int>> _stops = new();

    public Document Document { get; } = doc;
    public int Page { get; private set; }

    /// <summary>Index into the page's rows of the band's first line.</summary>
    public int Row { get; private set; }

    public int LinesInView { get; set; } = 2;
    public int Step { get; set; } = 2;

    public double? SpanLeft { get; private set; }
    public double? SpanRight { get; private set; }
    public bool HasColumnFocus => SpanLeft.HasValue && SpanRight.HasValue;

    private DocumentPage CurrentPage => Document.Pages[Page];

    /// <summary>Row indices on <paramref name="page"/> that the band can stop on.</summary>
    public IReadOnlyList<int> Stops(int page)
    {
        if (_stops.TryGetValue(page, out var cached))
            return cached;

        var p = Document.Pages[page];
        IReadOnlyList<int> stops;
        if (!HasColumnFocus || p.RowsAreFallback)
            stops = Enumerable.Range(0, p.Rows.Count).ToList();
        else
            stops = Enumerable.Range(0, p.Rows.Count).Where(i => Matches(p.Rows[i])).ToList();
        _stops[page] = stops;
        return stops;
    }

    private bool Matches(TextRow row)
    {
        var (left, right) = (SpanLeft!.Value, SpanRight!.Value);
        if (_anchors.Count == 0)
            return row.Cells.Any(c => c.Overlaps(left, right));

        var hits = _anchors.Count(a => row.Cells.Any(c =>
            c.IsNumeric == a.IsNumeric && c.Overlaps(a.Left, a.Right, AnchorTolerance)));
        return hits >= Math.Ceiling(MatchShare * _anchors.Count);
    }

    private void InvalidateStops() => _stops.Clear();

    private void RecomputeAnchors()
    {
        _anchors = [];
        if (HasColumnFocus && Row < CurrentPage.Rows.Count)
        {
            var (left, right) = (SpanLeft!.Value, SpanRight!.Value);
            // A cell counts as "in the band" when most of it is inside.
            _anchors = CurrentPage.Rows[Row].Cells
                .Where(c => Math.Min(c.Right, right) - Math.Max(c.Left, left) >= 0.5 * (c.Right - c.Left))
                .ToList();
        }
        InvalidateStops();
    }

    /// <summary>Puts the band's first line on a specific row and makes that row the reference shape.</summary>
    public void PlaceAt(int page, int row)
    {
        Page = Math.Clamp(page, 0, Document.Pages.Count - 1);
        Row = Math.Clamp(row, 0, Math.Max(0, CurrentPage.Rows.Count - 1));
        RecomputeAnchors();
    }

    /// <summary>Call after a page's rows were replaced (e.g. by OCR) so stops and the reference shape are rebuilt.</summary>
    public void RowsChanged()
    {
        Row = Math.Clamp(Row, 0, Math.Max(0, CurrentPage.Rows.Count - 1));
        RecomputeAnchors();
    }

    public void SetSpan(double left, double right)
    {
        SpanLeft = Math.Min(left, right);
        SpanRight = Math.Max(left, right);
        RecomputeAnchors();
    }

    public void ClearSpan()
    {
        SpanLeft = SpanRight = null;
        RecomputeAnchors();
    }

    /// <summary>Position of the band among the stops on its page (snapping forward if it sits between stops).</summary>
    private int Position(IReadOnlyList<int> stops)
    {
        for (var i = 0; i < stops.Count; i++)
            if (stops[i] >= Row)
                return i;
        return stops.Count - 1;
    }

    private int FindPageWithStops(int from, int direction)
    {
        for (var p = from + direction; p >= 0 && p < Document.Pages.Count; p += direction)
            if (Stops(p).Count > 0)
                return p;
        return -1;
    }

    /// <summary>Moves forward by <see cref="Step"/> stops. Returns false at the end of the document.</summary>
    public bool Next()
    {
        var stops = Stops(Page);
        var pos = Position(stops);
        if (stops.Count > 0 && pos + Step < stops.Count)
        {
            Row = stops[pos + Step];
            return true;
        }
        var page = FindPageWithStops(Page, +1);
        if (page < 0)
            return false;
        Page = page;
        Row = Stops(page)[0];
        return true;
    }

    /// <summary>Moves back by <see cref="Step"/> stops. Returns false at the start of the document.</summary>
    public bool Previous()
    {
        var stops = Stops(Page);
        var pos = Position(stops);
        if (stops.Count > 0 && stops[pos] < Row)
            pos++; // band sat between stops; the previous stop is the one before the snap target
        if (stops.Count > 0 && pos > 0)
        {
            Row = stops[Math.Max(0, pos - Step)];
            return true;
        }
        var page = FindPageWithStops(Page, -1);
        if (page < 0)
            return false;
        Page = page;
        var prevStops = Stops(page);
        Row = prevStops[Math.Max(0, prevStops.Count - LinesInView)];
        return true;
    }

    public void GoToPage(int page)
    {
        if (page < 0 || page >= Document.Pages.Count)
            return;
        Page = page;
        var stops = Stops(page);
        Row = stops.Count > 0 ? stops[0] : 0;
    }

    /// <summary>First and last row indices the band covers, or null when the page has no stops.</summary>
    public (int First, int Last)? BandRows()
    {
        var stops = Stops(Page);
        if (stops.Count == 0)
            return null;
        var pos = Position(stops);
        return (stops[pos], stops[Math.Min(stops.Count - 1, pos + LinesInView - 1)]);
    }

    /// <summary>1-based range of stops in the band, and the page's stop count, for "Lines 3–4 of 18".</summary>
    public (int From, int To, int Of)? Progress()
    {
        var stops = Stops(Page);
        if (stops.Count == 0)
            return null;
        var pos = Position(stops);
        return (pos + 1, Math.Min(stops.Count, pos + LinesInView), stops.Count);
    }

    public Rect? BandRect()
    {
        if (BandRows() is not { } band)
            return null;
        var rows = CurrentPage.Rows;
        double top = double.MaxValue, bottom = double.MinValue;
        for (var i = band.First; i <= band.Last; i++)
        {
            top = Math.Min(top, rows[i].Top);
            bottom = Math.Max(bottom, rows[i].Bottom);
        }
        var (left, right) = HorizontalExtent();
        return new Rect(left, top - PadY, right - left, bottom - top + 2 * PadY);
    }

    /// <summary>Outline of every stop on the page, shown while column focus is on.</summary>
    public Rect? RegionRect()
    {
        var stops = Stops(Page);
        if (!HasColumnFocus || stops.Count == 0)
            return null;
        var rows = CurrentPage.Rows;
        var top = stops.Min(i => rows[i].Top);
        var bottom = stops.Max(i => rows[i].Bottom);
        var (left, right) = HorizontalExtent();
        return new Rect(left - 4, top - PadY - 4, right - left + 8, bottom - top + 2 * PadY + 8);
    }

    private (double Left, double Right) HorizontalExtent()
    {
        var page = CurrentPage;
        if (HasColumnFocus)
            return (Math.Max(0, SpanLeft!.Value), Math.Min(page.Width, SpanRight!.Value));
        return (Math.Max(0, page.ContentLeft - PadX), Math.Min(page.Width, page.ContentRight + PadX));
    }

    /// <summary>
    /// Snaps a dragged band edge to the nearest cell edge on the page, so a rough drag lands
    /// neatly on a column boundary. Returns the input unchanged when nothing is close.
    /// </summary>
    public double SnapEdge(double x, bool isLeftEdge, double reach = 12)
    {
        var edges = CurrentPage.Rows.SelectMany(r => r.Cells)
            .Select(c => isLeftEdge ? c.Left - PadX : c.Right + PadX);
        var best = x;
        var bestDistance = reach;
        foreach (var e in edges)
        {
            var d = Math.Abs(e - x);
            if (d < bestDistance)
            {
                bestDistance = d;
                best = e;
            }
        }
        return best;
    }

    /// <summary>True when a span covers (nearly) the whole text width, i.e. isn't really a column focus.</summary>
    public bool IsFullWidth(double left, double right)
    {
        var page = CurrentPage;
        return left <= page.ContentLeft + 2 && right >= page.ContentRight - 2;
    }

    /// <summary>Counts stops from the band's first line whose centers lie above <paramref name="bottom"/>.</summary>
    public int StopsAbove(double bottom)
    {
        var stops = Stops(Page);
        var pos = Position(stops);
        var count = 0;
        for (var i = pos; i < stops.Count && CurrentPage.Rows[stops[i]].Center < bottom; i++)
            count++;
        return count;
    }

    /// <summary>The first stop whose center is below <paramref name="top"/>, or -1.</summary>
    public int FirstStopBelow(double top)
    {
        foreach (var i in Stops(Page))
            if (CurrentPage.Rows[i].Center > top)
                return i;
        return -1;
    }

    /// <summary>Row nearest to <paramref name="y"/> on a page: the one containing it, else the closest center.</summary>
    public static int NearestRow(DocumentPage page, double y)
    {
        var rows = page.Rows;
        if (rows.Count == 0)
            return -1;
        for (var i = 0; i < rows.Count; i++)
            if (y >= rows[i].Top - PadY && y <= rows[i].Bottom + PadY)
                return i;
        var best = 0;
        for (var i = 1; i < rows.Count; i++)
            if (Math.Abs(rows[i].Center - y) < Math.Abs(rows[best].Center - y))
                best = i;
        return best;
    }
}
