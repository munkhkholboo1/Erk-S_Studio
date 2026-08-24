namespace ErkS.Platform.Core;

using ErkS.Platform.Core.ProjectTypes.UrbanPlanning;

/// <summary>
/// The line on an album cover that says what kind of document this is.
///
/// It was written out twice - once in the PDF and once in Studio's preview of
/// the same page - and both said /ЗАГВАР ЗУРАГ/ whatever the project was. A
/// client opened their development project and found their album calling
/// itself a concept design.
///
/// Both now read it from here, so the preview cannot promise one thing and the
/// printed cover deliver another.
///
/// The mapping is explicit rather than derived from the stage name. A stage
/// name is a label somebody typed and can be edited; this line goes on the
/// front of a document that leaves the office. A new album template adds one
/// entry here, and until it does it gets the concept wording it would have got
/// anyway - no album silently renames itself because a field changed.
/// </summary>
public static class AlbumCoverStyle
{
    public const string Concept = "/ЗАГВАР ЗУРАГ/";
    public const string WorkingDrawing = "БАРИЛГА АРХИТЕКТУРЫН ХЭСЭГ-БА";
    public const string DevelopmentProject = "/БАРИЛГАЖИЛТЫН ТӨСӨЛ/";

    /// <param name="drawsWorkingDrawingEtalon">
    /// Whether this page is being drawn as a working drawing sheet, which has
    /// its own wording and is decided by the page rather than the template.
    /// </param>
    public static string Resolve(string? albumTemplateId, bool drawsWorkingDrawingEtalon)
    {
        if (drawsWorkingDrawingEtalon)
            return WorkingDrawing;

        return IsDevelopmentProject(albumTemplateId) ? DevelopmentProject : Concept;
    }

    /// <summary>
    /// Whether this album's cover is the approval cover - the landscape sheet
    /// with БАТЛАВ above and the ЗӨВШӨӨРӨЛЦСӨН / БОЛОВСРУУЛСАН tables below.
    ///
    /// The writer used to pick a cover by asking which template this was and
    /// falling through to a plain portrait title page when it recognised none.
    /// The development project album was added, matched nothing, and its first
    /// page came out as an A4 portrait sheet nobody had designed - the album
    /// looked finished and opened with the wrong document.
    ///
    /// Naming the rule here means the next album added has to answer this
    /// question rather than quietly inherit the answer for a page that was
    /// never meant to be anyone's cover.
    /// </summary>
    public static bool UsesApprovalCover(string? albumTemplateId) =>
        (albumTemplateId ?? "").Trim().Equals(
            BuildingArchitectureConceptTemplateId,
            StringComparison.OrdinalIgnoreCase) ||
        IsDevelopmentProject(albumTemplateId);

    /// <summary>
    /// Kept here rather than referenced from the concept template so that this
    /// file, which the PDF writer consults, does not depend on that one.
    /// </summary>
    private const string BuildingArchitectureConceptTemplateId = "building-architecture-concept-v1";

    private static bool IsDevelopmentProject(string? albumTemplateId) =>
        (albumTemplateId ?? "").Trim().Equals(
            UrbanPlanningAlbumTemplate.DevelopmentProjectTemplateId,
            StringComparison.OrdinalIgnoreCase);
}
