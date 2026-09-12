using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// The portfolio's pages obey the owner's raster rule, like the album's.
///
/// 🔴 «THE RASTER IS FIXED» WAS TRUE OF ONE WRITER OUT OF THREE. The visualisation
/// pages were capped and the portfolio was not - and the portfolio draws THE SAME
/// FILES, because its intake copies image.RelativePath straight off the owner's
/// visualisation records. So a 16k render came back whole the moment they exported a
/// portfolio, and the fix looked finished.
///
/// 🔴 THE RULE IS THE OWNER'S, NOT THE ALBUM'S: «яг зөв растерийн дүрэм 300dpi» is
/// about the product. Reading it as «raster that goes into the album» was a narrowing
/// nobody asked for.
/// </summary>
public sealed class THEPORTFOLIOObeysTheSameCeilingTests
{
    private const int DenseWidth = 15360;
    private const int DenseHeight = 8640;

    [Fact]
    public void A16KRenderOnAFullBleedPageIsBroughtDown()
    {
        // The case the owner actually has: their render, full bleed on the portfolio's
        // own page. The reduction is large enough to be the whole point.
        VisualizationRasterPlan plan = PortfolioRasterBudget.For(
            420, 297, PortfolioPageGeometry.FullBleed, hasCaption: false, DenseWidth, DenseHeight);

        Assert.True(plan.Resample);

        // ⚠ THE FIRST VERSION OF THIS ASSERTION INVENTED A THRESHOLD - «less than a
        // third» - and went red on a correct answer. The reduction is 15360 -> 6237, a
        // factor of 2.46, and a number picked for how impressive it sounds is not a
        // claim about anything. The claim is the RULE: the dimension that covers the
        // page lands on the rule's density, exactly.
        double coveringDensityPerMm = plan.PixelHeight / 297d;
        Assert.Equal(1d / AlbumRasterRule.MillimetresPerPixel, coveringDensityPerMm, 1);
        Assert.True(plan.PixelWidth < DenseWidth, "nothing was reduced at all");
    }

    [Fact]
    public void FULLBLEEDCostsMoreThanFITPAGEBecauseItCovers()
    {
        // 🔴 THE ASYMMETRY IS THE REASON THE FIT MODE IS PASSED THROUGH RATHER THAN
        // ASSUMED. A covered drawing is scaled until it fills the page, so what hangs
        // off the edges still has to exist at full density; a fitted one touches the
        // page on its limiting side only. One rule for both would leave every
        // full-bleed page soft.
        VisualizationRasterPlan covered = PortfolioRasterBudget.For(
            420, 297, PortfolioPageGeometry.FullBleed, false, DenseWidth, DenseHeight);
        VisualizationRasterPlan fitted = PortfolioRasterBudget.For(
            420, 297, PortfolioPageGeometry.FitPage, false, DenseWidth, DenseHeight);

        Assert.True(
            covered.PixelWidth > fitted.PixelWidth,
            "a covered page needs more pixels than a fitted one, not the same");
    }

    [Fact]
    public void ACAPTIONTakesItsBandOutOfTheFrameAndTheBudgetKnows()
    {
        // The caption band is 16 mm of the page that the drawing does not occupy, so a
        // captioned contained page needs fewer pixels. Asserted because the band lives
        // in the writer and the budget has to be reading the same number.
        // ⚠ MEASURED ON A TALL DRAWING, AND THE FIRST VERSION USED A WIDE ONE AND WENT
        // RED. In a contained frame the LIMITING side decides, and for a landscape
        // drawing on a landscape page that is the width - so the caption band changes
        // nothing at all, correctly. The band only costs pixels when the height is what
        // binds. That is a finding about the rule, not a weakness of it.
        VisualizationRasterPlan plain = PortfolioRasterBudget.For(
            420, 297, "Contain", hasCaption: false, DenseHeight, DenseWidth);
        VisualizationRasterPlan captioned = PortfolioRasterBudget.For(
            420, 297, "Contain", hasCaption: true, DenseHeight, DenseWidth);

        Assert.True(captioned.PixelHeight < plain.PixelHeight);

        // And the wide case, asserted as the no-change it is, so the reason is recorded
        // rather than rediscovered as a bug.
        Assert.Equal(
            PortfolioRasterBudget.For(420, 297, "Contain", false, DenseWidth, DenseHeight).PixelWidth,
            PortfolioRasterBudget.For(420, 297, "Contain", true, DenseWidth, DenseHeight).PixelWidth);
    }

