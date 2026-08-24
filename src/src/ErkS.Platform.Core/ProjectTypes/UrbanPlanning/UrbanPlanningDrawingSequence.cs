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
    bool Required = true,
    string Scale = "",
    string Section = "",
    AlbumGeneratedPageKind GeneratedPageKind = AlbumGeneratedPageKind.None)
{
    /// <summary>
    /// A page the album makes for itself rather than one a drawing arrives
    /// into. The two existing sequences decide this from the slot id; a third
    /// with more generated pages says it outright.
    /// </summary>
    public bool IsGenerated =>
        GeneratedPageKind != AlbumGeneratedPageKind.None ||
        Id is "cover" or "drawing-list-and-notes";

    /// <summary>
    /// The drawing number printed on the sheet, or empty for a page the
    /// standard does not number - a cover, a drawing list, a copy of the
    /// planning task.
    /// </summary>
    public string DrawingNumber =>
        string.IsNullOrWhiteSpace(Mark) ? "" : $"{Mark}-{MarkOrder:00}";
}

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
            new DevelopmentProjectDrawingSequence(),
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

    /// <summary>
    /// Барилгажилтын төслийн дараалал. Эх сурвалж:
    /// docs/DEVELOPMENT-PROJECT-ALBUM-CONTRACT.md.
    ///
    /// The opening five entries are Studio's own. The remaining thirty are the
    /// slots AutoCAD fills, numbered ЕТ-03 upward because the location scheme
    /// and the site overview take ЕТ-01 and ЕТ-02.
    /// </summary>
    public static IReadOnlyList<UrbanPlanningDrawingSlot> CreateDevelopmentProjectSequence() =>
    [
        Generated("cover", 1, "Нүүр хуудас", AlbumGeneratedPageKind.Cover),
        Generated("drawing-list-and-notes", 2, "Тайлбар бичиг, зургийн жагсаалт"),
        Generated("design-organization", 3, "Байгууллагын гэрчилгээ, тусгай зөвшөөрөл", AlbumGeneratedPageKind.DesignOrganization),
        Generated("planning-task", 4, "Архитектур төлөвлөлтийн даалгавар", AlbumGeneratedPageKind.PlanningTask),
        Generated("location-scheme", 5, "Байршлын схем", AlbumGeneratedPageKind.SiteContext, "ЕТ", 1, "М1:200000"),
        Generated("site-overview", 6, "Орчны тойм", AlbumGeneratedPageKind.SiteContext, "ЕТ", 2, "М1:20000"),

        // Одоогийн байдлын судалгаа.
        Survey("topographic-base", 7, "ЕТ", 3, "Байр зүйн дэвсгэр зураг", "М1:1500"),
        Survey("terrain-elevation", 8, "ЕТ", 4, "Газрын гадаргын өндөржилт", "М1:1500"),
        Survey("terrain-slope", 9, "ЕТ", 5, "Газрын гадаргын налуужилт", "М1:1500"),
        Survey("approved-planning-survey", 10, "ЕТ", 6, "Батлагдсан төлөвлөлтийн судалгаа", ""),
        Survey("environmental-condition", 11, "ЕТ", 7, "Байгаль орчны төлөв байдал", "М1:6000"),
        Survey("road-network-survey", 12, "ЕТ", 8, "Авто замын сүлжээ", "М1:10000"),
        Survey("land-use-survey", 13, "ЕТ", 9, "Газар ашиглалтын судалгаа", "М1:16000"),
        Survey("existing-engineering-preparation", 14, "ИДБ", 1, "Инженерийн бэлтгэл арга хэмжээ", "М1:1500"),
        Survey("existing-heating-supply", 15, "ИДБ", 2, "Дулаан хангамж", "М1:1500"),
        Survey("existing-water-and-sewer", 16, "ИДБ", 3, "Ус хангамж, ариутгах татуурга", "М1:1500"),
        Survey("existing-power-supply", 17, "ИДБ", 4, "Цахилгаан хангамж", "М1:1500"),
        Survey("existing-communications", 18, "ИДБ", 5, "Мэдээлэл холбооны сүлжээ", "М1:1500"),

        // Хувилбарууд нь төлөвлөлтийн ажил боловч жишиг альбом тэдгээрийг
        // судалгааны хэсэгт тавьсан. Дараагийн хүн үүнийг алдаа гэж бодож
        // зөөхөөс сэргийлж энд тэмдэглэв.
        Survey("planning-option-1", 19, "ЕТ", 10, "Төлөвлөлтийн хувилбар-1", "М1:1500"),
        Survey("site-layout-option-1", 20, "ЕТ", 11, "Талбайн зохион байгуулалт", "М1:1500"),
        Survey("planning-option-2", 21, "ЕТ", 12, "Төлөвлөлтийн хувилбар-2", "М1:1500"),
        Survey("site-layout-option-2", 22, "ЕТ", 13, "Талбайн зохион байгуулалт", "М1:1500"),

        // Төлөвлөлтийн шийдэл.
        Solution("general-plan-zoning", 23, "ЕТ", 14, "Төлөвлөлтийн үндсэн шийдэл", "М1:1500"),
        Solution("site-layout", 24, "ЕТ", 15, "Талбайн зохион байгуулалт", "М1:1500"),
        Solution("architectural-spatial-planning", 25, "ЕТ", 16, "Архитектур орон зайн төлөвлөлт", "М1:1500"),
        Solution("street-road-transport", 26, "ЕТ", 17, "Авто зам, тээврийн сүлжээний төлөвлөлт", "М1:1500"),
        Solution("traffic-organization", 27, "ЕТ", 18, "Хөдөлгөөн зохион байгуулалт", "М1:1500"),
        Solution("green-infrastructure", 28, "ЕТ", 19, "Ногоон байгууламжийн төлөвлөлт", "М1:1500"),
        Solution("waste-management", 29, "ЕТ", 20, "Хог хаягдлын менежмент", "М1:1500"),
        Solution("general-view-1", 30, "ЕТ", 21, "Ерөнхий харагдах байдал - 1", "М1:1500"),
        Solution("general-view-2", 31, "ЕТ", 22, "Ерөнхий харагдах байдал - 2", "М1:1500"),
        Solution("engineering-preparation", 32, "ИДБ", 6, "Инженерийн бэлтгэл арга хэмжээ", "М1:1500"),
        Solution("heating-supply", 33, "ИДБ", 7, "Дулаан хангамж", "М1:1500"),
        Solution("water-and-sewer", 34, "ИДБ", 8, "Ус хангамж, ариутгах татуурга", "М1:1500"),
        Solution("power-supply", 35, "ИДБ", 9, "Цахилгаан хангамж", "М1:1500"),
        Solution("communications-and-signaling", 36, "ИДБ", 10, "Мэдээлэл холбооны сүлжээ", "М1:1500"),
    ];

    private static UrbanPlanningDrawingSlot Generated(
        string id,
        int order,
        string title,
        AlbumGeneratedPageKind kind = AlbumGeneratedPageKind.None,
        string mark = "",
        int markOrder = 0,
        string scale = "") =>
        new(id, order, mark, markOrder, title, Scale: scale, GeneratedPageKind: kind);

    private static UrbanPlanningDrawingSlot Survey(
        string id,
        int order,
        string mark,
        int markOrder,
        string title,
        string scale) =>
        Tiled(id, order, mark, markOrder, title) with
        {
            Scale = scale,
            Section = DevelopmentProjectDrawingSequence.SurveySectionTitle,
        };

    private static UrbanPlanningDrawingSlot Solution(
        string id,
        int order,
        string mark,
        int markOrder,
        string title,
        string scale) =>
        Tiled(id, order, mark, markOrder, title) with
        {
            Scale = scale,
            Section = DevelopmentProjectDrawingSequence.SolutionSectionTitle,
        };

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
