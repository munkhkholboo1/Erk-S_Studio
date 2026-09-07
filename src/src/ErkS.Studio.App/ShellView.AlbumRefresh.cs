using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ErkS.Platform.Core;

namespace ErkS.Studio;

/// <summary>
/// «Альбомыг шинэчлэх» - the one action, and the indicator that says whether it
/// is worth pressing.
///
/// 🔴 WHY ONE ACTION. Three commands stood here - «Эх үүсвэрээс шинэчлэх»,
/// «Бүрэн дахин байгуулах» and Cloud Sync - and each did part of the job. A
/// person who wanted their album current had to know which part they needed,
/// and the failure mode was silent: press the wrong one and the album looks
/// refreshed while somebody else's work is still in the cloud. The distinction
/// between them was real but it was OUR distinction, not the user's.
///
/// It survived because it looked like a performance decision. Measured, it is
/// not: hashing every source PDF of a real project - 203 files, 332.6 MB - takes
/// 0.35 s. Reading the sources was never the expensive half. The network is,
/// which is why the cheap cloud check below matters and the source read does not
/// need an incremental mode.
///
/// 🔴 THE CLIENT DOES NOT ASSEMBLE THE SHARED ALBUM. Step 3 fetches and shows
/// what the SERVER assembled. The canonical order lives in one place, on the
/// server, and is covered there by an invariant that the same components in any
/// arrival order land in the same positions. A client-side reassembly would be a
/// second implementation of that rule, standing outside that guarantee - the
/// exact shape this codebase keeps getting bitten by. What step 4 recomputes is
/// the LOCAL stored page order, which drives the on-screen list, and it is held
/// to the server's answer by BuildingOrderAgreementTests.
/// </summary>
internal sealed partial class ShellView
{

    // ---- state -------------------------------------------------------------

    private bool albumRefreshInProgress;

    private AlbumRefreshReport? lastAlbumRefreshReport;

