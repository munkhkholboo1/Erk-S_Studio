using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// A board card obeys the owner's raster rule, and the crop is part of the question.
///
/// 🔴 THE BOARD ALREADY HELD HALF THIS RULE, WHICH IS WHY NOBODY NOTICED IT WAS MISSING.
/// 300 DPI has been in <see cref="BoardCardMeasurements"/> since the boards were built -
/// as a FLOOR, told to the person preparing artwork by hand: «has this render enough
/// pixels». Nothing asked whether it had too many. Same number, same file, opposite
/// direction, and only one direction existed - so «the board handles 300 dpi» was true
/// and useless.
///
/// 🔴 AND THE CROP IS WHAT MAKES THIS WRITER DIFFERENT FROM THE OTHER TWO. A card shows
/// a FRACTION of its source: the writer scales the source until the cropped part fills
/// the card and clips the rest, so the whole source is drawn 1/crop times larger than
/// the card. Measuring the card and stopping there would leave every cropped card at a
/// fraction of the density it needs, and softness on a sheet printed a metre across is
/// visible to everyone standing in front of it.
/// </summary>
public sealed class THEBOARDObeysTheSameCeilingTests
{
    private const int DenseWidth = 15360;
    private const int DenseHeight = 8640;

    /// <summary>A quarter of an A0 board, roughly: the size a real card comes out at.</summary>
    private static BoardRectMm Card => new(0, 0, 400, 300);

    [Fact]
    public void A16KRenderOnAFULLCardIsBroughtDownToTheRULESDensity()
    {
        VisualizationRasterPlan plan = BoardRasterBudget.For(
            Card, hasCaption: false, ProjectPortfolioLayouts.FullBleed,
            cropWidth: 1, cropHeight: 1, DenseWidth, DenseHeight);

        Assert.True(plan.Resample, "a 16k render on a 400 mm card is over the rule");

        // The claim is the RULE, not a threshold invented for how large it sounds: the
        // dimension that covers the card lands on the rule's density, exactly.
        // 🔴 AND THE COVERING DIMENSION HERE IS THE HEIGHT, WHICH THE FIRST VERSION OF
        // THIS ASSERTION GOT WRONG AND WENT RED OVER. A 16:9 source covering a 4:3 card
        // is scaled by its height; its width then reaches PAST the card and is clipped,
        // so the density measured across the card's 400 mm is higher than the rule - 15.8
        // px/mm against 11.8 - and correctly so. The rule is met on the axis that
        // touches, and the other axis is overhang.
        Assert.Equal(1d / AlbumRasterRule.MillimetresPerPixel, plan.PixelHeight / 300d, 1);
        Assert.True(
            plan.PixelWidth / 400d > 1d / AlbumRasterRule.MillimetresPerPixel,
            "the clipped axis carries overhang, so it is denser across the card than the rule");
    }

    [Fact]
    public void ACROPPEDCardNeedsMOREOfItsSourceNotTheSame()
    {
        // 🔴 THE FAILURE THIS TEST EXISTS FOR IS INVISIBLE. Showing half the source
        // across means the whole source is drawn twice as wide as the card, so it needs
        // twice the pixels the card's own size suggests. Getting this wrong produces a
        // board that builds, exports and prints - softly.
        VisualizationRasterPlan whole = BoardRasterBudget.For(
            Card, false, ProjectPortfolioLayouts.FullBleed, 1, 1, DenseWidth, DenseHeight);
        VisualizationRasterPlan half = BoardRasterBudget.For(
            Card, false, ProjectPortfolioLayouts.FullBleed, 0.5, 0.5, DenseWidth, DenseHeight);

        Assert.True(
            half.PixelWidth > whole.PixelWidth,
            "a card showing half its source needs more of the source, not the same");

        // And by the factor the geometry says, not merely «more»: the crop divides the
        // frame, so halving the crop doubles the demand.
        // ⚠ A RANGE, BECAUSE BOTH ANSWERS ARE ROUNDED UP. ceil(2x) and 2*ceil(x) differ
        // by at most one pixel, so an equality here would be a red about rounding rather
        // than about the rule.
        Assert.InRange(half.PixelWidth, whole.PixelWidth * 2 - 2, whole.PixelWidth * 2 + 2);
    }

    [Fact]
    public void THECROPDividesEACHAxisSeparately()
    {
        // The two crop fractions are independent, and a card cropped only across is the
        // ordinary case - a panorama trimmed to a column. Using one fraction for both
        // would under-provision whichever axis was cropped harder.
        //
        // 🔴 MEASURED ON A SQUARE SOURCE, FITTED, AND THE FIRST VERSION MEASURED
        // NOTHING AT ALL. It cropped a 16:9 source to a quarter on a covered card, which
        // asks for MORE pixels than a 16k render has - so both answers came back as the
        // untouched source and the test read 15360 == 15360 and called the axes equal.
        // A crop that leaves no work cannot show which axis did the dividing; the numbers
        // have to be chosen so that both sides still resample.
        const int square = 8640;
        VisualizationRasterPlan acrossOnly = BoardRasterBudget.For(
            Card, false, ProjectPortfolioLayouts.Contain, 0.5, 1, square, square);
        VisualizationRasterPlan downOnly = BoardRasterBudget.For(
            Card, false, ProjectPortfolioLayouts.Contain, 1, 0.5, square, square);

        Assert.True(acrossOnly.Resample && downOnly.Resample, "both sides must do work");
        Assert.NotEqual(acrossOnly.PixelWidth, downOnly.PixelWidth);
    }

