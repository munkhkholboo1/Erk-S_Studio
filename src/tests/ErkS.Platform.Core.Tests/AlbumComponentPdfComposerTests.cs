using ErkS.Platform.Pdf;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace ErkS.Platform.Core.Tests;

public sealed class AlbumComponentPdfComposerTests
{
    [Fact]
    public void ReplacingOneOwnersComponentPreservesEveryOtherOwnersPages()
    {
        using TemporaryFolder folder = new();
        string canonical = folder.PathFor("canonical.pdf");
        string replacement = folder.PathFor("replacement.pdf");
        string output = folder.PathFor("preview.pdf");
        WritePdf(canonical, 6);
        WritePdf(replacement, 4);

        AlbumComponentPdfCompositionResult result = AlbumComponentPdfComposer.Compose(
            canonical,
            6,
            [
                new("generated:cover", 0, [1]),
                new("source:owner-a:atd", 100, [2, 3]),
                new("source:owner-b:atd", 110, [4, 5, 6]),
            ],
            [new("source:owner-a:atd", 100, replacement)],
            output);

        Assert.Equal(8, result.PageCount);
        Assert.Equal([1], result.Components.Single(item => item.Code == "generated:cover").PageNumbers);
        Assert.Equal([2, 3, 4, 5], result.Components.Single(item => item.Code == "source:owner-a:atd").PageNumbers);
        Assert.Equal([6, 7, 8], result.Components.Single(item => item.Code == "source:owner-b:atd").PageNumbers);
        Assert.Equal(100, result.Components.Single(item => item.Code == "source:owner-a:atd").Order);
        using PdfDocument preview = PdfReader.Open(output, PdfDocumentOpenMode.Import);
        Assert.Equal(8, preview.PageCount);
    }

    [Fact]
    public void ReplacingExistingSubCoverMovesItBeforeItsBuildingSource()
    {
        using TemporaryFolder folder = new();
        string canonical = folder.PathFor("canonical.pdf");
        string replacement = folder.PathFor("sub-cover.pdf");
        string output = folder.PathFor("preview.pdf");
        WritePdf(canonical, 3);
        WritePdf(replacement, 1);

        AlbumComponentPdfCompositionResult result = AlbumComponentPdfComposer.Compose(
            canonical,
            3,
            [
                new("generated:cover", 0, [1]),
                new("source:building-a", 300, [2]),
                new("generated:building-sub-cover:building-a", 900, [3]),
            ],
            [new("generated:building-sub-cover:building-a", 200, replacement)],
            output);

        Assert.Equal(
            [
                "generated:cover",
                "generated:building-sub-cover:building-a",
                "source:building-a",
            ],
            result.Components.Select(item => item.Code));
        Assert.Equal(
            [2],
            result.Components
                .Single(item => item.Code == "generated:building-sub-cover:building-a")
                .PageNumbers);
        Assert.Equal(
            [3],
            result.Components.Single(item => item.Code == "source:building-a").PageNumbers);
    }

    [Fact]
    public void AddingSameTypeFromAnotherOwnerCreatesAnotherComponent()
    {
        using TemporaryFolder folder = new();
        string canonical = folder.PathFor("canonical.pdf");
        string addition = folder.PathFor("addition.pdf");
        string output = folder.PathFor("preview.pdf");
        WritePdf(canonical, 3);
        WritePdf(addition, 2);

        AlbumComponentPdfCompositionResult result = AlbumComponentPdfComposer.Compose(
            canonical,
            3,
            [
                new("generated:cover", 0, [1]),
                new("source:owner-a:atd", 100, [2, 3]),
            ],
            [new("source:owner-b:atd", 110, addition)],
            output);

        Assert.Equal(5, result.PageCount);
        Assert.Equal(3, result.Components.Count);
        Assert.Equal([4, 5], result.Components.Single(item => item.Code == "source:owner-b:atd").PageNumbers);
    }

