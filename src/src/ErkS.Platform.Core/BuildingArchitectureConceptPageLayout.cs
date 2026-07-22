namespace ErkS.Platform.Core;

public readonly record struct BuildingArchitectureConceptPageRegions(
    PageRectMm Frame,
    PageRectMm InformationArea,
    PageRectMm ApprovalRoleArea,
    PageRectMm ApprovalNameArea,
    PageRectMm DescriptionArea,
    PageRectMm SheetTitleArea,
    PageRectMm DrawingArea,
    PageRectMm TitleBlockArea);

public readonly record struct BuildingArchitectureConceptCornerGrid(
    double X0,
    double X1,
    double X2,
    double X3,
    double X4,
    double X5,
    double Y0,
    double Y1,
    double Y2,
    double Y3,
    double Y4);

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

    // Revit exports only the clean drawing-space geometry. These measurements
    // define Studio's visible page chrome for every orientation and bind edge.
    public const double NormalMarginMm = 5.0;
    public const double BindMarginMm = 15.0;
    public const double SheetTitleHeightMm = 9.0;
    public const double TitleBlockWidthMm = 190.0;
    public const double TitleBlockHeightMm = 28.0;
    public const double ElevationRoleColumnOffsetMm = 110.0;
    public const double ElevationApprovalPanelOffsetMm = 165.0;

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

    public const double SiteContextPanelGapMm = 2.0;
    public const double SiteContextPanelTitleHeightMm = 10.0;
    public const double SiteContextPanelWidthMm =
        (FrameRightMm - FrameLeftMm - SiteContextPanelGapMm) * 0.5;

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

    public static PageRectMm SiteContextLocationPanel => Rect(
        FrameLeftMm,
        SheetHeaderBottomMm,
        SiteContextPanelWidthMm,
        ContentBottomMm - SheetHeaderBottomMm);

    public static PageRectMm SiteContextOverviewPanel => Rect(
        FrameLeftMm + SiteContextPanelWidthMm + SiteContextPanelGapMm,
        SheetHeaderBottomMm,
        SiteContextPanelWidthMm,
        ContentBottomMm - SheetHeaderBottomMm);

    public static PageRectMm SiteContextLocationMapArea => Rect(
        SiteContextLocationPanel.X,
        SiteContextLocationPanel.Y + SiteContextPanelTitleHeightMm,
        SiteContextLocationPanel.Width,
        SiteContextLocationPanel.Height - SiteContextPanelTitleHeightMm);

    public static PageRectMm SiteContextOverviewMapArea => Rect(
        SiteContextOverviewPanel.X,
        SiteContextOverviewPanel.Y + SiteContextPanelTitleHeightMm,
        SiteContextOverviewPanel.Width,
        SiteContextOverviewPanel.Height - SiteContextPanelTitleHeightMm);

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

    public static bool SupportsStudioChrome(PageFormatDefinition format) =>
        format.Kind == PageFormatKind.Concept &&
        format.WidthMm > 0 &&
        format.HeightMm > 0 &&
        format.DrawingArea.Width > 0 &&
        format.DrawingArea.Height > 0 &&
        format.SheetTitleArea.Width > 0 &&
        format.SheetTitleArea.Height > 0 &&
        format.TitleBlockArea.Width > 0 &&
        format.TitleBlockArea.Height > 0;

    public static BuildingArchitectureConceptPageRegions Calculate(
        double pageWidthMm,
        double pageHeightMm,
        string? bindEdge,
        bool includeInformationHeader = false)
    {
        string edge = (bindEdge ?? "LEFT").Trim().ToUpperInvariant();
        double left = edge == "LEFT" ? BindMarginMm : NormalMarginMm;
        double top = edge == "TOP" ? BindMarginMm : NormalMarginMm;
        double right = edge == "RIGHT" ? BindMarginMm : NormalMarginMm;
        double bottom = edge == "BOTTOM" ? BindMarginMm : NormalMarginMm;

        double frameWidth = Math.Max(0.0, pageWidthMm - left - right);
        double frameHeight = Math.Max(0.0, pageHeightMm - top - bottom);
        double informationHeight = includeInformationHeader
            ? Math.Min(ElevationInformationHeightMm, frameHeight)
            : 0.0;
        double titleHeight = Math.Min(
            SheetTitleHeightMm,
            Math.Max(0.0, frameHeight - informationHeight));
        double cornerHeight = Math.Min(
            TitleBlockHeightMm,
            Math.Max(0.0, frameHeight - titleHeight));
        double cornerWidth = Math.Min(TitleBlockWidthMm, frameWidth);
        double drawingHeight = Math.Max(
            0.0,
            frameHeight - informationHeight - titleHeight - cornerHeight);
        double roleWidth = includeInformationHeader
            ? Math.Min(ElevationRoleColumnOffsetMm, frameWidth)
            : 0.0;
        double approvalWidth = includeInformationHeader
            ? Math.Min(
                Math.Max(0.0, ElevationApprovalPanelOffsetMm - ElevationRoleColumnOffsetMm),
                Math.Max(0.0, frameWidth - roleWidth))
            : 0.0;
        double descriptionWidth = Math.Max(0.0, frameWidth - roleWidth - approvalWidth);

        return new BuildingArchitectureConceptPageRegions(
            Rect(left, top, frameWidth, frameHeight),
            Rect(left, top, frameWidth, informationHeight),
            Rect(left, top, roleWidth, informationHeight),
            Rect(left + roleWidth, top, approvalWidth, informationHeight),
            Rect(left + roleWidth + approvalWidth, top, descriptionWidth, informationHeight),
            Rect(left, top + informationHeight, frameWidth, titleHeight),
            Rect(left, top + informationHeight + titleHeight, frameWidth, drawingHeight),
            Rect(
                left + frameWidth - cornerWidth,
                top + frameHeight - cornerHeight,
                cornerWidth,
                cornerHeight));
    }

    public static BuildingArchitectureConceptPageRegions ResolveRegions(
        PageFormatDefinition format,
        bool includeInformationHeader)
    {
        BuildingArchitectureConceptPageRegions calculated = Calculate(
            format.WidthMm,
            format.HeightMm,
            format.BindEdge,
            includeInformationHeader);

        return calculated with
        {
            SheetTitleArea = IsPositive(format.SheetTitleArea)
                ? format.SheetTitleArea
                : calculated.SheetTitleArea,
            DrawingArea = IsPositive(format.DrawingArea)
                ? format.DrawingArea
                : calculated.DrawingArea,
            TitleBlockArea = IsPositive(format.TitleBlockArea)
                ? format.TitleBlockArea
                : calculated.TitleBlockArea,
        };
    }

    public static BuildingArchitectureConceptCornerGrid ResolveCornerGrid(PageRectMm area)
    {
        double widthScale = area.Width / TitleBlockWidthMm;
        double heightScale = area.Height / TitleBlockHeightMm;
        double X(double offset) => area.X + offset * widthScale;
        double Y(double offset) => area.Y + offset * heightScale;

        return new BuildingArchitectureConceptCornerGrid(
            X(0),
            X(32),
            X(106),
            X(140),
            X(170),
            X(190),
            Y(0),
            Y(7),
            Y(14),
            Y(21),
            Y(28));
    }

    public static bool IsElevationSheet(
        string? contentKind,
        string? sheetName = null,
        string? templateSlotId = null)
    {
        if (string.Equals(templateSlotId, "elevations", StringComparison.OrdinalIgnoreCase))
            return true;

        return IsElevationText(contentKind) || IsElevationText(sheetName);
    }

    public static bool UsesInformationHeader(
        string? contentKind,
        string? sheetName = null,
        string? templateSlotId = null) =>
        IsElevationSheet(contentKind, sheetName, templateSlotId) ||
        string.Equals(templateSlotId, "master-plan", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(
            sheetName?.Trim(),
            "ЕРӨНХИЙ ТӨЛӨВЛӨГӨӨ",
            StringComparison.OrdinalIgnoreCase);

    public static PageFormatDefinition ApplyElevationGeometry(PageFormatDefinition source)
    {
        if (!SupportsStudioChrome(source))
            return source;

        BuildingArchitectureConceptPageRegions regions = Calculate(
            source.WidthMm,
            source.HeightMm,
            source.BindEdge,
            includeInformationHeader: true);
        bool isLandscapeElevation = string.Equals(
            source.Id,
            PageFormatCatalog.ConceptElevationA3LandscapeId,
            StringComparison.OrdinalIgnoreCase);
        bool isPortraitElevation = string.Equals(
            source.Id,
            PageFormatCatalog.ConceptElevationA3PortraitTopId,
            StringComparison.OrdinalIgnoreCase);
        string id = source.Id;
        if (string.Equals(
                source.Id,
                PageFormatCatalog.ConceptA3LandscapeId,
                StringComparison.OrdinalIgnoreCase))
        {
            id = PageFormatCatalog.ConceptElevationA3LandscapeId;
        }
        else if (string.Equals(
                     source.Id,
                     PageFormatCatalog.ConceptA3PortraitTopId,
                     StringComparison.OrdinalIgnoreCase))
        {
            id = PageFormatCatalog.ConceptElevationA3PortraitTopId;
        }

        return new PageFormatDefinition
        {
            SpecVersion = source.SpecVersion,
            Id = id,
            Name = source.Name,
            Kind = source.Kind,
            Code = source.Code,
            Orientation = source.Orientation,
            BindEdge = source.BindEdge,
            WidthMm = source.WidthMm,
            HeightMm = source.HeightMm,
            DrawingArea = regions.DrawingArea,
            SheetTitleArea = regions.SheetTitleArea,
            TitleBlockArea = regions.TitleBlockArea,
            ShowBorder = source.ShowBorder,
            ShowGrid = source.ShowGrid,
            Revision = Math.Max(source.Revision, 4),
            ModuleColumns = source.ModuleColumns,
            ModuleRows = source.ModuleRows,
            HasHalfModule = source.HasHalfModule,
            // Geometry changed from the source snapshot, so its hash no longer
            // describes this page. A producer-supplied elevation snapshot keeps
            // its own hash because it already resolves to this geometry.
            GeometryHash = isLandscapeElevation || isPortraitElevation
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

    private static bool IsPositive(PageRectMm area) =>
        area.Width > 0 && area.Height > 0;

    private static PageRectMm Rect(double x, double y, double width, double height) => new()
    {
        X = x,
        Y = y,
        Width = width,
        Height = height,
    };
}
