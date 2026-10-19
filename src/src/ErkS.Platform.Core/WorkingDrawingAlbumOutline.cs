namespace ErkS.Platform.Core;

/// <summary>One part of the working-drawing album as it prints.</summary>
public sealed record WorkingDrawingOutlineSection(string Title, IReadOnlyList<WorkingDrawingOutlinePage> Pages);

/// <summary>
/// One printed page: either a page Studio generates (<see cref="Generated"/>)
/// or a source sheet placed in the album (<see cref="Page"/>).
/// </summary>
public sealed record WorkingDrawingOutlinePage(
    string Number,
    string Title,
    ConceptGeneratedPagePlan? Generated,
    AlbumBuildPage? Page);

/// <summary>
/// The working-drawing album as the organizer shows it - the same parts, the
/// same order and the same numbers as the printed album, because both are read
/// from the one build request.
/// </summary>
/// <remarks>
/// 🔴 BATNASAN, 2026-10-20: the organizer drew the concept tree (a fixed
/// «Ерөнхий төлөвлөгөө» slot list, then buildings) with the sequencer's
/// numbers, while the album printed three parts numbered by
/// <see cref="AlbumBuilder"/>. Two numberings of one album cannot both be
/// right; the organizer now reads the print's.
/// </remarks>
public static class WorkingDrawingAlbumOutline
{
    /// <summary>
    /// The outline, or null when the album is not a working-drawing album or
    /// cannot be built yet (its sheets have not arrived).
    /// </summary>
    public static IReadOnlyList<WorkingDrawingOutlineSection>? Create(AlbumProject project, SheetLibrary library)
    {
        if (!string.Equals(project.Album.TemplateId, BuildingWorkingDrawingAlbumTemplate.TemplateId, StringComparison.OrdinalIgnoreCase))
            return null;
        if (!AlbumBuilder.TryCreateRequest(project, library, out AlbumBuildRequest request))
            return null;

        List<WorkingDrawingOutlinePage> generated = BuildingArchitectureConceptGeneratedPagePlanner.Create(project)
            .Select(plan => new WorkingDrawingOutlinePage(plan.Number, plan.Title, plan, null))
            .ToList();
        var sections = new List<WorkingDrawingOutlineSection>();
        bool generatedPlaced = false;
        foreach (AlbumBuildSection section in request.Sections)
        {
            List<WorkingDrawingOutlinePage> pages = section.Pages
                .Select(page => new WorkingDrawingOutlinePage(page.Number, page.Title, null, page))
                .ToList();
            if (!generatedPlaced && section.Title.Equals(
                    BuildingWorkingDrawingAlbumTemplate.GeneralPartSectionTitle,
                    StringComparison.OrdinalIgnoreCase))
            {
                pages.InsertRange(0, generated);
                generatedPlaced = true;
            }

            sections.Add(new WorkingDrawingOutlineSection(section.Title, pages));
        }

        if (!generatedPlaced && generated.Count > 0)
        {
            sections.Insert(0, new WorkingDrawingOutlineSection(
                BuildingWorkingDrawingAlbumTemplate.GeneralPartSectionTitle,
                generated));
        }

        return sections;
    }
}
