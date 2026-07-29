using ErkS.Platform.Contracts;
using ErkS.Platform.Core;

namespace ErkS.Studio.App.Tests;

public sealed class PdfSourcePageEditCommitPolicyTests
{
    [Fact]
    public void ApplyAcceptedEdit_ScaleOnlyOnSourceAsIs_CommitsShownStudioFormat()
    {
        var page = new AlbumPageDefinition
        {
            PageFormatId = PageFormatCatalog.SourceAsIsId,
            PlacementMode = PagePlacementMode.FitDrawingArea,
        };
        var entry = new SheetPackageEntry
        {
            WidthMm = 420,
            HeightMm = 297,
        };

        PdfSourcePageEditCommitPolicy.ApplyAcceptedEdit(
            page,
            entry,
            new SourcePageCropDefinition(),
            "100");

        Assert.Equal("1:100", page.ScaleTextOverride);
        Assert.NotNull(page.PageFormatSnapshot);
        Assert.Equal("A3", page.PageFormatSnapshot.Code);
        Assert.Equal(page.PageFormatSnapshot.Id, page.PageFormatId);
        Assert.Equal(PagePlacementMode.PreservePhysicalSize, page.PlacementMode);
        Assert.False(page.FollowSourceFormat);
        Assert.NotNull(page.SourceCrop);
        Assert.Equal(100, page.SourceCrop.ScalePercent);
    }

    [Fact]
    public void ApplyAcceptedEdit_UnchangedInheritedScaleAndNoCompositionEdit_RemainsSourceAsIs()
    {
        var page = new AlbumPageDefinition
        {
            PageFormatId = PageFormatCatalog.SourceAsIsId,
            PlacementMode = PagePlacementMode.FitDrawingArea,
            ScaleTextOverride = null,
        };
        var entry = new SheetPackageEntry
        {
            WidthMm = 420,
            HeightMm = 297,
            ScaleText = "1:100",
        };

        PdfSourcePageEditCommitPolicy.ApplyAcceptedEdit(
            page,
            entry,
            new SourcePageCropDefinition(),
            scaleTextOverride: null);

        Assert.Null(page.ScaleTextOverride);
        Assert.Equal(PageFormatCatalog.SourceAsIsId, page.PageFormatId);
        Assert.Null(page.PageFormatSnapshot);
        Assert.Equal(PagePlacementMode.FitDrawingArea, page.PlacementMode);
    }

    [Fact]
    public void ApplyAcceptedEdit_ExistingExplicitScaleOnSourceAsIs_RepairsStudioFormat()
    {
        var page = new AlbumPageDefinition
        {
            PageFormatId = PageFormatCatalog.SourceAsIsId,
            PlacementMode = PagePlacementMode.FitDrawingArea,
            ScaleTextOverride = "1:500",
        };
        var entry = new SheetPackageEntry
        {
            WidthMm = 420,
            HeightMm = 297,
        };

        PdfSourcePageEditCommitPolicy.ApplyAcceptedEdit(
            page,
            entry,
            new SourcePageCropDefinition(),
            "1:500");

        Assert.Equal("1:500", page.ScaleTextOverride);
        Assert.NotNull(page.PageFormatSnapshot);
        Assert.Equal(PagePlacementMode.PreservePhysicalSize, page.PlacementMode);
        Assert.False(page.FollowSourceFormat);
    }
}
