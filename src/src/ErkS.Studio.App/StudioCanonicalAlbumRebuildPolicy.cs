using ErkS.Platform.Core;

namespace ErkS.Studio;

internal sealed record StudioCanonicalAlbumRebuildResolution(
    bool IsPending,
    int RequiredBuildingCompositionVersion,
    int CurrentBuildingCompositionVersion,
    IReadOnlyList<string> PendingComponentCodes,
    IReadOnlyList<string> TombstoneCodes,
    IReadOnlyList<string> RejectedTombstoneCodes)
{
    public bool CanPresentCanonicalPdf => !IsPending;
}

/// <summary>
/// Converts the server's canonical-album rebuild signal into a deterministic
/// Studio component plan. Server-required work is persisted separately from
/// user dirty state so a later acknowledgement cannot erase local edits.
/// </summary>
internal static class StudioCanonicalAlbumRebuildPolicy
{
    public const string DiagnosticReasonCode = "cloud_album_rebuild_pending";

    public static StudioCanonicalAlbumRebuildResolution Resolve(
        ProjectWorkspace localProject,
        StudioCloudProjectDetail? cloudProject)
    {
        ArgumentNullException.ThrowIfNull(localProject);
        StudioCloudAlbum? album = SelectConceptAlbum(cloudProject?.Albums);
        StudioCloudBuildingComposition? composition = cloudProject?.BuildingComposition;
        StudioCloudAlbumRevision? revision = CurrentRevision(album);

        int requiredVersion = Math.Max(
            Math.Max(0, album?.RequiredBuildingCompositionVersion ?? 0),
            Math.Max(0, composition?.Version ?? 0));
        int currentVersion = Math.Max(
            0,
            revision?.BuildingCompositionVersion ?? 0);

        string[] suppliedTombstones = (album?.PendingComponentTombstoneCodes ?? [])
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim())
            .ToArray();
        string[] tombstones = suppliedTombstones
            .Where(ProjectCloudSyncMetadata.IsBuildingSubCoverComponentCode)
            .Select(code => code.ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToArray();
        string[] rejected = suppliedTombstones
            .Where(code => !ProjectCloudSyncMetadata.IsBuildingSubCoverComponentCode(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        bool pending = album is not null &&
            (album.CanonicalRebuildPending ||
             currentVersion < requiredVersion ||
             tombstones.Length > 0);
        if (!pending)
        {
            return new StudioCanonicalAlbumRebuildResolution(
                false,
                requiredVersion,
                currentVersion,
                [],
                [],
                rejected);
        }

        IEnumerable<ProjectBuildingGroup> groups =
            composition?.Groups is { Count: > 0 }
                ? composition.Groups
                    .Where(group => !string.IsNullOrWhiteSpace(group.Id))
                    .Select(group => new ProjectBuildingGroup
                    {
                        Id = group.Id.Trim(),
                        Name = group.Name,
                        Order = group.Order,
                    })
                : localProject.BuildingGroups ?? [];
        IEnumerable<string> groupCodes = ProjectBuildingComposition
            .NormalizeGroups(groups)
            .Select(ProjectCloudSyncMetadata.BuildingSubCoverComponentCode);
        IEnumerable<string> currentManifestCodes =
            (revision?.SectionManifest ?? [])
                .Where(component =>
                    ProjectCloudSyncMetadata.IsBuildingSubCoverComponentCode(
                        component.Code))
                .Select(component => component.Code.Trim());
        IEnumerable<string> cachedManifestCodes =
            (localProject.Cloud.SharedAlbumComponents ?? [])
                .Where(component =>
                    ProjectCloudSyncMetadata.IsBuildingSubCoverComponentCode(
                        component.Code))
                .Select(component => component.Code.Trim());
        string[] pendingCodes = groupCodes
            .Concat(currentManifestCodes)
            .Concat(cachedManifestCodes)
            .Concat(tombstones)
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new StudioCanonicalAlbumRebuildResolution(
            true,
            requiredVersion,
            currentVersion,
            pendingCodes,
            tombstones,
            rejected);
    }

    public static StudioCanonicalAlbumRebuildResolution Apply(
        ProjectWorkspace localProject,
        StudioCloudProjectDetail? cloudProject)
    {
        StudioCanonicalAlbumRebuildResolution resolution =
            Resolve(localProject, cloudProject);
        ProjectCloudLink cloud = localProject.Cloud;
        cloud.CanonicalAlbumRebuildPending = resolution.IsPending;
        cloud.CanonicalAlbumRequiredBuildingCompositionVersion =
            resolution.RequiredBuildingCompositionVersion;
        cloud.CanonicalAlbumCurrentBuildingCompositionVersion =
            resolution.CurrentBuildingCompositionVersion;
        cloud.CanonicalAlbumRebuildComponentCodes =
            resolution.PendingComponentCodes.ToList();
        cloud.CanonicalAlbumPendingComponentTombstoneCodes =
            resolution.TombstoneCodes.ToList();

        if (resolution.IsPending)
        {
            if (!cloud.SyncStatus.Equals(
                    ProjectSyncStatuses.Conflict,
                    StringComparison.OrdinalIgnoreCase) &&
                !cloud.SyncStatus.Equals(
                    ProjectSyncStatuses.Error,
                    StringComparison.OrdinalIgnoreCase))
            {
                cloud.SyncStatus = ProjectSyncStatuses.Pending;
            }
            cloud.LastSyncNote = Describe(resolution);
        }
        return resolution;
    }

    public static StudioCanonicalAlbumRebuildResolution ResolvePersisted(
        ProjectWorkspace localProject)
    {
        ArgumentNullException.ThrowIfNull(localProject);
        ProjectCloudLink cloud = localProject.Cloud;
        bool pending = cloud.CanonicalAlbumRebuildPending;
        return new StudioCanonicalAlbumRebuildResolution(
            pending,
            Math.Max(
                0,
                cloud.CanonicalAlbumRequiredBuildingCompositionVersion),
            Math.Max(
                0,
                cloud.CanonicalAlbumCurrentBuildingCompositionVersion),
            pending
                ? NormalizeCodes(cloud.CanonicalAlbumRebuildComponentCodes)
                : [],
            pending
                ? NormalizeCodes(
                    cloud.CanonicalAlbumPendingComponentTombstoneCodes,
                    lowerCase: true)
                : [],
            []);
    }

    public static IReadOnlyList<StudioAlbumComponentUpload> ApplyTombstoneUploads(
        StudioCanonicalAlbumRebuildResolution resolution,
        IEnumerable<StudioCloudAlbumSection> currentComponents,
        IEnumerable<StudioAlbumComponentUpload> existingUploads)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        Dictionary<string, StudioCloudAlbumSection> currentByCode =
            (currentComponents ?? [])
                .Where(component => !string.IsNullOrWhiteSpace(component.Code))
                .GroupBy(component => component.Code.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .OrderBy(component => component.Order)
                        .ThenBy(component =>
                            (component.PageNumbers ?? []).FirstOrDefault())
                        .First(),
                    StringComparer.OrdinalIgnoreCase);
        HashSet<string> tombstoneCodes = resolution.TombstoneCodes
            .Where(ProjectCloudSyncMetadata.IsBuildingSubCoverComponentCode)
            .Select(code => code.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // The server tombstone is authoritative for the accepted building
        // composition. Drop a stale local render of the same code and replace
        // it with one remove-only descriptor.
        var uploads = (existingUploads ?? [])
            .Where(upload =>
                !string.IsNullOrWhiteSpace(upload.Code) &&
                !tombstoneCodes.Contains(upload.Code.Trim()))
            .ToList();
        foreach (string code in tombstoneCodes
                     .OrderBy(code => code, StringComparer.OrdinalIgnoreCase))
        {
            currentByCode.TryGetValue(
                code,
                out StudioCloudAlbumSection? current);
            uploads.Add(new StudioAlbumComponentUpload(
                code,
                current?.Label ?? code,
                current?.Order ?? 0,
                "",
                Remove: true,
                SourceKey: current?.SourceKey ?? "",
                ComponentKind: string.IsNullOrWhiteSpace(current?.ComponentKind)
                    ? StudioAlbumComponentIdentity.GeneratedComponentKind
                    : current.ComponentKind));
        }
        return uploads;
    }

    public static string Describe(
        StudioCanonicalAlbumRebuildResolution resolution)
    {
        string rejectedNotice = resolution.RejectedTombstoneCodes.Count == 0
            ? ""
            : $"{resolution.RejectedTombstoneCodes.Count} invalid tombstone ignored; ";
        return "Canonical album rebuild pending: " +
            $"building composition v{resolution.CurrentBuildingCompositionVersion} -> " +
            $"v{resolution.RequiredBuildingCompositionVersion}; " +
            $"{resolution.TombstoneCodes.Count} subcover tombstone; " +
            rejectedNotice +
            $"{resolution.PendingComponentCodes.Count} component refresh. " +
            $"[reason: {DiagnosticReasonCode}]";
    }

    private static StudioCloudAlbum? SelectConceptAlbum(
        IEnumerable<StudioCloudAlbum>? albums) =>
        (albums ?? [])
            .FirstOrDefault(album =>
                album.AlbumType.Equals(
                    ProjectWorkspace.BuildingArchitectureConcept,
                    StringComparison.OrdinalIgnoreCase))
        ?? (albums ?? []).FirstOrDefault();

    private static StudioCloudAlbumRevision? CurrentRevision(
        StudioCloudAlbum? album) =>
        album?.Revisions.FirstOrDefault(revision =>
            revision.RevisionId.Equals(
                album.CurrentRevisionId,
                StringComparison.OrdinalIgnoreCase))
        ?? album?.Revisions
            .OrderByDescending(revision => revision.RevisionNumber)
            .FirstOrDefault();

    private static string[] NormalizeCodes(
        IEnumerable<string>? codes,
        bool lowerCase = false) =>
        (codes ?? [])
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => lowerCase
                ? code.Trim().ToLowerInvariant()
                : code.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
