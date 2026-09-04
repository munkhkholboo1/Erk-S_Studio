using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

public sealed class StudioBotSeatCountsTests
{
    [Fact]
    public void TheFlagDecidesRatherThanTheNumber()
    {
        // The panel read "1 / 2147483647", which a person reads as a bug. The
        // server now says so outright instead of leaving the sentinel to be
        // guessed at.
        Assert.Equal(
            "1 / хязгааргүй",
            StudioBotSeatCounts.DescribeOccupancy(1, int.MaxValue, deviceRightsUnlimited: true));
    }

    [Fact]
    public void ARealAllowanceIsStillTheNumberItIs()
    {
        Assert.Equal("3 / 4", StudioBotSeatCounts.DescribeOccupancy(3, 4, deviceRightsUnlimited: false));
    }

    [Fact]
    public void TheSentinelAloneNoLongerMeansAnything()
    {
        // The defect this rewrite exists for. int.MaxValue with the flag unset
        // is a server that never said "unlimited" - an older one, most likely -
        // and inventing the meaning here is how a platform ends up with a
        // fourth convention for the same idea.
        Assert.Equal(
            "1 / 2147483647",
            StudioBotSeatCounts.DescribeOccupancy(1, int.MaxValue, deviceRightsUnlimited: false));
    }

    [Fact]
    public void TheFlagWinsEvenWhenTheNumberLooksOrdinary()
    {
        // Nothing here re-derives the number from the flag or the other way
        // round: the flag is the whole answer.
        Assert.Equal(
            "2 / хязгааргүй",
            StudioBotSeatCounts.DescribeOccupancy(2, 5, deviceRightsUnlimited: true));
    }
}
