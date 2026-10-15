using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Media;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;

namespace LineByLine.Documents;

/// <summary>
/// Reads and writes Line by Line's highlights as standard PDF highlight annotations, so other
/// viewers (Acrobat, Edge, Chrome) show them too. Only annotations this app created (named
/// "LineByLine-…") are ever touched; anything else in the file is left alone.
/// </summary>
public static class PdfHighlights
{
    private const string NamePrefix = "LineByLine-";
    private static readonly byte[] Marker = Encoding.ASCII.GetBytes(NamePrefix);

    /// <summary>Quick check before parsing: does the file contain any of our annotations at all?</summary>
    public static bool ContainsOurs(byte[] pdf) => pdf.AsSpan().IndexOf(Marker) >= 0;

    /// <summary>
    /// Pulls our highlights out of a PDF and returns the file without them, so the app can draw
    /// them itself (and toggle them) instead of having them baked into the page image twice.
    /// </summary>
    public static (byte[] Pdf, List<LineMark> Marks) Extract(byte[] pdf)
    {
        using var input = new MemoryStream(pdf);
        var doc = PdfReader.Open(input, PdfDocumentOpenMode.Modify);
        var marks = new List<LineMark>();
        for (var i = 0; i < doc.Pages.Count; i++)
        {
            var page = doc.Pages[i];
            var frame = PageFrame.Of(page);
            foreach (var annot in RemoveOurs(page))
            {
                if (annot.Elements.GetArray("/Rect") is { Elements.Count: 4 } r)
                {
                    var a = frame.ToPoints(Num(r, 0), Num(r, 1));
                    var b = frame.ToPoints(Num(r, 2), Num(r, 3));
                    marks.Add(new LineMark(i, Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y)));
                }
            }
        }
        using var output = new MemoryStream();
        doc.Save(output);
        return (output.ToArray(), marks);
    }

    /// <summary>Replaces our highlights in the PDF at <paramref name="path"/> with <paramref name="marks"/>.</summary>
    public static void Write(string path, IReadOnlyList<LineMark> marks, Color color, double opacity)
    {
        PdfDocument doc;
        using (var input = new MemoryStream(File.ReadAllBytes(path)))
            doc = PdfReader.Open(input, PdfDocumentOpenMode.Modify);

        for (var i = 0; i < doc.Pages.Count; i++)
        {
            var page = doc.Pages[i];
            RemoveOurs(page);
            var frame = PageFrame.Of(page);
            foreach (var mark in marks.Where(m => m.Page == i))
                AddHighlight(doc, page, frame, mark, color, opacity);
        }

        // Write next to the original, then swap, so a failure never leaves a half-written file.
        var temp = path + ".linebyline.tmp";
        doc.Save(temp);
        File.Move(temp, path, overwrite: true);
    }

    private static List<PdfDictionary> RemoveOurs(PdfPage page)
    {
        var removed = new List<PdfDictionary>();
        if (page.Elements.GetArray("/Annots") is not { } annots)
            return removed;
        for (var i = annots.Elements.Count - 1; i >= 0; i--)
        {
            var item = annots.Elements[i];
            var dict = item is PdfReference reference ? reference.Value as PdfDictionary : item as PdfDictionary;
            if (dict != null && dict.Elements.GetString("/NM").StartsWith(NamePrefix, StringComparison.Ordinal))
            {
                annots.Elements.RemoveAt(i);
                removed.Add(dict);
            }
        }
        return removed;
    }

    private static void AddHighlight(PdfDocument doc, PdfPage page, PageFrame frame, LineMark mark, Color color, double opacity)
    {
        var a = frame.ToPdf(mark.Left, mark.Top);
        var b = frame.ToPdf(mark.Right, mark.Bottom);
        double x1 = Math.Min(a.X, b.X), y1 = Math.Min(a.Y, b.Y), x2 = Math.Max(a.X, b.X), y2 = Math.Max(a.Y, b.Y);
        double r = color.R / 255.0, g = color.G / 255.0, bl = color.B / 255.0;

        // Appearance: a multiply-blended box, so text under it stays readable in any viewer.
        var appearance = new PdfDictionary(doc);
        doc.Internals.AddObject(appearance);
        appearance.Elements.SetName("/Type", "/XObject");
        appearance.Elements.SetName("/Subtype", "/Form");
        appearance.Elements["/BBox"] = Array(doc, x1, y1, x2, y2);
        var gs = new PdfDictionary(doc);
        gs.Elements.SetName("/Type", "/ExtGState");
        gs.Elements.SetName("/BM", "/Multiply");
        gs.Elements["/ca"] = new PdfReal(opacity);
        var extGState = new PdfDictionary(doc);
        extGState.Elements["/GS0"] = gs;
        var resources = new PdfDictionary(doc);
        resources.Elements["/ExtGState"] = extGState;
        appearance.Elements["/Resources"] = resources;
        appearance.CreateStream(Encoding.ASCII.GetBytes(string.Format(CultureInfo.InvariantCulture,
            "/GS0 gs {0:0.###} {1:0.###} {2:0.###} rg {3:0.##} {4:0.##} {5:0.##} {6:0.##} re f",
            r, g, bl, x1, y1, x2 - x1, y2 - y1)));

        var annot = new PdfDictionary(doc);
        doc.Internals.AddObject(annot);
        annot.Elements.SetName("/Type", "/Annot");
        annot.Elements.SetName("/Subtype", "/Highlight");
        annot.Elements["/Rect"] = Array(doc, x1, y1, x2, y2);
        // Corners in the order viewers expect: top-left, top-right, bottom-left, bottom-right.
        annot.Elements["/QuadPoints"] = Array(doc, x1, y2, x2, y2, x1, y1, x2, y1);
        annot.Elements["/C"] = Array(doc, r, g, bl);
        annot.Elements["/CA"] = new PdfReal(opacity);
        annot.Elements.SetInteger("/F", 4); // print
        annot.Elements.SetString("/NM", NamePrefix + Guid.NewGuid().ToString("N"));
        annot.Elements.SetString("/T", "Line by Line");
        annot.Elements["/P"] = page.Reference;
        var ap = new PdfDictionary(doc);
        ap.Elements["/N"] = appearance.Reference;
        annot.Elements["/AP"] = ap;

        if (page.Elements.GetArray("/Annots") is not { } annots)
        {
            annots = new PdfArray(doc);
            page.Elements["/Annots"] = annots;
        }
        annots.Elements.Add(annot.Reference!);
    }

    private static PdfArray Array(PdfDocument doc, params double[] values) =>
        new(doc, values.Select(v => (PdfItem)new PdfReal(Math.Round(v, 3))).ToArray());

    private static double Num(PdfArray array, int i) => array.Elements.GetReal(i);

    /// <summary>
    /// Maps between the app's page points (top-left origin, as displayed, rotation applied) and PDF
    /// user space (bottom-left origin of the unrotated crop box).
    /// </summary>
    public readonly record struct PageFrame(double Llx, double Lly, double Urx, double Ury, int Rotate)
    {
        public static PageFrame Of(PdfPage page)
        {
            var box = page.Elements.ContainsKey("/CropBox") ? page.CropBox : page.MediaBox;
            var rotate = ((page.Rotate % 360) + 360) % 360;
            return new PageFrame(Math.Min(box.X1, box.X2), Math.Min(box.Y1, box.Y2),
                Math.Max(box.X1, box.X2), Math.Max(box.Y1, box.Y2), rotate);
        }

        public (double X, double Y) ToPdf(double x, double y) => Rotate switch
        {
            90 => (Llx + y, Lly + x),
            180 => (Urx - x, Lly + y),
            270 => (Urx - y, Ury - x),
            _ => (Llx + x, Ury - y),
        };

        public (double X, double Y) ToPoints(double pdfX, double pdfY) => Rotate switch
        {
            90 => (pdfY - Lly, pdfX - Llx),
            180 => (Urx - pdfX, pdfY - Lly),
            270 => (Ury - pdfY, Urx - pdfX),
            _ => (pdfX - Llx, Ury - pdfY),
        };
    }
}
