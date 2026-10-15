using System.Text;
using System.Text.RegularExpressions;

namespace LineByLine.Documents;

/// <summary>A value found on a line: what the page shows, and what goes on the clipboard.</summary>
public sealed record GrabbedValue(string Display, string Clipboard)
{
    /// <summary>Where the value sits on the line, in PDF points (for highlighting just the value).</summary>
    public double? Left { get; init; }
    public double? Right { get; init; }
}

/// <summary>Which number on a line Value Grab takes.</summary>
public enum ValuePick
{
    /// <summary>The rightmost number of any kind (except percentages): best for scans and odd layouts.</summary>
    RightmostValue,

    /// <summary>The rightmost amount written as money ("425.00", "$1,825"): skips quantities and codes.</summary>
    RightmostMoney,
}

/// <summary>Finds the value (or the whole row) on a line for Value Grab.</summary>
public static class ValueExtractor
{
    // Money: digits with exactly two decimals ("425.00", "1,825.59", "(45.00)", "-12.50", "$ 9.99", "120.00 CR"),
    // or a currency sign with whole dollars ("$1,825"). Quantities, SKUs and dates don't match.
    private static readonly Regex Money = new(
        @"^\(?\s*[-−]?\s*[$€£]?\s*[-−]?\s*(?:\d{1,3}(?:,\d{3})+|\d+)\.\d{2}\s*\)?(?:\s*(?:CR|-))?$" +
        @"|^\(?\s*[-−]?\s*[$€£]\s*(?:\d{1,3}(?:,\d{3})+|\d+)\s*\)?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Any plain number: "24", "3.40", "1,825.59", "$12", "(45.00)", "120.00 CR". Not "7%", dates or codes.
    private static readonly Regex Number = new(
        @"^\(?\s*[-−]?\s*[$€£]?\s*[-−]?\s*\d[\d,]*(?:\.\d+)?\s*\)?(?:\s*(?:CR|-))?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static bool IsMoney(string text) => Money.IsMatch(text.Trim());

    public static bool IsNumber(string text) => Number.IsMatch(text.Trim());

    /// <summary>
    /// The line's value: the rightmost number (or money amount, per <paramref name="pick"/>),
    /// since tables usually put the line total last. With column focus, only the focused columns count.
    /// </summary>
    public static GrabbedValue? Amount(TextRow row, double? spanLeft = null, double? spanRight = null,
        ValuePick pick = ValuePick.RightmostValue)
    {
        var accepts = pick == ValuePick.RightmostMoney ? (Func<string, bool>)IsMoney : IsNumber;
        foreach (var cell in CellsIn(row, spanLeft, spanRight).Reverse())
        {
            // A cell can hold a label and the amount together ("Total due $1,898.11"), so look
            // at its pieces from the right.
            var pieces = NumberPiecesWithPosition(cell.Text);
            for (var i = pieces.Count - 1; i >= 0; i--)
            {
                var (piece, start, end) = pieces[i];
                if (!accepts(piece))
                    continue;
                // Glyph positions aren't kept per character, so place the value within its
                // cell by character share: exact when the value is the whole cell (the usual case).
                var width = cell.Right - cell.Left;
                var length = Math.Max(1, cell.Text.Length);
                return new GrabbedValue(piece, Normalize(piece))
                {
                    Left = cell.Left + width * start / length,
                    Right = cell.Left + width * end / length,
                };
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

    /// <summary>
    /// Splits a cell into words, repairing the ways OCR mangles numbers: pieces split by a stray
    /// space ("1, 825.59", "425. 00", "$ 9.99"), letters read for digits ("1O5.5O", "l2.00"),
    /// and trailing specks ("425.00."). Non-numeric words pass through untouched.
    /// </summary>
    public static List<string> NumberPieces(string text) => NumberPiecesWithPosition(text).Select(p => p.Text).ToList();

    /// <summary><see cref="NumberPieces"/> plus each piece's character range [Start, End) in <paramref name="text"/>.</summary>
    private static List<(string Text, int Start, int End)> NumberPiecesWithPosition(string text)
    {
        var pieces = new List<(string Text, int Start, int End)>();
        foreach (Match m in Regex.Matches(text, @"\S+"))
        {
            var word = FixDigits(m.Value); // same length: only swaps look-alike characters
            if (pieces.Count > 0 && ShouldJoin(pieces[^1].Text, word))
            {
                var glue = word.Equals("CR", StringComparison.OrdinalIgnoreCase) ? " " : "";
                pieces[^1] = (pieces[^1].Text + glue + word, pieces[^1].Start, m.Index + m.Length);
            }
            else
            {
                pieces.Add((word, m.Index, m.Index + m.Length));
            }
        }

        var result = new List<(string Text, int Start, int End)>();
        foreach (var (piece, start, end) in pieces)
        {
            var trimmed = piece.TrimEnd('.', ',', ';', ':', '\'', '"', '|');
            if (trimmed.Length > 0)
                result.Add((trimmed, start, end - (piece.Length - trimmed.Length)));
        }
        return result;
    }

    private static bool ShouldJoin(string left, string right)
    {
        var leftEndsDigit = left.Length > 0 && char.IsDigit(left[^1]);
        var rightStartsDigit = right.Length > 0 && char.IsDigit(right[0]);
        return (left is "$" or "€" or "£" or "(" or "-" or "−" && (rightStartsDigit || right.StartsWith('$')))
               || ((left.EndsWith(',') || left.EndsWith('.')) && left.Any(char.IsDigit) && rightStartsDigit && right.All(c => char.IsDigit(c) || c is '.' or ',' or ')'))
               || (leftEndsDigit && (right.StartsWith('.') || right.StartsWith(',')) && right.Skip(1).Any(char.IsDigit))
               || (leftEndsDigit && right.Equals("CR", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>In a word that is mostly digits, reads O/o as 0 and l/I/| as 1.</summary>
    private static string FixDigits(string word)
    {
        var digits = word.Count(char.IsDigit);
        var letters = word.Count(c => c is 'O' or 'o' or 'l' or 'I' or '|');
        var others = word.Count(char.IsLetter) - word.Count(c => c is 'O' or 'o' or 'l' or 'I');
        if (digits == 0 || letters == 0 || others > 0 || digits < letters)
            return word;
        var sb = new StringBuilder(word.Length);
        foreach (var c in word)
            sb.Append(c switch { 'O' or 'o' => '0', 'l' or 'I' or '|' => '1', _ => c });
        return sb.ToString();
    }

    private static IEnumerable<TextCell> CellsIn(TextRow row, double? left, double? right)
    {
        if (left is not { } l || right is not { } r)
            return row.Cells;
        // Cells mostly inside the focused columns.
        return row.Cells.Where(c => Math.Min(c.Right, r) - Math.Max(c.Left, l) >= 0.5 * (c.Right - c.Left));
    }
}
