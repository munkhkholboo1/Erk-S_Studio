using System.Windows.Media;
using System.Windows.Media.Imaging;
using ErkS.Studio;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// Whether the stripes reported across the solar-exposure sheet's shadows can
/// come from Studio's rasteriser, or only from the drawing itself.
/// </summary>
/// <remarks>
/// Two explanations were in play. CGA's: a shadow whose outline cannot be kept
/// clean is drawn as triangulated translucent solids, so every shared edge is
/// painted twice and blends to a darker line. Studio's: the preview rasterises
/// at what used to be about 72 DPI, and thin features break up at that size.
///
/// They are distinguishable, and the distinction does not need the user's file.
/// A double-blended seam is in the PDF - it is darker ink, present at every
/// resolution. A rasterisation artefact is not: it appears at one pixel size
/// and goes at another.
///
/// So this builds both kinds of page and measures them through Studio's own
/// Pdfium path at both resolutions. What it establishes is which explanation
/// each symptom belongs to, not which one produced the user's sheet.
/// </remarks>
public sealed class ShadowSeamRasterTests
{
    private const int LowWidth = 900;    // about what the old preview gave a sheet
    private const int HighWidth = 3600;  // the same page rendered four times finer

    [Fact]
    public void ATranslucentSeamIsDarkerInkAndSurvivesEveryResolution()
    {
        string path = WritePdf(seamed: true);
        try
        {
            double low = SeamContrast(path, LowWidth);
            double high = SeamContrast(path, HighWidth);

            // Present at both. Rendering finer does not remove ink that is
            // actually in the page, which is what makes this the signature of
            // the drawing rather than of the viewer.
            Assert.True(low > 8, $"low-resolution seam contrast was {low:0.0}");
            Assert.True(high > 8, $"high-resolution seam contrast was {high:0.0}");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void OneUndividedShadowHasNoSeamAtEitherResolution()
    {
        // The counter-example, and the reason the first test means anything: if
        // both pages showed a seam, the measurement would be finding something
        // about the rasteriser instead.
        string path = WritePdf(seamed: false);
        try
        {
            Assert.True(SeamContrast(path, LowWidth) < 4);
            Assert.True(SeamContrast(path, HighWidth) < 4);
        }
        finally
        {
            File.Delete(path);
        }
    }
    [Fact]
    public void TheBandMeasurementCanSeeASeamWhenThereIsOne()
    {
        // The positive control for SeamDeviation, and it was missing.
        //
        // That measurement had only ever returned zero - on abutting bands -
        // and large numbers on real drawings, where the large numbers were
        // drawn lines. A metric that has never been shown to detect the thing
        // it looks for cannot support the conclusion "there is none here": the
        // zero could equally mean the metric is blind.
        //
        // The overlap test above validates a different measurement, on a
        // different axis. This one validates the one the abutting test uses.
        string path = WriteBandedPdf(overlapPoints: 6);
        try
        {
            Assert.True(
                SeamDeviation(path, LowWidth) > 8,
                $"the metric read {SeamDeviation(path, LowWidth):0.0} on bands that do overlap");
        }
        finally
        {
            File.Delete(path);
        }
    }


    // 120 is 47% opacity, which is what this file measured first. 112 is the
    // 56% transparency CGA measured in their own shadow fill. Close, and
    // "close" is exactly the reasoning that would leave the difference
    // untested, so both run.
    [Theory]
    [InlineData(120)]
    [InlineData(112)]
    public void AbuttingBandsShowNoSeamAtAll(int alpha)
    {
        // The result this file was built to find, and it came back negative.
        //
        // CGA's shadows are not overlapped triangles: they read their own
        // tessellator and it emits horizontal scanline bands whose edges meet
        // at exactly the same Y. Their direction matches the user's report -
        // "тасалдалтай хөндлөн зураас" - so the obvious theory was that
        // abutting edges show up as seams when rasterised coarsely.
        //
        // They do not. Not darker, not lighter, at either resolution: Pdfium
        // composites the shared edge cleanly. The deviation is measured in both
        // directions here because the first version of this file could only see
        // darker seams, and a lighter hairline - the usual abutting artefact -
        // would have read as zero and looked like proof.
        //
        // So the tessellation alone does not explain the stripes. Something
        // else does, and this rules out one answer rather than supplying one.
        string path = WriteBandedPdf(alpha);
        try
        {
            Assert.True(SeamDeviation(path, LowWidth) < 3);
            Assert.True(SeamDeviation(path, HighWidth) < 3);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// The largest departure - in either direction - of a band boundary from
    /// the rows either side of it.
    /// </summary>
    private static double SeamDeviation(string pdfPath, int pixelWidth)
    {
        using PdfiumDocument? document = PdfiumDocument.Open(pdfPath);
        Assert.NotNull(document);
        BitmapSource? image = document!.RenderPage(1, pixelWidth);
        Assert.NotNull(image);

        var converted = new FormatConvertedBitmap(image!, PixelFormats.Bgra32, null, 0);
        int stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);

        double Grey(int x, int y)
        {
            int offset = y * stride + x * 4;
            return (pixels[offset] + pixels[offset + 1] + pixels[offset + 2]) / 3d;
        }

        int x = converted.PixelWidth / 2;
        double worst = 0;
        for (int boundaryPoints = 80; boundaryPoints <= 320; boundaryPoints += 40)
        {
            int y = (int)Math.Round(boundaryPoints / 400d * converted.PixelHeight);
            int span = Math.Max(2, converted.PixelHeight / 60);
            if (y - span < 0 || y + span >= converted.PixelHeight)
                continue;

            worst = Math.Max(
                worst,
                Math.Abs((Grey(x, y - span) + Grey(x, y + span)) / 2d - Grey(x, y)));
        }

        return worst;
    }

    /// <summary>
    /// One translucent shadow cut into horizontal bands that meet exactly, the
    /// way a scanline tessellation leaves them.
    /// </summary>
    private static string WriteBandedPdf(int alpha = 120, double overlapPoints = 0)
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"erks-shadow-bands-{Guid.NewGuid():N}.pdf");

        using var document = new PdfDocument();
        PdfPage page = document.AddPage();
        page.Width = XUnit.FromPoint(400);
        page.Height = XUnit.FromPoint(400);

        using (XGraphics gfx = XGraphics.FromPdfPage(page))
        {
            gfx.DrawRectangle(XBrushes.White, 0, 0, 400, 400);
            var shadow = new XSolidBrush(XColor.FromArgb(alpha, 40, 40, 40));
            for (double top = 40; top < 360; top += 40)
            {
                gfx.DrawPolygon(
                    shadow,
                    [
                        new XPoint(100, top),
                        new XPoint(300, top),
                        new XPoint(300, top + 40 + overlapPoints),
                        new XPoint(100, top + 40 + overlapPoints),
                    ],
                    XFillMode.Winding);
            }
        }

        document.Save(path);
        return path;
    }

    private static double SeamContrast(string pdfPath, int pixelWidth)
    {
        using PdfiumDocument? document = PdfiumDocument.Open(pdfPath);
        Assert.NotNull(document);

        BitmapSource? image = document!.RenderPage(1, pixelWidth);
        Assert.NotNull(image);

        var converted = new FormatConvertedBitmap(image!, PixelFormats.Bgra32, null, 0);
        int stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);

        double Grey(int x, int y)
        {
            int offset = y * stride + x * 4;
            return (pixels[offset] + pixels[offset + 1] + pixels[offset + 2]) / 3d;
        }

        // The shape spans the middle half of the page and the seam runs down
        // its centre. Sample several rows so a single stray pixel cannot decide
        // the answer.
        int centreX = converted.PixelWidth / 2;
        // Far enough out to be clear of the overlap band, which spans 190-210
        // of the 400-point page: a sample on its edge reads as no seam at all,
        // which is how the first run of this came back zero.
        int sideOffset = Math.Max(8, converted.PixelWidth / 10);
        double worst = 0;
        for (int step = 1; step <= 8; step++)
        {
            int y = converted.PixelHeight * step / 9;
            double seam = Grey(centreX, y);
            double left = Grey(centreX - sideOffset, y);
            double right = Grey(centreX + sideOffset, y);
            worst = Math.Max(worst, (left + right) / 2d - seam);
        }

        return worst;
    }

