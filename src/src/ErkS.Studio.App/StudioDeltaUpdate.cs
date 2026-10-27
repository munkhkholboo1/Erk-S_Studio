using System.Diagnostics;
using System.IO;
using System.Net.Http;
using ErkS.Sync.Core;
using ErkS.Updates;

namespace ErkS.Studio;

internal enum StudioDeltaOutcome
{
    /// <summary>No install manifest here, no manifest on the server, or the delta is not worth it: the full installer runs.</summary>
    NotAvailable,

    /// <summary>Only «live» files changed and they are in place: nothing closes.</summary>
    AppliedLive,

    /// <summary>Staged; the program closes and <see cref="StudioDeltaUpdate.LaunchApplier"/> puts it in place.</summary>
    ReadyToRestart,
}

internal sealed record StudioDeltaResult(
    StudioDeltaOutcome Outcome,
    string Staging,
    IReadOnlyList<string> Live,
    IReadOnlyList<string> Deferred)
{
    public static readonly StudioDeltaResult None = new(StudioDeltaOutcome.NotAvailable, "", [], []);
}

/// <summary>
/// Studio's side of the platform delta update (contract _shared/delta-update-contract-2026-10-26.md):
/// owner, 2026-10-26 - «зөвхөн өөрчлөгдсөн хэсгүүдийг нь update хийдэг болгуулаарай». Every rule
/// lives in ErkS.Updates; this only names Studio's folder, its helpers and how it closes.
/// </summary>
internal static class StudioDeltaUpdate
{
    /// <summary>The applier ships inside the install, in its own folder, and runs from a temporary copy.</summary>
    public const string UpdaterFolder = ProductUpdate.UpdaterFolder;

    public static string InstallRoot => AppContext.BaseDirectory;

    private static string UpdatesRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Erk-S Studio", "Updates");

    /// <summary>
    /// The applier's sentence when the last update could not be put in place (another Studio window
    /// held its files …) - returned once, then forgotten; null when nothing failed. Then the update
    /// folders of versions already installed go (Master, 2026-10-27: a folder per release since July).
    /// </summary>
    public static string? TakeFailedApply()
    {
        try
        {
            return FailedSentence();
        }
        finally
        {
            try
            {
                UpdateStaging.Tidy(UpdatesRoot, InstalledVersion ?? StudioReleaseInfo.ShortVersion);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static string? FailedSentence()
    {
        try
        {
            if (!Directory.Exists(UpdatesRoot))
                return null;
            foreach (string folder in Directory.EnumerateDirectories(UpdatesRoot, "delta-*"))
            {
                string failed = Path.Combine(folder, "failed.txt");
                if (!File.Exists(failed))
                    continue;
                string sentence = File.ReadAllText(failed).Trim();
                File.Delete(failed);
                if (sentence.Length > 0)
                    return sentence;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
        return null;
    }

    /// <summary>The installed version when an installer laid the manifest (a live-only update moves it without a new exe).</summary>
    public static string? InstalledVersion => InstallManifest.TryReadVersion(InstallRoot);

    /// <summary>Raised with the install-relative paths of «live» files just replaced (contract 5a).</summary>
    public static event Action<IReadOnlyList<string>>? ReloadRequested;

    public static async Task<StudioDeltaResult> TryAsync(
        HttpClient http,
        Uri server,
        StudioUpdateLatestResponse update,
        IProgress<StudioUpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        // The one product update (ErkS.Updates.ProductUpdate) - Erk-S Sync runs the same while Studio is closed.
        var bytes = new Progress<UpdateProgress>(value => progress?.Report(new StudioUpdateProgress(value.Percent, "Өөрчлөгдсөн хэсгийг татаж байна: " + value.Text)));
        ProductUpdateStage stage = await ProductUpdate.StageAsync(
            http, server, StudioReleaseInfo.ProductCode, InstallRoot, update.Version, UpdatesRoot, bytes, cancellationToken).ConfigureAwait(true);
        if (stage.Live.Count > 0)
            ReloadRequested?.Invoke(stage.Live);
        return stage.Outcome switch
        {
            ProductUpdateOutcome.AppliedLive => new StudioDeltaResult(StudioDeltaOutcome.AppliedLive, stage.Staging, stage.Live, stage.Deferred),
            ProductUpdateOutcome.ReadyToRestart => new StudioDeltaResult(StudioDeltaOutcome.ReadyToRestart, stage.Staging, stage.Live, stage.Deferred),
            _ => StudioDeltaResult.None,
        };
    }

    /// <summary>
    /// Starts the applier from a temporary copy: it waits for this Studio to close, asks nothing,
    /// puts the staged files in place (rollback on failure) and starts Sync and Studio again.
    /// Erk-S Sync runs from the same folder, so it is asked to quit first.
    /// </summary>
    /// <summary>Whether this Studio already started an applier (the dialog, or the close) - never two.</summary>
    public static bool ApplierLaunched { get; private set; }

    /// <param name="relaunchStudio">False when the person closed Studio themselves: the update goes in, Studio stays closed.</param>
    public static void LaunchApplier(string staging, bool relaunchStudio = true)
    {
        if (ApplierLaunched)
            return;
        string syncExe = Path.Combine(InstallRoot, "ErkS.Sync.exe");
        bool syncWasRunning = SyncProtocol.TrySend(new SyncPipeRequest("quit", "")) is not null;
        var launch = new List<string>();
        if (syncWasRunning && File.Exists(syncExe))
            launch.Add("ErkS.Sync.exe");
        if (relaunchStudio)
            launch.Add("ErkS.Studio.exe");
        _ = ProductUpdate.LaunchApplier(InstallRoot, staging, [Environment.ProcessId], ["ErkS.Sync"], launch);
        ApplierLaunched = true;
    }
}
