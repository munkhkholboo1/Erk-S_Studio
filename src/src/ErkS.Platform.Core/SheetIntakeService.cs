using ErkS.Platform.Contracts;
using System.Text.Json;

namespace ErkS.Platform.Core;

public sealed class RejectedSheetPackage
{
    public required DateTimeOffset RejectedAtUtc { get; init; }
    public required string ManifestPath { get; init; }
    public Guid? PackageId { get; init; }
    public required IReadOnlyList<string> Issues { get; init; }
}

public sealed class SheetIntakeScanResult
{
    public int ManifestCount { get; internal set; }
    public int SkippedHistoricalManifestCount { get; internal set; }
    public int SkippedForeignManifestCount { get; internal set; }
    public int SilentlyHydratedManifestCount { get; internal set; }
    public int ChangedPackageCount { get; internal set; }
    public int UpdatedSheetCount { get; internal set; }
    public int RemovedSheetCount { get; internal set; }
    public int RejectedPackageCount { get; internal set; }
    public int ErrorCount { get; internal set; }

    internal void Add(SheetIntakeScanResult other)
    {
        ManifestCount += other.ManifestCount;
        SkippedHistoricalManifestCount += other.SkippedHistoricalManifestCount;
        SkippedForeignManifestCount += other.SkippedForeignManifestCount;
        SilentlyHydratedManifestCount += other.SilentlyHydratedManifestCount;
        ChangedPackageCount += other.ChangedPackageCount;
        UpdatedSheetCount += other.UpdatedSheetCount;
        RemovedSheetCount += other.RemovedSheetCount;
        RejectedPackageCount += other.RejectedPackageCount;
        ErrorCount += other.ErrorCount;
    }
}

/// <summary>
/// Last package already reconciled into the persisted project. Intake can use
/// it to rebuild its in-memory library without treating the same package as a
/// new source update on every project open.
/// </summary>
public sealed record SheetPackageCheckpoint(
    string ProjectId,
    string SourceId,
    Guid PackageId,
    DateTimeOffset ExportedAtUtc,
    string ManifestSha256);

/// <summary>
/// Watches source folders for sheet packages in real time. A package arrives
/// as PDFs plus a "*.erks-sheets.json" manifest written last; the manifest
/// write triggers verification and absorption into the <see cref="SheetLibrary"/>.
/// </summary>
public sealed class SheetIntakeService : IDisposable
{
    private readonly SheetLibrary library;
    private readonly object sync = new();
    private readonly Dictionary<string, WatcherRegistration> watchers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> recentManifests = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> rejectedSignatures = new(StringComparer.Ordinal);
    private readonly List<RejectedSheetPackage> rejectedPackages = [];
    private static readonly JsonSerializerOptions AuditJsonOptions = new(SheetPackageJson.Options)
    {
        WriteIndented = false,
    };

    public SheetIntakeService(SheetLibrary library)
    {
        this.library = library;
    }

    /// <summary>Raised after a manifest is processed (verified or not).</summary>
    public event Action<SheetPackageLoadResult>? PackageProcessed;

    public event Action<string>? IntakeError;

    public IReadOnlyList<RejectedSheetPackage> RejectedPackages
    {
        get
        {
            lock (sync)
            {
                return rejectedPackages.ToList();
            }
        }
    }

    public IReadOnlyList<string> WatchedFolders
    {
        get
        {
            lock (sync)
            {
                return watchers.Keys.ToList();
            }
        }
    }

    /// <summary>
    /// Starts watching a folder recursively. Existing packages are scanned
    /// immediately unless the caller defers that work until after the UI opens.
    /// </summary>
    public void WatchFolder(
        string folder,
        string? sourceId = null,
        string? projectId = null,
        bool scanExisting = true)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        var fullPath = Path.GetFullPath(folder);
        WatcherRegistration registration;
        lock (sync)
        {
            if (watchers.ContainsKey(fullPath))
            {
                return;
            }

            Directory.CreateDirectory(fullPath);
            var watcher = new FileSystemWatcher(fullPath)
            {
                Filter = "*" + SheetPackageManifest.ManifestSuffix,
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
            };
            registration = new WatcherRegistration(
                watcher,
                fullPath,
                CleanIdentity(sourceId),
                CleanIdentity(projectId));
            watcher.Created += (_, args) => ProcessManifestSoon(args.FullPath, registration);
            watcher.Changed += (_, args) => ProcessManifestSoon(args.FullPath, registration);
            watcher.Renamed += (_, args) => ProcessManifestSoon(args.FullPath, registration);
            watcher.Error += (_, args) => IntakeError?.Invoke($"Watcher error: {args.GetException().Message}");
            watchers[fullPath] = registration;
            watcher.EnableRaisingEvents = true;
        }

