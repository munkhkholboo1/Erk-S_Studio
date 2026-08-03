namespace ErkS.Platform.Core.ProjectTypes.UrbanPlanning;

/// <summary>ХЕТ төслийн эхний зургийн бүрдлийг альбомын бодит бүтэц болгоно.</summary>
public static class UrbanPlanningAlbumTemplate
{
    public const string MasterPlanTemplateId = "urban-planning-master-plan-v1";
    public const string PartialPlanTemplateId = "urban-planning-partial-plan-v1";
    public const string Abbreviation = "ХЕТ";

    public static AlbumDefinition CreateDefinition(string stageType)
    {
        IUrbanPlanningDrawingSequence sequence = UrbanPlanningDrawingSequenceRegistry.Resolve(stageType);
        string fullName = stageType.Equals(MasterPlanDrawingSequence.StageType, StringComparison.OrdinalIgnoreCase)
            ? "Хөгжлийн ерөнхий төлөвлөгөө"
            : "Хэсэгчилсэн ерөнхий төлөвлөгөө";

        var definition = new AlbumDefinition
        {
            Title = $"{fullName} ({Abbreviation})",
            TemplateId = stageType.Equals(MasterPlanDrawingSequence.StageType, StringComparison.OrdinalIgnoreCase)
                ? MasterPlanTemplateId
                : PartialPlanTemplateId,
            IncludeCover = false,
            IncludeTableOfContents = false,
            Sections =
            [
                new AlbumSection { Title = "Ерөнхий төлөвлөгөө / ЕТ" },
                new AlbumSection { Title = "Инженерийн дэд бүтэц / ИДБ" },
            ],
        };

        definition.Composition = sequence.Drawings.Select(slot => new AlbumCompositionItem
        {
            Id = slot.Id,
            Order = slot.Order,
            Number = $"{slot.Mark}-{slot.MarkOrder:00}",
            Title = slot.Title,
            SectionTitle = slot.Mark == UrbanPlanningDrawingMarks.GeneralPlan
                ? "Ерөнхий төлөвлөгөө / ЕТ"
                : "Инженерийн дэд бүтэц / ИДБ",
            Kind = slot.Id is "cover" or "drawing-list-and-notes"
                ? AlbumCompositionKind.Generated
                : AlbumCompositionKind.SourceSlot,
            GeneratedPageKind = slot.Id == "cover" ? AlbumGeneratedPageKind.Cover : AlbumGeneratedPageKind.None,
            Required = true,
            AllowMultiple = slot.AllowMultiplePages,
            MatchContentKinds = [slot.Id, slot.Mark],
            MatchNameTerms = [slot.Title],
        }).ToList();

        return definition;
    }

    public static bool Supports(string projectType, string stageType) =>
        projectType.Equals(global::ErkS.Platform.Core.ProjectTypes.UrbanPlanningProjectType.TypeId, StringComparison.OrdinalIgnoreCase) &&
        (stageType.Equals(MasterPlanDrawingSequence.StageType, StringComparison.OrdinalIgnoreCase) ||
         stageType.Equals(PartialMasterPlanDrawingSequence.StageType, StringComparison.OrdinalIgnoreCase));
}
