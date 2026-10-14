using System.Text.RegularExpressions;

namespace LineByLine.Documents;

/// <summary>A value found on a line: what the page shows, and what goes on the clipboard.</summary>
public sealed record GrabbedValue(string Display, string Clipboard);

/// <summary>Finds the dollar amount (or the whole row) on a line for Value Grab.</summary>
public static class ValueExtractor
{
    // Money: digits with exactly two decimals ("425.00", "1,825.59", "(45.00)", "-12.50", "$ 9.99", "120.00 CR"),
    // or a currency sign with whole dollars ("$1,825"). Quantities, SKUs and dates don't match.
    private static readonly Regex Money = new(
        @"^\(?\s*[-−]?\s*[$€£]?\s*[-−]?\s*(?:\d{1,3}(?:,\d{3})+|\d+)\.\d{2}\s*\)?(?:\s*(?:CR|-))?$" +
        @"|^\(?\s*[-−]?\s*[$€£]\s*(?:\d{1,3}(?:,\d{3})+|\d+)\s*\)?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static bool IsMoney(string text) => Money.IsMatch(text.Trim());

    /// <summary>
    /// The line's amount: the rightmost money value (invoices put the line total last). With
    /// column focus, only cells inside the focused columns count.
    /// </summary>
    public static GrabbedValue? Amount(TextRow row, double? spanLeft = null, double? spanRight = null)
    {
        foreach (var cell in CellsIn(row, spanLeft, spanRight).Reverse())
        {
            if (IsMoney(cell.Text))
                return new GrabbedValue(cell.Text.Trim(), Normalize(cell.Text));

            // A cell can hold a label and the amount together ("Total due $1,898.11").
            var tokens = cell.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (var i = tokens.Length - 1; i >= 0; i--)
            {
                // "$ 9.99" splits into two tokens; try each token and it joined with the one before.
                if (i > 0 && IsMoney(tokens[i - 1] + " " + tokens[i]))
                    return new GrabbedValue(tokens[i - 1] + " " + tokens[i], Normalize(tokens[i - 1] + tokens[i]));
                if (IsMoney(tokens[i]))
                    return new GrabbedValue(tokens[i], Normalize(tokens[i]));
            }
        }
        return null;
    }

    /// <summary>The whole line as tab-separated cells, which paste into Excel as separate columns.</summary>
    public static GrabbedValue? Row(TextRow row, double? spanLeft = null, double? spanRight = null)
    {
        var cells = CellsIn(row, spanLeft, spanRight).Select(c => c.Text.Trim()).Where(t => t.Length > 0).ToList();
        if (cells.Count == 0)
            return null;
        return new GrabbedValue(string.Join("  ·  ", cells), string.Join("\t", cells));
    }

    /// <summary>"$1,825.59" → "1825.59", "(45.00)" → "-45.00", "120.00 CR" → "-120.00": a plain number Excel always reads right.</summary>
    public static string Normalize(string money)
    {
        var t = money.Trim().Replace('−', '-');
        var negative = (t.StartsWith('(') && t.EndsWith(')')) || t.Contains('-')
                       || t.EndsWith("CR", StringComparison.OrdinalIgnoreCase);
        var digits = new string(t.Where(c => char.IsDigit(c) || c == '.').ToArray());
        return negative ? "-" + digits : digits;
    }

    private static IEnumerable<TextCell> CellsIn(TextRow row, double? left, double? right)
    {
        if (left is not { } l || right is not { } r)
            return row.Cells;
        // Cells mostly inside the focused columns.
        return row.Cells.Where(c => Math.Min(c.Right, r) - Math.Max(c.Left, l) >= 0.5 * (c.Right - c.Left));
    }
}
