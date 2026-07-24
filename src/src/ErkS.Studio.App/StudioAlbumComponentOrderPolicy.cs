using ErkS.Platform.Core;

namespace ErkS.Studio;

/// <summary>
/// Keeps Cloud album components in the same semantic order as Studio's album
/// sequence, independently of the order in which contributors registered
/// their source packages.
/// </summary>
internal static class StudioAlbumComponentOrderPolicy
{
    private const int GeneralPlanBase = 100_000;
    private const int BuildingBase = 200_000;
    private const int BuildingStride = 10_000;
    private const int BuildingSourceOffset = 1_000;
    private const int UnassignedSourceBase = 800_000;
    private const int VisualizationBase = 900_000;

    public static int Resolve(
        ProjectWorkspace project,
        string componentCode,
        string sourceKey,
        int localOrder,
        IReadOnlyDictionary<string, int> sourceOrder)
    {
        ArgumentNullException.ThrowIfNull(project);
        string code = (componentCode ?? "").Trim();
        string normalizedSourceKey = (sourceKey ?? "").Trim();
        int safeLocalOrder = Math.Max(0, localOrder);

        if (TryResolveFixedComponentOrder(
                code,
                normalizedSourceKey,
                out int fixedOrder))
        {
            return fixedOrder;
        }

        if (ProjectCloudSyncMetadata.IsBuildingSubCoverComponentCode(code) &&
            TryResolveSubCoverGroup(project, code, out ProjectBuildingGroup subCoverGroup))
        {
            return BuildingOrder(BuildingRank(project, subCoverGroup));
        }

        if (normalizedSourceKey.Equals(
                StudioAlbumComponentIdentity.VisualizationSourceKey,
                StringComparison.OrdinalIgnoreCase) ||
            code.Equals(
                ProjectCloudSyncMetadata.VisualizationsComponentCode,
                StringComparison.OrdinalIgnoreCase))
        {
            return VisualizationBase;
        }

        ProjectDesignSource? source = ResolveSource(project, normalizedSourceKey);
        int sourceTieBreaker = sourceOrder.TryGetValue(code, out int sourceIndex)
            ? sourceIndex
            : safeLocalOrder;
        if (source is not null)
        {
            if (ProjectDesignSourceClassification.IsGeneralPlan(source))
                return GeneralPlanBase + sourceTieBreaker;

            string buildingGroupId = ResolveBuildingGroupId(project, source);
            ProjectBuildingGroup? buildingGroup = project.BuildingGroups.FirstOrDefault(group =>
                group.Id.Equals(buildingGroupId, StringComparison.OrdinalIgnoreCase));
            if (buildingGroup is not null)
            {
                return BuildingOrder(BuildingRank(project, buildingGroup)) +
                    BuildingSourceOffset +
                    sourceTieBreaker;
            }
        }
        else
        {
            if (IsSharedGeneralPlanSource(project, normalizedSourceKey))
                return GeneralPlanBase + sourceTieBreaker;

            ProjectBuildingGroup? sharedBuilding =
                ResolveSharedBuildingGroup(project, normalizedSourceKey);
            if (sharedBuilding is not null)
            {
                return BuildingOrder(BuildingRank(project, sharedBuilding)) +
                    BuildingSourceOffset +
                    sourceTieBreaker;
            }
        }

        if (code.StartsWith("source:", StringComparison.OrdinalIgnoreCase))
            return UnassignedSourceBase + safeLocalOrder;

        return 50_000;
    }

    private static bool TryResolveFixedComponentOrder(
        string code,
        string sourceKey,
        out int order)
    {
        if (code.Equals(
                ProjectCloudSyncMetadata.CoverComponentCode,
                StringComparison.OrdinalIgnoreCase) ||
            code.StartsWith("generated:cover:", StringComparison.OrdinalIgnoreCase))
        {
            order = 0;
            return true;
        }

        if (code.StartsWith(
                "generated:table-of-contents",
                StringComparison.OrdinalIgnoreCase))
        {
            order = 5_000;
            return true;
        }

        if (code.Equals(
                ProjectCloudSyncMetadata.CompanyRegistrationComponentCode,
                StringComparison.OrdinalIgnoreCase))
        {
            order = 10_000;
            return true;
        }

        if (code.Equals(
                ProjectCloudSyncMetadata.CompanyLicenseComponentCode,
                StringComparison.OrdinalIgnoreCase))
        {
            order = 20_000;
            return true;
        }

        if (sourceKey.Equals(
                StudioAlbumComponentIdentity.AtdSourceKey,
                StringComparison.OrdinalIgnoreCase) ||
            code.Equals(
                ProjectCloudSyncMetadata.ApprovedAtdComponentCode,
                StringComparison.OrdinalIgnoreCase) ||
            code.EndsWith(
                ":" + StudioAlbumComponentIdentity.AtdSourceKey,
                StringComparison.OrdinalIgnoreCase))
        {
            order = 30_000;
            return true;
        }

        if (code.Equals(
                ProjectCloudSyncMetadata.SiteContextComponentCode,
                StringComparison.OrdinalIgnoreCase))
        {
            order = 40_000;
            return true;
        }

        order = 0;
        return false;
    }

    private static int BuildingOrder(int oneBasedBuildingOrder) =>
        BuildingBase + (Math.Max(1, oneBasedBuildingOrder) - 1) * BuildingStride;

