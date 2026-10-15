using System.IO;
using System.Windows.Media.Imaging;

namespace LineByLine.Documents;

/// <summary>An open file the band can move through: a PDF or an image (scan, photo, screenshot).</summary>
public abstract class Document(string filePath, IReadOnlyList<DocumentPage> pages)
{
    public static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".tif", ".tiff", ".bmp"];

    public const string OpenDialogFilter =
        "Documents (PDF or image)|*.pdf;*.png;*.jpg;*.jpeg;*.tif;*.tiff;*.bmp|PDF files (*.pdf)|*.pdf|Images|*.png;*.jpg;*.jpeg;*.tif;*.tiff;*.bmp";

    public string FilePath { get; } = filePath;
    public IReadOnlyList<DocumentPage> Pages { get; } = pages;

    /// <summary>Line by Line highlights that were saved inside the file (PDFs only).</summary>
    public IReadOnlyList<LineMark> EmbeddedHighlights { get; protected init; } = [];

    /// <summary>Renders a page to a frozen bitmap. <paramref name="scale"/> is pixels per PDF point.</summary>
    public abstract BitmapSource Render(int pageIndex, double scale);

    public static bool IsSupported(string path) =>
        path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) || IsImage(path);

    public static bool IsImage(string path) =>
        ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public static Document Load(string path) => IsImage(path) ? ImageDocument.Open(path) : PdfDoc.Open(path);
}