    private readonly TextBlock cloudAlbumIndicatorBadge = new()
    {
        FontWeight = FontWeights.Bold,
        FontSize = 12,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private readonly Border cloudAlbumIndicator = new()
    {
        CornerRadius = new CornerRadius(9),
        Padding = new Thickness(8, 2, 8, 2),
        Margin = new Thickness(8, 0, 0, 0),
        VerticalAlignment = VerticalAlignment.Center,
        BorderThickness = new Thickness(1),
    };

    /// <summary>
    /// Paints the indicator.
    ///
    /// 🔴 COLOUR IS THE REDUNDANT CHANNEL, NOT THE CARRIER. The glyph and the
    /// numbers say the same thing the colour does, so the badge is readable with
    /// no colour vision at all - and the tooltip says it a third time in words.
    /// A screen where the colour is the only difference between "everything is
    /// shared" and "somebody is waiting on you" is unreadable for roughly one
    /// man in twelve.
    /// </summary>
    private void RefreshCloudAlbumIndicator()
    {
        CloudAlbumStatus status = CurrentCloudAlbumStatus();
        cloudAlbumIndicator.Visibility = status.ShouldShow
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (!status.ShouldShow)
            return;

        cloudAlbumIndicatorBadge.Text = albumRefreshInProgress
            ? "…"
            : status.Badge;
        Brush colour = IndicatorBrush(status.State);
        cloudAlbumIndicatorBadge.Foreground = colour;
        cloudAlbumIndicator.BorderBrush = colour;
        cloudAlbumIndicator.Background = StudioTheme.PanelAltBrush;
        cloudAlbumIndicator.ToolTip = albumRefreshInProgress
            ? "Альбомыг шинэчилж байна…"
            : status.SummaryMn;
    }

    private static Brush IndicatorBrush(CloudAlbumChangeState state) => state switch
    {
        CloudAlbumChangeState.Merged => StudioTheme.SuccessBrush,
        CloudAlbumChangeState.OwnWaiting => StudioTheme.WarningBrush,
        CloudAlbumChangeState.OthersWaiting => OthersWaitingBrush,
        CloudAlbumChangeState.BothWaiting => StudioTheme.DangerBrush,
        // Grey, and never green: an unknown cloud is shown as unknown.
        _ => StudioTheme.MutedTextBrush,
    };

    /// <summary>
    /// Orange - between the yellow of "mine to send" and the red of "both".
    /// Defined here because the theme has no orange, and borrowing the warning
    /// yellow would make two different states look alike.
    /// </summary>
    private static readonly SolidColorBrush OthersWaitingBrush =
        CreateFrozen(Color.FromRgb(0xE8, 0x7A, 0x22));

    private static SolidColorBrush CreateFrozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// What the last cloud check produced.
    ///
    /// 🔴 THE DEFAULT IS DELIBERATELY THE IGNORANT ONE. A field that starts at
    /// "answered" would render green on a freshly opened project before anything
    /// had been asked - which is the single failure the indicator exists to
    /// prevent. Every path that fails to learn the cloud's state must leave this
    /// at NotAttempted or set it to Failed.
    /// </summary>
    private CloudProbeOutcome lastCloudProbeOutcome = CloudProbeOutcome.NotAttempted;

    /// <summary>
    /// Whether the cloud held a revision this device had not taken in, as of the
    /// last ANSWERED probe. Meaningless unless
    /// <see cref="lastCloudProbeOutcome"/> is
    /// <see cref="CloudProbeOutcome.Answered"/>, and
    /// <see cref="CloudAlbumStatus.Evaluate"/> enforces that rather than trusting
    /// callers to remember.
    /// </summary>
    private bool lastCloudProbeFoundNewerRevision;

    /// <summary>
    /// The indicator's current state, built from what is actually known.
    /// </summary>
    private CloudAlbumStatus CurrentCloudAlbumStatus()
    {
        if (!state.HasOpenProject)
            return CloudAlbumStatus.Evaluate(false, 0, CloudProbeOutcome.NotAttempted, false);

        ProjectCloudLink cloud = state.Project.Cloud;
        bool linked =
            cloud.Origin.Equals(ProjectOrigins.Cloud, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(cloud.ServerProjectId);

        return CloudAlbumStatus.Evaluate(
            linked,
            (cloud.PendingAlbumComponentCodes ?? []).Count,
            lastCloudProbeOutcome,
            lastCloudProbeFoundNewerRevision);
    }

    /// <summary>
    /// The cheap check: does the cloud hold anything this device has not taken
    /// in?
    ///
    /// 🔴 ONE MECHANISM, TWO JOBS. It both colours the indicator and decides
    /// whether the canonical album needs downloading at all. Asking twice, or
    /// asking with a full album fetch, would make the indicator cost more than
    /// the action it describes.
    ///
    /// WHAT IT ACTUALLY COSTS, stated precisely because the cheap-check claim is
    /// the whole justification: where a concurrency token is held this is a
    /// conditional GET and the server answers 304 with no body. Where none is
    /// held - a first refresh, or a cache the client can no longer trust - it
    /// reads the project record in full. Both are the project JSON. Neither is
    /// the canonical album PDF, which measured 27.6 MB on the user's own
    /// project and is what this exists to avoid pulling.
    ///
    /// Every failure lands on <see cref="CloudProbeOutcome.Failed"/> rather than
    /// on a guess. Being unable to ask is a state worth showing, not a reason to
    /// assume the best.
    /// </summary>
    /// <summary>
    /// Records that the cloud check did not produce an answer. Kept in one
    /// place so no caller invents its own idea of what a failed probe leaves
    /// behind - in particular, it must never leave the previous ANSWERED state
    /// standing, which would keep a stale green on screen.
    /// </summary>
    private void RecordCloudProbeFailure(Exception exception)
    {
        _ = exception;
        lastCloudProbeOutcome = CloudProbeOutcome.Failed;
        lastCloudProbeFoundNewerRevision = false;
    }

    private async Task<CloudProbeOutcome> ProbeCloudAlbumAsync()
    {
        if (!state.HasOpenProject || !account.IsSignedIn)
        {
            lastCloudProbeOutcome = CloudProbeOutcome.NotAttempted;
            lastCloudProbeFoundNewerRevision = false;
            return lastCloudProbeOutcome;
        }

        ProjectCloudLink cloud = state.Project.Cloud;
        if (!cloud.Origin.Equals(ProjectOrigins.Cloud, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(cloud.ServerProjectId))
        {
            lastCloudProbeOutcome = CloudProbeOutcome.NotAttempted;
            lastCloudProbeFoundNewerRevision = false;
            return lastCloudProbeOutcome;
        }

        try
        {
            StudioCloudAlbumChangeSummaryResponse summary =
                await account.GetAlbumChangeSummaryAsync(cloud.ServerProjectId);

            // 🔴 THE PROJECT TOKEN, NOT THE REVISION ID. The server states that
            // reordering a component manifest changes the album everyone pulls
            // back WITHOUT creating a revision - currentRevisionId,
            // revisionNumber and pdfSha256 all sit still through it. An
            // indicator built on the revision id would therefore stay green
            // through the most ordinary change in a multi-member project: it
            // would have reached the cloud and been told the wrong thing.
            //
            // 🔴 COMPARED AGAINST THE TOKEN FROM THE LAST TIME THIS DEVICE WAS
            // LEVEL, not against the last one seen. The token moves on ANY
            // write, this device's own included, so comparing with the newest
            // token seen would turn the indicator orange the moment the user
            // uploaded their own work - reporting their own contribution as
            // somebody else's.
            string levelToken = (cloud.LastLevelCloudToken ?? "").Trim();
            string serverToken = (summary.ProjectConcurrencyToken ?? "").Trim();

            lastCloudProbeFoundNewerRevision =
                levelToken.Length > 0 &&
                serverToken.Length > 0 &&
                !serverToken.Equals(levelToken, StringComparison.Ordinal);

            // An empty token on either side is "not known", not "the same".
            // Before the first sync there is nothing to compare against, and a
            // server that answered without one has told us nothing.
            lastCloudProbeOutcome = levelToken.Length > 0 && serverToken.Length > 0
                ? CloudProbeOutcome.Answered
                : CloudProbeOutcome.NotAttempted;
            return lastCloudProbeOutcome;
        }
        catch (Exception exception) when (
            exception is StudioAccountException or HttpRequestException or TaskCanceledException or
                InvalidDataException)
        {
            // Offline, refused, or a server that cannot answer this question -
            // all of them mean the same thing to the person looking at the
            // screen: the cloud's side is not known.
            RecordCloudProbeFailure(exception);
            return lastCloudProbeOutcome;
        }
    }

    /// <summary>
    /// «Альбомыг шинэчлэх». Reads this device's sources, gives its contribution
    /// to the cloud, takes in everyone else's, puts the stored page order back in
    /// step, and says in numbers what each of those did.
    ///
    /// 🔴 THE VERDICTS ARE MEASURED, NOT REPORTED BY THE STEPS THEMSELVES. Each
    /// step's outcome is derived from observable project state before and after -
    /// how many components were still waiting to be sent, which album revision
    /// this device holds. A step that fails quietly therefore cannot tell this
    /// method it succeeded, which is precisely how "sync" used to end with a
    /// cheerful sentence over a failed fetch.
    /// </summary>
    private async Task RefreshAlbumAsync()
    {
        if (!state.HasOpenProject)
        {
            SetStatus("Альбом шинэчлэх боломжгүй: нээлттэй төсөл алга.");
            return;
        }

        if (albumRefreshInProgress)
        {
            SetStatus("Альбомын шинэчлэл аль хэдийн ажиллаж байна.");
            return;
        }

        albumRefreshInProgress = true;
        RefreshCloudAlbumIndicator();
        var steps = new List<AlbumRefreshStepResult>();
        try
        {
            ProjectCloudLink cloud = state.Project.Cloud;
            bool linked =
                cloud.Origin.Equals(ProjectOrigins.Cloud, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(cloud.ServerProjectId);

            // ---- Step 1: read this device's own sources -------------------
            //
            // Awaited, and its own counts are used. An earlier draft called the
            // old void version and inferred "how much changed" from the album's
            // page count afterwards - which measured nothing, because the
            // refresh queued its real work and returned before doing it.
            SourceRefreshOutcome sources = await CheckForSourceUpdatesAsync();
            if (!sources.Succeeded)
            {
                // Steps 1-2 failing means NOTHING left this machine, and the
                // report has to be able to say so - so the run stops here
                // rather than pressing on and sending a half-read album.
                steps.Add(AlbumRefreshReport.SourcesFailed(
                    string.IsNullOrWhiteSpace(sources.FailureMn)
                        ? "шалтгаан тодорхойгүй."
                        : sources.FailureMn));
                FinishAlbumRefresh(steps);
                return;
            }

            steps.Add(AlbumRefreshReport.SourcesRead(
                sources.CheckedCount,
                sources.ChangedCount));

            if (!linked)
            {
                // A local-only project has no cloud half. Step 4 still runs,
                // because the stored order is a local concern.
                steps.Add(AlbumRefreshReport.ContributionSent(0));
                steps.Add(AlbumRefreshReport.CloudFetched(false, ""));
                steps.Add(RecomposeStep());
                FinishAlbumRefresh(steps);
                return;
            }

            // ---- Steps 2 and 3: give ours, take theirs --------------------
            int pendingBefore = (cloud.PendingAlbumComponentCodes ?? []).Count;
            string revisionBefore = cloud.LastReceivedAlbumRevisionId ?? "";

            await SynchronizeCurrentProjectAsync();

            ProjectCloudLink after = state.Project.Cloud;
            int pendingAfter = (after.PendingAlbumComponentCodes ?? []).Count;
            string revisionAfter = after.LastReceivedAlbumRevisionId ?? "";

            steps.Add(pendingBefore == 0
                ? AlbumRefreshReport.ContributionSent(0)
                : pendingAfter < pendingBefore
                    ? AlbumRefreshReport.ContributionSent(pendingBefore - pendingAfter)
                    : AlbumRefreshReport.ContributionFailed(
                        $"{pendingBefore} хэсэг хүлээгдсэн хэвээр."));

            // The probe runs AFTER the sync, so its answer describes the state
            // the person is being told about rather than the one before it.
            CloudProbeOutcome probe = await ProbeCloudAlbumAsync();
            bool revisionMoved = !revisionAfter.Equals(revisionBefore, StringComparison.OrdinalIgnoreCase);

            steps.Add(probe switch
            {
                // Still behind after a sync means the fetch did not complete,
                // whatever the sync said on its way past.
                CloudProbeOutcome.Answered when lastCloudProbeFoundNewerRevision =>
                    AlbumRefreshReport.CloudFetchFailed("үүлэнд шинэ хувилбар үлдсэн хэвээр."),
                CloudProbeOutcome.Answered =>
                    AlbumRefreshReport.CloudFetched(revisionMoved, revisionAfter),
                CloudProbeOutcome.Failed =>
                    AlbumRefreshReport.CloudFetchFailed("үүлэн талыг шалгаж чадсангүй."),
                _ => AlbumRefreshReport.CloudFetchFailed("үүлэн шалгалт хийгдээгүй."),
            });

            // ---- Step 4: the stored page order ----------------------------
            steps.Add(RecomposeStep());
            FinishAlbumRefresh(steps);
        }
        finally
        {
            albumRefreshInProgress = false;
            RefreshCloudAlbumIndicator();
            RefreshSyncUi();
        }
    }

    private AlbumRefreshStepResult RecomposeStep()
    {
        try
        {
            AlbumOrderHealResult heal = state.EnsureStoredAlbumOrder();
            return heal.Ran
                ? AlbumRefreshReport.PagesRecomposed(heal.PageCount, heal.MovedCount)
                : AlbumRefreshReport.RecomposeFailed(
                    "номын сан эсвэл хуудас хоосон тул дараалал шалгагдсангүй.");
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return AlbumRefreshReport.RecomposeFailed(exception.Message);
        }
    }

    private void FinishAlbumRefresh(IReadOnlyList<AlbumRefreshStepResult> steps)
    {
        AlbumRefreshReport report = AlbumRefreshReport.Create(steps, CurrentAlbumOnScreen());
        lastAlbumRefreshReport = report;
        // The whole line, numbers included. The verdict alone is what the three
        // old commands already said, and it is what made a run that did
        // something indistinguishable from one that did not.
        SetStatus(report.StatusLineMn);
        RefreshCloudAlbumIndicator();
    }

    /// <summary>
    /// Which album the screen is showing. Named on every report, because the
    /// local build and the server's assembly differ exactly when something has
    /// gone wrong or is still in flight - the moment a person most needs to know
    /// which one they are looking at.
    /// </summary>
    private AlbumOnScreen CurrentAlbumOnScreen()
    {
        if (!state.HasOpenProject)
            return AlbumOnScreen.None;

        string? shown = ResolveAlbumPreviewPath();
        if (string.IsNullOrWhiteSpace(shown))
            return AlbumOnScreen.None;

        string? canonical = ResolveLastReceivedCloudAlbumPath();
        return !string.IsNullOrWhiteSpace(canonical) &&
               shown.Equals(canonical, StringComparison.OrdinalIgnoreCase)
            ? AlbumOnScreen.CloudCanonical
            : AlbumOnScreen.LocalPreview;
    }
}
