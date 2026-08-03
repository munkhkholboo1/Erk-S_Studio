namespace ErkS.Platform.Core.ProjectTypes.UrbanPlanning;

public static class UrbanPlanningDrawingMarks
{
    public const string GeneralPlan = "ЕТ";
    public const string EngineeringInfrastructure = "ИДБ";
}

public sealed record UrbanPlanningDrawingSlot(
    string Id,
    int Order,
    string Mark,
    int MarkOrder,
    string Title,
    bool UsesNomenclatureGrid = false,
    bool AllowMultiplePages = false);

public interface IUrbanPlanningDrawingSequence
{
    string ProjectStageType { get; }
    IReadOnlyList<UrbanPlanningDrawingSlot> Drawings { get; }
}

public static class UrbanPlanningDrawingSequenceRegistry
{
    private static readonly IReadOnlyDictionary<string, IUrbanPlanningDrawingSequence> Sequences =
        new IUrbanPlanningDrawingSequence[]
        {
            new MasterPlanDrawingSequence(),
            new PartialMasterPlanDrawingSequence(),
        }.ToDictionary(item => item.ProjectStageType, StringComparer.OrdinalIgnoreCase);

    public static IUrbanPlanningDrawingSequence Resolve(string stageType) =>
        Sequences.TryGetValue((stageType ?? "").Trim(), out IUrbanPlanningDrawingSequence? sequence)
            ? sequence
            : throw new InvalidOperationException($"'{stageType}' үе шатанд хот төлөвлөлтийн зургийн дараалал бүртгэгдээгүй байна.");
}

internal static class UrbanPlanningDrawingSequenceFactory
{
    public static IReadOnlyList<UrbanPlanningDrawingSlot> CreateInitialSequence() =>
    [
        Fixed("cover", 1, UrbanPlanningDrawingMarks.GeneralPlan, 1, "Нүүр хуудас"),
        Fixed("drawing-list-and-notes", 2, UrbanPlanningDrawingMarks.GeneralPlan, 2, "Зургийн жагсаалт, тайлбар бичиг"),
        Fixed("existing-condition", 3, UrbanPlanningDrawingMarks.GeneralPlan, 3, "Одоогийн байдал"),
        Tiled("general-plan-zoning", 4, UrbanPlanningDrawingMarks.GeneralPlan, 4, "Ерөнхий төлөвлөгөөний бүсчлэл"),
        Tiled("development-projection", 5, UrbanPlanningDrawingMarks.GeneralPlan, 5, "Барилгажилтын төсөөлөл"),
        Tiled("green-infrastructure", 6, UrbanPlanningDrawingMarks.GeneralPlan, 6, "Ногоон байгууламж"),
        Tiled("waste-management", 7, UrbanPlanningDrawingMarks.GeneralPlan, 7, "Хог хаягдлын менежмент"),
        Tiled("disaster-management", 8, UrbanPlanningDrawingMarks.GeneralPlan, 8, "Гамшгийн менежмент"),
        Tiled("grading", 9, UrbanPlanningDrawingMarks.GeneralPlan, 9, "Өндөржилт"),
        Tiled("red-lines", 10, UrbanPlanningDrawingMarks.GeneralPlan, 10, "Улаан шугам"),
        Tiled("heating-supply", 11, UrbanPlanningDrawingMarks.EngineeringInfrastructure, 1, "Дулаан хангамж"),
        Tiled("power-supply", 12, UrbanPlanningDrawingMarks.EngineeringInfrastructure, 2, "Цахилгаан хангамж"),
        Tiled("water-and-sewer", 13, UrbanPlanningDrawingMarks.EngineeringInfrastructure, 3, "Ус хангамж, ариутгах татуурга"),
        Tiled("communications-and-signaling", 14, UrbanPlanningDrawingMarks.EngineeringInfrastructure, 4, "Холбоо дохиолол"),
        Tiled("engineering-preparation", 15, UrbanPlanningDrawingMarks.EngineeringInfrastructure, 5, "Инженерийн бэлтгэл арга хэмжээ"),
    ];

    private static UrbanPlanningDrawingSlot Fixed(string id, int order, string mark, int markOrder, string title) =>
        new(id, order, mark, markOrder, title);

    private static UrbanPlanningDrawingSlot Tiled(string id, int order, string mark, int markOrder, string title) =>
        new(id, order, mark, markOrder, title, UsesNomenclatureGrid: true, AllowMultiplePages: true);
}