    [Fact]
    public void ACAPTIONTakesItsReserveOutOfTheFrameAndTheBudgetKnows()
    {
        // The caption band and its gap are 10 mm of the card the drawing does not
        // occupy. Asserted because the number lives in Core and the writer reads it -
        // if they ever disagree, every captioned card is prepared for the wrong frame.
        // ⚠ MEASURED ON A TALL DRAWING. In a fitted frame the LIMITING side decides,
        // and for a landscape drawing on a landscape card that is the width - so the
        // reserve correctly changes nothing there. The wide case is asserted below as
        // the no-change it is, so the reason is recorded rather than rediscovered.
        VisualizationRasterPlan plain = BoardRasterBudget.For(
            Card, false, "Contain", 1, 1, DenseHeight, DenseWidth);
        VisualizationRasterPlan captioned = BoardRasterBudget.For(
            Card, true, "Contain", 1, 1, DenseHeight, DenseWidth);

        Assert.True(captioned.PixelHeight < plain.PixelHeight);

        Assert.Equal(
            BoardRasterBudget.For(Card, false, "Contain", 1, 1, DenseWidth, DenseHeight).PixelWidth,
            BoardRasterBudget.For(Card, true, "Contain", 1, 1, DenseWidth, DenseHeight).PixelWidth);
    }

    [Fact]
    public void THECAPTIONReserveHasONEHomeAndItIsTheWritersNumber()
    {
        // ⚠ WHAT THIS CAN AND CANNOT HOLD. It pins the values and the arithmetic in
        // Core. That the WRITER reads them rather than keeping a private pair is held by
        // THETHREEWritersShareONECeilingTests.THEWRITERReadsTheCaptionReserveRatherThan‑
        // KeepingItsOwn - which was named here before it was written, and then written.
        // Together those two are the seam; neither alone is.
        Assert.Equal(8d, BoardCardFrame.CaptionBandMm);
        Assert.Equal(2d, BoardCardFrame.CaptionGapMm);
        Assert.Equal(10d, BoardCardFrame.CaptionReserveMm);

        BoardRectMm content = BoardCardFrame.ContentAbove(new BoardRectMm(5, 7, 400, 300), true);
        Assert.Equal(5d, content.LeftMm);
        Assert.Equal(7d, content.TopMm);
        Assert.Equal(400d, content.WidthMm);
        Assert.Equal(290d, content.HeightMm);

        // Anchored at the top, because the caption sits under the drawing.
        Assert.Equal(7d, BoardCardFrame.ContentAbove(new BoardRectMm(5, 7, 400, 300), false).TopMm);
        Assert.Equal(300d, BoardCardFrame.ContentAbove(new BoardRectMm(5, 7, 400, 300), false).HeightMm);
    }

    [Fact]
    public void FULLBLEEDCostsMoreThanAFITTEDCardBecauseItCovers()
    {
        // A covered card is scaled until it fills the cell, so what hangs off the edges
        // still has to exist at full density. One rule for both would leave every
        // full-bleed card soft.
        VisualizationRasterPlan covered = BoardRasterBudget.For(
            Card, false, ProjectPortfolioLayouts.FullBleed, 1, 1, DenseWidth, DenseHeight);
        VisualizationRasterPlan fitted = BoardRasterBudget.For(
            Card, false, ProjectPortfolioLayouts.Contain, 1, 1, DenseWidth, DenseHeight);

        Assert.True(covered.PixelWidth > fitted.PixelWidth);
    }

    [Fact]
    public void THELayoutNameIsMatchedTheWayTheWriterMatchesIt()
    {
        // The writer compares case-insensitively against ProjectPortfolioLayouts, so a
        // board storing «fullbleed» draws covered. A rule that matched exactly would
        // compute a fitted frame for a card the writer then covers - fewer pixels than
        // the card shows, the one failure direction that looks like nothing at all.
        Assert.Equal(
            BoardRasterBudget.For(
                Card, false, ProjectPortfolioLayouts.FullBleed, 1, 1, DenseWidth, DenseHeight).PixelWidth,
            BoardRasterBudget.For(
                Card, false, "fullbleed", 1, 1, DenseWidth, DenseHeight).PixelWidth);
    }

