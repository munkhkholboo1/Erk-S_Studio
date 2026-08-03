namespace ErkS.Platform.Core;

public sealed record WorkingDrawingPageRegions(
    PageRectMm EtalonOuterFrame,
    PageRectMm EtalonInnerFrame,
    PageRectMm SheetTitleArea,
    PageRectMm TitleBlockArea,
    int GridColumns,
    int GridRows);

public static class WorkingDrawingPageLayout
{
    public const double EtalonBandMm = 5d;
    public const double HorizontalTitleBlockWidthMm = 180d;
    public const double HorizontalTitleBlockHeightMm = 36d;
    public const double SheetTitleWidthMm = 90d;
    public const double SheetTitleHeightMm = 24d;

    public static WorkingDrawingPageRegions Resolve(PageFormatDefinition format)
    {
        ArgumentNullException.ThrowIfNull(format);
        PageRectMm inner = Clone(format.DrawingArea);
        PageRectMm outer = new()
        {
            X = Math.Max(0, inner.X - EtalonBandMm),
            Y = Math.Max(0, inner.Y - EtalonBandMm),
            Width = Math.Min(format.WidthMm, inner.X + inner.Width + EtalonBandMm) -
                    Math.Max(0, inner.X - EtalonBandMm),
            Height = Math.Min(format.HeightMm, inner.Y + inner.Height + EtalonBandMm) -
                     Math.Max(0, inner.Y - EtalonBandMm),
        };
        PageRectMm corner = IsPositive(format.TitleBlockArea)
            ? NormalizeCornerTable(format.TitleBlockArea, inner)
            : new PageRectMm
            {
                Width = Math.Min(HorizontalTitleBlockWidthMm, inner.Width),
                Height = Math.Min(HorizontalTitleBlockHeightMm, inner.Height),
                X = inner.X + inner.Width - Math.Min(HorizontalTitleBlockWidthMm, inner.Width),
                Y = inner.Y + inner.Height - Math.Min(HorizontalTitleBlockHeightMm, inner.Height),
            };
        // Revit's BlueprintSheetHeader is a separate annotation across the
        // top of the drawing field. It is not a cell inside the corner table.
        PageRectMm title = IsPositive(format.SheetTitleArea)
            ? Clone(format.SheetTitleArea)
            : new PageRectMm
            {
                X = inner.X,
                Y = inner.Y,
                Width = inner.Width,
                Height = Math.Min(9d, inner.Height),
            };
        return new WorkingDrawingPageRegions(
            outer,
            inner,
            title,
            corner,
            ResolveGridDivision(format.WidthMm),
            ResolveGridDivision(format.HeightMm));
    }

    private static PageRectMm NormalizeCornerTable(PageRectMm source, PageRectMm inner)
    {
        bool vertical = source.Height > source.Width;
        double width = Math.Min(vertical ? HorizontalTitleBlockHeightMm : HorizontalTitleBlockWidthMm, inner.Width);
        double height = Math.Min(vertical ? HorizontalTitleBlockWidthMm : HorizontalTitleBlockHeightMm, inner.Height);
        return new PageRectMm
        {
            X = inner.X + inner.Width - width,
            Y = inner.Y + inner.Height - height,
            Width = width,
            Height = height,
        };
    }

    private static int ResolveGridDivision(double paperLengthMm) =>
        Math.Max(2, (int)(2d * Math.Round(paperLengthMm / 100d, MidpointRounding.AwayFromZero)));

    private static bool IsPositive(PageRectMm? rect) =>
        rect is { Width: > 0, Height: > 0 };

    private static PageRectMm Clone(PageRectMm rect) => new()
    {
        X = rect.X,
        Y = rect.Y,
        Width = rect.Width,
        Height = rect.Height,
    };
}
