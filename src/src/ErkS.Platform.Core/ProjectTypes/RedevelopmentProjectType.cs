namespace ErkS.Platform.Core.ProjectTypes;

public sealed class RedevelopmentProjectType : IStudioProjectTypeDefinition
{
    public const string TypeId = "redevelopment";
    public string Id => TypeId;
    public string Label => "Хот, суурин газрыг дахин хөгжүүлэх төсөл";
    public IReadOnlyList<StudioProjectStageDefinition> Stages { get; } =
    [
        new("base-study", "Суурь судалгаа"),
        new("proposal", "Дахин хөгжүүлэх санал"),
        new("approved-plan", "Батлагдсан төлөвлөгөө"),
        new("implementation", "Хэрэгжилтийн үе шат"),
    ];
}
