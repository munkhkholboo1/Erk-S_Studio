using ErkS.Platform.Core;
using System.Security.Cryptography;
using System.Text;

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
    private const int TemplateSlotStride = 100;
    private const int FallbackBuildingBase = 700_000;
    private const int UnassignedSourceBase = 800_000;
    private const int VisualizationBase = 900_000;

    public static IReadOnlyDictionary<string, int> CreateStableSourceOrder(
        IEnumerable<StudioCloudSourcePackage> sources) =>
        (sources ?? [])
            .Where(source =>
                !string.IsNullOrWhiteSpace(source.SourceKey) &&
                !string.IsNullOrWhiteSpace(source.RegisteredBy) &&
                (string.IsNullOrWhiteSpace(source.Status) ||
                 source.Status.Equals(
                     "Registered",
                     StringComparison.OrdinalIgnoreCase)))
            .Select(source => new
            {
                Code = StudioAlbumComponentIdentity.SourceCode(
                    source.RegisteredBy,
                    source.SourceKey),
                SourceId = (source.SourceId ?? "").Trim(),
            })
            .GroupBy(item => item.Code, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderBy(item => item.SourceId, StringComparer.OrdinalIgnoreCase)
                .First())
            .OrderBy(item => item.Code, StringComparer.OrdinalIgnoreCase)
            .Select((item, index) => new { item.Code, Index = index })
            .ToDictionary(
                item => item.Code,
                item => item.Index,
                StringComparer.OrdinalIgnoreCase);

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

        string baseSourceCode = StudioAlbumComponentIdentity.BaseSourceCode(code);
        int sourceTieBreaker = Math.Clamp(
            sourceOrder.TryGetValue(baseSourceCode, out int sourceIndex)
                ? sourceIndex
                : sourceOrder.TryGetValue(code, out sourceIndex)
                    ? sourceIndex
                    : StableIdentityTieBreaker(baseSourceCode),
            0,
            TemplateSlotStride - 1);
        bool hasSourceSlice = StudioAlbumComponentIdentity.TryGetSourceSlice(
            code,
            out string sectionKey,
            out string sequenceKey);
        int templateSlotOffset = ResolveTemplateSlotOrder(sequenceKey) *
            TemplateSlotStride;
        if (hasSourceSlice &&
            IsGeneralPlanSectionIdentity(sectionKey))
        {
            return GeneralPlanBase + templateSlotOffset + sourceTieBreaker;
        }
        if (hasSourceSlice &&
            TryResolveBuildingSectionGroup(project, sectionKey, out ProjectBuildingGroup sliceGroup))
        {
            return BuildingOrder(BuildingRank(project, sliceGroup)) +
                BuildingSourceOffset +
                templateSlotOffset +
                sourceTieBreaker;
        }

        string effectiveSourceKey = ResolveEffectiveSourceKey(
            project,
            normalizedSourceKey,
            code);
        ProjectDesignSource? source = ResolveSource(
            project,
            effectiveSourceKey,
            code);
        ProjectCloudSourceReference? sharedSource = ResolveSharedSource(
            project,
            effectiveSourceKey,
            code);
        ProjectDesignSourcePurpose sharedPurpose =
            SharedSourcePurpose(sharedSource);
        if (sharedPurpose == ProjectDesignSourcePurpose.GeneralPlan)
            return GeneralPlanBase + templateSlotOffset + sourceTieBreaker;

        if (source is not null)
        {
            if (sharedPurpose != ProjectDesignSourcePurpose.Building &&
                ProjectDesignSourceClassification.IsGeneralPlan(source))
                return GeneralPlanBase + templateSlotOffset + sourceTieBreaker;

            string buildingGroupId = ResolveBuildingGroupId(project, source);
            ProjectBuildingGroup? buildingGroup = project.BuildingGroups.FirstOrDefault(group =>
                group.Id.Equals(buildingGroupId, StringComparison.OrdinalIgnoreCase));
            if (buildingGroup is not null)
            {
                return BuildingOrder(BuildingRank(project, buildingGroup)) +
                    BuildingSourceOffset +
                    templateSlotOffset +
                    sourceTieBreaker;
            }

            ProjectBuildingGroup? sharedBuilding =
                ResolveSharedBuildingGroup(
                    project,
                    effectiveSourceKey,
                    SharedSourceOwner(sharedSource));
            if (sharedBuilding is not null)
            {
                return BuildingOrder(BuildingRank(project, sharedBuilding)) +
                    BuildingSourceOffset +
                    templateSlotOffset +
                    sourceTieBreaker;
            }
        }
        else
        {
            if (IsSharedGeneralPlanSource(
                    project,
                    effectiveSourceKey,
                    code))
                return GeneralPlanBase + templateSlotOffset + sourceTieBreaker;

            ProjectBuildingGroup? sharedBuilding =
                ResolveSharedBuildingGroup(
                    project,
                    effectiveSourceKey,
                    SharedSourceOwner(sharedSource));
            if (sharedBuilding is not null)
            {
                return BuildingOrder(BuildingRank(project, sharedBuilding)) +
                    BuildingSourceOffset +
                    templateSlotOffset +
                    sourceTieBreaker;
            }
        }

        if (hasSourceSlice &&
            IsBuildingSectionIdentity(sectionKey))
        {
            return FallbackBuildingBase +
                templateSlotOffset +
                sourceTieBreaker;
        }

        if (code.StartsWith("source:", StringComparison.OrdinalIgnoreCase))
            return UnassignedSourceBase +
                templateSlotOffset +
                sourceTieBreaker;

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

    private static int ResolveTemplateSlotOrder(string sequenceKey)
    {
        if (string.IsNullOrWhiteSpace(sequenceKey))
            return 0;

        return sequenceKey.Trim().ToLowerInvariant() switch
        {
            "planning-proposal" => 4,
            "traffic-scheme" => 5,
            "landscaping" => 6,
            "solar-study" => 7,
            "master-plan" => 8,
            "floor-plans" => 9,
            "sections" => 10,
            "elevations" => 11,
            "visualizations" => 12,
            _ => 79,
        };
    }

    private static int StableIdentityTieBreaker(string identity)
    {
        byte[] hash = SHA256.HashData(
            Encoding.UTF8.GetBytes((identity ?? "").Trim().ToLowerInvariant()));
        return (int)(
            BitConverter.ToUInt32(hash, 0) %
            (uint)TemplateSlotStride);
    }

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

    private static string ResolveEffectiveSourceKey(
        ProjectWorkspace project,
        string sourceKey,
        string componentCode)
    {
        if (!string.IsNullOrWhiteSpace(sourceKey))
            return sourceKey.Trim();

        string baseSourceCode =
            StudioAlbumComponentIdentity.BaseSourceCode(componentCode);
        const string sourcePrefix = "source:";
        if (!baseSourceCode.StartsWith(
                sourcePrefix,
                StringComparison.OrdinalIgnoreCase))
        {
            return "";
        }

        if (StudioAlbumComponentIdentity.IsOwnedSourceCode(baseSourceCode))
            return baseSourceCode.Split(':', 3)[2].Trim();

        string payload = baseSourceCode[sourcePrefix.Length..].Trim();
        if (string.IsNullOrWhiteSpace(payload))
            return "";

        IEnumerable<string> localSourceKeys = project.Sources.SelectMany(source =>
            new[]
            {
                source.Id,
                ProjectCloudSyncMetadata.CloudSourceKey(source),
            });
        IEnumerable<string> sharedSourceKeys =
            (project.Cloud.SharedSources ?? [])
            .Select(source => source.SourceKey);
        string? matchedSourceKey = localSourceKeys
            .Concat(sharedSourceKeys)
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .Select(candidate => candidate.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(candidate => candidate.Length)
            .FirstOrDefault(candidate =>
                payload.Equals(candidate, StringComparison.OrdinalIgnoreCase) ||
                payload.EndsWith(
                    ":" + candidate,
                    StringComparison.OrdinalIgnoreCase));
        return matchedSourceKey ?? payload;
    }

    private static ProjectDesignSource? ResolveSource(
        ProjectWorkspace project,
        string sourceKey,
        string componentCode)
    {
        ProjectDesignSource[] matches = project.Sources
            .Where(source =>
                source.Id.Equals(sourceKey, StringComparison.OrdinalIgnoreCase) ||
                ProjectCloudSyncMetadata.CloudSourceKey(source).Equals(
                    sourceKey,
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();
        string baseCode = StudioAlbumComponentIdentity.BaseSourceCode(componentCode);
        ProjectDesignSource? exact = matches.FirstOrDefault(source =>
        {
            string owner = ProjectCloudSyncMetadata.CloudOwnerEmail(source);
            return !string.IsNullOrWhiteSpace(owner) &&
                StudioAlbumComponentIdentity.SourceCode(
                    owner,
                    ProjectCloudSyncMetadata.CloudSourceKey(source))
                .Equals(baseCode, StringComparison.OrdinalIgnoreCase);
        });
        return exact ?? (matches.Length == 1 ? matches[0] : null);
    }

    private static bool IsSharedGeneralPlanSource(
        ProjectWorkspace project,
        string sourceKey,
        string componentCode)
    {
        ProjectCloudSourceReference[] sharedSources =
            ActiveSharedSources(project);
        ProjectCloudSourceReference? source = ResolveSharedSource(
            project,
            sourceKey,
            componentCode);
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
            ResolveSharedBuildingGroup(
                project,
                sourceKey,
                SharedSourceOwner(source)) is not null)
        {
            return false;
        }

        int unassignedAutoCadCount = sharedSources.Count(item =>
            item.SourceApplication.Contains(
                "autocad",
                StringComparison.OrdinalIgnoreCase) &&
            ResolveSharedBuildingGroup(
                project,
                item.SourceKey,
                SharedSourceOwner(item)) is null);
        return unassignedAutoCadCount == 1;
    }

    private static ProjectCloudSourceReference[] ActiveSharedSources(
        ProjectWorkspace project) =>
        (project.Cloud.SharedSources ?? [])
        .Where(source =>
            !string.IsNullOrWhiteSpace(source.SourceKey) &&
            (string.IsNullOrWhiteSpace(source.Status) ||
             source.Status.Equals("Registered", StringComparison.OrdinalIgnoreCase)))
        .ToArray();

    private static ProjectCloudSourceReference? ResolveSharedSource(
        ProjectWorkspace project,
        string sourceKey,
        string componentCode)
    {
        ProjectCloudSourceReference[] matches = ActiveSharedSources(project)
            .Where(source =>
                source.SourceKey.Equals(
                    sourceKey,
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();
        string baseCode = StudioAlbumComponentIdentity.BaseSourceCode(componentCode);
        ProjectCloudSourceReference? exact = matches.FirstOrDefault(source =>
        {
            string owner = SharedSourceOwner(source);
            return !string.IsNullOrWhiteSpace(owner) &&
                StudioAlbumComponentIdentity.SourceCode(owner, source.SourceKey)
                .Equals(baseCode, StringComparison.OrdinalIgnoreCase);
        });
        return exact ?? (matches.Length == 1 ? matches[0] : null);
    }

    private static string SharedSourceOwner(
        ProjectCloudSourceReference? source) =>
        !string.IsNullOrWhiteSpace(source?.RegisteredBy)
            ? source.RegisteredBy.Trim().ToLowerInvariant()
            : (source?.OwnerEmail ?? "").Trim().ToLowerInvariant();

    private static bool IsGeneralPlanApplication(string? application)
    {
        string value = application?.Trim() ?? "";
        return value.Contains("citygen", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("general plan", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("master plan", StringComparison.OrdinalIgnoreCase);
    }

    private static ProjectDesignSourcePurpose SharedSourcePurpose(
        ProjectCloudSourceReference? source) =>
        Enum.TryParse(
            source?.SourcePurpose?.Trim(),
            ignoreCase: true,
            out ProjectDesignSourcePurpose purpose)
            ? purpose
            : ProjectDesignSourcePurpose.Unspecified;

    private static ProjectBuildingGroup? ResolveSharedBuildingGroup(
        ProjectWorkspace project,
        string sourceKey,
        string sourceOwnerEmail)
    {
        ProjectCloudBuildingSheetAssignmentReference[] sourceAssignments =
            (project.Cloud.SharedBuildingSheetAssignments ?? [])
            .Where(assignment =>
                assignment.SourceKey.Equals(
                    sourceKey,
                    StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(assignment.BuildingGroupId))
            .ToArray();
        ProjectCloudBuildingSheetAssignmentReference[] exactAssignments =
            string.IsNullOrWhiteSpace(sourceOwnerEmail)
                ? []
                : sourceAssignments
                    .Where(assignment =>
                        assignment.SourceOwnerEmail.Equals(
                            sourceOwnerEmail,
                            StringComparison.OrdinalIgnoreCase))
                    .ToArray();
        IEnumerable<ProjectCloudBuildingSheetAssignmentReference> resolvedAssignments =
            exactAssignments.Length > 0
                ? exactAssignments
                : sourceAssignments.Where(assignment =>
                    string.IsNullOrWhiteSpace(assignment.SourceOwnerEmail));
        HashSet<string> assignedGroupIds = resolvedAssignments
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
        return TryResolveBuildingSectionGroup(project, identity, out group);
    }

    private static bool TryResolveBuildingSectionGroup(
        ProjectWorkspace project,
        string identity,
        out ProjectBuildingGroup group)
    {
        bool resolved = StudioAlbumComponentIdentity.TryResolveBuildingGroup(
            project,
            identity,
            out group);
        return resolved;
    }

    private static bool IsBuildingSectionIdentity(string sectionKey) =>
        sectionKey.StartsWith("studio-building:", StringComparison.OrdinalIgnoreCase) ||
        sectionKey.StartsWith("package-building:", StringComparison.OrdinalIgnoreCase);

    private static bool IsGeneralPlanSectionIdentity(string sectionKey)
    {
        string value = (sectionKey ?? "").Trim();
        if (value.StartsWith("fixed:", StringComparison.OrdinalIgnoreCase))
            value = value["fixed:".Length..].Trim();
        return value.Equals(
                   "Ерөнхий төлөвлөгөө",
                   StringComparison.OrdinalIgnoreCase) ||
               value.Equals(
                   "general-plan",
                   StringComparison.OrdinalIgnoreCase) ||
               value.Equals(
                   "general plan",
                   StringComparison.OrdinalIgnoreCase);
    }
}
