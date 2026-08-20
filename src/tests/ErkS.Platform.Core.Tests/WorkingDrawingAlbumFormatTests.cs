using ErkS.Platform.Core.ProjectTypes.UrbanPlanning;

namespace ErkS.Platform.Core.Tests;

public sealed class WorkingDrawingAlbumFormatTests
{
    [Theory]
    [InlineData(AlbumGeneratedPageKind.Cover, false, false)]
    [InlineData(AlbumGeneratedPageKind.None, true, true)]
    [InlineData(AlbumGeneratedPageKind.SiteContext, true, true)]
    public void GeneratedPageChrome_CoverKeepsOnlyFrameAndGrid(
        AlbumGeneratedPageKind pageKind,
        bool expectedSheetHeader,
        bool expectedTitleBlock)
    {
        WorkingDrawingGeneratedPageChrome chrome =
            WorkingDrawingGeneratedPageChromePolicy.Resolve(pageKind);

        Assert.Equal(expectedSheetHeader, chrome.ShowSheetHeader);
        Assert.Equal(expectedTitleBlock, chrome.ShowTitleBlock);
    }

    [Theory]
    [InlineData(1, 1, "A1", 420d, 297d)]
    [InlineData(2, 1, "B1", 828d, 297d)]
    [InlineData(1, 2, "A2", 582d, 420d)]
    [InlineData(4, 4, "D4", 1644d, 1152d)]
    public void Factory_JoinsA3ModulesWithTwelveMillimeterOverlap(
        int columns,
        int rows,
        string expectedCode,
        double expectedWidth,
        double expectedHeight)
    {
        PageFormatDefinition format = WorkingDrawingAlbumFormatFactory.Create(columns, rows);

        Assert.Equal(expectedCode, format.Code);
        Assert.Equal(expectedWidth, format.WidthMm, 3);
        Assert.Equal(expectedHeight, format.HeightMm, 3);
        Assert.Equal(columns, format.ModuleColumns);
        Assert.Equal(rows, format.ModuleRows);
        Assert.Equal("LANDSCAPE", format.Orientation);
        Assert.Equal("LEFT", format.BindEdge);
        Assert.Equal(PageFormatKind.WorkingDrawing, format.Kind);
        Assert.True(format.ShowBorder);
        Assert.True(format.ShowGrid);
        Assert.False(string.IsNullOrWhiteSpace(format.GeometryHash));
    }

    [Fact]
    public void Factory_PreservesHorizontalWorkingDrawingTitleBlockAndEtalonMargins()
    {
        PageFormatDefinition format = WorkingDrawingAlbumFormatFactory.Create(2, 2);

        Assert.Equal(20d, format.DrawingArea.X, 3);
        Assert.Equal(10d, format.DrawingArea.Y, 3);
        Assert.Equal(format.WidthMm - 30d, format.DrawingArea.Width, 3);
        Assert.Equal(format.HeightMm - 20d, format.DrawingArea.Height, 3);
        Assert.Equal(format.DrawingArea.X, format.SheetTitleArea.X, 3);
        Assert.Equal(format.DrawingArea.Width, format.SheetTitleArea.Width, 3);
        Assert.Equal(9d, format.SheetTitleArea.Height, 3);
        Assert.Equal(180d, format.TitleBlockArea.Width, 3);
        Assert.Equal(36d, format.TitleBlockArea.Height, 3);
        Assert.Equal(
            format.DrawingArea.X + format.DrawingArea.Width - 180d,
            format.TitleBlockArea.X,
            3);
        Assert.Equal(
            format.DrawingArea.Y + format.DrawingArea.Height - 36d,
            format.TitleBlockArea.Y,
            3);
    }

    [Fact]
    public void PartialPlanTemplate_DefaultsGeneratedPagesToA3WorkingDrawingFormat()
    {
        AlbumDefinition album = UrbanPlanningAlbumTemplate.CreateDefinition(
            PartialMasterPlanDrawingSequence.StageType);

        PageFormatDefinition format = Assert.IsType<PageFormatDefinition>(
            album.GeneratedPageFormat);
        Assert.Equal("A1", format.Code);
        Assert.Equal(420d, format.WidthMm, 3);
        Assert.Equal(297d, format.HeightMm, 3);
        Assert.Equal(PageFormatKind.WorkingDrawing, format.Kind);
        Assert.Equal(180d, format.TitleBlockArea.Width, 3);
        Assert.Equal(36d, format.TitleBlockArea.Height, 3);
    }

    [Fact]
    public void AlbumStore_RoundTripsSelectedGeneratedPageFormat()
    {
        var document = new StudioAlbumDocument
        {
            Definition = UrbanPlanningAlbumTemplate.CreateDefinition(
                PartialMasterPlanDrawingSequence.StageType),
        };
        document.Definition.GeneratedPageFormat =
            WorkingDrawingAlbumFormatFactory.Create(3, 2);
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.erksalbum");

        try
        {
            StudioAlbumDocumentStore.Save(document, path);
            StudioAlbumDocument loaded = StudioAlbumDocumentStore.Load(path);

            PageFormatDefinition format = Assert.IsType<PageFormatDefinition>(
                loaded.Definition.GeneratedPageFormat);
            Assert.Equal("C2", format.Code);
            Assert.Equal(3, format.ModuleColumns);
            Assert.Equal(2, format.ModuleRows);
            Assert.Equal(1236d, format.WidthMm, 3);
            Assert.Equal(582d, format.HeightMm, 3);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
