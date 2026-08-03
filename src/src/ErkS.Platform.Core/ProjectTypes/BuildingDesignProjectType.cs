namespace ErkS.Platform.Core.ProjectTypes;

public sealed class BuildingDesignProjectType : IStudioProjectTypeDefinition
{
    public const string TypeId = "building-design";
    public string Id => TypeId;
    public string Label => "Барилга байгууламжийн зураг төсөл";
    public IReadOnlyList<StudioProjectStageDefinition> Stages { get; } =
    [
        new("model-design", "Загвар зураг"),
        new("sketch-design", "Эх загвар зураг / эскиз"),
        new("technical-design", "Техникийн зураг"),
        new("working-drawings", "Ажлын зураг"),
    ];
}