    [Fact]
    public void THEGEOMETRYHasONEHomeAndTheWriterReadsIt()
    {
        // 🔴 THE CONSTANTS MOVED TO CORE RATHER THAN BEING COPIED. Working out how many
        // pixels a page needs means working out the frame, and the frame is these two
        // numbers - a private copy in the writer would have been the second home that
        // stops being updated.
        Assert.Equal(14d, PortfolioPageGeometry.ContainMarginMm);
        Assert.Equal(16d, PortfolioPageGeometry.CaptionBandMm);

        PageRectMm contained = PortfolioPageGeometry.AreaFor(420, 297, "Contain", false);
        Assert.Equal(14d, contained.X);
        Assert.Equal(420d - 28d, contained.Width);
        Assert.Equal(297d - 28d, contained.Height);

        PageRectMm bleed = PortfolioPageGeometry.AreaFor(420, 297, PortfolioPageGeometry.FullBleed, true);
        Assert.Equal(0d, bleed.X);
        Assert.Equal(420d, bleed.Width);
        Assert.Equal(297d, bleed.Height);
    }

    [Fact]
    public void ADRAWINGAlreadyCoarseEnoughIsLEFTALONE()
    {
        // Enlarging invents detail nobody drew, and re-encoding a file that is already
        // correct spends the disk the rule exists to save.
        VisualizationRasterPlan plan = PortfolioRasterBudget.For(
            420, 297, PortfolioPageGeometry.FullBleed, false, 800, 600);

        Assert.False(plan.Resample);
    }

    [Fact]
    public void ANUNKNOWNLayoutNameIsTreatedAsCONTAINEDNotAsFullBleed()
    {
        // ⚠ THE SAFE SIDE OF AN UNKNOWN, NAMED. A layout this rule has not heard of
        // gets the contained frame, which asks for FEWER pixels than covering would -
        // so a new layout added to the portfolio and not thought about here produces a
        // slightly soft page rather than an unbounded one. The opposite default would
        // hide the fault in a file that was merely large.
        VisualizationRasterPlan unknown = PortfolioRasterBudget.For(
            420, 297, "SomeLayoutNobodyHasWrittenYet", false, DenseWidth, DenseHeight);
        VisualizationRasterPlan contained = PortfolioRasterBudget.For(
            420, 297, "Contain", false, DenseWidth, DenseHeight);

        Assert.Equal(contained.PixelWidth, unknown.PixelWidth);
        Assert.Equal(contained.PixelHeight, unknown.PixelHeight);
    }

    [Fact]
    public void THELayoutNameIsMatchedTheWayTheWriterMatchesIt()
    {
        // The writer compares case-insensitively, so a portfolio storing «fullbleed»
        // draws covered. A rule that matched exactly would compute a contained frame
        // for a page the writer then covers - fewer pixels than the page shows, which
        // is the one failure direction that looks like nothing at all.
        VisualizationRasterPlan lower = PortfolioRasterBudget.For(
            420, 297, "fullbleed", false, DenseWidth, DenseHeight);
        VisualizationRasterPlan exact = PortfolioRasterBudget.For(
            420, 297, PortfolioPageGeometry.FullBleed, false, DenseWidth, DenseHeight);

        Assert.Equal(exact.PixelWidth, lower.PixelWidth);
    }
}
