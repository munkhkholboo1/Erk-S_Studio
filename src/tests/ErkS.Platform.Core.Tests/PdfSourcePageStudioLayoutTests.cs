using ErkS.Platform.Contracts;
using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

public sealed class PdfSourcePageStudioLayoutTests
{
    [Fact]
    public void CreateForSource_RecognizesA1LandscapeWithoutChangingPaperSize()
    {
        PageFormatDefinition format =
            PdfSourcePageFormatFactory.CreateForSource(841, 594);

        Assert.Equal("A1", format.Code);
        Assert.Equal("LANDSCAPE", format.Orientation);
        Assert.Equal(841, format.WidthMm);
        Assert.Equal(594, format.HeightMm);
        Assert.True(format.DrawingArea.Width > 0);
        Assert.True(format.TitleBlockArea.Width > 0);
    }

    [Fact]
    public void ApplyConfirmedCrop_ReplacesSourceAsIsWithMatchingStudioFrameAtPhysicalSize()
    {
        var page = new AlbumPageDefinition
        {
            PageFormatId = PageFormatCatalog.SourceAsIsId,
            PlacementMode = PagePlacementMode.PreservePhysicalSize,
            FollowSourceFormat = true,
        };
        var entry = new SheetPackageEntry
        {
            WidthMm = 420,
            HeightMm = 297,
        };

        PdfSourcePageStudioLayout.ApplyConfirmedCrop(page, entry);

        Assert.NotNull(page.PageFormatSnapshot);
        Assert.Equal("A3", page.PageFormatSnapshot.Code);
        Assert.Equal(420, page.PageFormatSnapshot.WidthMm);
        Assert.Equal(297, page.PageFormatSnapshot.HeightMm);
        Assert.Equal(page.PageFormatSnapshot.Id, page.PageFormatId);
        Assert.Equal(PagePlacementMode.PreservePhysicalSize, page.PlacementMode);
        Assert.False(page.FollowSourceFormat);
    }

    [Fact]
    public void ApplyConfirmedCrop_PreservesAnExistingStudioFormat()
    {
        PageFormatDefinition configured = PdfSourcePageFormatFactory.Create(
            "A2",
            "LANDSCAPE",
            "RIGHT");
        var page = new AlbumPageDefinition
        {
            PageFormatId = configured.Id,
            PageFormatSnapshot = configured,
            PlacementMode = PagePlacementMode.PreservePhysicalSize,
        };
        var entry = new SheetPackageEntry
        {
            WidthMm = 841,
            HeightMm = 594,
        };

        PdfSourcePageStudioLayout.ApplyConfirmedCrop(page, entry);

        Assert.Same(configured, page.PageFormatSnapshot);
        Assert.Equal(configured.Id, page.PageFormatId);
        Assert.Equal(PagePlacementMode.PreservePhysicalSize, page.PlacementMode);
        Assert.False(page.FollowSourceFormat);
    }
}
