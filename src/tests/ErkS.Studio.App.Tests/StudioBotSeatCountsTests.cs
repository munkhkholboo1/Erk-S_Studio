using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

public sealed class StudioBotSeatCountsTests
{
    [Fact]
    public void AnUnlimitedLicenceIsWordsRatherThanItsNumber()
    {
        // The panel read "1 / 2147483647", which a person reads as a bug.
        Assert.Equal("1 / хязгааргүй", StudioBotSeatCounts.DescribeOccupancy(1, int.MaxValue));
    }

    [Fact]
    public void ARealAllowanceIsStillTheNumberItIs()
    {
        Assert.Equal("3 / 4", StudioBotSeatCounts.DescribeOccupancy(3, 4));
    }

    [Fact]
    public void OnlyIntMaxValueItselfReadsAsUnlimited()
    {
        // The reading is narrow on purpose: the server names no sentinel, so a
        // licence that really does allow a great many seats must not be
        // described as having no limit at all.
        Assert.Equal("1000000", StudioBotSeatCounts.DescribeRights(1_000_000));
        Assert.Equal("2147483646", StudioBotSeatCounts.DescribeRights(int.MaxValue - 1));
    }
}
