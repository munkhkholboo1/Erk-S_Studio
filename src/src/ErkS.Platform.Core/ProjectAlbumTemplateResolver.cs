using ErkS.Platform.Core.ProjectTypes.UrbanPlanning;
using ErkS.Platform.Core.ProjectTypes.Building.WorkingDrawings;

namespace ErkS.Platform.Core;

/// <summary>
/// Whether the project's stage has an album template of its own, and what it
/// got instead when it does not.
/// </summary>
public sealed record ProjectAlbumTemplateCoverage(
    bool HasTemplateForStage,
    string ProjectType,
    string StageCode,
    string StageLabel)
{
    /// <summary>
    /// What to tell the user, or null when the stage is properly covered.
    ///
    /// A stage with no template of its own falls back to the concept album.
    /// That produces a usable album, which is exactly why it went unnoticed:
    /// the pages are numbered, the corner block is drawn, nothing errors. What
    /// is missing is the composition this stage is supposed to have, and the
    /// only way to find that out was to know what it should have looked like.
    /// </summary>
    public string? Notice => HasTemplateForStage
        ? null
        : $"«{StageLabel}» үе шатанд зориулсан альбомын загвар хараахан байхгүй тул " +
          "энэ төсөл «Загвар зураг»-ийн загвараар нээгдэж байна. Формат, булангийн " +
          "хүснэгт ажиллана, харин хуудасны бүрдэл, дараалал нь энэ үе шатных биш. " +
          "Загвар нэмэгдэх хүртэл хуудсаа гараар зохион байгуулна уу.";
}

public static class ProjectAlbumTemplateResolver
{
    /// <summary>
    /// Reports whether a stage is covered rather than quietly substituting a
    /// template for it. <see cref="CreateDefinition"/> has to return something
    /// usable and so cannot refuse; this is how the substitution gets said out
    /// loud.
    /// </summary>
    public static ProjectAlbumTemplateCoverage DescribeCoverage(ProjectWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        string projectType = (workspace.Identity.ProjectType ?? "").Trim();
        string stageCode = (workspace.Identity.StageCode ?? "").Trim();
        bool covered =
            UrbanPlanningAlbumTemplate.Supports(projectType, stageCode) ||
            BuildingWorkingDrawingAlbumTemplate.Supports(projectType, stageCode) ||
            IsConceptStage(projectType, stageCode);
        string label = (workspace.Identity.StageName ?? "").Trim();
        return new ProjectAlbumTemplateCoverage(
            covered,
            projectType,
            stageCode,
            label.Length > 0 ? label : stageCode);
    }

    /// <summary>
    /// The concept album is a template in its own right, not only the
    /// fallback. A building concept project reaching it is covered; an urban
    /// planning stage landing there is not.
    /// </summary>
    private static bool IsConceptStage(string projectType, string stageCode) =>
        !projectType.Equals(
            ProjectTypes.UrbanPlanningProjectType.TypeId,
            StringComparison.OrdinalIgnoreCase) &&
        stageCode.Length > 0;

    public static AlbumDefinition CreateDefinition(ProjectWorkspace workspace)
    {
        if (UrbanPlanningAlbumTemplate.Supports(
                workspace.Identity.ProjectType,
                workspace.Identity.StageCode))
        {
            return UrbanPlanningAlbumTemplate.CreateDefinition(workspace.Identity.StageCode);
        }

        if (BuildingWorkingDrawingAlbumTemplate.Supports(
                workspace.Identity.ProjectType,
                workspace.Identity.StageCode))
        {
            return BuildingWorkingDrawingAlbumTemplate.CreateDefinition(
                BuildingWorkingDrawingAlbumTemplate.DefaultTitle);
        }

        return BuildingArchitectureConceptAlbumTemplate.CreateDefinition(workspace.PrimaryAlbum.Title);
    }

    public static bool Apply(ProjectWorkspace workspace, StudioAlbumDocument album)
    {
        bool isUrbanPlanning = UrbanPlanningAlbumTemplate.Supports(
                workspace.Identity.ProjectType,
                workspace.Identity.StageCode);
        bool isBuildingWorkingDrawing = BuildingWorkingDrawingAlbumTemplate.Supports(
                workspace.Identity.ProjectType,
                workspace.Identity.StageCode);
        if (isBuildingWorkingDrawing)
            BuildingWorkingDrawingAlbumCatalog.EnsureAlbums(workspace);
        if (!isUrbanPlanning && !isBuildingWorkingDrawing)
        {
            return false;
        }

        AlbumDefinition expected = isBuildingWorkingDrawing
            ? BuildingWorkingDrawingAlbumTemplate.CreateDefinition(
                BuildingWorkingDrawingAlbumTemplate.DefaultTitle)
            : UrbanPlanningAlbumTemplate.CreateDefinition(workspace.Identity.StageCode);
        List<AlbumPageDefinition> existingPages = album.Definition.Pages ?? [];
        album.StageCode = workspace.Identity.StageCode;
        album.PackageType = workspace.Identity.ProjectType;
        workspace.PrimaryAlbum.Type = workspace.Identity.ProjectType;
        workspace.PrimaryAlbum.Title = expected.Title;
        album.Definition.Title = expected.Title;

        if (string.Equals(album.Definition.TemplateId, expected.TemplateId, StringComparison.OrdinalIgnoreCase))
        {
            bool changed = false;
            if (isUrbanPlanning &&
                expected.GeneratedPageFormat is not null &&
                (!PageFormatCatalog.IsUsable(album.Definition.GeneratedPageFormat) ||
                 album.Definition.GeneratedPageFormat!.Kind != PageFormatKind.WorkingDrawing))
            {
                album.Definition.GeneratedPageFormat = expected.GeneratedPageFormat;
                changed = true;
            }
            if (isUrbanPlanning &&
                UrbanPlanningAlbumTemplate.EnsureGeneratedPages(album.Definition))
            {
                changed = true;
            }

            // This front-matter page is owned by Studio. Migrate existing HET albums
            // without replacing their source pages or contributor content.
            AlbumCompositionItem? drawingList = album.Definition.Composition.FirstOrDefault(item =>
                item.Id.Equals("drawing-list-and-notes", StringComparison.OrdinalIgnoreCase));
            if (drawingList is null || drawingList.Kind == AlbumCompositionKind.Generated)
                return changed;

            drawingList.Kind = AlbumCompositionKind.Generated;
            drawingList.GeneratedPageKind = AlbumGeneratedPageKind.None;
            drawingList.AllowMultiple = false;
            return true;
        }

        // Ангиллыг солихыг зөвхөн эх үүсвэр, хуудасгүй төслүүдэд зөвшөөрсөн тул
        // хэрэглэгчийн бодит альбомын агуулгыг энд устгахгүй.
        album.Definition = expected;
        album.Definition.Pages.AddRange(existingPages);
        if (isUrbanPlanning)
        {
            UrbanPlanningAlbumTemplate.MigrateLegacyPages(
                album.Definition,
                album.Definition.Pages);
        }
        workspace.BuildingGroups.Clear();
        workspace.SheetBuildingAssignments.Clear();
        return true;
    }
}