    [Fact]
    public void ANUNUSABLECropLeavesTheFileALONE()
    {
        // ⚠ THE HONEST ANSWER TO A VALUE THAT MEANS NO CARD AT ALL. The writer refuses a
        // crop it cannot place and warns instead of drawing, so nothing reaches the
        // sheet that could be soft. Reducing on a guess would be the one move that can
        // still make the board worse.
        foreach (double bad in new[] { 0d, -0.5d, double.NaN, double.PositiveInfinity })
        {
            Assert.False(
                BoardRasterBudget.For(
                    Card, false, ProjectPortfolioLayouts.FullBleed, bad, 1, DenseWidth, DenseHeight)
                    .Resample,
                "crop width " + bad + " should leave the source untouched");
            Assert.False(
                BoardRasterBudget.For(
                    Card, false, ProjectPortfolioLayouts.FullBleed, 1, bad, DenseWidth, DenseHeight)
                    .Resample);
        }
    }

    [Fact]
    public void ACropWiderThanTheSourceIsHeldAtONEWhichIsTheSafeSide()
    {
        // A fraction over one shrinks the drawn source rather than enlarging it, so
        // holding the divisor at one asks for MORE pixels than such a card needs.
        Assert.Equal(
            BoardRasterBudget.For(
                Card, false, ProjectPortfolioLayouts.FullBleed, 1, 1, DenseWidth, DenseHeight).PixelWidth,
            BoardRasterBudget.For(
                Card, false, ProjectPortfolioLayouts.FullBleed, 4, 4, DenseWidth, DenseHeight).PixelWidth);
    }

    [Fact]
    public void ADRAWINGAlreadyCoarseEnoughIsLEFTALONE()
    {
        // Enlarging invents detail nobody drew, and re-encoding a correct file spends
        // the disk the rule exists to save.
        Assert.False(
            BoardRasterBudget.For(
                Card, false, ProjectPortfolioLayouts.FullBleed, 1, 1, 800, 600).Resample);
    }

    [Fact]
    public void ACardTOOShortForItsOwnCaptionAsksForNOWork()
    {
        // A degenerate cell reaches the budget at zero height and comes back «no work»,
        // rather than being floored to a second, differently-sized minimum here. The
        // source goes on untouched, which is the only answer that cannot be too soft.
        Assert.False(
            BoardRasterBudget.For(
                new BoardRectMm(0, 0, 400, 6), true, ProjectPortfolioLayouts.FullBleed,
                1, 1, DenseWidth, DenseHeight).Resample);
    }

    [Fact]
    public void THEBOARDSOWNFLOORStillSaysWhatItAlwaysSaidAndNowHasItsCeiling()
    {
        // 🔴 THE TWO HALVES, SIDE BY SIDE, BECAUSE THE FLOOR EXISTING IS WHY THE CEILING
        // WAS MISSED. IsSharpEnough answers «not enough pixels» and this budget answers
        // «too many»; a render can fail one, the other, or neither, and a single number
        // being present in the file made it look as though both were covered.
        BoardCardMeasurement measured = BoardCardMeasurements.Measure(
            Card, BoardCardMeasurements.PrintDpi)
            ?? throw new InvalidOperationException("a 400x300 card measures");

        // Too few: the floor says no, and the ceiling has nothing to reduce.
        Assert.False(BoardCardMeasurements.IsSharpEnough(measured, 1920, 1080));
        Assert.False(
            BoardRasterBudget.For(
                Card, false, ProjectPortfolioLayouts.FullBleed, 1, 1, 1920, 1080).Resample);

        // Too many: the floor is satisfied and says nothing further; the ceiling acts.
        Assert.True(BoardCardMeasurements.IsSharpEnough(measured, DenseWidth, DenseHeight));
        Assert.True(
            BoardRasterBudget.For(
                Card, false, ProjectPortfolioLayouts.FullBleed, 1, 1, DenseWidth, DenseHeight).Resample);

        // And they are the same density, read from the same rule rather than agreeing by
        // luck.
        Assert.Equal(BoardCardMeasurements.PrintDpi, AlbumRasterRule.DotsPerInch);

        // 🔴 ON THE COVERING AXIS, AND THE FIRST VERSION OF THIS ASSERTION USED THE
        // WRONG ONE. A 16:9 source covering a 4:3 card is scaled by its HEIGHT - its
        // width then reaches past the card and is clipped - so the floor's HEIGHT pixel
        // count is what the ceiling prepares to. Comparing widths asks the ceiling for
        // the 4725 px the card is wide and gets the 6299 px the source is drawn at, and
        // both numbers are correct. A range again, because both round up.
        VisualizationRasterPlan covered = BoardRasterBudget.For(
            Card, false, ProjectPortfolioLayouts.FullBleed, 1, 1, DenseWidth, DenseHeight);
        Assert.InRange(
            covered.PixelHeight, measured.HeightPixels - 2, measured.HeightPixels + 2);
        Assert.True(
            covered.PixelWidth > measured.WidthPixels,
            "the covered source reaches past the card, so it needs more than the card is wide");
    }
}
