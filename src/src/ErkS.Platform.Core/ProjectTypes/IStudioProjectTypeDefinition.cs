namespace ErkS.Platform.Core.ProjectTypes;

public sealed record StudioProjectStageDefinition(string Id, string Label, bool EnabledForNewProject = true);

public interface IStudioProjectTypeDefinition
{
    string Id { get; }
    string Label { get; }
    IReadOnlyList<StudioProjectStageDefinition> Stages { get; }
}

public static class StudioProjectTypeRegistry
{
    public static IReadOnlyList<IStudioProjectTypeDefinition> All { get; } =
    [
        new BuildingDesignProjectType(),
        new UrbanPlanningProjectType(),
        new EngineeringNetworkProjectType(),
        new RoadBridgeProjectType(),
        new LandscapeProjectType(),
        new RedevelopmentProjectType(),
    ];

    public static IStudioProjectTypeDefinition Resolve(string? id) =>
        All.FirstOrDefault(item => item.Id.Equals((id ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
        ?? All[0];
}
