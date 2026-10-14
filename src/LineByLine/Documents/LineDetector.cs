using System.Text;
using Docnet.Core.Models;

namespace LineByLine.Documents;

/// <summary>Groups positioned text (PDF characters or OCR words) into visual lines.</summary>
public static class LineDetector
{
    // Gaps wider than this many text-heights split a line into separate cells (columns).
    // Word spaces are well under one text-height; column gutters are several.
    private const double CellGapFactor = 1.5;

    /// <summary>
    /// A piece of text with its box in PDF points: one character from a PDF, or one word from OCR.
    /// <paramref name="AfterSpace"/> means a space came before it in reading order.
    /// </summary>
    public readonly record struct Glyph(string Text, double Left, double Top, double Right, double Bottom, bool AfterSpace)
    {
        public double Height => Bottom - Top;
        public double Center => (Top + Bottom) / 2;
    }

    private sealed class RowBuilder(Glyph first)
    {
        public readonly List<Glyph> Glyphs = [first];
        private double _centerSum = first.Center;
        private double _heightSum = first.Height;
        private double _top = first.Top, _bottom = first.Bottom;

        private double CenterAvg => _centerSum / Glyphs.Count;
        private double HeightAvg => _heightSum / Glyphs.Count;

        public bool Accepts(Glyph g)
        {
            // Glyph boxes are tight, so letters of one line have different boxes. Accept a glyph
            // whose center is close to the line's, or (for commas, periods, quotes) one that
            // sits mostly inside the line's vertical span.
            if (Math.Abs(g.Center - CenterAvg) < 0.5 * Math.Max(HeightAvg, g.Height))
                return true;
            var overlap = Math.Min(_bottom, g.Bottom) - Math.Max(_top, g.Top);
            return g.Height < HeightAvg && overlap >= 0.5 * g.Height;
        }

        public void Add(Glyph g)
        {
            Glyphs.Add(g);
            _centerSum += g.Center;
            _heightSum += g.Height;
            _top = Math.Min(_top, g.Top);
            _bottom = Math.Max(_bottom, g.Bottom);
        }

        public TextRow Build()
        {
            var ordered = Glyphs.OrderBy(g => g.Left).ToList();
            var sb = new StringBuilder();
            var cells = new List<TextCell>();
            var cell = new StringBuilder();
            var cellLeft = ordered[0].Left;
            for (var i = 0; i < ordered.Count; i++)
            {
                if (i > 0)
                {
                    var gap = ordered[i].Left - ordered[i - 1].Right;
                    var size = Math.Max(ordered[i].Height, ordered[i - 1].Height);

                    // A wide gap separates table columns: close the current cell.
                    if (gap > CellGapFactor * size)
                    {
                        cells.Add(TextCell.Create(cellLeft, ordered[i - 1].Right, cell.ToString()));
                        cell.Clear();
                        cellLeft = ordered[i].Left;
                        sb.Append(' ');
                    }
                    // PDFium's space markers are the main signal; the gap check only catches
                    // wide jumps between columns. Tighter thresholds split monospaced numbers
                    // ("89. 99"), because narrow glyphs like '.' leave room inside their cell.
                    else if (ordered[i].AfterSpace || gap > size)
                    {
                        sb.Append(' ');
                        cell.Append(' ');
                    }
                }
                sb.Append(ordered[i].Text);
                cell.Append(ordered[i].Text);
            }
            cells.Add(TextCell.Create(cellLeft, ordered[^1].Right, cell.ToString()));

            return new TextRow(
                ordered.Min(g => g.Left), ordered.Min(g => g.Top),
                ordered.Max(g => g.Right), ordered.Max(g => g.Bottom),
                sb.ToString())
            {
                Cells = cells,
            };
        }
    }

    /// <summary>Lines from PDFium characters, whose boxes are in pixels at <paramref name="scale"/> pixels per point.</summary>
    public static List<TextRow> Detect(IEnumerable<Character> chars, double scale)
    {
        // PDFium reports spaces (real and generated between text runs) in content order;
        // remember them so words stay separated even though spaces themselves aren't drawn.
        var glyphs = new List<Glyph>();
        var afterSpace = false;
        foreach (var c in chars)
        {
            if (char.IsWhiteSpace(c.Char) || char.IsControl(c.Char))
            {
                afterSpace = true;
                continue;
            }
            if (c.Box.Right <= c.Box.Left || c.Box.Bottom <= c.Box.Top)
                continue;
            glyphs.Add(new Glyph(c.Char.ToString(), c.Box.Left / scale, c.Box.Top / scale, c.Box.Right / scale, c.Box.Bottom / scale, afterSpace));
            afterSpace = false;
        }
        return Detect(glyphs);
    }

    /// <summary>Lines from glyphs already in PDF points.</summary>
    public static List<TextRow> Detect(IEnumerable<Glyph> input)
    {
        var glyphs = input.Where(g => g.Right > g.Left && g.Bottom > g.Top && g.Text.Length > 0).ToList();
        glyphs.Sort((a, b) => a.Center.CompareTo(b.Center));

        // Glyphs arrive sorted by center, but side-by-side blocks (e.g. a header's left and
        // right columns) can sit at slightly different heights and interleave. Checking the
        // last few rows, not just the newest, keeps those blocks from shredding each other.
        const int lookBack = 8;
        var rows = new List<RowBuilder>();
        foreach (var g in glyphs)
        {
            RowBuilder? target = null;
            for (var i = rows.Count - 1; i >= Math.Max(0, rows.Count - lookBack); i--)
            {
                if (rows[i].Accepts(g))
                {
                    target = rows[i];
                    break;
                }
            }

            if (target != null)
                target.Add(g);
            else
                rows.Add(new RowBuilder(g));
        }

        return rows.Select(r => r.Build()).OrderBy(r => r.Top).ToList();
    }
}
