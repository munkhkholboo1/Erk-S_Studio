using ErkS.Platform.Core.ProjectTypes.Building.WorkingDrawings;

namespace ErkS.Platform.Core.Tests;

public sealed class BuildingWorkingDrawingAlbumCatalogTests
{
    [Fact]
    public void WorkingDrawingProjectOwnsOneAlbumPerBuilding()
    {
        ProjectWorkspace workspace = WorkingDrawingProject();
        workspace.BuildingGroups =
        [
            new ProjectBuildingGroup { Id = "school", Name = "Сургууль", Order = 1 },
            new ProjectBuildingGroup { Id = "kindergarten", Name = "Цэцэрлэг", Order = 2 },
        ];
        workspace.Deliverables.Albums.Add(new ProjectAlbumRecord
        {
            IsPrimary = true,
            DocumentPath = "albums/working-drawing-ba.album.json",
        });

        BuildingWorkingDrawingAlbumCatalog.EnsureAlbums(workspace);

        Assert.Equal(2, workspace.Deliverables.Albums.Count);
        Assert.Equal("working-drawing-building-school", workspace.PrimaryAlbum.Id);
        Assert.Equal("Сургууль - ажлын зургийн альбом", workspace.PrimaryAlbum.Title);
        // The one album document Studio loads must follow the project across the
        // move off the old per-discipline records.
        Assert.Equal("albums/working-drawing-ba.album.json", workspace.PrimaryAlbum.DocumentPath);
        Assert.Contains(
            workspace.Deliverables.Albums,
            album => album.Id == "working-drawing-building-kindergarten");
    }

    [Fact]
    public void ProjectWithoutBuildingsStillOwnsOneAlbum()
    {
        ProjectWorkspace workspace = WorkingDrawingProject();
        workspace.Deliverables.Albums.Add(new ProjectAlbumRecord { IsPrimary = true });

        BuildingWorkingDrawingAlbumCatalog.EnsureAlbums(workspace);

        ProjectAlbumRecord album = Assert.Single(workspace.Deliverables.Albums);
        Assert.Equal(BuildingWorkingDrawingAlbumCatalog.UnassignedBuildingAlbumId, album.Id);
        Assert.True(album.IsPrimary);
    }

    [Fact]
    public void EveryDrawingSetOfTheWorkingStageIsCatalogued()
    {
        string[] marks = BuildingWorkingDrawingAlbumCatalog.All
            .Select(discipline => discipline.Mark)
            .ToArray();

        Assert.Equal(
            ["ЕХ", "БА", "ББ", "ХАС", "ДМ", "ЦБУ", "ХТ,ДГ", "ХД", "АУ"],
            marks);
    }

    [Theory]
    [InlineData("ХТ,ДГ", true)]
    [InlineData("ХТДГ", true)]
    [InlineData("ДГ,ХТ", true)]
    [InlineData("ДГ", true)]
    [InlineData("ХТ", true)]
    [InlineData("БА", false)]
    public void ElectricalSetAcceptsBothOrdersOfItsMark(string mark, bool expected)
    {
        IBuildingWorkingDrawingDiscipline discipline =
            BuildingWorkingDrawingAlbumCatalog.All.Single(x => x.Id == "working-drawing-dg-ht");

        Assert.Equal(
            expected,
            BuildingWorkingDrawingAlbumCatalog.MatchesMark(discipline, mark));
    }

    [Fact]
    public void TECHNOLOGYSheetsBindIntoTheArchitectureSetAsPFADefinesThem()
    {
        // PFA's ArchitectureWorkingDrawingDiscipline: «The architecture album also
        // carries ... technology (ТХ) sheets». It lists "ТХ - Технологийн
        // байгуулалт" as the last sheet of the БА album, and its catalogue test
        // asserts it there. This catalogue says it matches PFA's - and did not
        // know the mark, so every ТХ sheet fell to the general part and was bound
        // FIRST, ahead of the floor plans it belongs after. PFR measured it on the
        // Batnasan project (sheets 58-63) on 2026-10-19 and warned about it on
        // every export.
        Assert.Equal(
            "working-drawing-ba",
            BuildingWorkingDrawingAlbumCatalog.FindByMark("ТХ")?.Id);
        Assert.Equal(
            BuildingWorkingDrawingAlbumCatalog.SectionOrder("БА"),
            BuildingWorkingDrawingAlbumCatalog.SectionOrder("ТХ"));
    }

    [Fact]
    public void TheArchitectureSetStillOwnsItsOwnMarkAndNoOther()
    {
        // The widening is ТХ alone. A set that silently took marks it never named
        // would hide the next unknown mark in the same way ТХ was hidden.
        IBuildingWorkingDrawingDiscipline architecture =
            BuildingWorkingDrawingAlbumCatalog.All.Single(x => x.Id == "working-drawing-ba");

        Assert.True(BuildingWorkingDrawingAlbumCatalog.MatchesMark(architecture, "БА"));
        Assert.False(BuildingWorkingDrawingAlbumCatalog.MatchesMark(architecture, "ББ"));
        Assert.False(BuildingWorkingDrawingAlbumCatalog.MatchesMark(architecture, "ЕХ"));
    }

    [Fact]
    public void GeneralPartLeadsTheAlbumAndUnmarkedPagesLeadWithIt()
    {
        Assert.Equal(0, BuildingWorkingDrawingAlbumCatalog.SectionOrder("ЕХ"));
        Assert.Equal(0, BuildingWorkingDrawingAlbumCatalog.SectionOrder("EX"));
        // Studio's own generated pages carry no mark and belong to the general part.
        Assert.Equal(0, BuildingWorkingDrawingAlbumCatalog.SectionOrder(""));
        Assert.True(
            BuildingWorkingDrawingAlbumCatalog.SectionOrder("БА") <
            BuildingWorkingDrawingAlbumCatalog.SectionOrder("АУ"));
    }

    private static ProjectWorkspace WorkingDrawingProject()
    {
        var workspace = new ProjectWorkspace();
        workspace.Identity.ProjectType = ProjectTypes.BuildingDesignProjectType.TypeId;
        workspace.Identity.StageCode = "working-drawings";
        return workspace;
    }
}