        if (scanExisting)
            ScanFolder(registration);
    }

    public void UnwatchFolder(string folder)
    {
        var fullPath = Path.GetFullPath(folder);
        WatcherRegistration? registration = null;
        lock (sync)
        {
            watchers.Remove(fullPath, out registration);
        }

        registration?.StopAndWait();
    }

    /// <summary>Re-scans every watched folder (manual refresh).</summary>
    public SheetIntakeScanResult Rescan()
    {
        return Rescan(currentSnapshotsOnly: false, []);
    }

    /// <summary>
    /// Rehydrates the current library state without re-verifying superseded
    /// historical exports. For each source this processes the newest full
    /// snapshot plus any newer delta packages.
    /// </summary>
    public SheetIntakeScanResult RescanCurrentSnapshots()
    {
        return Rescan(currentSnapshotsOnly: true, []);
    }

    public SheetIntakeScanResult RescanCurrentSnapshots(
        IReadOnlyCollection<SheetPackageCheckpoint> checkpoints)
    {
        return Rescan(currentSnapshotsOnly: true, checkpoints ?? []);
    }

    private SheetIntakeScanResult Rescan(
        bool currentSnapshotsOnly,
        IReadOnlyCollection<SheetPackageCheckpoint> checkpoints)
    {
        List<WatcherRegistration> registrations;
        lock (sync)
        {
            registrations = watchers.Values.ToList();
        }

        var result = new SheetIntakeScanResult();
        foreach (var registration in registrations)
        {
            result.Add(ScanFolder(registration, currentSnapshotsOnly, checkpoints));
        }
        return result;
    }

    private SheetIntakeScanResult ScanFolder(
        WatcherRegistration registration,
        bool currentSnapshotsOnly = false,
        IReadOnlyCollection<SheetPackageCheckpoint>? checkpoints = null)
    {
        var scan = new SheetIntakeScanResult();
        try
        {
            if (registration.IsStopped)
            {
                return scan;
            }

            List<string> discovered = Directory.EnumerateFiles(
                    registration.Folder,
                    "*" + SheetPackageManifest.ManifestSuffix,
                    SearchOption.AllDirectories)
                .ToList();
            int skippedForeign = 0;
            IReadOnlyList<string> manifests = currentSnapshotsOnly
                ? SelectCurrentPackageManifests(discovered, registration, out skippedForeign)
                : discovered;
            scan.SkippedForeignManifestCount = skippedForeign;
            scan.SkippedHistoricalManifestCount =
                discovered.Count - manifests.Count - skippedForeign;

            foreach (string manifest in manifests)
            {
                if (registration.IsStopped)
                {
                    break;
                }

                scan.ManifestCount++;
                bool silentlyHydrated = false;
                SheetPackageCheckpoint? hydrationCheckpoint = FindCheckpoint(
                    registration,
                    checkpoints ?? []);
                var change = ProcessManifest(
                    manifest,
                    registration,
                    hydrationCheckpoint,
                    currentSnapshotsOnly && checkpoints is { Count: > 0 }
                        ? result =>
                        {
                            bool publish = ShouldPublishPackage(result, registration, checkpoints);
                            silentlyHydrated = !publish;
                            return publish;
                        }
                        : null);
                if (silentlyHydrated)
                {
                    scan.SilentlyHydratedManifestCount++;
                }
                if (change?.HasChanges == true)
                {
                    scan.ChangedPackageCount++;
                    scan.UpdatedSheetCount += change.UpdatedSheetCount;
                    scan.RemovedSheetCount += change.RemovedSheetKeys.Count;
                }
                if (change?.Rejected == true)
                {
                    scan.RejectedPackageCount++;
                    scan.ErrorCount++;
                }
            }
        }
        catch (Exception exception)
        {
            scan.ErrorCount++;
            IntakeError?.Invoke($"Scan failed for {registration.Folder}: {exception.Message}");
        }
        return scan;
    }

    private static IReadOnlyList<string> SelectCurrentPackageManifests(
        IReadOnlyList<string> manifestPaths,
        WatcherRegistration registration,
        out int skippedForeign)
    {
        skippedForeign = 0;
        var unreadable = new List<string>();
        var candidates = new List<ManifestScanCandidate>();
        foreach (string path in manifestPaths)
        {
            try
            {
                SheetPackageManifest? manifest = JsonSerializer.Deserialize<SheetPackageManifest>(
                    File.ReadAllText(path),
                    SheetPackageJson.Options);
                if (manifest is null)
                {
                    unreadable.Add(path);
                    continue;
                }

                manifest.Source ??= new SheetPackageSource();
                string? effectiveSourceId = string.IsNullOrWhiteSpace(manifest.Source.SourceId)
                    ? registration.SourceId
                    : manifest.Source.SourceId.Trim();
                string sourceIdentity = SheetRecord.MakeSourceIdentity(
                    manifest.Source,
                    effectiveSourceId);
                string projectIdentity = string.IsNullOrWhiteSpace(manifest.ProjectId)
                    ? registration.ProjectId ?? ""
                    : manifest.ProjectId.Trim();
                if ((!string.IsNullOrWhiteSpace(registration.SourceId) &&
                     !string.IsNullOrWhiteSpace(effectiveSourceId) &&
                     !effectiveSourceId.Equals(
                         registration.SourceId,
                         StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(registration.ProjectId) &&
                     !string.IsNullOrWhiteSpace(projectIdentity) &&
                     !projectIdentity.Equals(
                         registration.ProjectId,
                         StringComparison.OrdinalIgnoreCase)))
                {
                    // A source-specific inbox can contain historical packages
                    // left by an older link. Watcher events and a manual full
                    // rescan still reject new foreign input; startup hydration
                    // ignores it so it cannot stall every project open.
                    skippedForeign++;
                    continue;
                }
                candidates.Add(new ManifestScanCandidate(
                    path,
                    projectIdentity,
                    sourceIdentity,
                    manifest.PackageId,
                    manifest.PackageScope,
                    manifest.ExportedAtUtc));
            }
            catch (Exception exception) when (
                exception is JsonException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                // Keep malformed/unreadable input in the selected set so the
                // normal verifier and quarantine audit still see it.
                unreadable.Add(path);
            }
        }

        var selected = new List<ManifestScanCandidate>();
        foreach (IGrouping<string, ManifestScanCandidate> group in candidates.GroupBy(
                     candidate => candidate.ProjectIdentity + "|" + candidate.SourceIdentity,
                     StringComparer.OrdinalIgnoreCase))
        {
            ManifestScanCandidate? latestFullSnapshot = group
                .Where(candidate => candidate.Scope == SheetPackageScope.FullSnapshot)
                .OrderByDescending(candidate => candidate.ExportedAtUtc)
                .ThenByDescending(candidate => candidate.PackageId.ToString("N"), StringComparer.Ordinal)
                .FirstOrDefault();
            if (latestFullSnapshot is null)
            {
                selected.AddRange(group);
                continue;
            }

            selected.Add(latestFullSnapshot);
            selected.AddRange(group.Where(candidate =>
                candidate.Scope != SheetPackageScope.FullSnapshot &&
                candidate.ExportedAtUtc >= latestFullSnapshot.ExportedAtUtc));
        }

        return unreadable
            .Concat(selected
                .OrderBy(candidate => candidate.ExportedAtUtc)
                .ThenBy(candidate => candidate.PackageId.ToString("N"), StringComparer.Ordinal)
                .Select(candidate => candidate.Path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void ProcessManifestSoon(string manifestPath, WatcherRegistration registration)
    {
        // Writers may still be flushing; retry shortly on a worker thread and
        // collapse duplicate watcher events for the same file.
        lock (sync)
        {
            if (registration.IsStopped)
            {
                return;
            }

            var now = DateTime.UtcNow;
            var recentKey = $"{registration.Id:N}|{manifestPath}";
            if (recentManifests.TryGetValue(recentKey, out var last) &&
                (now - last) < TimeSpan.FromMilliseconds(500))
            {
                return;
            }

            recentManifests[recentKey] = now;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                for (var attempt = 0; attempt < 5; attempt++)
                {
                    await Task.Delay(200, registration.Lifetime).ConfigureAwait(false);
                    if (TryProcessManifest(manifestPath, registration))
                    {
                        return;
                    }
                }

                ProcessManifest(manifestPath, registration);
            }
            catch (OperationCanceledException) when (registration.IsStopped)
            {
            }
        });
    }

    private bool TryProcessManifest(string manifestPath, WatcherRegistration registration)
    {
        if (registration.IsStopped)
        {
            return true;
        }

        try
        {
            using (File.Open(manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
            }
        }
        catch (IOException)
        {
            return false;
        }
        catch (Exception)
        {
            // Missing/permission problems: report through the normal path.
        }

        ProcessManifest(manifestPath, registration);
        return true;
    }

    private SheetLibraryChange? ProcessManifest(
        string manifestPath,
        WatcherRegistration registration,
        SheetPackageCheckpoint? hydrationCheckpoint = null,
        Func<SheetPackageLoadResult, bool>? shouldPublish = null)
    {
        try
        {
            var result = hydrationCheckpoint is null
                ? SheetPackageReader.Load(manifestPath)
                : SheetPackageReader.LoadForHydration(
                    manifestPath,
                    hydrationCheckpoint.PackageId,
                    hydrationCheckpoint.ManifestSha256);
            AddOwnershipIssues(result, registration);
            var manifestSourceId = result.Manifest?.Source.SourceId;
            var effectiveSourceId = string.IsNullOrWhiteSpace(manifestSourceId)
                ? registration.SourceId
                : manifestSourceId;
            bool publish = shouldPublish?.Invoke(result) ?? true;

            SheetLibraryChange change;
            lock (registration.ProcessingGate)
            {
                if (registration.IsStopped)
                {
                    return null;
                }

                change = library.Absorb(result, effectiveSourceId, notifyChanged: publish);
            }
            if (change.Rejected)
            {
                RecordRejectedPackage(result, registration.Folder);
            }
            if (publish && (change.HasChanges || !result.IsLossless))
            {
                PackageProcessed?.Invoke(result);
            }
            return change;
        }
        catch (Exception exception)
        {
            IntakeError?.Invoke($"Package failed: {manifestPath}: {exception.Message}");
            return null;
        }
    }

    private static bool ShouldPublishPackage(
        SheetPackageLoadResult result,
        WatcherRegistration registration,
        IReadOnlyCollection<SheetPackageCheckpoint> checkpoints)
    {
        if (!result.IsLossless || result.Manifest is null)
        {
            return true;
        }

        SheetPackageManifest manifest = result.Manifest;
        string projectId = string.IsNullOrWhiteSpace(manifest.ProjectId)
            ? registration.ProjectId ?? ""
            : manifest.ProjectId.Trim();
        string sourceId = string.IsNullOrWhiteSpace(manifest.Source.SourceId)
            ? registration.SourceId ?? ""
            : manifest.Source.SourceId.Trim();
        if (string.IsNullOrWhiteSpace(projectId) || string.IsNullOrWhiteSpace(sourceId))
        {
            return true;
        }

        SheetPackageCheckpoint? checkpoint = checkpoints
            .Where(item =>
                item.ProjectId.Equals(projectId, StringComparison.OrdinalIgnoreCase) &&
                item.SourceId.Equals(sourceId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.ExportedAtUtc)
            .ThenByDescending(item => item.PackageId.ToString("N"), StringComparer.Ordinal)
            .FirstOrDefault();
        if (checkpoint is null)
        {
            return true;
        }

        if (manifest.PackageId == checkpoint.PackageId)
        {
            return !result.ManifestSha256.Equals(
                checkpoint.ManifestSha256,
                StringComparison.OrdinalIgnoreCase);
        }

        int timeComparison = manifest.ExportedAtUtc.CompareTo(checkpoint.ExportedAtUtc);
        if (timeComparison < 0)
        {
            return false;
        }
        if (timeComparison == 0 && string.CompareOrdinal(
                manifest.PackageId.ToString("N"),
                checkpoint.PackageId.ToString("N")) < 0)
        {
            return false;
        }
        return true;
    }

    private static SheetPackageCheckpoint? FindCheckpoint(
        WatcherRegistration registration,
        IReadOnlyCollection<SheetPackageCheckpoint> checkpoints)
    {
        if (string.IsNullOrWhiteSpace(registration.ProjectId) ||
            string.IsNullOrWhiteSpace(registration.SourceId))
        {
            return null;
        }

        return checkpoints
            .Where(item =>
                item.ProjectId.Equals(
                    registration.ProjectId,
                    StringComparison.OrdinalIgnoreCase) &&
                item.SourceId.Equals(
                    registration.SourceId,
                    StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.ExportedAtUtc)
            .ThenByDescending(item => item.PackageId.ToString("N"), StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static void AddOwnershipIssues(
        SheetPackageLoadResult result,
        WatcherRegistration registration)
    {
        SheetPackageManifest? manifest = result.Manifest;
        if (manifest is null || string.IsNullOrWhiteSpace(registration.ProjectId))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(manifest.ProjectId))
        {
            if (manifest.SchemaVersion >= 4)
            {
                result.Issues.Add(
                    $"Manifest project id is missing; expected '{registration.ProjectId}'.");
            }
        }
        else if (!string.Equals(
                     manifest.ProjectId.Trim(),
                     registration.ProjectId,
                     StringComparison.OrdinalIgnoreCase))
        {
            result.Issues.Add(
                $"Manifest belongs to project '{manifest.ProjectId.Trim()}', not '{registration.ProjectId}'.");
        }

        string manifestSourceId = manifest.Source.SourceId?.Trim() ?? "";
        if (!string.IsNullOrWhiteSpace(registration.SourceId) &&
            !string.IsNullOrWhiteSpace(manifestSourceId) &&
            !string.Equals(
                manifestSourceId,
                registration.SourceId,
                StringComparison.OrdinalIgnoreCase))
        {
            result.Issues.Add(
                $"Manifest belongs to source '{manifestSourceId}', not '{registration.SourceId}'.");
        }
    }

    private static string? CleanIdentity(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private void RecordRejectedPackage(SheetPackageLoadResult result, string quarantineRoot)
    {
        var fullManifestPath = Path.GetFullPath(result.ManifestPath);
        var signature = string.Join('|',
            fullManifestPath.ToUpperInvariant(),
            result.Manifest?.PackageId.ToString("N") ?? "no-package-id",
            File.Exists(fullManifestPath) ? File.GetLastWriteTimeUtc(fullManifestPath).Ticks : 0,
            string.Join(";", result.Issues));
        var record = new RejectedSheetPackage
        {
            RejectedAtUtc = DateTimeOffset.UtcNow,
            ManifestPath = fullManifestPath,
            PackageId = result.Manifest?.PackageId,
            Issues = result.Issues.ToList(),
        };

        lock (sync)
        {
            if (!rejectedSignatures.Add(signature))
            {
                return;
            }

            rejectedPackages.Add(record);
            try
            {
                var auditDirectory = Path.Combine(quarantineRoot, ".erks-quarantine");
                Directory.CreateDirectory(auditDirectory);
                var auditPath = Path.Combine(auditDirectory, "rejected-packages.jsonl");
                File.AppendAllText(
                    auditPath,
                    JsonSerializer.Serialize(record, AuditJsonOptions) + Environment.NewLine);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                IntakeError?.Invoke($"Rejected package audit failed: {exception.Message}");
            }
        }
    }

    public void Dispose()
    {
        List<WatcherRegistration> registrations;
        lock (sync)
        {
            registrations = watchers.Values.ToList();
            watchers.Clear();
        }

        foreach (var registration in registrations)
        {
            registration.StopAndWait();
        }
    }

    private sealed class WatcherRegistration
    {
        private readonly CancellationTokenSource lifetime = new();
        private int stopped;

        public WatcherRegistration(
            FileSystemWatcher watcher,
            string folder,
            string? sourceId,
            string? projectId)
        {
            Watcher = watcher;
            Folder = folder;
            SourceId = sourceId;
            ProjectId = projectId;
        }

        public Guid Id { get; } = Guid.NewGuid();
        public FileSystemWatcher Watcher { get; }
        public string Folder { get; }
        public string? SourceId { get; }
        public string? ProjectId { get; }
        public object ProcessingGate { get; } = new();
        public CancellationToken Lifetime => lifetime.Token;
        public bool IsStopped => Volatile.Read(ref stopped) != 0;

        public void StopAndWait()
        {
            if (Interlocked.Exchange(ref stopped, 1) == 0)
            {
                lifetime.Cancel();
                Watcher.Dispose();
            }

            lock (ProcessingGate)
            {
            }
        }
    }

    private sealed record ManifestScanCandidate(
        string Path,
        string ProjectIdentity,
        string SourceIdentity,
        Guid PackageId,
        SheetPackageScope Scope,
        DateTimeOffset ExportedAtUtc);
}
