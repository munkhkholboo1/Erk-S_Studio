using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ErkS.Updates;

public enum ProductUpdateOutcome
{
    /// <summary>No install manifest, no manifest on the server, or the delta is not worth it: the full installer.</summary>
    NotAvailable,

    /// <summary>Only «live» files changed and they are in place: nothing closes.</summary>
    AppliedLive,

    /// <summary>Staged; the program closes and the applier puts it in place.</summary>
    ReadyToRestart,
}

/// <summary>What staging an update did (a class, not a record: the library also builds for net48).</summary>
public sealed class ProductUpdateStage
{
    public ProductUpdateStage(ProductUpdateOutcome outcome, string staging, IReadOnlyList<string> live, IReadOnlyList<string> deferred)
    {
        Outcome = outcome;
        Staging = staging;
        Live = live;
        Deferred = deferred;
    }

    public static readonly ProductUpdateStage None = new(ProductUpdateOutcome.NotAvailable, "", Array.Empty<string>(), Array.Empty<string>());

    public ProductUpdateOutcome Outcome { get; }

    public string Staging { get; }

    public IReadOnlyList<string> Live { get; }

    public IReadOnlyList<string> Deferred { get; }
}

/// <summary>
/// One product's delta update, start to applier - the same for a program open in front of the person
/// (Studio) and for one working in the background (Erk-S Sync while Studio is closed; Erk-S Sync,
/// 2026-10-27: «гишүүн Studio нээгээгүй байсан ч синкийн протоколын засвар ирэх ёстой»).
/// </summary>
public static class ProductUpdate
{
    /// <summary>The applier ships inside the install, in its own folder, and runs from a temporary copy.</summary>
    public const string UpdaterFolder = "updater";

    /// <summary>
    /// Fetches the signed manifest of <paramref name="version"/>, plans against what is installed, downloads
    /// only the changed files into «&lt;updatesRoot&gt;\delta-&lt;version&gt;» and puts the «live» ones in place.
    /// </summary>
    public static async Task<ProductUpdateStage> StageAsync(
        HttpClient http,
        Uri server,
        string productCode,
        string installRoot,
        string version,
        string updatesRoot,
        IProgress<UpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        string? installedVersion = InstallManifest.TryReadVersion(installRoot);
        if (installedVersion is null || !File.Exists(Path.Combine(installRoot, UpdaterFolder, "ErkS.Updates.Apply.exe")))
            return ProductUpdateStage.None;

        var downloader = new DeltaDownloader(http, server, productCode);
        UpdateManifest? target;
        try
        {
            target = await downloader.GetManifestAsync(version, null, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            return ProductUpdateStage.None; // the full installer still works
        }
        if (target is null)
            return ProductUpdateStage.None;

        UpdateManifest installed = InstallManifest.LoadOrScan(installRoot, productCode, installedVersion);
        // The installer is compressed: comparing with the uncompressed layout keeps the delta whenever it is plainly smaller.
        UpdatePlan plan = UpdatePlanner.Plan(installed, target, fullInstallerBytes: target.TotalBytes);
        if (plan.UseFullInstaller || plan.IsEmpty)
            return ProductUpdateStage.None;

        string staging = Path.Combine(
            updatesRoot,
            "delta-" + string.Concat(target.Version.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character)));
        Directory.CreateDirectory(staging);
        await downloader.DownloadAsync(plan, staging, progress, cancellationToken).ConfigureAwait(false);
        File.WriteAllText(Path.Combine(staging, "target.json"), target.ToJson());

        LiveApplyResult live = LiveOverlay.ApplyLive(installRoot, staging, plan, downloader.LastSignedManifestJson ?? "");
        if (plan.NeedsRestart || live.Deferred.Count > 0)
            return new ProductUpdateStage(ProductUpdateOutcome.ReadyToRestart, staging, live.Applied, live.Deferred);
        UpdateStaging.Finished(staging); // all in place: the staged files are not kept
        return new ProductUpdateStage(ProductUpdateOutcome.AppliedLive, staging, live.Applied, live.Deferred);
    }

    /// <summary>
    /// Starts the applier from a temporary copy - the update's own applier when it carries a newer one
    /// (checked against the signed manifest like every staged file). It waits for <paramref name="waitFor"/>,
    /// asks <paramref name="close"/> to end, puts the staged files in place and starts <paramref name="launch"/>.
    /// </summary>
    public static Process LaunchApplier(string installRoot, string staging, IEnumerable<int> waitFor, IEnumerable<string> close, IEnumerable<string> launch)
    {
        string copy = Path.Combine(Path.GetTempPath(), "ErkS-Updates-Apply-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(copy);
        foreach (string file in Directory.EnumerateFiles(Path.Combine(installRoot, UpdaterFolder)))
            File.Copy(file, Path.Combine(copy, Path.GetFileName(file)), overwrite: true);
        try
        {
            UpdateManifest target = UpdateManifest.FromJson(File.ReadAllText(Path.Combine(staging, "target.json")));
            foreach (UpdateFile file in target.Files.Where(file => file.Path.StartsWith(UpdaterFolder + "/", StringComparison.OrdinalIgnoreCase) && !file.Path.Substring(UpdaterFolder.Length + 1).Contains('/')))
            {
                string staged = DeltaDownloader.StagedPath(staging, file.Sha256);
                if (File.Exists(staged) && Hashes.Sha256OfFile(staged) == file.Sha256)
                    File.Copy(staged, Path.Combine(copy, file.Path.Substring(UpdaterFolder.Length + 1)), overwrite: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            // The installed applier still does the work.
        }

        var arguments = new List<string> { "--install", installRoot.TrimEnd(Path.DirectorySeparatorChar), "--staging", staging };
        foreach (int process in waitFor)
            arguments.AddRange(new[] { "--wait", process.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        foreach (string name in close)
            arguments.AddRange(new[] { "--close", name });
        foreach (string program in launch)
            arguments.AddRange(new[] { "--launch", program });
        var start = new ProcessStartInfo
        {
            FileName = Path.Combine(copy, "ErkS.Updates.Apply.exe"),
            WorkingDirectory = copy,
            UseShellExecute = false,
            CreateNoWindow = true,
            Arguments = string.Join(" ", arguments.Select(Quote)),
        };
        return Process.Start(start) ?? throw new InvalidOperationException("Шинэчлэгч эхэлсэнгүй.");
    }

    private static string Quote(string argument) =>
        argument.Length > 0 && argument.IndexOfAny(new[] { ' ', '\t', '"' }) < 0 ? argument : "\"" + argument.Replace("\"", "\\\"") + "\"";
}
