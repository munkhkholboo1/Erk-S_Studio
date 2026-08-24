namespace ErkS.Platform.Core.ProjectTypes.UrbanPlanning;

/// <summary>
/// Барилгажилтын төслийн зургийн дараалал.
///
/// The third member of the urban planning family, after the master plan and
/// the partial plan. Its order comes from a reference album the client works
/// from rather than from a numbered clause of БД 30-103-21, and is recorded in
/// docs/DEVELOPMENT-PROJECT-ALBUM-CONTRACT.md, which AutoCAD builds from too.
///
/// Two things differ from its two siblings and are deliberate:
///
/// The opening pages carry no mark. In the other two the cover is ЕТ-01 and
/// the drawing list ЕТ-02; here the reference album leaves the cover, the
/// drawing list and the planning task unnumbered, and the ЕТ counter starts at
/// the location scheme. Following the family convention instead would shift
/// every drawing number away from the album the client is holding.
///
/// The planning task is one slot even though the reference album shows four
/// pages of it. How many there are depends on the document: "АТД янз бүр
/// байдаг. Даалгаврын хуудасны тооноос хамаарч хэдэн хуудас үүсгэхээ студио
/// өөрөө шийддэг." The generated page expands to the document it was given.
/// </summary>
public sealed class DevelopmentProjectDrawingSequence : IUrbanPlanningDrawingSequence
{
    public const string StageType = "development-project";

    /// <summary>Одоогийн байдлын судалгаа.</summary>
    public const string SurveySectionTitle = "Одоогийн байдлын судалгаа";

    /// <summary>Төлөвлөлтийн шийдэл.</summary>
    public const string SolutionSectionTitle = "Төлөвлөлтийн шийдэл";

    public string ProjectStageType => StageType;

    public IReadOnlyList<UrbanPlanningDrawingSlot> Drawings { get; } =
        UrbanPlanningDrawingSequenceFactory.CreateDevelopmentProjectSequence();
}
