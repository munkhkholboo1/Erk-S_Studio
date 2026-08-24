using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

public sealed class MemberPresenceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NeverHeardFromIsNotOffline()
    {
        // The complaint that started this was a dot claiming to know something
        // it did not. An absent timestamp has to read as absent knowledge.
        Assert.Equal(
            MemberPresenceState.Unknown,
            MemberPresence.Resolve(null, Now));
    }

    [Fact]
    public void RecentlySeenIsOnline()
    {
        Assert.Equal(
            MemberPresenceState.Online,
            MemberPresence.Resolve(Now.AddMinutes(-2), Now));
    }

    [Fact]
    public void LongAgoIsOffline()
    {
        Assert.Equal(
            MemberPresenceState.Offline,
            MemberPresence.Resolve(Now.AddHours(-3), Now));
    }

    [Fact]
    public void TheEdgeOfTheWindowCountsAsPresent()
    {
        Assert.Equal(
            MemberPresenceState.Online,
            MemberPresence.Resolve(Now.AddMinutes(-5), Now));
        Assert.Equal(
            MemberPresenceState.Offline,
            MemberPresence.Resolve(Now.AddMinutes(-5).AddSeconds(-1), Now));
    }

    [Fact]
    public void TheServersThresholdWins()
    {
        DateTimeOffset seen = Now.AddMinutes(-8);

        Assert.Equal(
            MemberPresenceState.Offline,
            MemberPresence.Resolve(seen, Now, TimeSpan.FromMinutes(5)));
        Assert.Equal(
            MemberPresenceState.Online,
            MemberPresence.Resolve(seen, Now, TimeSpan.FromMinutes(15)));
    }

    [Fact]
    public void AClockRunningAheadStillReadsAsPresent()
    {
        Assert.Equal(
            MemberPresenceState.Online,
            MemberPresence.Resolve(Now.AddMinutes(3), Now));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-30)]
    public void AnUnusableThresholdFallsBackRatherThanMarkingEveryoneOffline(int seconds)
    {
        // A rules document with a zero or negative window would otherwise put
        // the whole team offline at once, which looks like an outage.
        Assert.Equal(
            MemberPresenceState.Online,
            MemberPresence.Resolve(Now.AddMinutes(-1), Now, TimeSpan.FromSeconds(seconds)));
    }
}
