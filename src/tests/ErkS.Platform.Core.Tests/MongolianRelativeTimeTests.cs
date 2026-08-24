using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

public sealed class MongolianRelativeTimeTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void JustNow()
    {
        Assert.Equal("дөнгөж сая", MongolianRelativeTime.Describe(Now.AddSeconds(-20), Now));
    }

    [Fact]
    public void Minutes()
    {
        Assert.Equal("5 минутын өмнө", MongolianRelativeTime.Describe(Now.AddMinutes(-5), Now));
        Assert.Equal("59 минутын өмнө", MongolianRelativeTime.Describe(Now.AddMinutes(-59), Now));
    }

    [Fact]
    public void Hours()
    {
        Assert.Equal("1 цагийн өмнө", MongolianRelativeTime.Describe(Now.AddHours(-1), Now));
        Assert.Equal("3 цагийн өмнө", MongolianRelativeTime.Describe(Now.AddHours(-3), Now));
    }

    [Fact]
    public void Days()
    {
        Assert.Equal("1 өдрийн өмнө", MongolianRelativeTime.Describe(Now.AddDays(-1), Now));
        Assert.Equal("29 өдрийн өмнө", MongolianRelativeTime.Describe(Now.AddDays(-29), Now));
    }

    [Fact]
    public void PastAMonthTheDateIsClearerThanTheDistance()
    {
        string described = MongolianRelativeTime.Describe(Now.AddDays(-200), Now);

        Assert.DoesNotContain("өмнө", described);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", described);
    }

    [Fact]
    public void AClockRunningAheadDoesNotProduceANegativePhrase()
    {
        // The colleague's machine being a minute ahead of ours is ordinary;
        // "in 2 minutes" would read as a bug rather than as a clock.
        Assert.Equal("дөнгөж сая", MongolianRelativeTime.Describe(Now.AddMinutes(2), Now));
    }

    [Fact]
    public void ExactBoundariesRoundDownRatherThanUp()
    {
        // 60 minutes is an hour, not "60 минутын өмнө".
        Assert.Equal("1 цагийн өмнө", MongolianRelativeTime.Describe(Now.AddMinutes(-60), Now));
        Assert.Equal("1 өдрийн өмнө", MongolianRelativeTime.Describe(Now.AddHours(-24), Now));
    }

    [Fact]
    public void NoTimestampSaysSoRatherThanGuessing()
    {
        // A person whose presence was never recorded is not offline - nobody
        // has heard from them, which is a different thing.
        Assert.Equal("Мэдээлэл алга", MongolianRelativeTime.DescribeLastSeen(null, Now));
    }

    [Fact]
    public void TheTooltipSaysConnectedNotWorking()
    {
        // The signal is that Studio was open and talking to the server. Saying
        // someone was "working" claims something this cannot see.
        string tooltip = MongolianRelativeTime.DescribeLastSeen(Now.AddHours(-3), Now);

        Assert.Equal("3 цагийн өмнө холбогдсон", tooltip);
        Assert.DoesNotContain("ажил", tooltip);
    }
}
