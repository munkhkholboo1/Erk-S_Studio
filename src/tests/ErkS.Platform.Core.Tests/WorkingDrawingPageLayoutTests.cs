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
        // A producer still reserving the old 180 × 36 gets the owner's 170 × 34,
        // anchored to the same bottom-right corner.
        Assert.Equal((661d, 550d, 170d, 34d), Rect(regions.TitleBlockArea));
        Assert.Equal(16, regions.GridColumns);
        Assert.Equal(12, regions.GridRows);
    }

    [Fact]
    public void AVerticalCornerTableIsTheCONTRACTS70By90NotTheHorizontalOneTurnedOnItsSide()
    {
        // 🔴 _shared/corner-table-space-contract-2026-09-06.json: the vertical
        // corner table is 70 × 90 mm, read from PFR's own generator and the same
        // in PFA. This layout used to produce 36 × 180 - the horizontal table's
        // two numbers swapped - a rule from the first commit of this file, a month
        // before the contract existed. A producer that reserves the contract's
        // 70 × 90 would have had Studio reserve, and later draw, a strip 36 mm
        // wide and twice as tall: over the drawing on one side, leaving the
        // reserved space half empty on the other.
        var format = new PageFormatDefinition
        {
            Kind = PageFormatKind.WorkingDrawing,
            WidthMm = 297,
            HeightMm = 420,
            DrawingArea = new PageRectMm { X = 20, Y = 10, Width = 267, Height = 400 },
            SheetTitleArea = new PageRectMm { X = 20, Y = 10, Width = 267, Height = 9 },
            // What the producer reserved: the contract's vertical table.
            TitleBlockArea = new PageRectMm { X = 217, Y = 320, Width = 70, Height = 90 },
        };

        WorkingDrawingPageRegions regions = WorkingDrawingPageLayout.Resolve(format);

        // Bottom-right of the inner frame, as the contract's origin formula says:
        // x = gridX + gridWidth - 70, y = gridY + gridHeight - 90.
        Assert.Equal((217d, 320d, 70d, 90d), Rect(regions.TitleBlockArea));
    }

    [Fact]
    public void AHorizontalCornerTableIsTheOwners170By34()
    {
        // The positive control for the test above, and its own correction:
        // the horizontal table was 180 × 36 here too, a size matching no family.
        // PFR's TitleBlockHorizontal.rfa and the owner's sheets are 170 × 34.
        var format = new PageFormatDefinition
        {
            Kind = PageFormatKind.WorkingDrawing,
            WidthMm = 420,
            HeightMm = 297,
            DrawingArea = new PageRectMm { X = 20, Y = 10, Width = 390, Height = 277 },
            TitleBlockArea = new PageRectMm { X = 240, Y = 253, Width = 170, Height = 34 },
        };

        WorkingDrawingPageRegions regions = WorkingDrawingPageLayout.Resolve(format);

        Assert.Equal((240d, 253d, 170d, 34d), Rect(regions.TitleBlockArea));
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
        Assert.Equal((240d, 253d, 170d, 34d), Rect(regions.TitleBlockArea));
        Assert.Equal(8, regions.GridColumns);
        Assert.Equal(6, regions.GridRows);
    }

    private static (double X, double Y, double Width, double Height) Rect(PageRectMm rect) =>
        (rect.X, rect.Y, rect.Width, rect.Height);
}
