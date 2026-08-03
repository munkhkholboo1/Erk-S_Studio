namespace ErkS.Platform.Core.ProjectTypes;

public sealed class RoadBridgeProjectType : IStudioProjectTypeDefinition
{
    public const string TypeId = "road-bridge";
    public string Id => TypeId;
    public string Label => "Авто зам, гүүр, замын байгууламж";
    public IReadOnlyList<StudioProjectStageDefinition> Stages { get; } =
    [
        new("feasibility", "Техник, эдийн засгийн үндэслэл"),
        new("technical-design", "Техникийн зураг төсөл"),
        new("detailed-engineering", "Инженерийн нарийвчилсан зураг төсөл"),
    ];
}
