using ErkS.Platform.Core.ProjectTypes;

namespace ErkS.Platform.Core.Tests;

public sealed class BuildingWorkingDrawingAlbumTemplateTests
{
    [Fact]
    public void Resolver_UsesWorkingTemplateWithoutConceptDocuments()
    {
        var workspace = new ProjectWorkspace();
        workspace.Identity.ProjectType = BuildingDesignProjectType.TypeId;
        workspace.Identity.StageCode = "working-drawings";
        workspace.Deliverables.Albums.Add(new ProjectAlbumRecord
        {
            IsPrimary = true,
            Title = "Барилгын ажлын зургийн альбум",
        });

        AlbumDefinition definition = ProjectAlbumTemplateResolver.CreateDefinition(workspace);

        Assert.Equal(BuildingWorkingDrawingAlbumTemplate.TemplateId, definition.TemplateId);
        Assert.Equal(
            ["cover", "drawing-list-and-notes", "working-drawing-sheets"],
            definition.Composition.Select(item => item.Id));
        Assert.DoesNotContain(definition.Composition, item =>
            item.GeneratedPageKind is AlbumGeneratedPageKind.DesignOrganization or
                AlbumGeneratedPageKind.PlanningTask);
    }

    [Fact]
    public void Resolver_ReplacesConceptFrontMatterAndPreservesSourcePages()
    {
        var workspace = new ProjectWorkspace();
        workspace.Identity.ProjectType = BuildingDesignProjectType.TypeId;
        workspace.Identity.StageCode = "working-drawings";
        workspace.Deliverables.Albums.Add(new ProjectAlbumRecord { IsPrimary = true });
        var sourcePage = new AlbumPageDefinition { SheetKey = "source|sheet" };
        var album = new StudioAlbumDocument
        {
            Definition = BuildingArchitectureConceptAlbumTemplate.CreateDefinition("old"),
        };
        album.Definition.Pages.Add(sourcePage);

        Assert.True(ProjectAlbumTemplateResolver.Apply(workspace, album));
        Assert.Equal(BuildingWorkingDrawingAlbumTemplate.TemplateId, album.Definition.TemplateId);
        Assert.Equal(BuildingWorkingDrawingAlbumTemplate.DefaultTitle, album.Definition.Title);
        Assert.Equal(BuildingWorkingDrawingAlbumTemplate.DefaultTitle, workspace.PrimaryAlbum.Title);
        Assert.Same(sourcePage, Assert.Single(album.Definition.Pages));
        Assert.DoesNotContain(album.Definition.Composition, item =>
            item.GeneratedPageKind is AlbumGeneratedPageKind.DesignOrganization or
                AlbumGeneratedPageKind.PlanningTask);
    }
}