    /// <summary>
    /// A page carrying one translucent shadow, drawn either as a single shape
    /// or as two halves that share their long edge.
    /// </summary>
    private static string WritePdf(bool seamed)
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"erks-shadow-seam-{(seamed ? "split" : "whole")}-{Guid.NewGuid():N}.pdf");

        using var document = new PdfDocument();
        PdfPage page = document.AddPage();
        page.Width = XUnit.FromPoint(400);
        page.Height = XUnit.FromPoint(400);

        using (XGraphics gfx = XGraphics.FromPdfPage(page))
        {
            gfx.DrawRectangle(XBrushes.White, 0, 0, 400, 400);
            var shadow = new XSolidBrush(XColor.FromArgb(120, 40, 40, 40));

            // Overlapping by two points, not merely touching: "the joined edge
            // is painted twice" is what produces the darker line. Abutting
            // shapes were measured first and produced no seam at all.
            if (seamed)
            {
                gfx.DrawPolygon(
                    shadow,
                    [new XPoint(100, 40), new XPoint(210, 40), new XPoint(210, 360), new XPoint(100, 360)],
                    XFillMode.Winding);
                gfx.DrawPolygon(
                    shadow,
                    [new XPoint(190, 40), new XPoint(300, 40), new XPoint(300, 360), new XPoint(190, 360)],
                    XFillMode.Winding);
            }
            else
            {
                gfx.DrawPolygon(
                    shadow,
                    [new XPoint(100, 40), new XPoint(300, 40), new XPoint(300, 360), new XPoint(100, 360)],
                    XFillMode.Winding);
            }
        }

        document.Save(path);
        return path;
    }
}
