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

    /// <summary>
    /// Who composes the two front-matter slots of a working-drawing album, and
    /// what has to happen elsewhere when that answer changes.
    /// </summary>
    /// <remarks>
    /// The sheet-package contract carries an obligation with no owner:
    ///
    ///   "In the current Blueprint workflow these two remain producer-owned on
    ///    the Revit side; when working-drawing albums move to Studio
    ///    composition, producers update their block lists together with this
    ///    table."
    ///
    /// This test was written to fire on the day that move happened. It fired on
    /// its first run: the move has ALREADY happened here and the contract still
    /// describes the state before it. Both slots are Studio-composed, and PFR's
    /// export boundary - which returns early for anything outside Sketch mode -
    /// still lets a Revit cover through, because the contract told it to.
    ///
    /// So the assertion is inverted from what it was written as. It now pins
    /// the real state, and still fires if anyone moves these back, because the
    /// same notification is owed in that direction too.
    /// </remarks>
    [Fact]
    public void WorkingDrawingFrontMatterIsStudioComposed()
    {
        AlbumDefinition definition =
            BuildingWorkingDrawingAlbumTemplate.CreateDefinition(
                BuildingWorkingDrawingAlbumTemplate.DefaultTitle);

        AlbumCompositionItem[] frontMatter = definition.Composition
            .Where(item => item.Id is "cover" or "drawing-list-and-notes")
            .ToArray();
        Assert.Equal(2, frontMatter.Length);

        foreach (AlbumCompositionItem item in frontMatter)
        {
            Assert.True(
                item.Kind == AlbumCompositionKind.Generated,
                $"'{item.Id}' is no longer Studio-composed. Before updating "
                + "this test: tell PFR, because RevitStudioWorkflowBoundary "
                + "decides what it exports from this, and amend the "
                + "'Studio-generated album pages' table in "
                + "docs/SHEET-PACKAGE-CONTRACT.md to match.");
        }
    }
}
