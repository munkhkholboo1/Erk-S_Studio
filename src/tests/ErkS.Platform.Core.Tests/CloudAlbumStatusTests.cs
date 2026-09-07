using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// The indicator's one dangerous mistake is showing "all merged" when the cloud
/// was never asked, so most of this file is aimed at that single sentence.
/// </summary>
public sealed class CloudAlbumStatusTests
{
    [Theory]
    [InlineData(CloudProbeOutcome.NotAttempted)]
    [InlineData(CloudProbeOutcome.Failed)]
    public void ANUnansweredProbeCanNEVERReachTheMergedState(CloudProbeOutcome probe)
    {
        // 🔴 THE ASSERTION THE WHOLE TYPE EXISTS FOR, and it is written over the
        // WHOLE input space rather than one tidy case: no arrangement of the
        // local numbers may produce a claim about the cloud when the cloud did
        // not answer. A single-case version of this test would pass against an
        // implementation that checks the probe only on one branch.
        foreach (int own in new[] { 0, 1, 7 })
        {
            foreach (bool cloudIsAhead in new[] { false, true })
            {
                CloudAlbumStatus status = CloudAlbumStatus.Evaluate(
                    isLinkedToCloud: true,
                    ownWaitingCount: own,
                    probe: probe,
                    cloudIsAhead: cloudIsAhead);

                Assert.Equal(CloudAlbumChangeState.Unknown, status.State);
                Assert.NotEqual(CloudAlbumChangeState.Merged, status.State);
            }
        }
    }

    [Fact]
    public void ASTALECloudFlagCannotSNEAKThroughAFailedProbe()
    {
        // The realistic call site: the last successful check said the cloud was
        // NOT ahead, the next check fails, and the caller still holds that old
        // `false`. If the gate read the flag first, this would render green -
        // the exact "silent green" the fifth state was added to prevent.
        CloudAlbumStatus status = CloudAlbumStatus.Evaluate(
            isLinkedToCloud: true,
            ownWaitingCount: 0,
            probe: CloudProbeOutcome.Failed,
            cloudIsAhead: false);

        Assert.Equal(CloudAlbumChangeState.Unknown, status.State);
        Assert.Equal(0, status.OthersWaitingCount);
    }

    [Fact]
    public void THETwoUnknownCausesSayDIFFERENTThings()
    {
        // Collapsing these to one boolean costs the report its only honest
        // sentence about what happened: "we have not looked yet" and "we looked
        // and could not see" call for different actions from the person.
        string notAttempted = CloudAlbumStatus
            .Evaluate(true, 0, CloudProbeOutcome.NotAttempted, false).SummaryMn;
        string failed = CloudAlbumStatus
            .Evaluate(true, 0, CloudProbeOutcome.Failed, false).SummaryMn;

        Assert.NotEqual(notAttempted, failed);
        Assert.Contains("шалгаагүй", notAttempted, StringComparison.Ordinal);
        Assert.Contains("холбогдож чадсангүй", failed, StringComparison.Ordinal);
    }

    [Fact]
    public void GREYKeepsTheOneNumberItActuallyKNOWS()
    {
        // What this device has not sent is a LOCAL fact - true whether or not
        // the network answered. Dropping it into the grey state would throw
        // away information we hold, and would leave a person with unsent work
        // looking at a badge that says nothing.
        CloudAlbumStatus status = CloudAlbumStatus.Evaluate(
            isLinkedToCloud: true,
            ownWaitingCount: 3,
            probe: CloudProbeOutcome.Failed,
            cloudIsAhead: false);

        Assert.Equal(CloudAlbumChangeState.Unknown, status.State);
        Assert.Equal(3, status.OwnWaitingCount);
        Assert.Contains("3", status.Badge, StringComparison.Ordinal);
        Assert.Contains("3", status.SummaryMn, StringComparison.Ordinal);
    }

    [Fact]
    public void MERGEDRequiresBOTHAnAnswerAndAnEmptyBoard()
    {
        Assert.Equal(
            CloudAlbumChangeState.Merged,
            CloudAlbumStatus.Evaluate(true, 0, CloudProbeOutcome.Answered, false).State);
    }

    [Theory]
    [InlineData(2, true, CloudAlbumChangeState.BothWaiting)]
    [InlineData(2, false, CloudAlbumChangeState.OwnWaiting)]
    [InlineData(0, true, CloudAlbumChangeState.OthersWaiting)]
    [InlineData(0, false, CloudAlbumChangeState.Merged)]
    public void ANANSWEREDProbeSeparatesWHOSEWorkIsWaiting(
        int own,
        bool cloudIsAhead,
        CloudAlbumChangeState expected)
    {
        Assert.Equal(
            expected,
            CloudAlbumStatus.Evaluate(true, own, CloudProbeOutcome.Answered, cloudIsAhead).State);
    }

