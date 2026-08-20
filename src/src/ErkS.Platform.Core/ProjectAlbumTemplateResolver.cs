using ErkS.Platform.Core.ProjectTypes.UrbanPlanning;
using ErkS.Platform.Core.ProjectTypes.Building.WorkingDrawings;

namespace ErkS.Platform.Core;

public static class ProjectAlbumTemplateResolver
{
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
