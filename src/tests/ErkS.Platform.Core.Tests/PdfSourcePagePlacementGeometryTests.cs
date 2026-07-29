using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

public sealed class PdfSourcePagePlacementGeometryTests
{
    [Fact]
    public void PreservePhysicalSize_CropChangesOnlyTheVisibleRegionAndPosition()
    {
        var crop = new SourcePageCropDefinition
        {
            Enabled = true,
            LeftMm = 94.74,
            TopMm = 25.68,
            RightMm = 90.64,
            BottomMm = 67.94,
            OffsetXmm = -55.78,
            OffsetYmm = 0,
            ScalePercent = 100,
        };
        var target = new PageRectMm
        {
            X = 15,
            Y = 14,
            Width = 400,
            Height = 250,
        };

        PdfSourcePagePlacementMm placement =
            PdfSourcePagePlacementGeometry.Calculate(
                420,
                297,
                target,
                PagePlacementMode.PreservePhysicalSize,
                crop,
                "a3");

        Assert.Equal(234.62, placement.SourceRectangle.Width, 6);
        Assert.Equal(203.38, placement.SourceRectangle.Height, 6);
        Assert.Equal(
            placement.SourceRectangle.Width,
            placement.DestinationRectangle.Width,
            6);
        Assert.Equal(
            placement.SourceRectangle.Height,
            placement.DestinationRectangle.Height,
            6);
        Assert.Equal(41.91, placement.DestinationRectangle.X, 6);
        Assert.Equal(37.31, placement.DestinationRectangle.Y, 6);
        Assert.Equal(420, placement.CompleteSourceDestination.Width, 6);
        Assert.Equal(297, placement.CompleteSourceDestination.Height, 6);
    }

    [Fact]
    public void FitDrawingArea_UsesTheSameCropAndDestinationForPreviewAndWriter()
    {
        var crop = new SourcePageCropDefinition
        {
            Enabled = true,
            LeftMm = 98.21,
            TopMm = 23.25,
            RightMm = 92.72,
            BottomMm = 72.10,
            OffsetXmm = -57.98,
            OffsetYmm = 0,
            ScalePercent = 100,
        };
        var target = new PageRectMm
        {
            X = 15,
            Y = 14,
            Width = 400,
            Height = 250,
        };

        PdfSourcePagePlacementMm placement =
            PdfSourcePagePlacementGeometry.Calculate(
                420,
                297,
                target,
                PagePlacementMode.FitDrawingArea,
                crop,
                "a3");

        Assert.Equal(98.21, placement.SourceRectangle.X, 6);
        Assert.Equal(23.25, placement.SourceRectangle.Y, 6);
        Assert.Equal(229.07, placement.SourceRectangle.Width, 6);
        Assert.Equal(201.65, placement.SourceRectangle.Height, 6);
        Assert.InRange(placement.DestinationRectangle.X, 15, 15.03);
        Assert.Equal(14, placement.DestinationRectangle.Y, 6);
        Assert.Equal(250, placement.DestinationRectangle.Height, 6);

        double scale = placement.DestinationRectangle.Height /
                       placement.SourceRectangle.Height;
        Assert.Equal(
            placement.DestinationRectangle.X,
            placement.CompleteSourceDestination.X +
            placement.SourceRectangle.X * scale,
            6);
        Assert.Equal(
            placement.DestinationRectangle.Y,
            placement.CompleteSourceDestination.Y +
            placement.SourceRectangle.Y * scale,
            6);
    }

    [Fact]
    public void ClampOffsets_KeepsTheFittedCropInsideTheStudioDrawingArea()
    {
        var crop = new SourcePageCropDefinition
        {
            Enabled = true,
            LeftMm = 90,
            TopMm = 20,
            RightMm = 90,
            BottomMm = 70,
            OffsetXmm = -500,
            OffsetYmm = 500,
        };
        var target = new PageRectMm
        {
            X = 15,
            Y = 14,
            Width = 400,
            Height = 250,
        };

        (double x, double y) =
            PdfSourcePagePlacementGeometry.ClampOffsetsToTarget(
                420,
                297,
                target,
                PagePlacementMode.FitDrawingArea,
                crop);

        Assert.InRange(x, -60, 0);
        Assert.Equal(0, y, 6);
    }

    [Fact]
    public void HasCompositionEdits_IncludesPlacementAndMasksWithoutCrop()
    {
        Assert.True(PdfSourcePagePlacementGeometry.HasCompositionEdits(
            new SourcePageCropDefinition { OffsetXmm = 2 }));
        Assert.True(PdfSourcePagePlacementGeometry.HasCompositionEdits(
            new SourcePageCropDefinition
            {
                Masks =
                [
                    new SourcePageMaskDefinition
                    {
                        Shape = SourcePageMaskShape.Rectangle,
                        Points =
                        [
                            new SourcePagePointDefinition { X = 0.1, Y = 0.1 },
                            new SourcePagePointDefinition { X = 0.2, Y = 0.2 },
                        ],
                    },
                ],
            }));
        Assert.False(PdfSourcePagePlacementGeometry.HasCompositionEdits(
            new SourcePageCropDefinition()));
    }
}
