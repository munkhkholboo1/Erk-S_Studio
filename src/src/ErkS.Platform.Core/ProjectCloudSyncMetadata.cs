using System.Globalization;
using System.Security.Cryptography;
using ErkS.Platform.Contracts;

namespace ErkS.Platform.Core;

public sealed record ProjectSourceSyncCandidate(
    ProjectDesignSource Source,
    string SourceKey,
    string SourceApplication,
    string SourcePurpose,
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
    public const string CurrentSourceSemanticSyncVersion = "1";
    public const string CoverComponentCode = "generated:cover:Cover";
    public const string CompanyRegistrationComponentCode =
        "generated:design-organization:CompanyRegistrationCertificate";
    public const string CompanyLicenseComponentCode =
        "generated:design-organization:CompanyDesignLicense";
    public const string ApprovedAtdComponentCode =
        "generated:planning-task:ApprovedPlanningTask";
    public const string VisualizationsComponentCode = "generated:visualizations";
    public const string SiteContextComponentCode = "generated:site-context:SiteContext";
    public const string BuildingSubCoverComponentCodePrefix =
        "generated:building-sub-cover:";

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
    private const string SyncedSemanticVersionKey = "cloud.syncedSemanticVersion";
    private const string SyncedSourcePurposeKey = "cloud.syncedSourcePurpose";
    private const string OwnerEmailKey = "cloud.ownerEmail";

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
        ProjectDesignSourceClassification.RecordDetectedPurpose(source, manifest);
        MarkPending(project);
    }

    public static string CloudSourceKey(ProjectDesignSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        source.Metadata ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        return Value(source.Metadata, SourceKeyKey, source.Id);
    }

    public static string RecordedSourceManifestId(ProjectDesignSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        source.Metadata ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        return Value(source.Metadata, ManifestIdKey);
    }

    public static string RecordedSourceContentHash(ProjectDesignSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        source.Metadata ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        return Value(source.Metadata, ContentHashKey).ToLowerInvariant();
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

    public static string CloudOwnerEmail(ProjectDesignSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        source.Metadata ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        return Value(source.Metadata, OwnerEmailKey).ToLowerInvariant();
    }

    public static void BindCloudOwner(ProjectDesignSource source, string ownerEmail)
    {
        ArgumentNullException.ThrowIfNull(source);
        string normalized = (ownerEmail ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
            throw new ArgumentException("Cloud source owner email is required.", nameof(ownerEmail));
        source.Metadata ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        source.Metadata[OwnerEmailKey] = normalized;
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
                ProjectDesignSourceClassification.EffectivePurpose(source).ToString(),
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
        candidate.Source.Metadata[SyncedSemanticVersionKey] =
            CurrentSourceSemanticSyncVersion;
        candidate.Source.Metadata[SyncedSourcePurposeKey] =
            candidate.SourcePurpose;
    }

    public static IReadOnlyList<string> PendingAlbumComponents(ProjectWorkspace project)
    {
        ArgumentNullException.ThrowIfNull(project);
        project.Cloud.PendingAlbumComponentCodes ??= [];
        project.Cloud.CanonicalAlbumRebuildComponentCodes ??= [];
        IEnumerable<string> serverRequiredComponents =
            project.Cloud.CanonicalAlbumRebuildPending
                ? project.Cloud.CanonicalAlbumRebuildComponentCodes
                : [];
        return project.Cloud.PendingAlbumComponentCodes
            .Concat(serverRequiredComponents)
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static void MarkAlbumComponentsPending(
        ProjectWorkspace project,
        IEnumerable<string> componentCodes)
    {
        ArgumentNullException.ThrowIfNull(project);
        project.Cloud.PendingAlbumComponentCodes ??= [];
        foreach (string code in componentCodes ?? [])
        {
            string normalized = code?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(normalized) ||
                project.Cloud.PendingAlbumComponentCodes.Contains(
                    normalized,
                    StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }
            project.Cloud.PendingAlbumComponentCodes.Add(normalized);
        }
        MarkPending(project);
    }

    public static void MarkAlbumComponentPendingForBinding(
        ProjectWorkspace project,
        string componentCode,
        string ownerEmail,
        string deviceFingerprint,
        bool isRemoval,
        DateTimeOffset? claimedAtUtc = null,
        string? registrySourceId = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        string code = componentCode?.Trim() ?? "";
        string owner = (ownerEmail ?? "").Trim().ToLowerInvariant();
        string device = (deviceFingerprint ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException(
                "Album component code is required.",
                nameof(componentCode));
        if (string.IsNullOrWhiteSpace(owner))
            throw new ArgumentException(
                "Album component owner is required.",
                nameof(ownerEmail));
        if (string.IsNullOrWhiteSpace(device))
            throw new ArgumentException(
                "Album component device is required.",
                nameof(deviceFingerprint));

        project.Cloud.PendingAlbumComponentClaims ??= [];
        ProjectLocalAlbumComponentClaim? claim =
            project.Cloud.PendingAlbumComponentClaims.FirstOrDefault(candidate =>
                candidate.ComponentCode.Equals(
                    code,
                    StringComparison.OrdinalIgnoreCase) &&
                candidate.OwnerEmail.Equals(
                    owner,
                    StringComparison.OrdinalIgnoreCase) &&
                candidate.DeviceFingerprint.Equals(
                    device,
                    StringComparison.OrdinalIgnoreCase));
        if (claim is null)
        {
            claim = new ProjectLocalAlbumComponentClaim
            {
                ComponentCode = code,
                OwnerEmail = owner,
                DeviceFingerprint = device,
            };
            project.Cloud.PendingAlbumComponentClaims.Add(claim);
        }
        claim.ClaimToken = Guid.NewGuid().ToString("N");
        claim.IsRemoval = isRemoval;
        claim.ClaimedAtUtc = claimedAtUtc ?? DateTimeOffset.UtcNow;
        if (registrySourceId is not null)
            claim.RegistrySourceId = registrySourceId.Trim();
        MarkAlbumComponentsPending(project, [code]);
    }

    public static ProjectLocalAlbumComponentClaim? PendingAlbumComponentClaim(
        ProjectWorkspace project,
        string componentCode,
        string? ownerEmail,
        string? deviceFingerprint)
    {
        ArgumentNullException.ThrowIfNull(project);
        string code = componentCode?.Trim() ?? "";
        string owner = (ownerEmail ?? "").Trim().ToLowerInvariant();
        string device = (deviceFingerprint ?? "").Trim().ToLowerInvariant();
        project.Cloud.PendingAlbumComponentClaims ??= [];
        return project.Cloud.PendingAlbumComponentClaims.FirstOrDefault(claim =>
            claim.ComponentCode.Equals(code, StringComparison.OrdinalIgnoreCase) &&
            claim.OwnerEmail.Equals(owner, StringComparison.OrdinalIgnoreCase) &&
            claim.DeviceFingerprint.Equals(device, StringComparison.OrdinalIgnoreCase));
    }

    public static void MarkAlbumComponentsSynced(
        ProjectWorkspace project,
        IEnumerable<string> componentCodes)
    {
        ArgumentNullException.ThrowIfNull(project);
        project.Cloud.PendingAlbumComponentCodes ??= [];
        HashSet<string> completed = (componentCodes ?? [])
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        project.Cloud.PendingAlbumComponentCodes.RemoveAll(completed.Contains);
        project.Cloud.PendingAlbumComponentClaims ??= [];
        project.Cloud.PendingAlbumComponentClaims.RemoveAll(claim =>
            completed.Contains(claim.ComponentCode));
    }

    public static void MarkAlbumComponentsSyncedForBinding(
        ProjectWorkspace project,
        IEnumerable<string> componentCodes,
        string? ownerEmail,
        string? deviceFingerprint)
    {
        MarkAlbumComponentsSyncedForBinding(
            project,
            componentCodes,
            ownerEmail,
            deviceFingerprint,
            acceptedClaims: null);
    }

    public static void MarkAlbumComponentsSyncedForBinding(
        ProjectWorkspace project,
        IEnumerable<string> componentCodes,
        string? ownerEmail,
        string? deviceFingerprint,
        IEnumerable<ProjectAlbumComponentClaimAcknowledgement>? acceptedClaims)
    {
        ArgumentNullException.ThrowIfNull(project);
        HashSet<string> completed = (componentCodes ?? [])
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        string owner = (ownerEmail ?? "").Trim().ToLowerInvariant();
        string device = (deviceFingerprint ?? "").Trim().ToLowerInvariant();
        Dictionary<string, string>? acceptedTokens = acceptedClaims?
            .Where(claim =>
                claim.OwnerEmail.Equals(owner, StringComparison.OrdinalIgnoreCase) &&
                claim.DeviceFingerprint.Equals(device, StringComparison.OrdinalIgnoreCase) &&
                completed.Contains(claim.ComponentCode) &&
                !string.IsNullOrWhiteSpace(claim.ClaimToken))
            .GroupBy(claim => claim.ComponentCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Last().ClaimToken,
                StringComparer.OrdinalIgnoreCase);
        project.Cloud.PendingAlbumComponentClaims ??= [];
        project.Cloud.PendingAlbumComponentClaims.RemoveAll(claim =>
            completed.Contains(claim.ComponentCode) &&
            claim.OwnerEmail.Equals(owner, StringComparison.OrdinalIgnoreCase) &&
            claim.DeviceFingerprint.Equals(device, StringComparison.OrdinalIgnoreCase) &&
            (acceptedTokens is null ||
             acceptedTokens.TryGetValue(claim.ComponentCode, out string? token) &&
             claim.ClaimToken.Equals(token, StringComparison.OrdinalIgnoreCase)));

        project.Cloud.PendingAlbumComponentCodes ??= [];
        project.Cloud.PendingAlbumComponentCodes.RemoveAll(code =>
            completed.Contains(code) &&
            !project.Cloud.PendingAlbumComponentClaims.Any(claim =>
                claim.ComponentCode.Equals(code, StringComparison.OrdinalIgnoreCase)));
    }

    public static void MarkCanonicalTitleBlockPending(ProjectWorkspace project)
    {
        ArgumentNullException.ThrowIfNull(project);
        project.Cloud.CanonicalTitleBlockPending = true;
        MarkPending(project);
    }

    public static void MarkCanonicalTitleBlockPublished(
        ProjectWorkspace project,
        string signature)
    {
        ArgumentNullException.ThrowIfNull(project);
        project.Cloud.CanonicalTitleBlockPending = false;
        project.Cloud.LastPublishedTitleBlockSignature =
            signature?.Trim().ToLowerInvariant() ?? "";
    }

    public static void CaptureBuildingCompositionEditBase(ProjectWorkspace project)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (project.Cloud.BuildingCompositionEditBaseCaptured)
            return;

        project.Cloud.BuildingCompositionEditBaseCaptured = true;
        project.Cloud.BuildingCompositionEditBaseVersion =
            Math.Max(0, project.Cloud.SharedBuildingCompositionVersion);
        project.Cloud.BuildingCompositionEditBaseGroups =
            ProjectBuildingComposition.NormalizeGroups(
                    (project.Cloud.SharedBuildingGroups ?? [])
                        .OfType<ProjectCloudBuildingGroupReference>()
                        .Select(group => new ProjectBuildingGroup
                        {
                            Id = group.Id,
                            Name = group.Name,
                            Order = group.Order,
                        }))
                .Select(group => new ProjectCloudBuildingGroupReference
                {
                    Id = group.Id,
                    Name = group.Name,
                    Order = group.Order,
                })
                .ToList();
    }

    public static void MarkBuildingCompositionPending(ProjectWorkspace project)
    {
        ArgumentNullException.ThrowIfNull(project);
        CaptureBuildingCompositionEditBase(project);
        project.Cloud.BuildingCompositionPending = true;
        IEnumerable<string> currentCodes = ProjectBuildingComposition
            .NormalizeGroups(project.BuildingGroups)
            .Select(BuildingSubCoverComponentCode);
        IEnumerable<string> existingCodes = (project.Cloud.SharedAlbumComponents ?? [])
            .Select(component => component.Code?.Trim() ?? "")
            .Where(IsBuildingSubCoverComponentCode);
        MarkAlbumComponentsPending(project, currentCodes.Concat(existingCodes));
    }

    public static string BuildingSubCoverComponentCode(ProjectBuildingGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        return BuildingSubCoverComponentCodePrefix + "studio-building:" + group.Id.Trim();
    }

    public static bool IsBuildingSubCoverComponentCode(string? componentCode) =>
        !string.IsNullOrWhiteSpace(componentCode) &&
        componentCode.Trim().StartsWith(
            BuildingSubCoverComponentCodePrefix,
            StringComparison.OrdinalIgnoreCase);

    public static void MarkBuildingCompositionSynced(ProjectWorkspace project)
    {
        ArgumentNullException.ThrowIfNull(project);
        project.Cloud.BuildingCompositionPending = false;
        project.Cloud.BuildingCompositionEditBaseCaptured = false;
        project.Cloud.BuildingCompositionEditBaseVersion = 0;
        project.Cloud.BuildingCompositionEditBaseGroups = [];
        project.Cloud.PendingBuildingGroupDeletionIds = [];
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

    public static void MarkCloudChecked(ProjectWorkspace project, DateTimeOffset checkedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(project);
        project.Cloud.LastCloudCheckedAtUtc = checkedAtUtc;
    }

    public static void MarkCloudRefreshed(
        ProjectWorkspace project,
        string concurrencyToken,
        DateTimeOffset refreshedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(project);
        project.Cloud.LastCloudCheckedAtUtc = refreshedAtUtc;
        project.Cloud.LastCloudRefreshedAtUtc = refreshedAtUtc;
        project.Cloud.LastServerConcurrencyToken = concurrencyToken?.Trim() ?? "";
    }

    public static void RecordReceivedAlbum(
        ProjectWorkspace project,
        string revisionId,
        int revisionNumber,
        string sha256,
        string pdfPath = "")
    {
        ArgumentNullException.ThrowIfNull(project);
        project.Cloud.LastReceivedAlbumRevisionId = revisionId?.Trim() ?? "";
        project.Cloud.LastReceivedAlbumRevisionNumber = Math.Max(0, revisionNumber);
        project.Cloud.LastReceivedAlbumSha256 = sha256?.Trim().ToLowerInvariant() ?? "";
        project.Cloud.LastReceivedAlbumPdfPath = pdfPath?.Trim() ?? "";
    }

    public static void ClearReceivedAlbum(ProjectWorkspace project)
    {
        ArgumentNullException.ThrowIfNull(project);
        project.Cloud.LastReceivedAlbumRevisionId = "";
        project.Cloud.LastReceivedAlbumRevisionNumber = 0;
        project.Cloud.LastReceivedAlbumSha256 = "";
        project.Cloud.LastReceivedAlbumPdfPath = "";
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
            candidate.ContentHash.Equals(Value(metadata, SyncedContentHashKey), StringComparison.OrdinalIgnoreCase) &&
            CurrentSourceSemanticSyncVersion.Equals(
                Value(metadata, SyncedSemanticVersionKey),
                StringComparison.Ordinal) &&
            candidate.SourcePurpose.Equals(
                Value(metadata, SyncedSourcePurposeKey),
                StringComparison.OrdinalIgnoreCase);
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

public sealed record ProjectAlbumComponentClaimAcknowledgement(
    string ComponentCode,
    string OwnerEmail,
    string DeviceFingerprint,
    string ClaimToken);
