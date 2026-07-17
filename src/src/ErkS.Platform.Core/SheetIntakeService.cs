using ErkS.Platform.Contracts;

namespace ErkS.Platform.Core;

public sealed class SheetIntakeScanResult
{
    public int ManifestCount { get; internal set; }
    public int ChangedPackageCount { get; internal set; }
    public int UpdatedSheetCount { get; internal set; }
    public int RemovedSheetCount { get; internal set; }
    public int ErrorCount { get; internal set; }

    internal void Add(SheetIntakeScanResult other)
    {
        ManifestCount += other.ManifestCount;
        ChangedPackageCount += other.ChangedPackageCount;
        UpdatedSheetCount += other.UpdatedSheetCount;
        RemovedSheetCount += other.RemovedSheetCount;
        ErrorCount += other.ErrorCount;
    }
}

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

    public SheetIntakeService(SheetLibrary library)
    {
        this.library = library;
    }

    /// <summary>Raised after a manifest is processed (verified or not).</summary>
    public event Action<SheetPackageLoadResult>? PackageProcessed;

    public event Action<string>? IntakeError;

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
    /// Starts watching a folder (recursive) and immediately scans packages
    /// already present so restarts never miss sheets.
    /// </summary>
    public void WatchFolder(string folder, string? sourceId = null)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        var fullPath = Path.GetFullPath(folder);
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
            watcher.Created += (_, args) => ProcessManifestSoon(args.FullPath, sourceId);
            watcher.Changed += (_, args) => ProcessManifestSoon(args.FullPath, sourceId);
            watcher.Renamed += (_, args) => ProcessManifestSoon(args.FullPath, sourceId);
            watcher.Error += (_, args) => IntakeError?.Invoke($"Watcher error: {args.GetException().Message}");
            watcher.EnableRaisingEvents = true;
            watchers[fullPath] = new WatcherRegistration(watcher, sourceId);
        }

        ScanFolder(fullPath, sourceId);
    }

    public void UnwatchFolder(string folder)
    {
        var fullPath = Path.GetFullPath(folder);
        lock (sync)
        {
            if (watchers.Remove(fullPath, out var registration))
            {
                registration.Watcher.Dispose();
            }
        }
    }

    /// <summary>Re-scans every watched folder (manual refresh).</summary>
    public SheetIntakeScanResult Rescan()
    {
        List<(string Folder, string? SourceId)> registrations;
        lock (sync)
        {
            registrations = watchers
                .Select(pair => (pair.Key, pair.Value.SourceId))
                .ToList();
        }

        var result = new SheetIntakeScanResult();
        foreach (var registration in registrations)
        {
            result.Add(ScanFolder(registration.Folder, registration.SourceId));
        }
        return result;
    }

    private SheetIntakeScanResult ScanFolder(string folder, string? sourceId)
    {
        var scan = new SheetIntakeScanResult();
        try
        {
            foreach (var manifest in Directory.EnumerateFiles(
                folder,
                "*" + SheetPackageManifest.ManifestSuffix,
                SearchOption.AllDirectories))
            {
                scan.ManifestCount++;
                var change = ProcessManifest(manifest, sourceId);
                if (change?.HasChanges == true)
                {
                    scan.ChangedPackageCount++;
                    scan.UpdatedSheetCount += change.UpdatedSheetCount;
                    scan.RemovedSheetCount += change.RemovedSheetKeys.Count;
                }
            }
        }
        catch (Exception exception)
        {
            scan.ErrorCount++;
            IntakeError?.Invoke($"Scan failed for {folder}: {exception.Message}");
        }
        return scan;
    }

    private void ProcessManifestSoon(string manifestPath, string? sourceId)
    {
        // Writers may still be flushing; retry shortly on a worker thread and
        // collapse duplicate watcher events for the same file.
        lock (sync)
        {
            var now = DateTime.UtcNow;
            if (recentManifests.TryGetValue(manifestPath, out var last) &&
                (now - last) < TimeSpan.FromMilliseconds(500))
            {
                return;
            }

            recentManifests[manifestPath] = now;
        }

        _ = Task.Run(async () =>
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                await Task.Delay(200).ConfigureAwait(false);
                if (TryProcessManifest(manifestPath, sourceId))
                {
                    return;
                }
            }

            ProcessManifest(manifestPath, sourceId);
        });
    }

    private bool TryProcessManifest(string manifestPath, string? sourceId)
    {
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

        ProcessManifest(manifestPath, sourceId);
        return true;
    }

    private SheetLibraryChange? ProcessManifest(string manifestPath, string? sourceId)
    {
        try
        {
            var result = SheetPackageReader.Load(manifestPath);
            var manifestSourceId = result.Manifest?.Source.SourceId;
            var effectiveSourceId = string.IsNullOrWhiteSpace(manifestSourceId)
                ? sourceId
                : manifestSourceId;
            var change = library.Absorb(result, effectiveSourceId);
            if (change.HasChanges || !result.IsLossless)
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

    public void Dispose()
    {
        lock (sync)
        {
            foreach (var registration in watchers.Values)
            {
                registration.Watcher.Dispose();
            }

            watchers.Clear();
        }
    }

    private sealed record WatcherRegistration(FileSystemWatcher Watcher, string? SourceId);
}
