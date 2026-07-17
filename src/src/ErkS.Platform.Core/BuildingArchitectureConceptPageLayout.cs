namespace ErkS.Platform.Core;

/// <summary>
/// Canonical A3 landscape geometry used by Erk-S for Revit sketch sheets.
/// Studio owns the visible frame, sheet header and corner table; authoring
/// integrations provide only content for <see cref="DrawingArea"/>.
/// </summary>
public static class BuildingArchitectureConceptPageLayout
{
    public const string FontFamilyName = "Arial";
    public const double CoverBodyTextHeightMm = 2.5;
    public const double CoverProjectNameTextHeightMm = 4.0;
    public const double ArialCapHeightRatio = 0.72;
    public const double CoverLineHeightFactor = 1.35;

    public const double PageWidthMm = 420.0;
    public const double PageHeightMm = 297.0;

    public const double FrameLeftMm = 15.0;
    public const double FrameTopMm = 5.0;
    public const double FrameRightMm = 415.0;
    public const double FrameBottomMm = 292.0;

    public const double SheetHeaderBottomMm = 14.0;
    public const double ContentBottomMm = 264.0;

    public const double CornerX0Mm = 231.0;
    public const double CornerX1Mm = 259.0;
    public const double CornerX2Mm = 335.0;
    public const double CornerX3Mm = 365.0;
    public const double CornerX4Mm = 395.0;
    public const double CornerX5Mm = 415.0;

    public const double CornerY0Mm = 264.0;
    public const double CornerY1Mm = 271.0;
    public const double CornerY2Mm = 278.0;
    public const double CornerY3Mm = 285.0;
    public const double CornerY4Mm = 292.0;

    public static PageRectMm Frame => Rect(
        FrameLeftMm,
        FrameTopMm,
        FrameRightMm - FrameLeftMm,
        FrameBottomMm - FrameTopMm);

    public static PageRectMm SheetTitleArea => Rect(
        FrameLeftMm,
        FrameTopMm,
        FrameRightMm - FrameLeftMm,
        SheetHeaderBottomMm - FrameTopMm);

    public static PageRectMm DrawingArea => Rect(
        FrameLeftMm,
        SheetHeaderBottomMm,
        FrameRightMm - FrameLeftMm,
        ContentBottomMm - SheetHeaderBottomMm);

    public static PageRectMm TitleBlockArea => Rect(
        CornerX0Mm,
        CornerY0Mm,
        CornerX5Mm - CornerX0Mm,
        CornerY4Mm - CornerY0Mm);

    /// <summary>
    /// Converts Revit-family coordinates (origin at bottom-left) to Studio/PDF
    /// coordinates (origin at top-left).
    /// </summary>
    public static PageRectMm FromBottomLeft(
        double x0Mm,
        double y0Mm,
        double x1Mm,
        double y1Mm) => Rect(
            x0Mm,
            PageHeightMm - y1Mm,
            x1Mm - x0Mm,
            y1Mm - y0Mm);

    public static PageRectMm CenteredFromBottomLeft(
        double centerXMm,
        double centerYMm,
        double widthMm,
        double heightMm) => FromBottomLeft(
            centerXMm - widthMm * 0.5,
            centerYMm - heightMm * 0.5,
            centerXMm + widthMm * 0.5,
            centerYMm + heightMm * 0.5);

    public static bool IsCanonical(PageFormatDefinition format) =>
        format.Kind == PageFormatKind.Concept &&
        Math.Abs(format.WidthMm - PageWidthMm) <= 0.5 &&
        Math.Abs(format.HeightMm - PageHeightMm) <= 0.5 &&
        (string.IsNullOrWhiteSpace(format.BindEdge) ||
         string.Equals(format.BindEdge, "LEFT", StringComparison.OrdinalIgnoreCase));

    private static PageRectMm Rect(double x, double y, double width, double height) => new()
    {
        X = x,
        Y = y,
        Width = width,
        Height = height,
    };
}
