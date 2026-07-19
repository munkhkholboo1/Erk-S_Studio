using System.Globalization;
using System.Security.Cryptography;
using ErkS.Platform.Contracts;

namespace ErkS.Platform.Core;

public sealed record ProjectSourceSyncCandidate(
    ProjectDesignSource Source,
    string SourceKey,
    string SourceApplication,
    string SourceDocumentReference,
    string ManifestId,
    string ManifestSchemaVersion,
    DateTimeOffset ExportedAtUtc,
    string WorkPackageId,
    int SheetCount,
    string ContentHash);

/// <summary>
/// Persists the explicit Studio-to-server sync boundary inside the project.
/// Exporters update Studio locally; only the user's Sync command advances the
/// server acknowledgement values.
/// </summary>
public static class ProjectCloudSyncMetadata
{
    private const string SourceKeyKey = "cloud.sourceKey";
    private const string SourceApplicationKey = "cloud.sourceApplication";
    private const string SourceDocumentReferenceKey = "cloud.sourceDocumentReference";
    private const string ManifestIdKey = "cloud.manifestId";
    private const string ManifestSchemaVersionKey = "cloud.manifestSchemaVersion";
    private const string ExportedAtUtcKey = "cloud.exportedAtUtc";
    private const string WorkPackageIdKey = "cloud.workPackageId";
    private const string SheetCountKey = "cloud.sheetCount";
    private const string ContentHashKey = "cloud.contentHash";
    private const string SyncedManifestIdKey = "cloud.syncedManifestId";
    private const string SyncedContentHashKey = "cloud.syncedContentHash";

    public static void RecordPackage(
        ProjectWorkspace project,
        ProjectDesignSource source,
        SheetPackageManifest manifest,
        string manifestSha256)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(manifest);
        if (string.IsNullOrWhiteSpace(manifestSha256))
            throw new ArgumentException("Manifest SHA-256 is required.", nameof(manifestSha256));

