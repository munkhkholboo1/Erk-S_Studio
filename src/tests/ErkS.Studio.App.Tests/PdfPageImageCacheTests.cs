using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace ErkS.Studio.App.Tests;

public sealed class PdfPageImageCacheTests : IDisposable
{
    private readonly string workDirectory = Path.Combine(
        Path.GetTempPath(),
        "erks-pdf-thumbnail-tests-" + Guid.NewGuid().ToString("N"));

    public PdfPageImageCacheTests()
    {
        Directory.CreateDirectory(workDirectory);
    }

    [Fact]
    public async Task MultiPagePdf_RendersTheRequestedPageAsAFrozenBitmap()
    {
        string pdfPath = Path.Combine(workDirectory, "source.pdf");
        WritePdf(pdfPath, pageCount: 3);
        var cache = new PdfPageImageCache();

        var image = await cache.GetPageAsync(
            pdfPath,
            pageNumber: 2,
            pixelWidth: 300,
            CancellationToken.None);

        Assert.NotNull(image);
        Assert.True(image.IsFrozen);
        Assert.True(image.PixelWidth >= 300);
        Assert.True(image.PixelHeight > 0);
    }

    [Fact]
    public async Task MissingOrOutOfRangePage_ReturnsNull()
    {
        string pdfPath = Path.Combine(workDirectory, "source.pdf");
        WritePdf(pdfPath, pageCount: 1);
        var cache = new PdfPageImageCache();

        Assert.Null(await cache.GetPageAsync(
            pdfPath,
            pageNumber: 2,
            pixelWidth: 300,
            CancellationToken.None));
        Assert.Null(await cache.GetPageAsync(
            Path.Combine(workDirectory, "missing.pdf"),
            pageNumber: 1,
            pixelWidth: 300,
            CancellationToken.None));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(workDirectory, recursive: true);
        }
        catch
        {
            // Test cleanup must not hide the assertion that failed.
        }
    }

    private static void WritePdf(string path, int pageCount)
    {
        using var document = new PdfDocument();
        for (int index = 0; index < pageCount; index++)
        {
            PdfPage page = document.AddPage();
            page.Width = XUnit.FromMillimeter(420);
            page.Height = XUnit.FromMillimeter(297);
            using XGraphics graphics = XGraphics.FromPdfPage(page);
            graphics.DrawRectangle(
                new XPen(XColors.Black, 1),
                24 + index,
                32 + index,
                180,
                96);
        }
        document.Save(path);
    }
}
