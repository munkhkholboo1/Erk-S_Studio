using ErkS.Platform.Core;
using ErkS.Platform.Core.ProjectTypes.UrbanPlanning;

namespace ErkS.Studio.App.Tests;

public sealed class StudioAlbumCompositionProgressTests
{
    [Fact]
    public void Resolve_SeparatesRequiredAndOptionalPartialPlanDrawings()
    {
        AlbumDefinition album = UrbanPlanningAlbumTemplate.CreateDefinition(
            PartialMasterPlanDrawingSequence.StageType);
        album.Pages.Add(new AlbumPageDefinition { TemplateSlotId = "existing-condition" });
        album.Pages.Add(new AlbumPageDefinition { TemplateSlotId = "waste-management" });

        StudioAlbumCompositionProgress progress =
            StudioAlbumCompositionProgress.Resolve(album, visualizationImageCount: 0);

        Assert.Equal(4, progress.ReadyRequired);
        Assert.Equal(21, progress.RequiredCount);
        Assert.Equal(1, progress.ReadyOptional);
        Assert.Equal(2, progress.OptionalCount);
        Assert.Equal("Үндсэн бүрдэл 4/21 · Нэмэлт 1/2", progress.Summary);
    }

    [Fact]
    public void Resolve_CountsVisualizationOnlyWhenItHasImages()
    {
        AlbumDefinition album = BuildingArchitectureConceptAlbumTemplate.CreateDefinition("Concept");

        StudioAlbumCompositionProgress withoutImages =
            StudioAlbumCompositionProgress.Resolve(album, visualizationImageCount: 0);
        StudioAlbumCompositionProgress withImages =
            StudioAlbumCompositionProgress.Resolve(album, visualizationImageCount: 1);

        Assert.Equal(withoutImages.ReadyRequired + 1, withImages.ReadyRequired);
    }
}
