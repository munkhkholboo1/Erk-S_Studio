using ErkS.Platform.Pdf;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// Each pending component was cut from the 127 MB local album by opening it again - 9 s of an
/// Erin refresh that changed nothing (profiled 2026-10-31). They are cut in one reading now.
/// </summary>
public sealed class COMPONENTSAreCutFromTheAlbumInOneReadingTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "erks-extract-" + Guid.NewGuid().ToString("N"));

    public COMPONENTSAreCutFromTheAlbumInOneReadingTests() => Directory.CreateDirectory(folder);

    [Fact]
    public void EveryComponentGetsItsOwnPagesInOrder()
    {
        string album = Album(pages: 6);
        string first = Path.Combine(folder, "a.pdf");
        string second = Path.Combine(folder, "b.pdf");

        IReadOnlyList<int> counts = AlbumComponentPdfExtractor.ExtractMany(
            album,
            [([4, 2], first), ([6], second)]);

        Assert.Equal([2, 1], counts);
        Assert.Equal([2, 4], Widths(first));
        Assert.Equal([6], Widths(second));
    }

    [Fact]
    public void AnOutOfRangePageWritesNothing()
    {
        string album = Album(pages: 2);
        string first = Path.Combine(folder, "a.pdf");

        Assert.Throws<InvalidDataException>(() =>
            AlbumComponentPdfExtractor.ExtractMany(album, [([1], first), ([3], Path.Combine(folder, "b.pdf"))]));
        Assert.False(File.Exists(first));
    }

    /// <summary>Page n is (100 + n) points wide, so a page is told by its width.</summary>
    private string Album(int pages)
    {
        string path = Path.Combine(folder, "album.pdf");
        using var document = new PdfDocument();
        for (int index = 1; index <= pages; index++)
        {
            PdfPage page = document.AddPage();
            page.Width = XUnit.FromPoint(100 + index);
            page.Height = XUnit.FromPoint(100);
        }
        document.Save(path);
        return path;
    }

    private static int[] Widths(string path)
    {
        using PdfDocument document = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        return document.Pages.Cast<PdfPage>().Select(page => (int)Math.Round(page.Width.Point) - 100).ToArray();
    }

    public void Dispose()
    {
        try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
    }
}
