using ErkS.Platform.Contracts;

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

    /// <summary>Studio's own front matter and the sheet standing in its 01.</summary>
    public const string GeneralPartSectionTitle = "Ерөнхий хэсэг";

    /// <summary>
    /// The general plan (ЕТ) - its own part, never a building's (owner,
    /// 2026-10-20).
    /// </summary>
    public const string GeneralPlanSectionTitle = "Ерөнхий төлөвлөгөө";

    /// <summary>The sets of the building: БА, ББ and the rest.</summary>
    public const string SheetsSectionTitle = "Ажлын зураг";

    private static readonly string[] CanonicalSections =
        [GeneralPartSectionTitle, GeneralPlanSectionTitle, SheetsSectionTitle];

    public static AlbumDefinition CreateDefinition(string title)
    {
        var definition = new AlbumDefinition
        {
            Title = DefaultTitle,
            TemplateId = TemplateId,
            IncludeCover = false,
            IncludeTableOfContents = false,
            Sections = CanonicalSections.Select(title => new AlbumSection { Title = title }).ToList(),
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
                    SectionTitle = SheetsSectionTitle,
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

    /// <summary>
    /// Gives an album made before the general-plan part its three parts, in
    /// order, without replacing a section it already has (its pages keep their
    /// ids), and files Studio's generated pages under the general part.
    /// </summary>
    public static bool EnsureSections(AlbumDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        bool changed = false;
        var ordered = new List<AlbumSection>();
        foreach (string title in CanonicalSections)
        {
            AlbumSection? existing = definition.Sections.FirstOrDefault(section =>
                section.Title.Equals(title, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                existing = new AlbumSection { Title = title };
                changed = true;
            }
            ordered.Add(existing);
        }
        ordered.AddRange(definition.Sections.Where(section => !ordered.Contains(section)));
        if (!ordered.SequenceEqual(definition.Sections))
        {
            definition.Sections.Clear();
            definition.Sections.AddRange(ordered);
            changed = true;
        }

        foreach (AlbumCompositionItem item in definition.Composition.Where(item =>
                     item.Kind == AlbumCompositionKind.Generated &&
                     !item.SectionTitle.Equals(GeneralPartSectionTitle, StringComparison.OrdinalIgnoreCase)))
        {
            item.SectionTitle = GeneralPartSectionTitle;
            changed = true;
        }
        return changed;
    }

    /// <summary>The part a delivered sheet stands in: a general-plan sheet in its own.</summary>
    public static Guid? ResolveSectionId(
        AlbumDefinition definition,
        SheetPackageEntry entry,
        AlbumCompositionItem? slot)
    {
        ArgumentNullException.ThrowIfNull(definition);
        string title = ProjectDesignSourceClassification.IsGeneralPlanSheet(entry)
            ? GeneralPlanSectionTitle
            : slot?.SectionTitle ?? SheetsSectionTitle;
        return definition.Sections
            .FirstOrDefault(section => section.Title.Equals(title, StringComparison.OrdinalIgnoreCase))
            ?.Id;
    }

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
            SectionTitle = GeneralPartSectionTitle,
            Kind = AlbumCompositionKind.Generated,
            GeneratedPageKind = kind,
            Required = true,
            AllowMultiple = false,
        };
}
