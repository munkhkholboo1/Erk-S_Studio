using ErkS.Platform.Core.ProjectTypes.Building.WorkingDrawings;

namespace ErkS.Platform.Core.Tests;

public sealed class BuildingWorkingDrawingAlbumCatalogTests
{
    [Fact]
    public void WorkingDrawingProjectOwnsOneAlbumPerDiscipline()
    {
        var workspace = new ProjectWorkspace();
        workspace.Identity.ProjectType = ProjectTypes.BuildingDesignProjectType.TypeId;
        workspace.Identity.StageCode = "working-drawings";
        workspace.Deliverables.Albums.Add(new ProjectAlbumRecord { IsPrimary = true });

        BuildingWorkingDrawingAlbumCatalog.EnsureAlbums(workspace);

        Assert.Equal(6, workspace.Deliverables.Albums.Count);
        Assert.Equal("working-drawing-ba", workspace.PrimaryAlbum.Id);
        Assert.Contains(workspace.Deliverables.Albums, x => x.Id == "working-drawing-bb");
        Assert.Contains(workspace.Deliverables.Albums, x => x.Id == "working-drawing-has");
        Assert.Contains(workspace.Deliverables.Albums, x => x.Id == "working-drawing-dg-ht");
        Assert.Contains(workspace.Deliverables.Albums, x => x.Id == "working-drawing-cbu");
        Assert.Contains(workspace.Deliverables.Albums, x => x.Id == "working-drawing-hd");
    }

    [Theory]
    [InlineData("ДГ", true)] [InlineData("ХТ", true)] [InlineData("ДГ,ХТ", true)] [InlineData("БА", false)]
    public void ElectricalAlbumAcceptsBothMarks(string mark, bool expected)
    {
        var discipline = BuildingWorkingDrawingAlbumCatalog.All.Single(x => x.Id == "working-drawing-dg-ht");
        Assert.Equal(expected, BuildingWorkingDrawingAlbumCatalog.MatchesMark(discipline, mark));
    }
}
