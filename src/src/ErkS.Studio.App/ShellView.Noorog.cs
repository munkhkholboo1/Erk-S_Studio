using System.Net.Http;
using ErkS.Platform.Core;

namespace ErkS.Studio;

/// <summary>
/// Noorog packages (erk-s.mn/noorog) into the open project: read when the project opens, on
/// «Эх үүсвэрээс шинэчлэх» and when the server says one arrived («noorog-delivery», through
/// Erk-S Sync) - never on a clock. The intake then takes them like any source's delivery.
/// </summary>
internal sealed partial class ShellView
{
    private bool noorogFetchInProgress;

    private bool noorogFetchAgain;

    private NoorogFetchReport lastNoorogFetch = NoorogFetchReport.Nothing;

    /// <summary>The server project id of the open project, when it has one.</summary>
    private string? OpenServerProjectId() =>
        state.HasOpenProject && state.Project.Cloud.ServerProjectId is { Length: > 0 } id ? id.Trim() : null;

    private async Task OnNoorogDeliveryArrivedAsync(string projectId)
    {
        if (OpenServerProjectId() is not { } open || !open.Equals(projectId, StringComparison.OrdinalIgnoreCase))
            return;
        NoorogFetchReport report = await FetchNoorogDeliveriesAsync();
        if (report.Fetched > 0 && !sourceRefreshInProgress)
            await CheckForSourceUpdatesAsync();
    }

    /// <summary>After a project opened: what arrived while it was closed.</summary>
    private async Task FetchNoorogDeliveriesAfterOpenAsync()
    {
        NoorogFetchReport report = await FetchNoorogDeliveriesAsync();
        if (report.Fetched > 0 && !sourceRefreshInProgress)
            await CheckForSourceUpdatesAsync();
    }

    /// <summary>
    /// Brings this project's Noorog packages that are not here yet. Quiet when there is nothing (a
    /// project without Noorog, an older server without the route); a failure is said in the status line.
    /// </summary>
    private async Task<NoorogFetchReport> FetchNoorogDeliveriesAsync()
    {
        if (OpenServerProjectId() is not { } projectId ||
            account.Current?.Email is not { Length: > 0 } email ||
            !CanEditProjectContent())
        {
            return NoorogFetchReport.Nothing;
        }
        if (noorogFetchInProgress)
        {
            noorogFetchAgain = true;
            return NoorogFetchReport.Nothing;
        }

        noorogFetchInProgress = true;
        NoorogFetchReport total = NoorogFetchReport.Nothing;
        try
        {
            do
            {
                noorogFetchAgain = false;
                NoorogFetchReport report = await NoorogDeliveryIntake.FetchAsync(
                    account,
                    projectId,
                    delivery => NoorogSlotFor(delivery, projectId, email));
                total = new NoorogFetchReport(
                    report.Listed,
                    total.Fetched + report.Fetched,
                    report.AlreadyHere,
                    report.Elsewhere,
                    [.. total.Failures, .. report.Failures]);
            }
            while (noorogFetchAgain && OpenServerProjectId() == projectId);
        }
        catch (StudioAccountException exception) when (IsMissingRoute(exception))
        {
            return NoorogFetchReport.Nothing;
        }
        catch (Exception exception) when (exception is StudioAccountException or HttpRequestException or TaskCanceledException or System.IO.IOException or UnauthorizedAccessException)
        {
            SetStatus("Ноорогийн багц уншигдсангүй: " + exception.Message);
            return NoorogFetchReport.Nothing;
        }
        finally
        {
            noorogFetchInProgress = false;
        }

        lastNoorogFetch = total;
        if (total.Fetched > 0)
            SetStatus($"Ноорогоос {total.Fetched} шинэ багц ирлээ.");
        if (total.Failures.Count > 0)
            SetStatus("Ноорог: " + string.Join(" · ", total.Failures.Take(3)));
        return total;
    }

    private static bool IsMissingRoute(StudioAccountException exception) =>
        exception.StatusCode == System.Net.HttpStatusCode.NotFound;

    /// <summary>
    /// Where a board's packages go on this device. A board the project does not know yet becomes
    /// its source here (bound to this account and device, like «Эх үүсвэр нэмэх»); a board whose
    /// source is bound to another device is left to it - its pages reach this one through the cloud.
    /// </summary>
    private NoorogSourceSlot NoorogSlotFor(NoorogDelivery delivery, string projectId, string email)
    {
        if (OpenServerProjectId() != projectId)
            return NoorogSourceSlot.Elsewhere("төсөл солигдсон");
        ProjectDesignSource? source = FindSource(delivery.SourceId);
        if (source is null)
        {
            RegisterDesignSource(NoorogDeliveryIntake.NewSource(delivery));
            source = FindSource(delivery.SourceId);
            if (source is null)
                return NoorogSourceSlot.Elsewhere("эх үүсвэр бүртгэгдсэнгүй");
        }
        // The binding and the cloud authority, not the payload: a new board's inbox is still empty.
        bool here = StudioRuntimeSourceScope.IsAuthorizedLocal(
            state.Project,
            source,
            email,
            StudioDeviceIdentity.Fingerprint,
            static _ => true);
        return here && !string.IsNullOrWhiteSpace(source.InboxFolder)
            ? NoorogSourceSlot.Inbox(source.InboxFolder)
            : NoorogSourceSlot.Elsewhere($"«{source.Name}» өөр төхөөрөмжид холбогдсон");

        ProjectDesignSource? FindSource(string sourceId) =>
            state.Project.Sources.FirstOrDefault(item => item.Id.Equals(sourceId.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}
