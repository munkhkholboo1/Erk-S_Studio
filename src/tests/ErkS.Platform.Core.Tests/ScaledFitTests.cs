using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// The project cover preview cropped album pages because it filled its frame
/// instead of fitting inside it, and because the frame assumed A4 portrait.
/// </summary>
public sealed class ScaledFitTests
{
    [Fact]
    public void ALandscapePageKeepsItsWholeWidth()
    {
        // A3 landscape in a portrait-ish box. Filling would have cut the sides
        // off - the exact complaint.
        (double Width, double Height)? box = ScaledFit.Within(420, 297, 230, 330);

        Assert.NotNull(box);
        Assert.Equal(230, box!.Value.Width, 3);
        Assert.Equal(297d * (230d / 420d), box.Value.Height, 3);
        Assert.True(box.Value.Height <= 330);
    }

    [Fact]
    public void APortraitPageIsLimitedByWhicheverBoundBitesFirst()
    {
        (double Width, double Height)? box = ScaledFit.Within(210, 297, 230, 330);

        Assert.NotNull(box);
        // Tolerance because the scale factor lands a few ulps above the bound
        // when it divides exactly; a fraction of a pixel is not a crop.
        Assert.True(box!.Value.Width <= 230.0001);
        Assert.True(box.Value.Height <= 330.0001);
        // One of the two bounds has to be reached, or the page was not fitted
        // as large as it could be.
        Assert.True(box.Value.Width >= 229.9 || box.Value.Height >= 329.9);
    }

    [Fact]
    public void ProportionsSurviveTheFit()
    {
        (double Width, double Height)? box = ScaledFit.Within(1600, 900, 230, 330);

        Assert.NotNull(box);
        Assert.Equal(1600d / 900d, box!.Value.Width / box.Value.Height, 6);
    }

    [Fact]
    public void ASmallPageIsAllowedToGrowIntoTheBox()
    {
        // The preview is meant to be readable, so a small render is scaled up
        // rather than left tiny in a large frame.
        (double Width, double Height)? box = ScaledFit.Within(100, 100, 230, 330);

        Assert.NotNull(box);
        Assert.Equal(230, box!.Value.Width, 3);
        Assert.Equal(230, box.Value.Height, 3);
    }

    [Theory]
    [InlineData(0, 297)]
    [InlineData(210, 0)]
    [InlineData(-5, 297)]
    [InlineData(double.NaN, 297)]
    [InlineData(210, double.PositiveInfinity)]
    public void AnUnmeasurablePageReturnsNothingRatherThanAGuess(double width, double height)
    {
        // An image that has not reported its size yet must not collapse the
        // frame to zero, and must not be given an invented shape here - the
        // caller shows its placeholder instead.
        Assert.Null(ScaledFit.Within(width, height, 230, 330));
    }

    [Fact]
    public void BoundsOfZeroReturnNothing()
    {
        Assert.Null(ScaledFit.Within(210, 297, 0, 330));
        Assert.Null(ScaledFit.Within(210, 297, 230, 0));
    }
}
