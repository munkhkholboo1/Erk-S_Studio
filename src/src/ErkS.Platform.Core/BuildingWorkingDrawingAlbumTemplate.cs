namespace ErkS.Platform.Core;

/// <summary>
/// Studio-owned front matter for building working drawings. Unlike the concept
/// album it never creates organization certificates, design licences or the
/// approved planning-task pages.
/// </summary>
public static class BuildingWorkingDrawingAlbumTemplate
{
    public const string TemplateId = "building-working-drawings-v1";
    public const string DefaultTitle = "Барилгын ажлын зургийн альбум";

    public static AlbumDefinition CreateDefinition(string title)
    {
        var definition = new AlbumDefinition
        {
            Title = DefaultTitle,
            TemplateId = TemplateId,
            IncludeCover = false,
            IncludeTableOfContents = false,
            Sections = [new AlbumSection { Title = "Ажлын зураг" }],
            Composition =
            [
                Generated("cover", 0, "00", "НҮҮР ХУУДАС", AlbumGeneratedPageKind.Cover),
                Generated("drawing-list-and-notes", 1, "01", "ЗУРГИЙН ЖАГСААЛТ, ТАЙЛБАР БИЧИГ", AlbumGeneratedPageKind.None),
                new AlbumCompositionItem
                {
                    Id = "working-drawing-sheets",
                    Order = 2,
                    Number = "02+",
                    Title = "АЖЛЫН ЗУРГИЙН ХУУДАСНУУД",
                    SectionTitle = "Ажлын зураг",
                    Kind = AlbumCompositionKind.SourceSlot,
                    Required = false,
                    AllowMultiple = true,
                    MatchContentKinds = ["Blueprint", "WorkingDrawing", "working-drawing-sheets"],
                    MatchNameTerms = [],
                },
            ],
        };
        return definition;
    }

    public static bool Supports(string? projectType, string? stageCode) =>
        string.Equals(projectType, ProjectTypes.BuildingDesignProjectType.TypeId, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(stageCode, "working-drawings", StringComparison.OrdinalIgnoreCase);

    public static AlbumCompositionItem? FindSourceSlot(AlbumDefinition definition) =>
        definition.Composition.FirstOrDefault(item =>
            item.Id.Equals("working-drawing-sheets", StringComparison.OrdinalIgnoreCase));

    private static AlbumCompositionItem Generated(
        string id,
        int order,
        string number,
        string title,
        AlbumGeneratedPageKind kind) => new()
        {
            Id = id,
            Order = order,
            Number = number,
            Title = title,
            SectionTitle = "Ажлын зураг",
            Kind = AlbumCompositionKind.Generated,
            GeneratedPageKind = kind,
            Required = true,
            AllowMultiple = false,
        };
}