    private static int BuildingRank(
        ProjectWorkspace project,
        ProjectBuildingGroup buildingGroup)
    {
        ProjectBuildingGroup[] orderedGroups = project.BuildingGroups
            .OrderBy(group => Math.Max(1, group.Order))
            .ThenBy(group => group.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        int index = Array.FindIndex(
            orderedGroups,
            group => group.Id.Equals(
                buildingGroup.Id,
                StringComparison.OrdinalIgnoreCase));
        return index >= 0 ? index + 1 : Math.Max(1, buildingGroup.Order);
    }

    private static ProjectDesignSource? ResolveSource(
        ProjectWorkspace project,
        string sourceKey) =>
        project.Sources.FirstOrDefault(source =>
            source.Id.Equals(sourceKey, StringComparison.OrdinalIgnoreCase) ||
            ProjectCloudSyncMetadata.CloudSourceKey(source).Equals(
                sourceKey,
                StringComparison.OrdinalIgnoreCase));

    private static bool IsSharedGeneralPlanSource(
        ProjectWorkspace project,
        string sourceKey)
    {
        ProjectCloudSourceReference[] sharedSources =
            (project.Cloud.SharedSources ?? [])
            .Where(source =>
                !string.IsNullOrWhiteSpace(source.SourceKey) &&
                (string.IsNullOrWhiteSpace(source.Status) ||
                 source.Status.Equals("Registered", StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        ProjectCloudSourceReference? source = sharedSources.FirstOrDefault(item =>
            item.SourceKey.Equals(sourceKey, StringComparison.OrdinalIgnoreCase));
        if (source is null)
            return false;

        if (IsGeneralPlanApplication(source.SourceApplication))
            return true;

        bool hasExplicitGeneralPlan = sharedSources.Any(item =>
            IsGeneralPlanApplication(item.SourceApplication));
        if (hasExplicitGeneralPlan ||
            !source.SourceApplication.Contains(
                "autocad",
                StringComparison.OrdinalIgnoreCase) ||
            ResolveSharedBuildingGroup(project, sourceKey) is not null)
        {
            return false;
        }

        int unassignedAutoCadCount = sharedSources.Count(item =>
            item.SourceApplication.Contains(
                "autocad",
                StringComparison.OrdinalIgnoreCase) &&
            ResolveSharedBuildingGroup(project, item.SourceKey) is null);
        return unassignedAutoCadCount == 1;
    }

    private static bool IsGeneralPlanApplication(string? application)
    {
        string value = application?.Trim() ?? "";
        return value.Contains("citygen", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("general plan", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("master plan", StringComparison.OrdinalIgnoreCase);
    }

    private static ProjectBuildingGroup? ResolveSharedBuildingGroup(
        ProjectWorkspace project,
        string sourceKey)
    {
        HashSet<string> assignedGroupIds =
            (project.Cloud.SharedBuildingSheetAssignments ?? [])
            .Where(assignment =>
                assignment.SourceKey.Equals(
                    sourceKey,
                    StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(assignment.BuildingGroupId))
            .Select(assignment => assignment.BuildingGroupId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return project.BuildingGroups
            .Where(group => assignedGroupIds.Contains(group.Id))
            .OrderBy(group => BuildingRank(project, group))
            .FirstOrDefault();
    }

    private static string ResolveBuildingGroupId(
        ProjectWorkspace project,
        ProjectDesignSource source)
    {
        string explicitGroupId = ProjectDesignSourceClassification.BuildingGroupId(source);
        if (!string.IsNullOrWhiteSpace(explicitGroupId))
            return explicitGroupId;

        string sourceIdPrefix = source.Id.Trim() + "|";
        string sourceKeyPrefix = ProjectCloudSyncMetadata.CloudSourceKey(source).Trim() + "|";
        string[] assignedGroups = (project.SheetBuildingAssignments ??
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))
            .Where(assignment =>
                assignment.Key.StartsWith(sourceIdPrefix, StringComparison.OrdinalIgnoreCase) ||
                assignment.Key.StartsWith(sourceKeyPrefix, StringComparison.OrdinalIgnoreCase))
            .Select(assignment => assignment.Value?.Trim() ?? "")
            .Where(groupId => !string.IsNullOrWhiteSpace(groupId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return assignedGroups.Length == 1 ? assignedGroups[0] : "";
    }

    private static bool TryResolveSubCoverGroup(
        ProjectWorkspace project,
        string componentCode,
        out ProjectBuildingGroup group)
    {
        string identity = componentCode[
            ProjectCloudSyncMetadata.BuildingSubCoverComponentCodePrefix.Length..].Trim();
        const string studioBuildingPrefix = "studio-building:";
        const string packageBuildingIdPrefix = "package-building:id:";
        const string packageBuildingNamePrefix = "package-building:name:";
        string groupId = identity;
        string groupName = identity;
        if (identity.StartsWith(studioBuildingPrefix, StringComparison.OrdinalIgnoreCase))
        {
            groupId = identity[studioBuildingPrefix.Length..].Trim();
            groupName = "";
        }
        else if (identity.StartsWith(packageBuildingIdPrefix, StringComparison.OrdinalIgnoreCase))
        {
            groupId = identity[packageBuildingIdPrefix.Length..].Trim();
            groupName = "";
        }
        else if (identity.StartsWith(packageBuildingNamePrefix, StringComparison.OrdinalIgnoreCase))
        {
            groupId = "";
            groupName = identity[packageBuildingNamePrefix.Length..].Trim();
        }

        ProjectBuildingGroup? matched = project.BuildingGroups.FirstOrDefault(candidate =>
            (!string.IsNullOrWhiteSpace(groupId) &&
             candidate.Id.Equals(groupId, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(groupName) &&
             candidate.Name.Equals(groupName, StringComparison.OrdinalIgnoreCase)));
        group = matched!;
        return matched is not null;
    }
}
