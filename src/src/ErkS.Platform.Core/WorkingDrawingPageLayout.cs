namespace ErkS.Platform.Core;

public sealed record WorkingDrawingPageRegions(
    PageRectMm EtalonOuterFrame,
    PageRectMm EtalonInnerFrame,
    PageRectMm SheetTitleArea,
    PageRectMm TitleBlockArea,
    int GridColumns,
    int GridRows);

public sealed record WorkingDrawingGeneratedPageChrome(
    bool ShowSheetHeader,
    bool ShowTitleBlock);

public static class WorkingDrawingGeneratedPageChromePolicy
{
    public static WorkingDrawingGeneratedPageChrome Resolve(
        AlbumGeneratedPageKind pageKind) => pageKind == AlbumGeneratedPageKind.Cover
        ? new WorkingDrawingGeneratedPageChrome(
            ShowSheetHeader: false,
            ShowTitleBlock: false)
        : new WorkingDrawingGeneratedPageChrome(
            ShowSheetHeader: true,
            ShowTitleBlock: true);
}

public static class WorkingDrawingPageLayout
{
    public const double EtalonBandMm = 5d;
    /// <summary>
    /// The horizontal corner table: 170 × 34 mm - PFR's TitleBlockHorizontal.rfa,
    /// confirmed by the owner and measured on the owner's own sheets (Eco Garden
    /// AZ, Grand Mark AZ 2026: x 239 → 409 on A3). It was 180 × 36, a size from
    /// the 2026-09-06 space contract that matched no family anyone places.
    /// </summary>
    public const double HorizontalTitleBlockWidthMm = 170d;
    public const double HorizontalTitleBlockHeightMm = 34d;

    /// <summary>
    /// The vertical corner table: 70 × 90 mm, from
    /// <c>_shared/corner-table-space-contract-2026-09-06.json</c>.
    /// </summary>
    /// <remarks>
    /// Its own pair of numbers, NOT the horizontal table's two swapped. That swap
    /// was this file's rule from its first commit, a month before the contract
    /// existed, and it gave 36 × 180 - a strip over the drawing on one side and a
    /// reserved space left half empty on the other.
    /// </remarks>
    public const double VerticalTitleBlockWidthMm = 70d;
    public const double VerticalTitleBlockHeightMm = 90d;
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
        double width = Math.Min(vertical ? VerticalTitleBlockWidthMm : HorizontalTitleBlockWidthMm, inner.Width);
        double height = Math.Min(vertical ? VerticalTitleBlockHeightMm : HorizontalTitleBlockHeightMm, inner.Height);
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
