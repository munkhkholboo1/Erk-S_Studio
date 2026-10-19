namespace ErkS.Studio;

/// <summary>
/// Which of a project's cloud albums is "the" album, and which revision of it
/// is current.
///
/// 🔴 EXTRACTED RATHER THAN REWRITTEN. This rule already existed, inline, in the
/// middle of the method that links a project to its cloud record. The album
/// refresh needs the same answer, and writing a second version of it - even an
/// obviously equivalent one - is how two rules come to answer one question and
/// then quietly part company. The wording below is the original expression,
/// moved; the behaviour is deliberately unchanged.
///
/// The rule: the first album whose <c>CurrentRevisionId</c> actually resolves to
/// a revision it carries. An album naming a revision it did not send is skipped
/// rather than half-believed.
/// </summary>
internal static class StudioCloudAlbumSelection
{
    /// <summary>The server's album type for working drawings (SRV 88d049f).</summary>
    public const string BuildingWorkingDrawingsAlbumType = "BuildingWorkingDrawings";

    /// <summary>
    /// Whether a server album is the project's building album - the concept
    /// album, or, since the server types an album by its own stage (SRV
    /// 88d049f, 2026-10-20), the working-drawing album. Asking for the concept
    /// type alone would skip a working-drawing album in every fallback.
    /// </summary>
    public static bool IsBuildingAlbumType(string? albumType) =>
        string.Equals(albumType, ErkS.Platform.Core.ProjectWorkspace.BuildingArchitectureConcept, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(albumType, BuildingWorkingDrawingsAlbumType, StringComparison.OrdinalIgnoreCase);

    public static StudioCloudAlbumRevision? CurrentRevision(StudioCloudProjectDetail? detail) =>
        (detail?.Albums ?? [])
            .OfType<StudioCloudAlbum>()
            .Select(album => (album.Revisions ?? [])
                .OfType<StudioCloudAlbumRevision>()
                .FirstOrDefault(revision => string.Equals(
                    revision.RevisionId,
                    album.CurrentRevisionId,
                    StringComparison.OrdinalIgnoreCase)))
            .FirstOrDefault(revision => revision is not null);

    /// <summary>
    /// The current revision's id, or an empty string when the project carries
    /// no resolvable album revision.
    ///
    /// The empty string means "the cloud did not name a current revision" and
    /// must not be compared as though it were one: a caller that treats "" as a
    /// revision would read a project with no album yet as being in step with
    /// every device that also has nothing.
    /// </summary>
    public static string CurrentRevisionId(StudioCloudProjectDetail? detail) =>
        CurrentRevision(detail)?.RevisionId ?? "";
}
