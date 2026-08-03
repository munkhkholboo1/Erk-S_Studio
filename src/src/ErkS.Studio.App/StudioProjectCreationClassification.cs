using System.IO;
using ErkS.Platform.Core.ProjectTypes;

namespace ErkS.Studio;

internal static class StudioProjectCreationClassification
{
    public static string ResolveTemplateId(string? projectType, string? stageType) =>
        string.Equals(projectType?.Trim(), BuildingDesignProjectType.TypeId, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(stageType?.Trim(), "model-design", StringComparison.OrdinalIgnoreCase)
            ? StudioCloudTemplateIds.BuildingArchitectureConcept
            : "";

    public static string ResolveStageName(string? projectType, string? stageType)
    {
        IStudioProjectTypeDefinition type = StudioProjectTypeRegistry.Resolve(projectType ?? "");
        return type.Stages.FirstOrDefault(stage =>
                   stage.Id.Equals(stageType?.Trim(), StringComparison.OrdinalIgnoreCase))?.Label
               ?? stageType?.Trim()
               ?? "";
    }

    public static string ResolveCloudStageType(StudioCloudProjectDetail cloud) =>
        FirstNonEmpty(
            cloud.ProjectInformation?.StageType,
            cloud.Project?.StageType,
            cloud.Project?.CurrentStage);

    public static string ResolveCloudProjectType(StudioCloudProjectDetail cloud) =>
        ResolveCloudProjectType(
            FirstNonEmpty(cloud.ProjectInformation?.ProjectDomain, cloud.Project?.ProjectDomain),
            ResolveCloudStageType(cloud));

    public static string ResolveCloudProjectType(string? projectType, string? stageType)
    {
        if (!string.IsNullOrWhiteSpace(projectType))
            return StudioProjectTypeRegistry.Resolve(projectType).Id;

        string normalizedStage = stageType?.Trim() ?? "";
        IStudioProjectTypeDefinition[] matches = StudioProjectTypeRegistry.All
            .Where(type => type.Stages.Any(stage =>
                stage.Id.Equals(normalizedStage, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        if (matches.Length == 1)
            return matches[0].Id;

        // Legacy Cloud responses did not expose ProjectDomain. The only enabled
        // legacy template was building design, so ambiguous stages belong there.
        return BuildingDesignProjectType.TypeId;
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";
}

internal static class StudioProjectCloudIsolation
{
    public static void ValidateEnvelope(StudioCloudProjectDetail cloud)
    {
        ArgumentNullException.ThrowIfNull(cloud);
        string projectId = cloud.Project?.ProjectId?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(projectId))
            throw new InvalidDataException("Cloud project ID is empty.");

        string informationProjectId = cloud.ProjectInformation?.ProjectId?.Trim() ?? "";
        if (!string.IsNullOrWhiteSpace(informationProjectId) &&
            !informationProjectId.Equals(projectId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Cloud project payload mixed project data: information '{informationProjectId}' does not belong to project '{projectId}'.");
        }
    }
}
