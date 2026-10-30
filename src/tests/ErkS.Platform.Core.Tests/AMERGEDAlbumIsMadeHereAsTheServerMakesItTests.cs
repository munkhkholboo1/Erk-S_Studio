using ErkS.Platform.Pdf;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// After a merge Studio downloaded the whole merged album back - 127 MB, 23.6 s of every Erin
/// sync. The merge is the server's own code compiled in (ErkS.AlbumMerge), deterministic, so
/// Studio makes the same bytes and keeps them when their hash is the revision's.
/// </summary>
public sealed class AMERGEDAlbumIsMadeHereAsTheServerMakesItTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "erks-merge-" + Guid.NewGuid().ToString("N"));

    public AMERGEDAlbumIsMadeHereAsTheServerMakesItTests() => Directory.CreateDirectory(folder);

    [Fact]
    public void TheSameInputsGiveTheSameBytesAndTheReplacedPages()
    {
        if (LocalAlbumMerge.AlgorithmVersion == 0)
            return; // A checkout without the server beside it builds without the shared merge.

        string album = Pdf("album.pdf", 1, 2, 3, 4);
        string replacement = Pdf("b.pdf", 21, 22, 23);
        LocalMergeComponent[] manifest =
        [
            Component("a", 10, [1, 2]),
            Component("b", 20, [3, 4]),
        ];
        LocalMergeReplacement[] replacements =
        [
            new("b", "b", 20, replacement, false, "owner@example.mn", "", "", "", "", null),
        ];
        var orders = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["a"] = 10, ["b"] = 20 };

        string? first = LocalAlbumMerge.TryWrite(album, 4, manifest, replacements, orders, Path.Combine(folder, "m1.pdf"));
        Thread.Sleep(1100);
        string? second = LocalAlbumMerge.TryWrite(album, 4, manifest, replacements, orders, Path.Combine(folder, "m2.pdf"));

        Assert.NotNull(first);
        Assert.Equal(first, second);
        Assert.Equal([1, 2, 21, 22, 23], Widths(Path.Combine(folder, "m1.pdf")));
    }

    [Fact]
    public void ARefusedMergeIsNullNotAnError()
    {
        string album = Pdf("album.pdf", 1, 2);

        string? hash = LocalAlbumMerge.TryWrite(
            album,
            2,
            [Component("a", 10, [1, 2])],
            [new("a", "a", 10, Path.Combine(folder, "missing.pdf"), false, "", "", "", "", "", null)],
            new Dictionary<string, int>(),
            Path.Combine(folder, "m.pdf"));

        Assert.Null(hash);
    }

    private static LocalMergeComponent Component(string code, int order, int[] pages) =>
        new(code, code, order, pages, "Available", "owner@example.mn", "", "", "", "", []);

    /// <summary>Page widths are 100 + the given marks, so a page is told by its width.</summary>
    private string Pdf(string name, params int[] marks)
    {
        string path = Path.Combine(folder, name);
        using var document = new PdfDocument();
        foreach (int mark in marks)
        {
            PdfPage page = document.AddPage();
            page.Width = XUnit.FromPoint(100 + mark);
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