    [Fact]
    public void THECountsMOVEWithTheirInputs()
    {
        // 🔴 THE POSITIVE CONTROL. A counter that is always zero passes every
        // "is it zero?" assertion in this file, and today's own measurements
        // are full of numbers that turned out to mean "not measured". So the
        // count is shown to RESPOND before any test is allowed to trust a zero.
        Assert.Equal(0, CloudAlbumStatus.Evaluate(true, 0, CloudProbeOutcome.Answered, false).OwnWaitingCount);
        Assert.Equal(4, CloudAlbumStatus.Evaluate(true, 4, CloudProbeOutcome.Answered, false).OwnWaitingCount);

        Assert.Equal(0, CloudAlbumStatus.Evaluate(true, 0, CloudProbeOutcome.Answered, false).OthersWaitingCount);
        Assert.Equal(1, CloudAlbumStatus.Evaluate(true, 0, CloudProbeOutcome.Answered, true).OthersWaitingCount);
    }

    [Fact]
    public void ALocalOnlyProjectShowsNOIndicatorRatherThanAGreyOne()
    {
        // A project with no cloud behind it has nothing to be behind ON. Grey
        // would invite the person to go looking for a connection problem that
        // does not exist.
        CloudAlbumStatus status = CloudAlbumStatus.Evaluate(
            isLinkedToCloud: false,
            ownWaitingCount: 5,
            probe: CloudProbeOutcome.NotAttempted,
            cloudIsAhead: true);

        Assert.Equal(CloudAlbumChangeState.NotLinked, status.State);
        Assert.False(status.ShouldShow);
        Assert.Equal("", status.SummaryMn);
    }

    [Fact]
    public void EVERYVisibleStateIsSeparableWITHOUTColour()
    {
        // Master's third condition. If two states share a glyph, a colour-blind
        // reader is left with colour as the only channel - which is precisely
        // the arrangement the condition rules out.
        CloudAlbumChangeState[] visible =
        [
            CloudAlbumChangeState.Unknown,
            CloudAlbumChangeState.Merged,
            CloudAlbumChangeState.OwnWaiting,
            CloudAlbumChangeState.OthersWaiting,
            CloudAlbumChangeState.BothWaiting,
        ];

        List<string> glyphs = visible.Select(state => StatusOf(state).Glyph).ToList();

        Assert.All(glyphs, glyph => Assert.False(string.IsNullOrWhiteSpace(glyph)));
        Assert.Equal(glyphs.Count, glyphs.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void EVERYVisibleStateSAYSSomething()
    {
        // A state with an empty sentence is a state the person cannot act on.
        foreach (CloudAlbumChangeState state in new[]
        {
            CloudAlbumChangeState.Unknown,
            CloudAlbumChangeState.Merged,
            CloudAlbumChangeState.OwnWaiting,
            CloudAlbumChangeState.OthersWaiting,
            CloudAlbumChangeState.BothWaiting,
        })
        {
            CloudAlbumStatus status = StatusOf(state);
            Assert.True(status.ShouldShow, state + " must be shown");
            Assert.False(
                string.IsNullOrWhiteSpace(status.SummaryMn),
                state + " has no sentence");
        }
    }

    private static CloudAlbumStatus StatusOf(CloudAlbumChangeState state) => state switch
    {
        CloudAlbumChangeState.Unknown => CloudAlbumStatus.Evaluate(true, 0, CloudProbeOutcome.Failed, false),
        CloudAlbumChangeState.Merged => CloudAlbumStatus.Evaluate(true, 0, CloudProbeOutcome.Answered, false),
        CloudAlbumChangeState.OwnWaiting => CloudAlbumStatus.Evaluate(true, 2, CloudProbeOutcome.Answered, false),
        CloudAlbumChangeState.OthersWaiting => CloudAlbumStatus.Evaluate(true, 0, CloudProbeOutcome.Answered, true),
        CloudAlbumChangeState.BothWaiting => CloudAlbumStatus.Evaluate(true, 2, CloudProbeOutcome.Answered, true),
        _ => CloudAlbumStatus.Evaluate(false, 0, CloudProbeOutcome.NotAttempted, false),
    };
}
