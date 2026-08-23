using ErkS.Platform.Pdf;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// A portfolio must not lose drawing off the edges of a page. These pin the
/// geometry that guarantees it, and the one placement that deliberately crops.
/// </summary>
public sealed class PortfolioPlacementTests
{
    private const double Tolerance = 1e-9;

    // A tall source on a wide page: the aspect ratios disagree, which is where
    // fitting and covering part company.
    private const double SourceWidth = 297;
    private const double SourceHeight = 420;
    private const double PageWidth = 420;
    private const double PageHeight = 297;

    [Fact]
    public void Fit_KeepsTheWholeSourceInsideThePage()
    {
        PortfolioPlacementRect placement = Fit();

        Assert.True(placement.Left >= -Tolerance, $"left {placement.Left} fell off the page");
        Assert.True(placement.Top >= -Tolerance, $"top {placement.Top} fell off the page");
        Assert.True(placement.Right <= PageWidth + Tolerance, $"right {placement.Right} fell off the page");
        Assert.True(placement.Bottom <= PageHeight + Tolerance, $"bottom {placement.Bottom} fell off the page");
    }

    [Fact]
    public void Fit_PreservesTheSourceProportions()
    {
        PortfolioPlacementRect placement = Fit();

        Assert.Equal(
            SourceWidth / SourceHeight,
            placement.Width / placement.Height,
            precision: 9);
    }

    [Fact]
    public void Fit_TouchesTheEdgeItIsLimitedBy()
    {
        // Fitted to the page rather than inside a margin, so the limiting
        // dimension reaches the edge: an imported page is not shrunk further.
        PortfolioPlacementRect placement = Fit();

        Assert.Equal(PageHeight, placement.Height, precision: 9);
        Assert.Equal(0d, placement.Top, precision: 9);
    }

    [Fact]
    public void Cover_OverflowsThePageAndSoCrops()
    {
        PortfolioPlacementRect placement = Cover(0.5, 0.5);

        // This is why an authored CAD page is not placed this way.
        Assert.True(
            placement.Width > PageWidth + Tolerance || placement.Height > PageHeight + Tolerance,
            "covering a mismatched aspect must overflow, otherwise it is not covering");
    }

    [Fact]
    public void Cover_HonoursTheFocalPoint()
    {
        PortfolioPlacementRect top = Cover(0.5, 0);
        PortfolioPlacementRect bottom = Cover(0.5, 1);

        Assert.Equal(0d, top.Top, precision: 9);
        Assert.Equal(PageHeight, bottom.Bottom, precision: 9);
    }

    [Fact]
    public void DegenerateSource_IsRefusedRatherThanDrawnWrong()
    {
        Assert.Null(PortfolioPlacement.Fit(0, 0, 0, 0, PageWidth, PageHeight));
        Assert.Null(PortfolioPlacement.Cover(0, 0, 0, 0, PageWidth, PageHeight, 0.5, 0.5));
    }

    private static PortfolioPlacementRect Fit() =>
        PortfolioPlacement.Fit(SourceWidth, SourceHeight, 0, 0, PageWidth, PageHeight)
        ?? throw new InvalidOperationException("Fit returned no placement.");

    private static PortfolioPlacementRect Cover(double focalX, double focalY) =>
        PortfolioPlacement.Cover(SourceWidth, SourceHeight, 0, 0, PageWidth, PageHeight, focalX, focalY)
        ?? throw new InvalidOperationException("Cover returned no placement.");
}
