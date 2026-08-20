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
    bool AllowMultiplePages = false,
    bool Required = true);

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
    public static IReadOnlyList<UrbanPlanningDrawingSlot> CreateMasterPlanSequence() =>
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

    /// <summary>
    /// Хэсэгчилсэн ерөнхий төлөвлөгөөний зураглалыг БД 30-103-21-ийн 8.10-д
    /// заасан шийдвэрлэх асуудлуудтай нэг бүрчлэн уялдуулсан дараалал.
    /// </summary>
    public static IReadOnlyList<UrbanPlanningDrawingSlot> CreatePartialPlanSequence() =>
    [
        Fixed("cover", 1, UrbanPlanningDrawingMarks.GeneralPlan, 1, "Нүүр хуудас"),
        Fixed("drawing-list-and-notes", 2, UrbanPlanningDrawingMarks.GeneralPlan, 2, "Зургийн жагсаалт, тайлбар бичиг"),
        Fixed("development-context", 3, UrbanPlanningDrawingMarks.GeneralPlan, 3, "Хөгжлийн чиг хандлага, уялдаа"),
        Tiled("existing-condition", 4, UrbanPlanningDrawingMarks.GeneralPlan, 4, "Өнөөгийн байдлын судалгаа, хот байгуулалтын иж бүрэн үнэлгээ"),
        Fixed("demographic-economic-analysis", 5, UrbanPlanningDrawingMarks.GeneralPlan, 5, "Хүн ам, орон сууц, нийгмийн үйлчилгээ, эдийн засгийн тооцоо"),
        Tiled("street-road-transport", 6, UrbanPlanningDrawingMarks.GeneralPlan, 6, "Гудамж, зам, тээврийн төлөвлөлт"),
        Tiled("pedestrian-movement", 7, UrbanPlanningDrawingMarks.GeneralPlan, 7, "Явган хүний замын хөдөлгөөний схем"),
        Tiled("red-lines", 8, UrbanPlanningDrawingMarks.GeneralPlan, 8, "Автозамын тэнхлэгийн улаан шугам, зай хэмжээ"),
        Tiled("general-plan-zoning", 9, UrbanPlanningDrawingMarks.GeneralPlan, 9, "Төлөвлөлтийн үндсэн шийдэл"),
        Multiple("development-projection", 10, UrbanPlanningDrawingMarks.GeneralPlan, 10, "Барилгажилтын төрх, өндөр намын харьцаа, харагдах байдал (3D)"),
        Tiled("social-service-accessibility", 11, UrbanPlanningDrawingMarks.GeneralPlan, 11, "Олон нийт, нийгмийн үйлчилгээний хүртээмж, үйлчлэх хүрээ"),
        Tiled("green-infrastructure", 12, UrbanPlanningDrawingMarks.GeneralPlan, 12, "Цэцэрлэгжүүлэлт, ногоон байгууламжийн систем"),
        Tiled("waste-management", 13, UrbanPlanningDrawingMarks.GeneralPlan, 13, "Хог хаягдлын менежмент", required: false),
        Tiled("grading", 14, UrbanPlanningDrawingMarks.GeneralPlan, 14, "Зам, талбайн өндөржилт, инженерийн бэлтгэл ажил"),
        Tiled("disaster-management", 15, UrbanPlanningDrawingMarks.GeneralPlan, 15, "Гамшгийн эрсдэлийн менежмент", required: false),
        Tiled("first-phase-land-management", 16, UrbanPlanningDrawingMarks.GeneralPlan, 16, "Эхний ээлжийн барилгажилт, газар зохион байгуулалтын төлөвлөлт"),
        Fixed("technical-economic-indicators", 17, UrbanPlanningDrawingMarks.GeneralPlan, 17, "Техник, эдийн засгийн нэгдсэн үзүүлэлт"),
        Tiled("integrated-engineering-networks", 18, UrbanPlanningDrawingMarks.EngineeringInfrastructure, 1, "Инженерийн шугам сүлжээний нэгдсэн зураг"),
        Tiled("water-and-sewer", 19, UrbanPlanningDrawingMarks.EngineeringInfrastructure, 2, "Ус хангамж, ариутгах татуурга"),
        Tiled("heating-supply", 20, UrbanPlanningDrawingMarks.EngineeringInfrastructure, 3, "Дулаан хангамж"),
        Tiled("power-supply", 21, UrbanPlanningDrawingMarks.EngineeringInfrastructure, 4, "Эрчим хүчний хангамж, сэргээгдэх эрчим хүч"),
        Tiled("communications-and-signaling", 22, UrbanPlanningDrawingMarks.EngineeringInfrastructure, 5, "Холбоо, мэдээллийн сүлжээ"),
    ];

    private static UrbanPlanningDrawingSlot Fixed(string id, int order, string mark, int markOrder, string title) =>
        new(id, order, mark, markOrder, title);

    private static UrbanPlanningDrawingSlot Multiple(
        string id,
        int order,
        string mark,
        int markOrder,
        string title,
        bool required = true) =>
        new(id, order, mark, markOrder, title, AllowMultiplePages: true, Required: required);

    private static UrbanPlanningDrawingSlot Tiled(
        string id,
        int order,
        string mark,
        int markOrder,
        string title,
        bool required = true) =>
        new(
            id,
            order,
            mark,
            markOrder,
            title,
            UsesNomenclatureGrid: true,
            AllowMultiplePages: true,
            Required: required);
}
