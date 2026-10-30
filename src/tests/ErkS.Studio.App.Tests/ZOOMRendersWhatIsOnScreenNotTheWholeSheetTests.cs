using System.Windows.Media.Imaging;
using ErkS.Platform.Core;
using ErkS.Studio;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// Zooming a sheet re-rasterised the WHOLE page at the new width - 34 MP for an A2 at 300 dpi,
/// again at every zoom step - while a 4K screen shows at most 8 MP of it (owner, 2026-10-30:
/// «хуудсыг zoom хийхэд маш удаан тодроод байна»). Past a full-page ceiling only the part on
/// screen, with a margin for panning, is drawn at the zoom's own density.
/// </summary>
public sealed class ZOOMRendersWhatIsOnScreenNotTheWholeSheetTests
{
    [Fact]
    public void AFittedSheetNeedsNoDetail()
    {
        // 1191 pt = A2 width; on screen 1500 px wide, the 2400 px first pass is sharper already.
        PreviewRegion? region = PreviewViewportRegion.For(
            pageWidthDip: 1000, pageHeightDip: 707,
            visibleX: 0, visibleY: 0, visibleWidth: 1000, visibleHeight: 707,
            dpiScale: 1.5, pageWidthPoints: 1191, fullPageRenderedPx: 2400);

        Assert.Null(region);
    }

    [Fact]
    public void AZoomedSheetDrawsOnlyTheVisiblePartAndAMargin()
    {
        // 800% of a 1000 dip page: 8000 dip wide, 12 000 px on a 150% screen; 1600×900 dip on view.
        PreviewRegion? region = PreviewViewportRegion.For(
            pageWidthDip: 8000, pageHeightDip: 5656,
            visibleX: 3000, visibleY: 2000, visibleWidth: 1600, visibleHeight: 900,
            dpiScale: 1.5, pageWidthPoints: 1191, fullPageRenderedPx: 4800);

        Assert.NotNull(region);
        Assert.Equal(12000, region!.FullWidthPx);
        // A quarter of the view added on each side, then in pixels.
        Assert.Equal((3000 - 400) * 1.5, region.X, 1);
        Assert.Equal(Math.Floor((2000 - 225) * 1.5), region.Y);
        // Rounded outward to whole pixels, so at most one over.
        Assert.InRange(region.Width, (1600 + 800) * 1.5, (1600 + 800) * 1.5 + 1);
        Assert.InRange(region.Height, (900 + 450) * 1.5, (900 + 450) * 1.5 + 1);
        Assert.True((long)region.Width * region.Height < 8_000_000);
    }

    [Fact]
    public void TheRegionStopsAtThePageEdge()
    {
        PreviewRegion? region = PreviewViewportRegion.For(
            pageWidthDip: 8000, pageHeightDip: 5656,
            visibleX: 0, visibleY: 0, visibleWidth: 1600, visibleHeight: 900,
            dpiScale: 1, pageWidthPoints: 1191, fullPageRenderedPx: 4800);

        Assert.NotNull(region);
        Assert.Equal(0, region!.X);
        Assert.Equal(0, region.Y);
        Assert.Equal(2000, region.Width);
    }

    [Fact]
    public void TheFullPageStopsAtItsCeilingAndDetailTakesOver()
    {
        // 300 dpi of an A2 width is 4963 px; the full page is held at the ceiling, never 34 MP.
        Assert.Equal(
            PreviewViewportRegion.FullPageCeilingPx,
            PreviewViewportRegion.FullPageWidth(pageWidthPoints: 1191 * 2, displayWidthPx: 20000));
    }

    [Fact]
    public void OneWholePageRenderServesEveryZoomUpToTheCeiling()
    {
        // The preview asks once for the ceiling, not once per quantised zoom step.
        Assert.Equal(
            PreviewViewportRegion.FullPageCeilingPx,
            PreviewViewportRegion.FullPageWidth(pageWidthPoints: 1191, displayWidthPx: 1_000_000d));
        // A small sheet stops at its own 300 dpi.
        Assert.Equal(
            PreviewRenderResolution.Ceiling(842),
            PreviewViewportRegion.FullPageWidth(pageWidthPoints: 842, displayWidthPx: 1_000_000d));
    }

    [Fact]
    public void ARegionRendersTheSamePixelsAsThatPartOfTheWholePage()
    {
        string path = WritePdf();
        try
        {
            using PdfiumDocument? document = PdfiumDocument.Open(path);
            Assert.NotNull(document);

            BitmapSource whole = document!.RenderPage(1, 1200)!;
            BitmapSource part = document.RenderRegion(1, 1200, 300, 200, 400, 250)!;

            Assert.Equal(400, part.PixelWidth);
            Assert.Equal(250, part.PixelHeight);
            byte[] expected = Pixels(new CroppedBitmap(whole, new System.Windows.Int32Rect(300, 200, 400, 250)));
            byte[] actual = Pixels(part);
            int differing = 0;
            for (int index = 0; index < expected.Length; index++)
                if (Math.Abs(expected[index] - actual[index]) > 40)
                    differing++;
            Assert.True(differing < expected.Length / 100, $"{differing} of {expected.Length} bytes differ");
            Assert.Contains(actual, value => value < 100);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static byte[] Pixels(BitmapSource image)
    {
        var bytes = new byte[image.PixelWidth * image.PixelHeight * 4];
        new FormatConvertedBitmap(image, System.Windows.Media.PixelFormats.Bgra32, null, 0)
            .CopyPixels(bytes, image.PixelWidth * 4, 0);
        return bytes;
    }

    private static string WritePdf()
    {
        string path = Path.Combine(Path.GetTempPath(), $"erks-region-{Guid.NewGuid():N}.pdf");
        using var document = new PdfDocument();
        PdfPage page = document.AddPage();
        page.Width = XUnit.FromPoint(600);
        page.Height = XUnit.FromPoint(400);
        using (XGraphics gfx = XGraphics.FromPdfPage(page))
        {
            var pen = new XPen(XColors.Black, 3);
            for (int x = 0; x < 600; x += 37)
                gfx.DrawLine(pen, x, 0, 600 - x, 400);
            gfx.DrawRectangle(XBrushes.Black, 150, 100, 60, 40);
        }
        document.Save(path);
        return path;
    }
}
