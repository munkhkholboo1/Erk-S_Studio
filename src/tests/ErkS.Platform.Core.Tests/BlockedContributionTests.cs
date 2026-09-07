using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// The user's own report, as a test: a yellow cloud showing «3», sync pressed
/// again and again, nothing ever changing.
///
/// 🔴 THE COUNT PROMISED SOMETHING THE BUTTON COULD NOT DELIVER. All three
/// waiting components were ones that machine cannot produce - a building
/// sub-cover it cannot render, sources whose custodian is another device. The
/// sync was right to leave them alone; the indicator was wrong to count them.
/// Yellow has to mean "press this and it resolves", or pressing teaches people
/// the button is broken.
/// </summary>
public sealed class BlockedContributionTests
{
    [Fact]
    public void WORKThisDeviceCannotSendIsNOTCountedAsYours()
    {
        // The user's exact situation: nothing sendable, three blocked.
        CloudAlbumStatus status = CloudAlbumStatus.Evaluate(
            isLinkedToCloud: true,
            ownWaitingCount: 0,
            probe: CloudProbeOutcome.Answered,
            cloudIsAhead: false,
            blockedCount: 3);

        Assert.Equal(0, status.OwnWaitingCount);
        Assert.Equal(3, status.BlockedCount);
        Assert.NotEqual(CloudAlbumChangeState.OwnWaiting, status.State);
    }

    [Fact]
    public void BLOCKEDWorkIsSTILLSaidOutLoud()
    {
        // Not counting it must not mean hiding it. A person looking at a calm
        // cloud while a page is missing from the album needs this sentence most
        // of all - and it has to name whose turn it is, not just that something
        // is wrong.
        CloudAlbumStatus status = CloudAlbumStatus.Evaluate(
            true, 0, CloudProbeOutcome.Answered, false, blockedCount: 3);

        Assert.Contains("3", status.SummaryMn, StringComparison.Ordinal);
        Assert.Contains("боломжгүй", status.SummaryMn, StringComparison.Ordinal);
        Assert.Contains("эзэмшигч", status.SummaryMn, StringComparison.Ordinal);
    }

    [Fact]
    public void BLOCKEDWorkIsNamedInEVERYState()
    {
        // Including the ones that would otherwise look settled. Green plus a
        // silent gap is the arrangement that produced the original complaint.
        foreach ((int own, bool ahead) in new[] { (0, false), (2, false), (0, true), (2, true) })
        {
            CloudAlbumStatus status = CloudAlbumStatus.Evaluate(
                true, own, CloudProbeOutcome.Answered, ahead, blockedCount: 1);
            Assert.Contains("боломжгүй", status.SummaryMn, StringComparison.Ordinal);
        }

        // ...and in the grey state too, where the cloud could not be reached.
        Assert.Contains(
            "боломжгүй",
            CloudAlbumStatus.Evaluate(true, 0, CloudProbeOutcome.Failed, false, 1).SummaryMn,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SENDABLEWorkStillTurnsTheCloudYellow()
    {
        // The positive control. If the split were wrong in the other direction
        // - everything treated as blocked - the indicator would go quiet and
        // never ask anyone for anything.
        CloudAlbumStatus status = CloudAlbumStatus.Evaluate(
            true, 2, CloudProbeOutcome.Answered, false, blockedCount: 3);

        Assert.Equal(CloudAlbumChangeState.OwnWaiting, status.State);
        Assert.Equal(2, status.OwnWaitingCount);
        Assert.Contains("2", status.Badge, StringComparison.Ordinal);
    }

    [Fact]
    public void ALocalOnlyProjectSaysNothingAboutBlockedWork()
    {
        // No cloud, no contributors, nothing to be blocked on.
        CloudAlbumStatus status = CloudAlbumStatus.Evaluate(
            false, 0, CloudProbeOutcome.NotAttempted, false, blockedCount: 5);

        Assert.Equal("", status.SummaryMn);
    }

    [Fact]
    public void APressThatSendsNOTHINGSaysWHYRatherThanFailing()
    {
        // 🔴 Pressing and being told nothing is the defect the user reported.
        // "Blocked" is NOTHING-TO-DO, not a failure: reporting a failure tells
        // them to try again, which is exactly what had already not worked.
        AlbumRefreshStepResult blocked = AlbumRefreshReport.ContributionBlocked(3);

        Assert.Equal(AlbumRefreshStepOutcome.NothingToDo, blocked.Outcome);
        Assert.Contains("3", blocked.DetailMn, StringComparison.Ordinal);
        Assert.Contains("боломжгүй", blocked.DetailMn, StringComparison.Ordinal);

        // And a run whose only "problem" is blocked work still counts as
        // complete - there was nothing else this device could have done.
        AlbumRefreshReport report = AlbumRefreshReport.Create(
            [
                AlbumRefreshReport.SourcesRead(12, 0),
                blocked,
                AlbumRefreshReport.CloudFetched(false, ""),
                AlbumRefreshReport.PagesRecomposed(32, 0),
            ],
            AlbumOnScreen.CloudCanonical);

        Assert.True(report.IsComplete);
        Assert.Contains("боломжгүй", report.StatusLineMn, StringComparison.Ordinal);
    }
}
