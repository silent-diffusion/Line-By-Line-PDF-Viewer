using System.Text.Json.Serialization;
using System.Windows;

namespace LineByLine.Documents;

/// <summary>A highlighted area on a page, in PDF points from the page's top-left (like <see cref="TextRow"/>).</summary>
public sealed record LineMark(int Page, double Left, double Top, double Right, double Bottom)
{
    [JsonIgnore]
    public Rect Rect => new(Left, Top, Right - Left, Bottom - Top);

    public static LineMark From(int page, Rect r) => new(page, r.Left, r.Top, r.Right, r.Bottom);

    /// <summary>Same spot: on the same page and overlapping by at least half of the smaller area.</summary>
    public bool SameSpotAs(LineMark other)
    {
        if (Page != other.Page)
            return false;
        var overlap = Rect.Intersect(Rect, other.Rect);
        if (overlap.IsEmpty)
            return false;
        var smaller = Math.Min(Rect.Width * Rect.Height, other.Rect.Width * other.Rect.Height);
        return overlap.Width * overlap.Height >= 0.5 * smaller;
    }
}

/// <summary>The highlights in one document, with highlighter-pen semantics.</summary>
public sealed class HighlightSet
{
    private readonly List<LineMark> _marks = [];

    public HighlightSet(IEnumerable<LineMark>? marks = null)
    {
        if (marks != null)
            _marks.AddRange(marks);
    }

    public IReadOnlyList<LineMark> All => _marks;

    public IEnumerable<LineMark> OnPage(int page) => _marks.Where(m => m.Page == page);

    /// <summary>
    /// Highlights the targets, or, when every target is already highlighted, removes those
    /// highlights. Returns true if it added, false if it removed.
    /// </summary>
    public bool Toggle(IReadOnlyList<LineMark> targets)
    {
        if (targets.Count == 0)
            return false;
        if (targets.All(t => _marks.Any(m => m.SameSpotAs(t))))
        {
            _marks.RemoveAll(m => targets.Any(t => t.SameSpotAs(m)));
            return false;
        }
        Add(targets);
        return true;
    }

    /// <summary>Adds highlights that aren't there yet (used by auto-highlight, which never removes).</summary>
    public bool Add(IEnumerable<LineMark> targets)
    {
        var added = false;
        foreach (var t in targets)
        {
            if (_marks.Any(m => m.SameSpotAs(t)))
                continue;
            _marks.Add(t);
            added = true;
        }
        return added;
    }

    public void Clear() => _marks.Clear();
}
