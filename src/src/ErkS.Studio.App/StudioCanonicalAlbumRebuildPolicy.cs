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

        IEnumerable<ProjectBuildingGroup> candidateGroups =
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
        List<ProjectBuildingGroup> groups = ProjectBuildingComposition
            .NormalizeGroups(candidateGroups);
        HashSet<string> referencedGroupIds = ResolveReferencedBuildingGroupIds(
            localProject,
            composition,
            revision,
            groups);
        IEnumerable<string> groupCodes = groups
            .Where(group => referencedGroupIds.Contains(group.Id))
            .Select(ProjectCloudSyncMetadata.BuildingSubCoverComponentCode);
        string[] pendingCodes = groupCodes
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

    private static HashSet<string> ResolveReferencedBuildingGroupIds(
        ProjectWorkspace localProject,
        StudioCloudBuildingComposition? composition,
        StudioCloudAlbumRevision? revision,
        IReadOnlyList<ProjectBuildingGroup> groups)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var resolutionProject = new ProjectWorkspace
        {
            BuildingGroups = groups.ToList(),
        };
        IReadOnlyList<BuildingAssignment> assignments =
            ResolveAssignments(localProject, composition);
        IEnumerable<SourceComponent> components = revision is not null
            ? (revision.SectionManifest ?? [])
                .Where(IsActiveSourceComponent)
                .Select(component => new SourceComponent(
                    component.Code,
                    component.OwnerEmail,
                    component.SourceKey))
            : (localProject.Cloud.SharedAlbumComponents ?? [])
                .Where(IsActiveSourceComponent)
                .Select(component => new SourceComponent(
                    component.Code,
                    component.OwnerEmail,
                    component.SourceKey));

        foreach (SourceComponent component in components.Distinct())
        {
            if (StudioAlbumComponentIdentity.TryGetBuildingSectionKey(
                    component.Code,
                    out string sectionKey))
            {
                if (StudioAlbumComponentIdentity.TryResolveBuildingGroup(
                        resolutionProject,
                        sectionKey,
                        out ProjectBuildingGroup slicedGroup))
                {
                    result.Add(slicedGroup.Id);
                }
                continue;
            }

            string owner = NormalizeOwner(component.OwnerEmail);
            string sourceKey = component.SourceKey?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(sourceKey))
                continue;
            foreach (BuildingAssignment assignment in assignments.Where(assignment =>
                         assignment.SourceKey.Equals(
                             sourceKey,
                             StringComparison.OrdinalIgnoreCase) &&
                         OwnersMatch(
                             owner,
                             NormalizeOwner(assignment.OwnerEmail))))
            {
                result.Add(assignment.BuildingGroupId);
            }
        }

        result.IntersectWith(groups.Select(group => group.Id));
        return result;
    }

    private static IReadOnlyList<BuildingAssignment> ResolveAssignments(
        ProjectWorkspace localProject,
        StudioCloudBuildingComposition? composition)
    {
        if (composition is not null)
        {
            return (composition.SheetAssignments ?? [])
                .Where(assignment =>
                    !string.IsNullOrWhiteSpace(assignment.SourceKey) &&
                    !string.IsNullOrWhiteSpace(assignment.BuildingGroupId))
                .Select(assignment => new BuildingAssignment(
                    assignment.SourceOwnerEmail,
                    assignment.SourceKey.Trim(),
                    assignment.BuildingGroupId.Trim()))
                .ToList();
        }

        return (localProject.Cloud.SharedBuildingSheetAssignments ?? [])
            .Where(assignment =>
                !string.IsNullOrWhiteSpace(assignment.SourceKey) &&
                !string.IsNullOrWhiteSpace(assignment.BuildingGroupId))
            .Select(assignment => new BuildingAssignment(
                assignment.SourceOwnerEmail,
                assignment.SourceKey.Trim(),
                assignment.BuildingGroupId.Trim()))
            .ToList();
    }

    private static bool IsActiveSourceComponent(
        StudioCloudAlbumSection component) =>
        component is not null &&
        (component.PageNumbers?.Length ?? 0) > 0 &&
        !IsInactiveStatus(component.Status) &&
        StudioAlbumComponentIdentity.IsSourceComponent(component);

    private static bool IsActiveSourceComponent(
        ProjectCloudAlbumComponentReference component) =>
        component is not null &&
        (component.PageNumbers?.Count ?? 0) > 0 &&
        !IsInactiveStatus(component.Status) &&
        ((component.ComponentKind ?? "").Equals(
             StudioAlbumComponentIdentity.SourceComponentKind,
             StringComparison.OrdinalIgnoreCase) ||
         (component.Code ?? "").StartsWith(
             "source:",
             StringComparison.OrdinalIgnoreCase));

    private static bool IsInactiveStatus(string? status)
    {
        string normalized = (status ?? "").Trim();
        return normalized.Equals("Removed", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("Deleted", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("Retired", StringComparison.OrdinalIgnoreCase);
    }

    private static bool OwnersMatch(string left, string right) =>
        string.IsNullOrWhiteSpace(left) ||
        string.IsNullOrWhiteSpace(right) ||
        left.Equals(right, StringComparison.OrdinalIgnoreCase);

    private static string NormalizeOwner(string? value) =>
        (value ?? "").Trim().ToLowerInvariant();

    private sealed record SourceComponent(
        string Code,
        string OwnerEmail,
        string SourceKey);

    private sealed record BuildingAssignment(
        string OwnerEmail,
        string SourceKey,
        string BuildingGroupId);

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
