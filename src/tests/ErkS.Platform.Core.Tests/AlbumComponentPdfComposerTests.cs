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