        source.Metadata ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        source.Metadata[SourceKeyKey] = Value(source.Metadata, SourceKeyKey, source.Id);
        source.Metadata[SourceApplicationKey] = SourceApplication(manifest.Source.Application);
        source.Metadata[SourceDocumentReferenceKey] = manifest.Source.DocumentTitle?.Trim() ?? "";
        source.Metadata[ManifestIdKey] = manifest.PackageId.ToString("N");
        source.Metadata[ManifestSchemaVersionKey] = manifest.SchemaVersion.ToString(CultureInfo.InvariantCulture);
        source.Metadata[ExportedAtUtcKey] = manifest.ExportedAtUtc.ToString("O", CultureInfo.InvariantCulture);
        source.Metadata[WorkPackageIdKey] = manifest.WorkPackageId?.Trim() ?? "";
        source.Metadata[SheetCountKey] = manifest.Sheets.Count.ToString(CultureInfo.InvariantCulture);
        source.Metadata[ContentHashKey] = manifestSha256.Trim().ToLowerInvariant();
        MarkPending(project);
    }

    public static string CloudSourceKey(ProjectDesignSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        source.Metadata ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        return Value(source.Metadata, SourceKeyKey, source.Id);
    }

    public static void BindToCloudSource(
        ProjectWorkspace project,
        ProjectDesignSource source,
        string sourceKey)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(source);
        string normalized = (sourceKey ?? "").Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            throw new ArgumentException("Cloud source key is required.", nameof(sourceKey));
        source.Metadata ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        source.Metadata[SourceKeyKey] = normalized;
        source.Metadata.Remove(SyncedManifestIdKey);
        source.Metadata.Remove(SyncedContentHashKey);
        MarkPending(project);
    }

    public static IReadOnlyList<ProjectSourceSyncCandidate> SourcePackages(ProjectWorkspace project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var candidates = new List<ProjectSourceSyncCandidate>();
        foreach (ProjectDesignSource source in project.Sources)
        {
            Dictionary<string, string> metadata = source.Metadata ?? new(StringComparer.OrdinalIgnoreCase);
            string manifestId = Value(metadata, ManifestIdKey);
            string contentHash = Value(metadata, ContentHashKey);
            if (string.IsNullOrWhiteSpace(manifestId) || string.IsNullOrWhiteSpace(contentHash))
                continue;

            _ = DateTimeOffset.TryParse(
                Value(metadata, ExportedAtUtcKey),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out DateTimeOffset exportedAtUtc);
            _ = int.TryParse(Value(metadata, SheetCountKey), NumberStyles.Integer, CultureInfo.InvariantCulture, out int sheetCount);
            candidates.Add(new ProjectSourceSyncCandidate(
                source,
                Value(metadata, SourceKeyKey, source.Id),
                Value(metadata, SourceApplicationKey, "Studio"),
                Value(metadata, SourceDocumentReferenceKey),
                manifestId,
                Value(metadata, ManifestSchemaVersionKey, "1"),
                exportedAtUtc,
                Value(metadata, WorkPackageIdKey),
                Math.Max(0, sheetCount),
                contentHash));
        }
        return candidates;
    }

    public static IReadOnlyList<ProjectSourceSyncCandidate> PendingSourcePackages(ProjectWorkspace project) =>
        SourcePackages(project)
            .Where(candidate => !IsSynced(candidate))
            .ToList();

    public static bool HasSourcePackageSnapshot(ProjectWorkspace project) => SourcePackages(project).Count > 0;

    public static void MarkSourceSynced(ProjectSourceSyncCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        candidate.Source.Metadata ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        candidate.Source.Metadata[SyncedManifestIdKey] = candidate.ManifestId;
        candidate.Source.Metadata[SyncedContentHashKey] = candidate.ContentHash;
    }

    public static void ValidateSourceAcknowledgement(
        string expectedManifestId,
        string expectedContentHash,
        string actualManifestId,
        string actualContentHash)
    {
        if (string.IsNullOrWhiteSpace(actualManifestId) ||
            !actualManifestId.Trim().Equals(expectedManifestId?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Cloud source acknowledgement manifest ID does not match the pending package.");
        }
        if (string.IsNullOrWhiteSpace(actualContentHash) ||
            !actualContentHash.Trim().Equals(expectedContentHash?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Cloud source acknowledgement content hash does not match the pending package.");
        }
    }

    public static void ValidateAlbumAcknowledgement(
        string expectedPdfSha256,
        string actualPdfSha256,
        string revisionId)
    {
        if (string.IsNullOrWhiteSpace(revisionId))
            throw new InvalidDataException("Cloud album acknowledgement revision ID is empty.");
        if (string.IsNullOrWhiteSpace(actualPdfSha256) ||
            !actualPdfSha256.Trim().Equals(expectedPdfSha256?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Cloud album acknowledgement PDF hash does not match the uploaded canonical album.");
        }
    }

    public static void RecordBuiltAlbum(
        ProjectWorkspace project,
        string projectPath,
        string outputPath,
        int pageCount,
        string pageSizeSummary)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (!File.Exists(outputPath))
            throw new FileNotFoundException("Built album PDF was not found.", outputPath);
        if (pageCount < 1)
            throw new ArgumentOutOfRangeException(nameof(pageCount));

        using FileStream stream = File.OpenRead(outputPath);
        string sha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        ProjectAlbumRecord album = project.PrimaryAlbum;
        album.LastPdfPath = ProjectWorkspacePaths.ToRelativePath(projectPath, outputPath);
        album.LastPdfSha256 = sha256;
        album.LastPageCount = pageCount;
        album.LastPageSizeSummary = pageSizeSummary?.Trim() ?? "";
        if (!sha256.Equals(project.Cloud.LastSyncedAlbumSha256, StringComparison.OrdinalIgnoreCase))
            MarkPending(project);
    }

    public static void RecordBuiltAlbum(
        ProjectWorkspace project,
        StudioAlbumDocument albumDocument,
        string projectPath,
        string outputPath,
        int pageCount,
        string pageSizeSummary,
        string createdBy)
    {
        ArgumentNullException.ThrowIfNull(albumDocument);
        RecordBuiltAlbum(project, projectPath, outputPath, pageCount, pageSizeSummary);
        ProjectAlbumRecord album = project.PrimaryAlbum;
        DeliverableRevisionLifecycle.CreateDraft(
            albumDocument,
            new DeliverableRevisionInput
            {
                PdfPath = album.LastPdfPath,
                Sha256 = album.LastPdfSha256,
                SourcePackageIds = SourcePackages(project).Select(item => item.ManifestId).ToList(),
                FoundationVersion = project.Foundation.Version,
                CompanySnapshotId = project.Foundation.DesignCompany.OrganizationId,
                PageCount = pageCount,
                PageSizeSummary = pageSizeSummary,
                CreatedBy = createdBy,
                AuditNote = "Studio canonical album build",
            },
            DateTimeOffset.UtcNow);
    }

    public static void MarkSynced(
        ProjectWorkspace project,
        string albumSha256,
        string revisionId,
        string concurrencyToken,
        DateTimeOffset syncedAtUtc,
        string note = "")
    {
        ArgumentNullException.ThrowIfNull(project);
        project.Cloud.SyncStatus = ProjectSyncStatuses.Synced;
        project.Cloud.LastSyncedAtUtc = syncedAtUtc;
        project.Cloud.LastSyncedAlbumSha256 = albumSha256?.Trim().ToLowerInvariant() ?? "";
        project.Cloud.LastSyncedRevisionId = revisionId?.Trim() ?? "";
        project.Cloud.LastServerConcurrencyToken = concurrencyToken?.Trim() ?? "";
        project.Cloud.LastSyncError = "";
        project.Cloud.LastSyncNote = note?.Trim() ?? "";
    }

    public static void MarkError(ProjectWorkspace project, string message)
    {
        ArgumentNullException.ThrowIfNull(project);
        project.Cloud.SyncStatus = ProjectSyncStatuses.Error;
        project.Cloud.LastSyncError = message?.Trim() ?? "";
        project.Cloud.LastSyncNote = "";
    }

    public static void MarkConflict(
        ProjectWorkspace project,
        PendingProjectInformationUpdate pendingInformation,
        string serverConcurrencyToken,
        string message)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(pendingInformation);
        project.Cloud.PendingProjectInformation = pendingInformation;
        project.Cloud.SyncStatus = ProjectSyncStatuses.Conflict;
        project.Cloud.LastServerConcurrencyToken = serverConcurrencyToken?.Trim() ?? "";
        project.Cloud.LastSyncError = message?.Trim() ?? "";
        project.Cloud.LastSyncNote =
            "Local edit was preserved. Review the server snapshot before saving or syncing again.";
    }

    private static void MarkPending(ProjectWorkspace project)
    {
        if (project.Cloud.Origin.Equals(ProjectOrigins.Cloud, StringComparison.OrdinalIgnoreCase))
        {
            project.Cloud.SyncStatus = ProjectSyncStatuses.Pending;
            project.Cloud.LastSyncError = "";
            project.Cloud.LastSyncNote = "";
        }
    }

    private static bool IsSynced(ProjectSourceSyncCandidate candidate)
    {
        Dictionary<string, string> metadata = candidate.Source.Metadata ?? new(StringComparer.OrdinalIgnoreCase);
        return candidate.ManifestId.Equals(Value(metadata, SyncedManifestIdKey), StringComparison.OrdinalIgnoreCase) &&
            candidate.ContentHash.Equals(Value(metadata, SyncedContentHashKey), StringComparison.OrdinalIgnoreCase);
    }

    private static string SourceApplication(SheetSourceApplication application) => application switch
    {
        SheetSourceApplication.AutoCad => "AutoCAD",
        SheetSourceApplication.Revit => "Revit",
        SheetSourceApplication.CityGen => "CityGen",
        SheetSourceApplication.Pdf => "PDF",
        _ => "Studio"
    };

    private static string Value(Dictionary<string, string> metadata, string key, string fallback = "") =>
        metadata.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : fallback;
}
