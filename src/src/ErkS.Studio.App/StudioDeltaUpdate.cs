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
    public const string UpdaterFolder = "updater";

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
        if (InstalledVersion is null || !File.Exists(Path.Combine(InstallRoot, UpdaterFolder, "ErkS.Updates.Apply.exe")))
            return StudioDeltaResult.None;

        var downloader = new DeltaDownloader(http, server, StudioReleaseInfo.ProductCode);
        UpdateManifest? target;
        try
        {
            target = await downloader.GetManifestAsync(update.Version, null, cancellationToken).ConfigureAwait(true);
        }
        catch (HttpRequestException)
        {
            return StudioDeltaResult.None; // the full installer still works
        }
        if (target is null)
            return StudioDeltaResult.None;

        UpdateManifest installed = InstallManifest.LoadOrScan(InstallRoot, StudioReleaseInfo.ProductCode, InstalledVersion ?? "?");
        // The installer is compressed: comparing with the uncompressed layout keeps the delta whenever it is plainly smaller.
        UpdatePlan plan = UpdatePlanner.Plan(installed, target, fullInstallerBytes: target.TotalBytes);
        if (plan.UseFullInstaller || plan.IsEmpty)
            return StudioDeltaResult.None;

        string staging = Path.Combine(
            UpdatesRoot,
            "delta-" + string.Concat(target.Version.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character)));
        Directory.CreateDirectory(staging);
        var bytes = new Progress<UpdateProgress>(value => progress?.Report(new StudioUpdateProgress(value.Percent, "Өөрчлөгдсөн хэсгийг татаж байна: " + value.Text)));
        await downloader.DownloadAsync(plan, staging, bytes, cancellationToken).ConfigureAwait(true);
        File.WriteAllText(Path.Combine(staging, "target.json"), target.ToJson());

        LiveApplyResult live = LiveOverlay.ApplyLive(InstallRoot, staging, plan, downloader.LastSignedManifestJson ?? "");
        if (live.Applied.Count > 0)
            ReloadRequested?.Invoke(live.Applied);
        if (plan.NeedsRestart || live.Deferred.Count > 0)
            return new StudioDeltaResult(StudioDeltaOutcome.ReadyToRestart, staging, live.Applied, live.Deferred);
        UpdateStaging.Finished(staging); // all in place: the staged files are not kept
        return new StudioDeltaResult(StudioDeltaOutcome.AppliedLive, staging, live.Applied, live.Deferred);
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
        string source = Path.Combine(InstallRoot, UpdaterFolder);
        string copy = Path.Combine(Path.GetTempPath(), "ErkS-Updates-Apply-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(copy);
        foreach (string file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(copy, Path.GetFileName(file)), overwrite: true);
        // The update's own applier when it carries a newer one (checked against the signed manifest
        // like every staged file): a fix in the applier works on the very update that brings it.
        try
        {
            UpdateManifest target = UpdateManifest.FromJson(File.ReadAllText(Path.Combine(staging, "target.json")));
            foreach (UpdateFile file in target.Files.Where(file => file.Path.StartsWith(UpdaterFolder + "/", StringComparison.OrdinalIgnoreCase) && !file.Path[(UpdaterFolder.Length + 1)..].Contains('/')))
            {
                string staged = DeltaDownloader.StagedPath(staging, file.Sha256);
                if (File.Exists(staged) && Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(staged))).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                    File.Copy(staged, Path.Combine(copy, file.Path[(UpdaterFolder.Length + 1)..]), overwrite: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
        {
            // The installed applier still does the work.
        }

        string syncExe = Path.Combine(InstallRoot, "ErkS.Sync.exe");
        bool syncWasRunning = SyncProtocol.TrySend(new SyncPipeRequest("quit", "")) is not null;
        var start = new ProcessStartInfo
        {
            FileName = Path.Combine(copy, "ErkS.Updates.Apply.exe"),
            WorkingDirectory = copy,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string argument in new[]
        {
            "--install", InstallRoot.TrimEnd(Path.DirectorySeparatorChar),
            "--staging", staging,
            "--wait", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--close", "ErkS.Sync",
        })
        {
            start.ArgumentList.Add(argument);
        }
        if (syncWasRunning && File.Exists(syncExe))
        {
            start.ArgumentList.Add("--launch");
            start.ArgumentList.Add("ErkS.Sync.exe");
        }
        if (relaunchStudio)
        {
            start.ArgumentList.Add("--launch");
            start.ArgumentList.Add("ErkS.Studio.exe");
        }
        _ = Process.Start(start) ?? throw new InvalidOperationException("Шинэчлэгч эхэлсэнгүй.");
        ApplierLaunched = true;
    }
}
