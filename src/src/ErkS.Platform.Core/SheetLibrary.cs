using ErkS.Platform.Contracts;

namespace ErkS.Platform.Core;

/// <summary>
/// One received sheet: a manifest entry resolved against its package folder.
/// </summary>
public sealed class SheetRecord
{
    public required string Key { get; init; }
    public required string SourceId { get; init; }
    public required string SourceIdentity { get; init; }
    public required SheetPackageEntry Entry { get; init; }
    public required SheetPackageSource Source { get; init; }
    public required Guid PackageId { get; init; }
    public required string ManifestPath { get; init; }
    public required string PdfPath { get; init; }
    public required DateTimeOffset ExportedAtUtc { get; init; }
    public DateTimeOffset ReceivedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>True when the package hash check passed for this sheet's package.</summary>
    public required bool IsVerified { get; init; }

    public string DisplayLabel =>
        string.IsNullOrWhiteSpace(Entry.Number) ? Entry.Name : $"{Entry.Number}  {Entry.Name}";

    public static string MakeKey(SheetPackageSource source, SheetPackageEntry entry, string? sourceId = null)
    {
        // Stable across re-exports of the same document: the same layout/sheet
        // replaces its older version instead of duplicating.
        return $"{MakeSourceIdentity(source, sourceId)}|{entry.SheetId}".ToLowerInvariant();
    }

    public static string MakeSourceIdentity(SheetPackageSource source, string? sourceId = null)
    {
        var effectiveSourceId = string.IsNullOrWhiteSpace(sourceId) ? source.SourceId : sourceId;
        return !string.IsNullOrWhiteSpace(effectiveSourceId)
            ? effectiveSourceId.Trim().ToLowerInvariant()
            : $"{source.Application}|{source.DocumentPath}".ToLowerInvariant();
    }

    public static bool BelongsToSource(
        string sheetKey,
        SheetPackageSource source,
        string? sourceId = null) =>
        sheetKey.StartsWith(MakeSourceIdentity(source, sourceId) + "|", StringComparison.Ordinal);
}

public sealed class SheetLibraryChange
{
    public Guid PackageId { get; init; }
    public int UpdatedSheetCount { get; init; }
    public IReadOnlyList<string> RemovedSheetKeys { get; init; } = [];
    public IReadOnlyList<string> Issues { get; init; } = [];
    public bool Rejected { get; init; }
    public bool FullSnapshotApplied { get; init; }
    public bool StaleSnapshotIgnored { get; init; }
    public bool HasChanges => UpdatedSheetCount > 0 || RemovedSheetKeys.Count > 0 || FullSnapshotApplied;
}

/// <summary>
/// In-memory registry of every sheet received from watched source folders.
/// Re-exports of the same sheet replace the previous record (latest wins).
/// </summary>
public sealed class SheetLibrary
{
    private readonly object sync = new();
    private readonly Dictionary<string, SheetRecord> sheets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SourceSnapshotState> sourceSnapshots = new(StringComparer.Ordinal);

    public event Action? Changed;

