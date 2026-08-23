using ErkS.Platform.Pdf;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// A card shows part of a page: the drawn area of a sheet plotted at whatever
/// size the source program allows, without that program having to produce a
/// page the size of the card. The whole plan rests on this working, so it is
/// pinned here rather than assumed.
/// </summary>
public sealed class BoardCardCropTests
{
    private const double Tolerance = 1e-9;

    // A B2 sheet in points, the size real deliveries arrive at.
    private const double SourceWidth = 828;
    private const double SourceHeight = 582;

    // A card three columns wide on an A0 board.
    private const double AreaLeft = 40;
    private const double AreaTop = 60;
    private const double AreaWidth = 300;
    private const double AreaHeight = 200;

    [Fact]
    public void ShowingTheWholeSourceIsExactlyTheOrdinaryFit()
    {
        // Cropping must not become a second way of placing things. With nothing
        // cropped away it has to agree, to the last decimal, with the placement
        // the portfolio has always used.
        PortfolioPlacementRect cropped = Require(BoardPlacement.FitCropped(
            SourceWidth, SourceHeight, 0, 0, 1, 1,
            AreaLeft, AreaTop, AreaWidth, AreaHeight));
        PortfolioPlacementRect plain = Require(PortfolioPlacement.Fit(
            SourceWidth, SourceHeight, AreaLeft, AreaTop, AreaWidth, AreaHeight));

        Assert.Equal(plain.Left, cropped.Left, precision: 9);
        Assert.Equal(plain.Top, cropped.Top, precision: 9);
        Assert.Equal(plain.Width, cropped.Width, precision: 9);
        Assert.Equal(plain.Height, cropped.Height, precision: 9);
    }

    [Fact]
    public void TheCroppedPartLandsWhollyInsideTheCard()
    {
        // The viewport of a real sheet: the drawn area sits inside the page,
        // with the title block and margins around it.
        PortfolioPlacementRect placed = Require(BoardPlacement.FitCropped(
            SourceWidth, SourceHeight, 0.12, 0.14, 0.72, 0.69,
            AreaLeft, AreaTop, AreaWidth, AreaHeight));

        (double left, double top, double right, double bottom) =
            CroppedBounds(placed, 0.12, 0.14, 0.72, 0.69);

        Assert.True(left >= AreaLeft - Tolerance, $"left {left} fell outside the card");
        Assert.True(top >= AreaTop - Tolerance, $"top {top} fell outside the card");
        Assert.True(right <= AreaLeft + AreaWidth + Tolerance, $"right {right} fell outside");
        Assert.True(bottom <= AreaTop + AreaHeight + Tolerance, $"bottom {bottom} fell outside");
    }

    [Fact]
    public void TheCroppedPartIsCentredInTheCard()
    {
        PortfolioPlacementRect placed = Require(BoardPlacement.FitCropped(
            SourceWidth, SourceHeight, 0.12, 0.14, 0.72, 0.69,
            AreaLeft, AreaTop, AreaWidth, AreaHeight));

        (double left, double top, double right, double bottom) =
            CroppedBounds(placed, 0.12, 0.14, 0.72, 0.69);

        Assert.Equal(
            AreaLeft + AreaWidth / 2,
            (left + right) / 2,
            precision: 9);
        Assert.Equal(
            AreaTop + AreaHeight / 2,
            (top + bottom) / 2,
            precision: 9);
    }

    [Fact]
    public void TheCroppedPartKeepsTheProportionsOfWhatWasDrawn()
    {
        // A drawing squeezed to fit a card would be a lie about the building.
        const double cropWidth = 0.5;
        const double cropHeight = 0.25;
        PortfolioPlacementRect placed = Require(BoardPlacement.FitCropped(
            SourceWidth, SourceHeight, 0.25, 0.3, cropWidth, cropHeight,
            AreaLeft, AreaTop, AreaWidth, AreaHeight));

        (double left, double top, double right, double bottom) =
            CroppedBounds(placed, 0.25, 0.3, cropWidth, cropHeight);

        Assert.Equal(
            SourceWidth * cropWidth / (SourceHeight * cropHeight),
            (right - left) / (bottom - top),
            precision: 9);
    }

    [Fact]
    public void TheCroppedPartReachesTheEdgeItIsLimitedBy()
    {
        // Fitted to the card rather than inside a margin, so the card's space
        // is used: this is what makes a board's grid worth having.
        const double cropWidth = 0.5;
        const double cropHeight = 0.5;
        PortfolioPlacementRect placed = Require(BoardPlacement.FitCropped(
            SourceWidth, SourceHeight, 0.1, 0.1, cropWidth, cropHeight,
            AreaLeft, AreaTop, AreaWidth, AreaHeight));

        (double left, double top, double right, double bottom) =
            CroppedBounds(placed, 0.1, 0.1, cropWidth, cropHeight);
        double filledWidth = right - left;
        double filledHeight = bottom - top;

        Assert.True(
            Math.Abs(filledWidth - AreaWidth) < Tolerance ||
            Math.Abs(filledHeight - AreaHeight) < Tolerance,
            $"the crop filled {filledWidth}x{filledHeight} of a {AreaWidth}x{AreaHeight} card");
    }

    [Fact]
    public void CoveringFillsTheCardAndSoCropsFurther()
    {
        PortfolioPlacementRect placed = Require(BoardPlacement.CoverCropped(
            SourceWidth, SourceHeight, 0.1, 0.1, 0.5, 0.25,
            AreaLeft, AreaTop, AreaWidth, AreaHeight, 0.5, 0.5));

        (double left, double top, double right, double bottom) =
            CroppedBounds(placed, 0.1, 0.1, 0.5, 0.25);

        Assert.True(right - left >= AreaWidth - Tolerance);
        Assert.True(bottom - top >= AreaHeight - Tolerance);
    }

    [Fact]
    public void ACropOfNothingIsRefused()
    {
        Assert.Null(BoardPlacement.FitCropped(
            SourceWidth, SourceHeight, 0.5, 0.5, 0, 0.5,
            AreaLeft, AreaTop, AreaWidth, AreaHeight));
    }

    [Fact]
    public void ACardNormalizesACropThatReachesPastItsSource()
    {
        var element = new BoardElement { CropX = 0.8, CropWidth = 0.5 };

        element.Normalize();

        Assert.Equal(0.8, element.CropX, precision: 9);
        Assert.Equal(0.2, element.CropWidth, precision: 9);
    }

    [Fact]
    public void ACardWithNoAssetIsAPlaceholderRatherThanAFault()
    {
        var element = new BoardElement();

        element.Normalize();

        Assert.True(element.IsPlaceholder);
        Assert.True(element.ShowsWholeSource);
    }

    /// <summary>
    /// Where the cropped part of the source ends up, given the rectangle the
    /// whole source is drawn in. This is the mapping the clip then reveals.
    /// </summary>
    private static (double Left, double Top, double Right, double Bottom) CroppedBounds(
        PortfolioPlacementRect placed,
        double cropX,
        double cropY,
        double cropWidth,
        double cropHeight)
    {
        double left = placed.Left + placed.Width * cropX;
        double top = placed.Top + placed.Height * cropY;
        return (left, top, left + placed.Width * cropWidth, top + placed.Height * cropHeight);
    }

    private static PortfolioPlacementRect Require(PortfolioPlacementRect? rect)
    {
        Assert.NotNull(rect);
        return rect!.Value;
    }
}
