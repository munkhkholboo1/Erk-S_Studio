namespace ErkS.Platform.Core.ProjectTypes;

public sealed class EngineeringNetworkProjectType : IStudioProjectTypeDefinition
{
    public const string TypeId = "engineering-network";
    public string Id => TypeId;
    public string Label => "Инженерийн шугам сүлжээ";
    public IReadOnlyList<StudioProjectStageDefinition> Stages { get; } =
    [
        new("feasibility", "Техник, эдийн засгийн үндэслэл"),
        new("technical-design", "Техникийн зураг"),
        new("working-drawings", "Ажлын зураг"),
    ];
}
