using ErkS.Platform.Core;

namespace ErkS.Studio;

internal sealed record StudioBuildingSubCoverSelection(
    IReadOnlyList<StudioCloudAlbumSection> Components,
    IReadOnlyList<string> MissingRequiredCoverCodes);

/// <summary>
/// Keeps a building source contribution and its generated sub-cover atomic.
/// A contributor owns the source pages, while concept.write authorizes the
/// canonical cover; publishing only one of them would leave a structurally
/// incomplete shared album.
/// </summary>
internal static class StudioBuildingSubCoverSelectionPolicy
{
    public static StudioBuildingSubCoverSelection IncludeRequiredCovers(
        ProjectWorkspace project,
        IReadOnlyList<StudioCloudAlbumSection> rendered,
        IEnumerable<StudioCloudAlbumSection> selected)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(rendered);
        ArgumentNullException.ThrowIfNull(selected);

        HashSet<string> selectedCodes = selected
            .Where(component => component is not null)
            .Select(component => component.Code?.Trim() ?? "")
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        HashSet<string> requiredCoverCodes = selected
            .Where(component =>
                component is not null &&
                StudioAlbumComponentIdentity.IsSourceComponent(component))
            .SelectMany(component => ResolveBuildingGroups(project, component))
            .Select(ProjectCloudSyncMetadata.BuildingSubCoverComponentCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (StudioCloudAlbumSection cover in rendered.Where(component =>
                     requiredCoverCodes.Contains(
                         StudioAlbumComponentIdentity.CanonicalBuildingSubCoverCode(
                             project,
                             component.Code))))
        {
            selectedCodes.Add(cover.Code);
        }

        string[] renderedCoverCodes = rendered
            .Where(component =>
                ProjectCloudSyncMetadata.IsBuildingSubCoverComponentCode(
                    component.Code))
            .Select(component =>
                StudioAlbumComponentIdentity.CanonicalBuildingSubCoverCode(
                    project,
                    component.Code))
            .ToArray();
        string[] missing = requiredCoverCodes
            .Where(code =>
                !renderedCoverCodes.Contains(
                    code,
                    StringComparer.OrdinalIgnoreCase))
            .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new StudioBuildingSubCoverSelection(
            rendered.Where(component => selectedCodes.Contains(component.Code)).ToList(),
            missing);
    }

    private static IEnumerable<ProjectBuildingGroup> ResolveBuildingGroups(
        ProjectWorkspace project,
        StudioCloudAlbumSection component)
    {
        if (StudioAlbumComponentIdentity.TryGetBuildingSectionKey(
                component.Code,
                out string sectionKey))
        {
            return StudioAlbumComponentIdentity.TryResolveBuildingGroup(
                project,
                sectionKey,
                out ProjectBuildingGroup slicedGroup)
                    ? [slicedGroup]
                    : [];
        }

        var groupIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string owner = NormalizeOwner(component.OwnerEmail);
        string sourceKey = (component.SourceKey ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(sourceKey))
        {
            foreach (ProjectCloudBuildingSheetAssignmentReference assignment in
                     project.Cloud.SharedBuildingSheetAssignments ?? [])
            {
                if (!assignment.SourceKey.Equals(
                        sourceKey,
                        StringComparison.OrdinalIgnoreCase) ||
                    !OwnersMatch(
                        owner,
                        NormalizeOwner(assignment.SourceOwnerEmail)))
                {
                    continue;
                }
                groupIds.Add(assignment.BuildingGroupId);
            }

            foreach (ProjectDesignSource source in project.Sources.Where(source =>
                         ProjectCloudSyncMetadata.CloudSourceKey(source).Equals(
                             sourceKey,
                             StringComparison.OrdinalIgnoreCase) &&
                         OwnersMatch(
                             owner,
                             NormalizeOwner(
                                 ProjectCloudSyncMetadata.CloudOwnerEmail(source)))))
            {
                string groupId =
                    ProjectDesignSourceClassification.BuildingGroupId(source);
                if (!string.IsNullOrWhiteSpace(groupId))
                    groupIds.Add(groupId);
            }
        }

        return project.BuildingGroups
            .Where(group => groupIds.Contains(group.Id))
            .OrderBy(group => group.Order)
            .ThenBy(group => group.Id, StringComparer.OrdinalIgnoreCase);
    }

    private static bool OwnersMatch(string left, string right) =>
        string.IsNullOrWhiteSpace(left) ||
        string.IsNullOrWhiteSpace(right) ||
        left.Equals(right, StringComparison.OrdinalIgnoreCase);

    private static string NormalizeOwner(string? value) =>
        (value ?? "").Trim().ToLowerInvariant();
}
