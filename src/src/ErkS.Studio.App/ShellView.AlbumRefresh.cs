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

    /// <summary>
    /// Paints the cloud icon on the project card.
    ///
    /// 🔴 IT PAINTS THE ICON THAT WAS ALREADY THERE. The first version of this
    /// put a coloured badge on the album toolbar - a fourth item on the very row
    /// the user is trying to reduce to one. What they asked for was that the
    /// cloud they already had be told apart by colour.
    ///
    /// 🔴 COLOUR IS THE REDUNDANT CHANNEL, NOT THE CARRIER. The count under the
    /// glyph and the tooltip say the same thing the colour does, so the state is
    /// readable with no colour vision at all. A screen where colour is the only
    /// difference between "everything is shared" and "somebody is waiting on
    /// you" is unreadable for roughly one man in twelve.
    /// </summary>
    private void RefreshCloudAlbumIndicator()
    {
        if (cloudStateGlyph is null)
            return;

        CloudAlbumStatus status = CurrentCloudAlbumStatus();
        if (!status.ShouldShow)
        {
            // A local-only project has no cloud to be behind ON. The icon keeps
            // its ordinary look rather than showing a state it cannot have.
            cloudStateGlyph.Foreground = StudioTheme.MutedTextBrush;
            cloudStateCount.Text = "";
            return;
        }

        Brush colour = IndicatorBrush(status.State);
        cloudStateGlyph.Foreground = colour;
        cloudStateCount.Foreground = colour;
        cloudStateCount.Text = albumRefreshInProgress ? "…" : CountTextOf(status);
        cloudSyncButton.ToolTip = albumRefreshInProgress
            ? "Альбомыг шинэчилж байна…"
            : status.SummaryMn;
    }

    /// <summary>
    /// The numbers under the glyph. Deliberately not the glyph shapes used
    /// before: the icon IS the shape now, and stacking a second one under it
    /// would say the same thing twice in less room.
    /// </summary>
    private static string CountTextOf(CloudAlbumStatus status) => status.State switch
    {
        CloudAlbumChangeState.OwnWaiting => status.OwnWaitingCount.ToString(),
        CloudAlbumChangeState.OthersWaiting => "•",
        CloudAlbumChangeState.BothWaiting => status.OwnWaitingCount + "•",
        CloudAlbumChangeState.Unknown when status.OwnWaitingCount > 0 =>
            "?" + status.OwnWaitingCount,
        CloudAlbumChangeState.Unknown => "?",
        _ => "",
    };

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

        (int sendableComponents, int blocked) = PendingSplit(cloud);

        // 🔴 THE INDICATOR USED TO READ ONE BUCKET OF SIX. It counted album
        // components and nothing else, so a project with two changed sources
        // waiting to go up painted GREEN and said «✓ Шинэчлэлт алга». The owner
        // read that as «everything is delivered» - it was the opposite - and
        // carried on for a morning.
        //
        // Every kind is counted now, and StudioPendingWork is positional so a
        // seventh kind cannot be added without breaking this line.
        //
        // Counted STRUCTURALLY: which buckets hold something, never whether each
        // piece could be sent from here. That second question hashes files, and
        // asking it from the indicator is what froze the window.
        StudioPendingWork pending = StudioPendingWork.Of(state.Project);
        int sendable = Math.Max(
            sendableComponents,
            pending.Total - blocked);

        return CloudAlbumStatus.Evaluate(
            linked,
            sendable,
            lastCloudProbeOutcome,
            lastCloudProbeFoundNewerRevision,
            blocked);
    }

    private string pendingSplitSignature = "\u0000";
    private int cachedSendableCount;
    private int cachedBlockedCount;

    /// <summary>
    /// How many waiting components this device could actually send, and how
    /// many it could not.
    ///
    /// 🔴 CACHED, AND THAT IS NOT AN OPTIMISATION - IT IS THE FIX FOR A FREEZE
    /// THIS METHOD CAUSED. The split is decided by the sync's own rule, and
    /// that rule asks whether this device holds the payload for each auxiliary
    /// document and visualisation - which it answers by SHA-256 HASHING THE
    /// FILE. Reached from the indicator, which is repainted from RefreshSyncUi,
    /// which runs on two dozen ordinary UI paths, that put a full read-and-hash
    /// of every document and image on the UI THREAD of every refresh. The
    /// application stopped responding on a single button press, and it had been
    /// fine that morning.
    ///
    /// The lesson is not "cache things": it is that an indicator must cost less
    /// than the action it describes, and this one silently cost more. The
    /// expensive answer is computed when the WAITING SET CHANGES - the only
    /// thing that can change it in practice - and read from a field otherwise.
    ///
    /// The signature starts at a value the real one can never take, so the
    /// first call always computes rather than trusting a zero.
    /// </summary>
    private (int Sendable, int Blocked) PendingSplit(ProjectCloudLink cloud)
    {
        IReadOnlyList<string> pending = cloud.PendingAlbumComponentCodes ?? [];
        if (pending.Count == 0)
        {
            pendingSplitSignature = "";
            cachedSendableCount = 0;
            cachedBlockedCount = 0;
            return (0, 0);
        }

        string signature = string.Join("\u0001", pending);
        if (signature.Equals(pendingSplitSignature, StringComparison.Ordinal))
            return (cachedSendableCount, cachedBlockedCount);

        string ownerEmail = CurrentCloudOwnerEmail();
        var renderable = StudioAlbumRendererMigration
            .SelectLocallyRenderableComponents(
                state.Project,
                cloud.SharedAlbumComponents ?? [],
                ownerEmail,
                HasOwnedAtdDocuments(ownerEmail),
                HasLocalVisualizationImages(),
                ProjectCloudSyncAuthority.CanManageCanonicalMetadata(cloud, ownerEmail))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        int sendable = 0;
        int blocked = 0;
        foreach (string code in pending)
        {
            if (renderable.Contains(code))
                sendable++;
            else
                blocked++;
        }

        pendingSplitSignature = signature;
        cachedSendableCount = sendable;
        cachedBlockedCount = blocked;
        return (sendable, blocked);
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
            // 🔴 THE EXPENSIVE HALF RUNS ONLY WHEN SOMETHING IS WAITING.
            //
            // MEASURED, on the user's own project: loading and verifying the 45
            // source packages takes 5 531 ms. That is the whole freeze - press
            // the cloud, and the window stops answering for five and a half
            // seconds while Windows greys it out. It reads as the application
            // having vanished.
            //
            // 🔴 AND IT IS A MEASUREMENT I ALREADY HAD WRONG. Earlier I timed
            // SHA-256 over every source PDF - 426 ms for 348 MB - and concluded
            // "reading the sources is cheap, no incremental mode needed". The
            // hashing IS cheap. The package LOAD, which is a different thing
            // entirely, is thirteen times more expensive, and I never measured
            // it before building an action that runs it on every press.
            //
            // The inbox survey reads manifest HEADERS only and answers the one
            // question that matters - is there a delivery this project has not
            // absorbed - so the common press costs nothing at all.
            bool anyDeliveryWaiting = state.Project.Sources
                .Any(source => SurveyPendingDeliveries(source).Any);

            // 🔴 THE SKIP IS NAMED, NOT DRESSED UP AS A RESULT. This branch used
            // to build SourceRefreshOutcome.Completed(Sources.Count, 0) and
            // report «N шалгав, 0 өөрчлөгдсөн» - a count of sources presented as
            // a count of sources CHECKED, when nothing had been. The owner had
            // changed exactly three sources, the project had exactly three, and
            // the sentence told them their three files had been examined and
            // found unchanged.
            //
            // The outcome is no longer constructed at all on this path, so the
            // invented number has nowhere to come from.
            int changedCount = 0;
            if (!anyDeliveryWaiting)
            {
                steps.Add(AlbumRefreshReport.SourcesNotChecked(
                    "хүлээгдэж буй шинэ багц алга тул уншаагүй. " +
                    "Зураг засварласан бол Revit/AutoCAD-аасаа «Studio руу илгээх» " +
                    "хийсний дараа энэ товч түүнийг авна."));
            }
            else
            {
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

                changedCount = sources.ChangedCount;
                steps.Add(AlbumRefreshReport.SourcesRead(
                    sources.CheckedCount,
                    sources.ChangedCount));
            }

            if (!linked)
            {
                // A local-only project has no cloud half. Step 4 still runs,
                // because the stored order is a local concern.
                steps.Add(AlbumRefreshReport.SkippedForLocalProject(
                    AlbumRefreshStep.SendOwnContribution));
                steps.Add(AlbumRefreshReport.SkippedForLocalProject(
                    AlbumRefreshStep.FetchCloudUpdates));
                steps.Add(RecomposeStep());
                FinishAlbumRefresh(steps);
                return;
            }

            // ---- Steps 2 and 3: give ours, take theirs --------------------
            //
            // 🔴 THE WORK «Бүрэн дахин байгуулах» USED TO DO, FOLDED IN. That
            // command marked this device's own album components for redrawing
            // so a rebuild reached the SHARED album rather than only the local
            // copy. Removing the button without this would have quietly dropped
            // that: the pages would look right here and the other members would
            // keep receiving the old ones.
            //
            // It runs when the sources actually changed, which is the condition
            // that made it a decision worth a button in the first place. A
            // person should not have to know that redrawing their own
            // components is a separate idea from refreshing the album - it is
            // our distinction, not theirs.
            //
            // Marked BEFORE the count below is taken, so the components it adds
            // are included in "N хэсэг үүл рүү өгөв" rather than silently
            // missing from it.
            if (changedCount > 0)
                _ = MarkOwnAlbumComponentsForRerender();

            // 🔴 COUNTED ACROSS EVERY KIND OF UNSENT WORK, not album components
            // alone. Reading one bucket is what told the owner «Таны оруулга:
            // илгээх зүйл байсангүй» while two changed sources went up in the
            // same run - the report simply could not see them.
            int pendingBefore = StudioPendingWork.Of(state.Project).Total;
            string revisionBefore = cloud.LastReceivedAlbumRevisionId ?? "";

            await SynchronizeCurrentProjectAsync();

            ProjectCloudLink after = state.Project.Cloud;
            int pendingAfter = StudioPendingWork.Of(state.Project).Total;
            string revisionAfter = after.LastReceivedAlbumRevisionId ?? "";

            // 🔴 "NOTHING MOVED" HAS TWO CAUSES AND THEY READ DIFFERENTLY. If
            // everything still waiting is work this device cannot produce, the
            // run did exactly what it could and saying "failed" would tell the
            // person to press again - which is what they had already been doing
            // when nothing happened. Only genuinely sendable work that stayed
            // put is a failure.
            int blockedNow = CurrentCloudAlbumStatus().BlockedCount;
            steps.Add(pendingBefore == 0
                ? AlbumRefreshReport.ContributionSent(0)
                : pendingAfter < pendingBefore
                    ? AlbumRefreshReport.ContributionSent(pendingBefore - pendingAfter)
                    : blockedNow >= pendingAfter
                        ? AlbumRefreshReport.ContributionBlocked(blockedNow)
                        : AlbumRefreshReport.ContributionFailed(
                            $"{pendingAfter - blockedNow} хэсэг хүлээгдсэн хэвээр."));

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
        catch (Exception exception)
        {
            // 🔴 THE HANDLER THAT RUNS THIS IS `async void`, SO AN ESCAPING
            // EXCEPTION KILLS THE PROCESS. The user pressed the cloud and the
            // application vanished - «нэг товч дараад л гацаад алга болчихоод
            // байхын». Every step here reaches disk, the network and another
            // member's data; something WILL throw, and the answer to that has
            // to be a sentence on the status bar, not a closed window.
            //
            // Caught broadly on purpose. A typed list would be a list of the
            // failures thought of in advance, and the one that closes the app
            // is by definition the one nobody thought of.
            steps.Add(AlbumRefreshReport.RecomposeFailed(exception.Message));
            FinishAlbumRefresh(steps);
            SetStatus("Альбом шинэчлэхэд алдаа гарлаа: " + exception.Message);
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
