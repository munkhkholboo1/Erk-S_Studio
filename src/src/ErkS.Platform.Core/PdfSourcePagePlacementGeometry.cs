namespace ErkS.Platform.Core;

/// <summary>
/// Shared top-left, millimeter geometry for a PDF source page placed on a
/// Studio sheet. The editor preview and the PDF writer must both use this
/// calculation so the saved result matches what the user positioned.
/// </summary>
public sealed record PdfSourcePagePlacementMm(
    PageRectMm SourceRectangle,
    PageRectMm DestinationRectangle,
    PageRectMm CompleteSourceDestination,
    double RotationDegrees);

public static class PdfSourcePagePlacementGeometry
{
    private const double PreserveSizeToleranceMm = 0.75;

    public static PdfSourcePagePlacementMm Calculate(
        double sourceWidthMm,
        double sourceHeightMm,
        PageRectMm target,
        PagePlacementMode placementMode,
        SourcePageCropDefinition? crop,
        string formatId = "")
    {
        sourceWidthMm = Math.Max(0.01, Finite(sourceWidthMm, 0.01));
        sourceHeightMm = Math.Max(0.01, Finite(sourceHeightMm, 0.01));
        PageRectMm source = ResolveSourceRectangle(sourceWidthMm, sourceHeightMm, crop);
        double width;
        double height;

        if (placementMode == PagePlacementMode.PreserveDrawingSpace)
        {
            if (Math.Abs(target.Width - source.Width) > PreserveSizeToleranceMm ||
                Math.Abs(target.Height - source.Height) > PreserveSizeToleranceMm)
            {
                throw new InvalidDataException(
                    $"Clean drawing-space PDF is {source.Width:0.##} x " +
                    $"{source.Height:0.##} mm, but format '{formatId}' requires " +
                    $"{target.Width:0.##} x {target.Height:0.##} mm. " +
                    "The source was not resized.");
            }

            width = source.Width;
            height = source.Height;
        }
        else if (placementMode == PagePlacementMode.PreservePhysicalSize)
        {
            width = source.Width;
            height = source.Height;
        }
        else
        {
            double scaleX = target.Width / source.Width;
            double scaleY = target.Height / source.Height;
            double scale = placementMode == PagePlacementMode.FillCrop
                ? Math.Max(scaleX, scaleY)
                : Math.Min(scaleX, scaleY);
            width = source.Width * scale;
            height = source.Height * scale;
        }

        double adjustmentScale =
            placementMode is PagePlacementMode.PreserveDrawingSpace or
                PagePlacementMode.PreservePhysicalSize
                ? 1
                : ResolveScalePercent(crop) / 100;
        width *= adjustmentScale;
        height *= adjustmentScale;

        var destination = new PageRectMm
        {
            X = target.X + (target.Width - width) / 2 + Finite(crop?.OffsetXmm),
            Y = target.Y + (target.Height - height) / 2 + Finite(crop?.OffsetYmm),
            Width = width,
            Height = height,
        };
        double scaleToDestinationX = destination.Width / source.Width;
        double scaleToDestinationY = destination.Height / source.Height;
        var completeSourceDestination = new PageRectMm
        {
            X = destination.X - source.X * scaleToDestinationX,
            Y = destination.Y - source.Y * scaleToDestinationY,
            Width = sourceWidthMm * scaleToDestinationX,
            Height = sourceHeightMm * scaleToDestinationY,
        };

        return new PdfSourcePagePlacementMm(
            source,
            destination,
            completeSourceDestination,
            NormalizeRotation(crop?.RotationDegrees));
    }

    public static (double OffsetXmm, double OffsetYmm) ClampOffsetsToTarget(
        double sourceWidthMm,
        double sourceHeightMm,
        PageRectMm target,
        PagePlacementMode placementMode,
        SourcePageCropDefinition crop,
        string formatId = "")
    {
        SourcePageCropDefinition centered = crop.DeepClone();
        centered.OffsetXmm = 0;
        centered.OffsetYmm = 0;
        PdfSourcePagePlacementMm placement = Calculate(
            sourceWidthMm,
            sourceHeightMm,
            target,
            placementMode,
            centered,
            formatId);
        double maximumX = Math.Max(0, (target.Width - placement.DestinationRectangle.Width) / 2);
        double maximumY = Math.Max(0, (target.Height - placement.DestinationRectangle.Height) / 2);
        return (
            Math.Clamp(Finite(crop.OffsetXmm), -maximumX, maximumX),
            Math.Clamp(Finite(crop.OffsetYmm), -maximumY, maximumY));
    }

    public static bool HasCompositionEdits(SourcePageCropDefinition? crop)
    {
        if (crop is null)
            return false;

        return crop.Enabled ||
               Math.Abs(Finite(crop.OffsetXmm)) > 0.0001 ||
               Math.Abs(Finite(crop.OffsetYmm)) > 0.0001 ||
               Math.Abs(ResolveScalePercent(crop) - 100) > 0.0001 ||
               Math.Abs(NormalizeRotation(crop.RotationDegrees)) > 0.0001 ||
               (crop.Masks?.Any(mask => mask is not null &&
                    mask.Points is not null &&
                    mask.Points.Count >=
                        (mask.Shape == SourcePageMaskShape.Rectangle ? 2 : 3)) ?? false);
    }

    private static PageRectMm ResolveSourceRectangle(
        double sourceWidthMm,
        double sourceHeightMm,
        SourcePageCropDefinition? crop)
    {
        if (crop is not { Enabled: true })
        {
            return new PageRectMm
            {
                Width = sourceWidthMm,
                Height = sourceHeightMm,
            };
        }

        double left = Math.Max(0, Finite(crop.LeftMm));
        double top = Math.Max(0, Finite(crop.TopMm));
        double right = Math.Max(0, Finite(crop.RightMm));
        double bottom = Math.Max(0, Finite(crop.BottomMm));
        double width = sourceWidthMm - left - right;
        double height = sourceHeightMm - top - bottom;
        if (width <= 0.01 || height <= 0.01)
        {
            throw new InvalidDataException(
                "PDF source crop removes the complete page. Reduce the crop margins.");
        }

        return new PageRectMm
        {
            X = left,
            Y = top,
            Width = width,
            Height = height,
        };
    }

    private static double ResolveScalePercent(SourcePageCropDefinition? crop)
    {
        double value = Finite(crop?.ScalePercent, 100);
        return value <= 0 ? 100 : Math.Clamp(value, 5, 1000);
    }

    private static double NormalizeRotation(double? value)
    {
        double rotation = Finite(value);
        rotation %= 360;
        if (rotation > 180)
            rotation -= 360;
        if (rotation < -180)
            rotation += 360;
        return rotation;
    }

    private static double Finite(double? value, double fallback = 0) =>
        value is double number && double.IsFinite(number) ? number : fallback;
}
