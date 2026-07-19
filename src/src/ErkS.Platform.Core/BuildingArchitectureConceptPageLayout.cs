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
    public const double CornerTextHeightMm = 2.5;
    public const double CornerMinimumTextHeightMm = 1.25;
    public const double CornerLineHeightFactor = 1.15;
    public const double ArialCapHeightRatio = 0.72;
    public const double CoverLineHeightFactor = 1.35;

    // Canonical cover approval table. PDF export and the live Studio preview
    // must use these same boundaries so the cover never changes between views.
    public const double CoverTableLeftMm = 68.275;
    public const double CoverReviewRoleRightMm = 131.275;
    public const double CoverReviewNameRightMm = 171.275;
    public const double CoverProcessedLeftMm = 196.275;
    public const double CoverProcessedLogoRightMm = 226.275;
    public const double CoverProcessedRoleRightMm = 284.975;
    public const double CoverProcessedNameRightMm = 326.725;
    public const double CoverTableRightMm = 351.725;
    public const double CoverTableTopMm = 161.86;
    public const double CoverColumnHeaderBottomMm = 153.86;
    public const double CoverReviewRowsBaseHeightMm = 60.0;
    public const double CoverClientDataBaseHeightMm = 26.0;
    public const double CoverSectionHeaderHeightMm = 8.0;
    public const double CoverCompanyDataBaseHeightMm = 26.0;
    public const string CoverProcessedTopSectionTitle = "Гүйцэтгэгч";
    public const string CoverProcessedBottomSectionTitle = "Захиалагч";

    public const double PageWidthMm = 420.0;
    public const double PageHeightMm = 297.0;

    public const double FrameLeftMm = 15.0;
    public const double FrameTopMm = 5.0;
    public const double FrameRightMm = 415.0;
    public const double FrameBottomMm = 292.0;

    public const double SheetHeaderBottomMm = 14.0;
    public const double ContentBottomMm = 264.0;

    // Facade sheets reserve a project-information band before the ordinary
    // sheet-name band. These measurements are shared with the Revit guide.
    public const double ElevationInformationHeightMm = 55.0;
    public const double ElevationInformationBottomMm = FrameTopMm + ElevationInformationHeightMm;
    public const double ElevationSheetHeaderBottomMm = ElevationInformationBottomMm +
        (SheetHeaderBottomMm - FrameTopMm);
    public const double ElevationRoleColumnRightMm = 125.0;
    public const double ElevationApprovalPanelRightMm = 180.0;

    // The role/name boundary controls text layout only. The description panel
    // is the sole visible vertical partition in the information band.
    public static IReadOnlyList<double> ElevationInformationDividerXMm { get; } =
        new[] { ElevationApprovalPanelRightMm };

    // Studio owns this table. It is intentionally a little larger than the
    // original 184 x 28 mm table while remaining anchored to the frame's
    // bottom-right corner.
    public const double CornerX0Mm = 225.0;
    public const double CornerX1Mm = 257.0;
    public const double CornerX2Mm = 331.0;
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

    public static PageRectMm ElevationInformationArea => Rect(
        FrameLeftMm,
        FrameTopMm,
        FrameRightMm - FrameLeftMm,
        ElevationInformationHeightMm);

    public static PageRectMm ElevationApprovalRoleArea => Rect(
        FrameLeftMm,
        FrameTopMm,
        ElevationRoleColumnRightMm - FrameLeftMm,
        ElevationInformationHeightMm);

    public static PageRectMm ElevationApprovalNameArea => Rect(
        ElevationRoleColumnRightMm,
        FrameTopMm,
        ElevationApprovalPanelRightMm - ElevationRoleColumnRightMm,
        ElevationInformationHeightMm);

    public static PageRectMm ElevationDescriptionArea => Rect(
        ElevationApprovalPanelRightMm,
        FrameTopMm,
        FrameRightMm - ElevationApprovalPanelRightMm,
        ElevationInformationHeightMm);

    public static PageRectMm ElevationSheetTitleArea => Rect(
        FrameLeftMm,
        ElevationInformationBottomMm,
        FrameRightMm - FrameLeftMm,
        ElevationSheetHeaderBottomMm - ElevationInformationBottomMm);

    public static PageRectMm ElevationDrawingArea => Rect(
        FrameLeftMm,
        ElevationSheetHeaderBottomMm,
        FrameRightMm - FrameLeftMm,
        ContentBottomMm - ElevationSheetHeaderBottomMm);

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

    public static bool IsElevationSheet(
        string? contentKind,
        string? sheetName = null,
        string? templateSlotId = null)
    {
        if (string.Equals(templateSlotId, "elevations", StringComparison.OrdinalIgnoreCase))
            return true;

        return IsElevationText(contentKind) || IsElevationText(sheetName);
    }

    public static PageFormatDefinition ApplyElevationGeometry(PageFormatDefinition source)
    {
        if (!IsCanonical(source))
            return source;

        return new PageFormatDefinition
        {
            SpecVersion = source.SpecVersion,
            Id = string.Equals(source.Id, PageFormatCatalog.ConceptA3LandscapeId, StringComparison.OrdinalIgnoreCase)
                ? PageFormatCatalog.ConceptElevationA3LandscapeId
                : source.Id,
            Name = source.Name,
            Kind = source.Kind,
            Code = source.Code,
            Orientation = source.Orientation,
            BindEdge = source.BindEdge,
            WidthMm = source.WidthMm,
            HeightMm = source.HeightMm,
            DrawingArea = ElevationDrawingArea,
            SheetTitleArea = ElevationSheetTitleArea,
            TitleBlockArea = BuildingArchitectureConceptPageLayout.TitleBlockArea,
            ShowBorder = source.ShowBorder,
            ShowGrid = source.ShowGrid,
            Revision = Math.Max(source.Revision, 4),
            ModuleColumns = source.ModuleColumns,
            ModuleRows = source.ModuleRows,
            HasHalfModule = source.HasHalfModule,
            // Geometry changed from the source snapshot, so its hash no longer
            // describes this page. A producer-supplied elevation snapshot keeps
            // its own hash because it already resolves to this geometry.
            GeometryHash = string.Equals(
                source.Id,
                PageFormatCatalog.ConceptElevationA3LandscapeId,
                StringComparison.OrdinalIgnoreCase)
                ? source.GeometryHash
                : "",
        };
    }

    private static bool IsElevationText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        string text = value.Trim();
        return text.Contains("НҮҮР ТАЛ", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Нүүр тал", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("elevation", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("facade", StringComparison.OrdinalIgnoreCase);
    }

    private static PageRectMm Rect(double x, double y, double width, double height) => new()
    {
        X = x,
        Y = y,
        Width = width,
        Height = height,
    };
}
