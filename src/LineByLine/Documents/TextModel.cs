using System.Text.RegularExpressions;

namespace LineByLine.Documents;

/// <summary>One text line on a page, in PDF points (1/72 in) measured from the page's top-left.</summary>
public sealed record TextRow(double Left, double Top, double Right, double Bottom, string Text)
{
    public double Height => Bottom - Top;
    public double Center => (Top + Bottom) / 2;

    /// <summary>The line split at wide gaps, so each cell is roughly one table column.</summary>
    public IReadOnlyList<TextCell> Cells { get; init; } = [];
}

/// <summary>A run of text on a line with no wide gaps in it, e.g. one table cell.</summary>
public sealed record TextCell(double Left, double Right, string Text, bool IsNumeric)
{
    // Amounts and quantities: "425.00", "$1,825.59", "(45.00)", "-12", "7%".
    private static readonly Regex NumericPattern = new(@"^[\$€£¥(+\-]*\s*\d[\d,.\s]*\)?%?$", RegexOptions.Compiled);

    public static TextCell Create(double left, double right, string text) =>
        new(left, right, text, NumericPattern.IsMatch(text.Trim()));

    public bool Overlaps(double left, double right, double tolerance = 0) =>
        Left - tolerance < right && Right + tolerance > left;
}

/// <summary>Where a page's lines came from.</summary>
public enum RowSource
{
    /// <summary>Text embedded in the PDF: exact.</summary>
    Text,

    /// <summary>Read from the page image by OCR: good, but check numbers.</summary>
    Ocr,

    /// <summary>No text known yet: evenly spaced stripes.</summary>
    Fallback,
}

public sealed class DocumentPage
{
    private const double FallbackRowHeight = 18.0;

    public required int Index { get; init; }
    public required double Width { get; init; }
    public required double Height { get; init; }
    public IReadOnlyList<TextRow> Rows { get; private set; } = [];
    public RowSource Source { get; private set; }

    public bool RowsAreFallback => Source == RowSource.Fallback;
    public double ContentLeft => Rows.Count == 0 ? 0 : Rows.Min(r => r.Left);
    public double ContentRight => Rows.Count == 0 ? Width : Rows.Max(r => r.Right);

    /// <summary>Replaces the page's lines. Empty input switches to fallback stripes.</summary>
    public void SetRows(IReadOnlyList<TextRow> rows, RowSource source)
    {
        if (rows.Count == 0)
        {
            Rows = FallbackRows(Width, Height);
            Source = RowSource.Fallback;
            return;
        }
        Rows = rows;
        Source = source;
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
