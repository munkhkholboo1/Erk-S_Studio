using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// An imported portfolio page is not an album sheet, so nothing else in the
/// intake reports it. These pin what the user is told.
/// </summary>
public sealed class PortfolioArrivalMessageTests
{
    [Fact]
    public void PackageWithoutPortfolioPages_SaysNothingAboutThePortfolio()
    {
        var recorded = new PackageRecordResult("source", RemovedAlbumPageCount: 2);

        Assert.False(recorded.BroughtPortfolioPages);
    }

    [Fact]
    public void CreatedPagesAreReported()
    {
        var recorded = new PackageRecordResult(
            "source",
            RemovedAlbumPageCount: 0,
            CreatedPortfolioItemCount: 3);

        Assert.True(recorded.BroughtPortfolioPages);
        Assert.Contains("3", Describe(recorded), StringComparison.Ordinal);
    }

    [Fact]
    public void UpdatedPagesAreReportedSeparatelyFromNewOnes()
    {
        var created = new PackageRecordResult("source", 0, CreatedPortfolioItemCount: 1);
        var updated = new PackageRecordResult("source", 0, UpdatedPortfolioItemCount: 1);
        var both = new PackageRecordResult("source", 0, 2, 4);

        Assert.True(updated.BroughtPortfolioPages);
        Assert.NotEqual(Describe(created), Describe(updated));
        string mixed = Describe(both);
        Assert.Contains("2", mixed, StringComparison.Ordinal);
        Assert.Contains("4", mixed, StringComparison.Ordinal);
    }

    private static string Describe(PackageRecordResult recorded) =>
        (string)typeof(ShellView)
            .GetMethod(
                "DescribePortfolioArrival",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, [recorded])!;
}
