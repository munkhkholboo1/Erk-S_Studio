namespace ErkS.Platform.Core.ProjectTypes;

public sealed class LandscapeProjectType : IStudioProjectTypeDefinition
{
    public const string TypeId = "landscape";
    public string Id => TypeId;
    public string Label => "Гадна тохижилт, ландшафт";
    public IReadOnlyList<StudioProjectStageDefinition> Stages { get; } =
    [
        new("concept", "Үзэл баримтлал, загвар шийдэл"),
        new("technical-design", "Техникийн зураг"),
        new("working-drawings", "Ажлын зураг"),
    ];
}
