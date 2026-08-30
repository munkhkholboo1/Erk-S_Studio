using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// The preview's raster width, which used to be the constant 2400 that the
/// user called "маш муу".
/// </summary>
public sealed class PreviewRenderResolutionTests
{
    // A1 landscape: 841 x 594 mm.
    private const double A1WidthPoints = 841d / 25.4d * 72d;

    [Fact]
    public void AnA1SheetReaches300DpiAtItsCeiling()
    {
        int ceiling = PreviewRenderResolution.Ceiling(A1WidthPoints);

        double dpi = ceiling / (A1WidthPoints / 72d);
        Assert.InRange(dpi, 300d, 301d);
    }

    [Fact]
    public void TheOldConstantWasAboutSeventyDpiOnThatSheet()
    {
        // Recorded rather than asserted about behaviour: this is the number the
        // complaint was about, and it explains why the ceiling sits where it
        // does.
        double dpi = PreviewRenderResolution.FirstPassWidthPx / (A1WidthPoints / 72d);

        Assert.InRange(dpi, 70d, 75d);
    }

    [Fact]
    public void ZoomingInAsksForMorePixels()
    {
        int atFit = PreviewRenderResolution.ForDisplay(A1WidthPoints, 1000);
        int zoomedIn = PreviewRenderResolution.ForDisplay(A1WidthPoints, 6000);

        Assert.True(zoomedIn > atFit, $"fit={atFit} zoomed={zoomedIn}");
    }

    [Fact]
    public void ZoomingOutNeverGoesBelowTheFirstPass()
    {
        // A quarter-size page needs 250 pixels; rendering that would make the
        // image worse the moment anyone zooms back in, and the first pass is
        // already cheap.
        Assert.Equal(
            PreviewRenderResolution.FirstPassWidthPx,
            PreviewRenderResolution.ForDisplay(A1WidthPoints, 250));
    }

    [Fact]
    public void TheCeilingHoldsHoweverFarSomeoneZooms()
    {
        int ceiling = PreviewRenderResolution.Ceiling(A1WidthPoints);

        Assert.Equal(ceiling, PreviewRenderResolution.ForDisplay(A1WidthPoints, 50_000));
    }

    [Fact]
    public void NearbyZoomsLandOnTheSameWidth()
    {
        // What keeps a zoom drag from rasterising at every intermediate size.
        int a = PreviewRenderResolution.ForDisplay(A1WidthPoints, 3610);
        int b = PreviewRenderResolution.ForDisplay(A1WidthPoints, 3625);
        int c = PreviewRenderResolution.ForDisplay(A1WidthPoints, 3700);

        Assert.Equal(a, b);
        Assert.Equal(a, c);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(double.NaN)]
    [InlineData(-100d)]
    public void APageOfUnknownSizeRendersConservatively(double pageWidthPoints)
    {
        // Guessing A1 here would rasterise a postcard at ten thousand pixels.
        Assert.Equal(
            PreviewRenderResolution.FirstPassWidthPx,
            PreviewRenderResolution.Ceiling(pageWidthPoints));
    }

    [Fact]
    public void ASmallPageStillGetsItsOwn300Dpi()
    {
        // A4 portrait, 210 mm wide: the ceiling is lower in pixels than A1's
        // but the same in DPI, which is the point of measuring the paper.
        double a4WidthPoints = 210d / 25.4d * 72d;

        int ceiling = PreviewRenderResolution.Ceiling(a4WidthPoints);

        Assert.True(ceiling < PreviewRenderResolution.Ceiling(A1WidthPoints));
        Assert.InRange(ceiling / (a4WidthPoints / 72d), 300d, 301d);
    }
}
