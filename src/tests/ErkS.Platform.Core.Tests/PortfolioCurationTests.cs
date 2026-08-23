using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// What the user arranges in the portfolio outlives what the drawing does,
/// and what the drawing stops offering is said rather than silently kept.
/// </summary>
public sealed class PortfolioCurationTests
{
    [Fact]
    public void RemovedPage_IsHiddenButKept()
    {
        var portfolio = new ProjectPortfolio();
        portfolio.Items.Add(Page("kept"));
        ProjectPortfolioItem taken = Page("taken");
        taken.RemovedAtUtc = DateTimeOffset.UtcNow;
        portfolio.Items.Add(taken);
        portfolio.Normalize();

        Assert.Equal(2, portfolio.Items.Count);
        Assert.Single(portfolio.OrderedVisibleItems());
        Assert.Equal("kept", Assert.Single(portfolio.OrderedVisibleItems()).SourceSheetKey);
        Assert.Equal("taken", Assert.Single(portfolio.OrderedRemovedItems()).SourceSheetKey);
    }

    [Fact]
    public void RestoringAPage_BringsItBackWithItsContent()
    {
        ProjectPortfolioItem page = Page("p");
        page.Caption = "Гараар бичсэн";
        page.RelativePath = "foundation/documents/Portfolio/abc.pdf";
        page.RemovedAtUtc = DateTimeOffset.UtcNow;

        page.RemovedAtUtc = null;

        Assert.False(page.IsRemoved);
        Assert.Equal("Гараар бичсэн", page.Caption);
        Assert.Equal("foundation/documents/Portfolio/abc.pdf", page.RelativePath);
    }

    [Fact]
    public void NormalizeKeepsTheOrderOfRemovedPages()
    {
        // A removed page holds its place, so restoring it does not send it to
        // the end of a presentation the user has already arranged.
        var portfolio = new ProjectPortfolio();
        portfolio.Items.Add(Page("first"));
        ProjectPortfolioItem middle = Page("middle");
        middle.RemovedAtUtc = DateTimeOffset.UtcNow;
        portfolio.Items.Add(middle);
        portfolio.Items.Add(Page("last"));
        portfolio.Normalize();

        Assert.Equal(2, middle.Order);
        Assert.Equal(["first", "middle", "last"], portfolio.OrderedItems().Select(item => item.SourceSheetKey));
    }

    [Fact]
    public void UnknownLayout_FallsBackToContain()
    {
        var portfolio = new ProjectPortfolio();
        ProjectPortfolioItem page = Page("p");
        page.Layout = "Something";
        portfolio.Items.Add(page);

        portfolio.Normalize();

        Assert.Equal(ProjectPortfolioLayouts.Contain, page.Layout);
    }

    [Fact]
    public void FitPageLayout_SurvivesNormalization()
    {
        var portfolio = new ProjectPortfolio();
        ProjectPortfolioItem page = Page("p");
        page.Layout = ProjectPortfolioLayouts.FitPage;
        portfolio.Items.Add(page);

        portfolio.Normalize();

        Assert.Equal(ProjectPortfolioLayouts.FitPage, page.Layout);
    }

    private static ProjectPortfolioItem Page(string key) => new()
    {
        Kind = ProjectPortfolioItemKinds.CadPage,
        SourceSheetKey = key,
        Title = key,
        SourceTitle = key,
    };
}
