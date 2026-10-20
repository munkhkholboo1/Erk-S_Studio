using ErkS.Platform.Core;

namespace ErkS.Studio;

/// <summary>
/// Whether the shared album already holds a building's sub-cover as it stands
/// in this project: present, with pages, not retired, and titled with the
/// building's current name - the sub-cover carries nothing else.
/// </summary>
internal static class StudioSubCoverServerState
{
    public static bool IsCurrent(ProjectWorkspace project, StudioCloudAlbumSection component)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (component is null ||
            !ProjectCloudSyncMetadata.IsBuildingSubCoverComponentCode(component.Code) ||
            (component.PageNumbers?.Length ?? 0) == 0)
        {
            return false;
        }

        string status = (component.Status ?? "").Trim();
        if (status.Equals("Removed", StringComparison.OrdinalIgnoreCase) ||
            status.Equals("Deleted", StringComparison.OrdinalIgnoreCase) ||
            status.Equals("Retired", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string code = StudioAlbumComponentIdentity.CanonicalBuildingSubCoverCode(project, component.Code);
        ProjectBuildingGroup? group = (project.BuildingGroups ?? []).FirstOrDefault(item =>
            ProjectCloudSyncMetadata.BuildingSubCoverComponentCode(item)
                .Equals(code, StringComparison.OrdinalIgnoreCase));
        string name = group?.Name?.Trim() ?? "";
        string label = (component.Label ?? "").Trim();
        return name.Length > 0 &&
            (label.Equals(name, StringComparison.OrdinalIgnoreCase) ||
             label.StartsWith(name + " ·", StringComparison.OrdinalIgnoreCase));
    }
}