    public IReadOnlyList<SheetRecord> Snapshot()
    {
        lock (sync)
        {
            return sheets.Values
                .OrderBy(record => record.Source.Application)
                .ThenBy(record => record.Entry.Discipline, StringComparer.OrdinalIgnoreCase)
                .ThenBy(record => record.Entry.Number, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    public SheetRecord? Find(string key)
    {
        lock (sync)
        {
            return sheets.GetValueOrDefault(key);
        }
    }

    public IReadOnlyList<SheetRecord> VerifiedSnapshot()
    {
        lock (sync)
        {
            return sheets.Values
                .Where(record => record.IsVerified)
                .OrderBy(record => record.Source.Application)
                .ThenBy(record => record.Entry.Discipline, StringComparer.OrdinalIgnoreCase)
                .ThenBy(record => record.Entry.Number, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    public SheetRecord? FindVerified(string key)
    {
        lock (sync)
        {
            var record = sheets.GetValueOrDefault(key);
            return record?.IsVerified == true ? record : null;
        }
    }

    public SheetLibraryChange Absorb(
        SheetPackageLoadResult loadResult,
        string? sourceIdOverride = null,
        bool notifyChanged = true)
    {
        if (!loadResult.IsLossless || loadResult.Manifest is null)
        {
            return new SheetLibraryChange
            {
                PackageId = loadResult.Manifest?.PackageId ?? Guid.Empty,
                Rejected = true,
                Issues = loadResult.Issues.ToList(),
            };
        }

        var manifest = loadResult.Manifest;
        var sourceId = string.IsNullOrWhiteSpace(sourceIdOverride)
            ? manifest.Source.SourceId
            : sourceIdOverride;
        var sourceIdentity = SheetRecord.MakeSourceIdentity(manifest.Source, sourceId);
        var verifiedPaths = new Dictionary<SheetPackageEntry, string>(ReferenceEqualityComparer.Instance);
        foreach (var entry in manifest.Sheets)
        {
            if (!loadResult.TryGetVerifiedPdfPath(entry, out var verifiedPath))
            {
                return new SheetLibraryChange
                {
                    PackageId = manifest.PackageId,
                    Rejected = true,
                    Issues = ["Verified PDF path is unavailable."],
                };
            }
            verifiedPaths[entry] = verifiedPath;
        }
        var incomingKeys = manifest.Sheets
            .Select(entry => SheetRecord.MakeKey(manifest.Source, entry, sourceId))
            .ToHashSet(StringComparer.Ordinal);
        var removedKeys = new List<string>();
        var updatedSheetCount = 0;
        var fullSnapshotApplied = false;
        var staleSnapshotIgnored = false;

        lock (sync)
        {
            if (manifest.PackageScope == SheetPackageScope.FullSnapshot && loadResult.IsLossless)
            {
                if (!sourceSnapshots.TryGetValue(sourceIdentity, out var currentSnapshot) ||
                    IsNewerSnapshot(manifest, currentSnapshot))
                {
                    fullSnapshotApplied = true;
                    sourceSnapshots[sourceIdentity] = new SourceSnapshotState(
                        manifest.PackageId,
                        manifest.ExportedAtUtc,
                        incomingKeys);

                    foreach (var key in sheets.Values
                        .Where(record =>
                            string.Equals(record.SourceIdentity, sourceIdentity, StringComparison.Ordinal) &&
                            record.ExportedAtUtc <= manifest.ExportedAtUtc &&
                            !incomingKeys.Contains(record.Key))
                        .Select(record => record.Key)
                        .ToList())
                    {
                        sheets.Remove(key);
                        removedKeys.Add(key);
                    }
                }
                else if (currentSnapshot.PackageId != manifest.PackageId)
                {
                    staleSnapshotIgnored = true;
                }
            }

            sourceSnapshots.TryGetValue(sourceIdentity, out var activeSnapshot);
            foreach (var entry in manifest.Sheets)
            {
                var key = SheetRecord.MakeKey(manifest.Source, entry, sourceId);
                if (activeSnapshot is not null &&
                    activeSnapshot.PackageId != manifest.PackageId &&
                    manifest.ExportedAtUtc <= activeSnapshot.ExportedAtUtc &&
                    !activeSnapshot.SheetKeys.Contains(key))
                {
                    // An older delta/full package must not resurrect a sheet
                    // omitted from the latest authoritative source snapshot.
                    continue;
                }

                var record = new SheetRecord
                {
                    Key = key,
                    SourceId = sourceId,
                    SourceIdentity = sourceIdentity,
                    Entry = entry,
                    Source = manifest.Source,
                    PackageId = manifest.PackageId,
                    ManifestPath = loadResult.ManifestPath,
                    PdfPath = verifiedPaths[entry],
                    ExportedAtUtc = manifest.ExportedAtUtc,
                    IsVerified = true,
                };

                // Latest export wins; ignore stale packages arriving late.
                if (sheets.TryGetValue(record.Key, out var existing) &&
                    (existing.ExportedAtUtc > record.ExportedAtUtc ||
                     (existing.ExportedAtUtc == record.ExportedAtUtc &&
                      existing.PackageId == record.PackageId)))
                {
                    continue;
                }

                sheets[record.Key] = record;
                updatedSheetCount++;
            }
        }

        var change = new SheetLibraryChange
        {
            PackageId = manifest.PackageId,
            UpdatedSheetCount = updatedSheetCount,
            RemovedSheetKeys = removedKeys,
            FullSnapshotApplied = fullSnapshotApplied,
            StaleSnapshotIgnored = staleSnapshotIgnored,
        };
        if (change.HasChanges && notifyChanged)
        {
            Changed?.Invoke();
        }
        return change;
    }

    public bool IsCurrentAuthoritativeSnapshot(
        SheetPackageManifest manifest,
        string? sourceIdOverride = null)
    {
        if (manifest.PackageScope != SheetPackageScope.FullSnapshot)
        {
            return false;
        }

        var sourceIdentity = SheetRecord.MakeSourceIdentity(manifest.Source, sourceIdOverride);
        lock (sync)
        {
            return sourceSnapshots.TryGetValue(sourceIdentity, out var snapshot) &&
                snapshot.PackageId == manifest.PackageId;
        }
    }

    public void Clear()
    {
        lock (sync)
        {
            sheets.Clear();
            sourceSnapshots.Clear();
        }

        Changed?.Invoke();
    }

    private static bool IsNewerSnapshot(
        SheetPackageManifest manifest,
        SourceSnapshotState current)
    {
        var timeComparison = manifest.ExportedAtUtc.CompareTo(current.ExportedAtUtc);
        if (timeComparison != 0)
        {
            return timeComparison > 0;
        }
        if (manifest.PackageId == current.PackageId)
        {
            return false;
        }
        return string.CompareOrdinal(
            manifest.PackageId.ToString("N"),
            current.PackageId.ToString("N")) > 0;
    }

    private sealed record SourceSnapshotState(
        Guid PackageId,
        DateTimeOffset ExportedAtUtc,
        HashSet<string> SheetKeys);
}
