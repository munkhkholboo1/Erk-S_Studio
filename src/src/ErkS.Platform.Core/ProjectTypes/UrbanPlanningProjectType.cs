namespace ErkS.Platform.Core.ProjectTypes;

public sealed class UrbanPlanningProjectType : IStudioProjectTypeDefinition
{
    public const string TypeId = "urban-planning";
    public string Id => TypeId;
    public string Label => "Хот байгуулалтын баримт бичиг";
    public IReadOnlyList<StudioProjectStageDefinition> Stages { get; } =
    [
        new("base-study", "Суурь судалгаа"),
        new("master-plan", "Хөгжлийн ерөнхий төлөвлөгөө"),
        new("partial-plan", "Хэсэгчилсэн ерөнхий төлөвлөгөө"),
        new("development-project", "Барилгажилтын төсөл"),
    ];
}
