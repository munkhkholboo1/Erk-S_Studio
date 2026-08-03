using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

public sealed class WorkingDrawingPageLayoutTests
{
    [Fact]
    public void Resolve_PreservesAutoCadChromeAndCreatesFiveMillimetreEtalonBand()
    {
        var format = new PageFormatDefinition
        {
            Kind = PageFormatKind.WorkingDrawing,
            WidthMm = 841,
            HeightMm = 594,
            DrawingArea = new PageRectMm { X = 20, Y = 10, Width = 811, Height = 574 },
            SheetTitleArea = new PageRectMm { X = 20, Y = 10, Width = 811, Height = 9 },
            TitleBlockArea = new PageRectMm { X = 651, Y = 548, Width = 180, Height = 36 },
            ModuleColumns = 2,
            ModuleRows = 3,
        };

        WorkingDrawingPageRegions regions = WorkingDrawingPageLayout.Resolve(format);

        Assert.Equal((15d, 5d, 821d, 584d), Rect(regions.EtalonOuterFrame));
        Assert.Equal((20d, 10d, 811d, 574d), Rect(regions.EtalonInnerFrame));
        Assert.Equal((20d, 10d, 811d, 9d), Rect(regions.SheetTitleArea));
        Assert.Equal((651d, 548d, 180d, 36d), Rect(regions.TitleBlockArea));
        Assert.Equal(16, regions.GridColumns);
        Assert.Equal(12, regions.GridRows);
    }

    [Fact]
    public void Resolve_LegacyWorkingFormatReceivesStudioOwnedTitleAndHorizontalCornerTable()
    {
        var format = new PageFormatDefinition
        {
            Kind = PageFormatKind.WorkingDrawing,
            WidthMm = 420,
            HeightMm = 297,
            DrawingArea = new PageRectMm { X = 20, Y = 10, Width = 390, Height = 277 },
        };

        WorkingDrawingPageRegions regions = WorkingDrawingPageLayout.Resolve(format);

        Assert.Equal((20d, 10d, 390d, 9d), Rect(regions.SheetTitleArea));
        Assert.Equal((230d, 251d, 180d, 36d), Rect(regions.TitleBlockArea));
        Assert.Equal(8, regions.GridColumns);
        Assert.Equal(6, regions.GridRows);
    }

    private static (double X, double Y, double Width, double Height) Rect(PageRectMm rect) =>
        (rect.X, rect.Y, rect.Width, rect.Height);
}