    [Fact]
    public void TwoClientsAppendingEqualRankSourcesConvergeRegardlessOfArrivalOrder()
    {
        using TemporaryFolder folder = new();
        string canonical = folder.PathFor("canonical.pdf");
        string sourceA = folder.PathFor("source-a.pdf");
        string sourceB = folder.PathFor("source-b.pdf");
        WritePdfWithWidths(canonical, [100]);
        WritePdfWithWidths(sourceA, [200]);
        WritePdfWithWidths(sourceB, [300]);

        (AlbumComponentPdfCompositionResult Result, string Path) afterA = Compose(
            canonical,
            [new("generated:cover", 0, [1])],
            [new("source:owner-a:plans", 100, sourceA)],
            folder.PathFor("after-a.pdf"));
        (AlbumComponentPdfCompositionResult Result, string Path) afterAB = Compose(
            afterA.Path,
            afterA.Result.Components,
            [new("source:owner-b:plans", 100, sourceB)],
            folder.PathFor("after-a-b.pdf"));

        (AlbumComponentPdfCompositionResult Result, string Path) afterB = Compose(
            canonical,
            [new("generated:cover", 0, [1])],
            [new("source:owner-b:plans", 100, sourceB)],
            folder.PathFor("after-b.pdf"));
        (AlbumComponentPdfCompositionResult Result, string Path) afterBA = Compose(
            afterB.Path,
            afterB.Result.Components,
            [new("source:owner-a:plans", 100, sourceA)],
            folder.PathFor("after-b-a.pdf"));

        string[] expectedCodes =
        [
            "generated:cover",
            "source:owner-a:plans",
            "source:owner-b:plans",
        ];
        Assert.Equal(expectedCodes, afterAB.Result.Components.Select(item => item.Code));
        Assert.Equal(expectedCodes, afterBA.Result.Components.Select(item => item.Code));
        Assert.Equal([100d, 200d, 300d], PageWidths(afterAB.Path));
        Assert.Equal([100d, 200d, 300d], PageWidths(afterBA.Path));
        Assert.Equal(3, afterAB.Result.PageCount);
        Assert.Equal(3, afterBA.Result.PageCount);
    }

    [Fact]
    public void RemovingAnAliasDropsOnlyItsPhysicalPages()
    {
        using TemporaryFolder folder = new();
        string canonical = folder.PathFor("canonical.pdf");
        string output = folder.PathFor("preview.pdf");
        WritePdfWithWidths(canonical, [100, 200, 300]);

        AlbumComponentPdfCompositionResult result = AlbumComponentPdfComposer.Compose(
            canonical,
            3,
            [
                new("generated:sub-cover:alias", 200, [1]),
                new("generated:sub-cover:canonical", 200, [2]),
                new("source:owner-a:plans", 300, [3]),
            ],
            [new("generated:sub-cover:alias", 0, "", Remove: true)],
            output);

        Assert.Equal(2, result.PageCount);
        Assert.Equal(
            ["generated:sub-cover:canonical", "source:owner-a:plans"],
            result.Components.Select(item => item.Code));
        using PdfDocument preview = PdfReader.Open(output, PdfDocumentOpenMode.Import);
        Assert.Equal(200, preview.Pages[0].Width.Point, precision: 3);
        Assert.Equal(300, preview.Pages[1].Width.Point, precision: 3);
    }

    [Fact]
    public void IncompleteCanonicalManifestIsRejectedBeforeWritingPreview()
    {
        using TemporaryFolder folder = new();
        string canonical = folder.PathFor("canonical.pdf");
        string replacement = folder.PathFor("replacement.pdf");
        string output = folder.PathFor("preview.pdf");
        WritePdf(canonical, 3);
        WritePdf(replacement, 1);

        Assert.Throws<InvalidDataException>(() => AlbumComponentPdfComposer.Compose(
            canonical,
            3,
            [new("source:owner-a", 10, [1, 2])],
            [new("source:owner-a", 10, replacement)],
            output));
        Assert.False(File.Exists(output));
    }

    private static void WritePdf(string path, int pageCount)
    {
        using var document = new PdfDocument();
        for (int index = 0; index < pageCount; index++)
            document.AddPage();
        document.Save(path);
    }

    private static void WritePdfWithWidths(string path, double[] widths)
    {
        using var document = new PdfDocument();
        foreach (double width in widths)
        {
            PdfPage page = document.AddPage();
            page.Width = PdfSharp.Drawing.XUnit.FromPoint(width);
            page.Height = PdfSharp.Drawing.XUnit.FromPoint(400);
        }
        document.Save(path);
    }

    private static (AlbumComponentPdfCompositionResult Result, string Path) Compose(
        string canonicalPath,
        IReadOnlyList<AlbumComponentPdfSlot> canonicalComponents,
        IReadOnlyList<AlbumComponentPdfPatch> patches,
        string outputPath) =>
        (
            AlbumComponentPdfComposer.Compose(
                canonicalPath,
                canonicalComponents.SelectMany(item => item.PageNumbers).Count(),
                canonicalComponents,
                patches,
                outputPath),
            outputPath
        );

    private static double[] PageWidths(string path)
    {
        using PdfDocument document = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        return Enumerable.Range(0, document.PageCount)
            .Select(index => document.Pages[index].Width.Point)
            .ToArray();
    }

    private sealed class TemporaryFolder : IDisposable
    {
        private readonly string root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "erks-component-preview-" + Guid.NewGuid().ToString("N"));

        public TemporaryFolder() => Directory.CreateDirectory(root);

        public string PathFor(string fileName) => System.IO.Path.Combine(root, fileName);

        public void Dispose()
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
