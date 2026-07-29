using ErkS.Platform.Contracts;

namespace ErkS.Platform.Core;

public sealed class PageRectMm
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
}

public enum PageFormatKind
{
    SourceAsIs,
    WorkingDrawing,
    Concept,
    Document,
    Cover,
}

/// <summary>
/// Lightweight Studio page geometry. It describes printable zones and fields,
/// not CAD entities, so one format can serve Revit, AutoCAD and CityGen.
/// </summary>
public sealed class PageFormatDefinition
{
    public int SpecVersion { get; set; } = 1;
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public PageFormatKind Kind { get; set; }
    public string Code { get; set; } = "";
    public string Orientation { get; set; } = "";
    public string BindEdge { get; set; } = "";
    public double WidthMm { get; set; }
    public double HeightMm { get; set; }
    public PageRectMm DrawingArea { get; set; } = new();
    public PageRectMm SheetTitleArea { get; set; } = new();
    public PageRectMm TitleBlockArea { get; set; } = new();
    public bool ShowBorder { get; set; } = true;
    public bool ShowGrid { get; set; }
    public int Revision { get; set; } = 1;
    public int ModuleColumns { get; set; }
    public int ModuleRows { get; set; }
    public bool HasHalfModule { get; set; }
    public string GeometryHash { get; set; } = "";

    public override string ToString() => Name;
}

public static class PageFormatCatalog
{
    public const string SourceAsIsId = "source-as-is";
    public const string WorkingDrawingA3LandscapeId = "erks-working-a3-landscape";
    public const string ConceptA3LandscapeId = "erks-concept-a3-landscape";
    public const string ConceptElevationA3LandscapeId = "erks-concept-elevation-a3-landscape";
    public const string ConceptA3PortraitTopId = "erks-concept-a3-portrait-top";
    public const string ConceptElevationA3PortraitTopId = "erks-concept-elevation-a3-portrait-top";
    public const string DocumentA4PortraitId = "erks-document-a4-portrait";

    public static IReadOnlyList<PageFormatDefinition> All { get; } =
    [
        new()
        {
            Id = SourceAsIsId,
            Name = "Эх PDF хэмжээгээр",
            Kind = PageFormatKind.SourceAsIs,
        },
        new()
        {
            Id = WorkingDrawingA3LandscapeId,
            Name = "Ажлын зураг - A3 хэвтээ",
            Kind = PageFormatKind.WorkingDrawing,
            WidthMm = 420,
            HeightMm = 297,
            DrawingArea = new PageRectMm { X = 10, Y = 10, Width = 340, Height = 277 },
            TitleBlockArea = new PageRectMm { X = 350, Y = 187, Width = 60, Height = 100 },
            ShowGrid = true,
        },
        new()
        {
            Id = ConceptA3LandscapeId,
            Name = "Загвар зураг - A3 хэвтээ, зүүн нуруулдах",
            Kind = PageFormatKind.Concept,
            Code = "A3",
            Orientation = "LANDSCAPE",
            BindEdge = "LEFT",
            WidthMm = BuildingArchitectureConceptPageLayout.PageWidthMm,
            HeightMm = BuildingArchitectureConceptPageLayout.PageHeightMm,
            DrawingArea = BuildingArchitectureConceptPageLayout.DrawingArea,
            SheetTitleArea = BuildingArchitectureConceptPageLayout.SheetTitleArea,
            TitleBlockArea = BuildingArchitectureConceptPageLayout.TitleBlockArea,
            Revision = 3,
        },
        new()
        {
            Id = ConceptElevationA3LandscapeId,
            Name = "Загвар зураг - Нүүр тал, A3 хэвтээ",
            Kind = PageFormatKind.Concept,
            Code = "A3",
            Orientation = "LANDSCAPE",
            BindEdge = "LEFT",
            WidthMm = BuildingArchitectureConceptPageLayout.PageWidthMm,
            HeightMm = BuildingArchitectureConceptPageLayout.PageHeightMm,
            DrawingArea = BuildingArchitectureConceptPageLayout.ElevationDrawingArea,
            SheetTitleArea = BuildingArchitectureConceptPageLayout.ElevationSheetTitleArea,
            TitleBlockArea = BuildingArchitectureConceptPageLayout.TitleBlockArea,
            Revision = 4,
        },
        CreateConceptA3Portrait(includeInformationHeader: false),
        CreateConceptA3Portrait(includeInformationHeader: true),
        new()
        {
            Id = DocumentA4PortraitId,
            Name = "Баримт бичиг - A4 босоо",
            Kind = PageFormatKind.Document,
            WidthMm = 210,
            HeightMm = 297,
            DrawingArea = new PageRectMm { X = 18, Y = 15, Width = 174, Height = 252 },
            TitleBlockArea = new PageRectMm { X = 18, Y = 272, Width = 174, Height = 15 },
        },
    ];

    public static PageFormatDefinition Resolve(string? id) =>
        All.FirstOrDefault(format => string.Equals(format.Id, id, StringComparison.OrdinalIgnoreCase))
        ?? All[0];

    public static PageFormatDefinition Resolve(AlbumPageDefinition definition) =>
        IsUsable(definition.PageFormatSnapshot)
            ? definition.PageFormatSnapshot!
            : Resolve(definition.PageFormatId);

    public static bool IsUsable(PageFormatDefinition? format) => format is not null &&
        (format.Kind == PageFormatKind.SourceAsIs ||
         (format.WidthMm > 0 && format.HeightMm > 0 &&
          format.DrawingArea.Width > 0 && format.DrawingArea.Height > 0));

    public static PageFormatDefinition DefaultWorkingDrawing => Resolve(WorkingDrawingA3LandscapeId);

    private static PageFormatDefinition CreateConceptA3Portrait(bool includeInformationHeader)
    {
        BuildingArchitectureConceptPageRegions regions =
            BuildingArchitectureConceptPageLayout.Calculate(
                297,
                420,
                "TOP",
                includeInformationHeader);
        return new PageFormatDefinition
        {
            Id = includeInformationHeader
                ? ConceptElevationA3PortraitTopId
                : ConceptA3PortraitTopId,
            Name = includeInformationHeader
                ? "Загвар зураг - Нүүр тал, A3 босоо"
                : "Загвар зураг - A3 босоо, дээд нуруулдах",
            Kind = PageFormatKind.Concept,
            Code = "A3",
            Orientation = "PORTRAIT",
            BindEdge = "TOP",
            WidthMm = 297,
            HeightMm = 420,
            DrawingArea = regions.DrawingArea,
            SheetTitleArea = regions.SheetTitleArea,
            TitleBlockArea = regions.TitleBlockArea,
            Revision = includeInformationHeader ? 4 : 3,
        };
    }

    public static PageFormatDefinition ResolveForConceptPage(
        AlbumPageDefinition page,
        SheetPackageEntry entry)
    {
        PageFormatDefinition resolved = Resolve(page);
        return BuildingArchitectureConceptPageLayout.UsesInformationHeader(
            AlbumPageSourceMetadata.ResolveContentKind(page, entry),
            entry.Name,
            page.TemplateSlotId)
            ? BuildingArchitectureConceptPageLayout.ApplyElevationGeometry(resolved)
            : resolved;
    }
}

public static class PageFormatResolver
{
    public static bool ApplySourceFormat(AlbumPageDefinition page, SheetPackageEntry entry)
    {
        if (page.FollowSourceFormat == false || entry.Format is null ||
            !TryResolveSourceFormat(entry, out var format))
        {
            return false;
        }

        if (!entry.IsCleanDrawingSpace)
        {
            var asIsChanged = page.PageFormatSnapshot is not null ||
                !string.Equals(
                    page.PageFormatId,
                    PageFormatCatalog.SourceAsIsId,
                    StringComparison.OrdinalIgnoreCase) ||
                page.PlacementMode != PagePlacementMode.FullPage ||
                page.FollowSourceFormat != true;

            page.PageFormatId = PageFormatCatalog.SourceAsIsId;
            page.PageFormatSnapshot = null;
            page.PlacementMode = PagePlacementMode.FullPage;
            page.FollowSourceFormat = true;
            return asIsChanged;
        }

        var changed = page.PageFormatSnapshot is null ||
            !string.Equals(page.PageFormatId, format.Id, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                page.PageFormatSnapshot.GeometryHash,
                format.GeometryHash,
                StringComparison.OrdinalIgnoreCase) ||
            page.PlacementMode != PagePlacementMode.PreserveDrawingSpace ||
            page.FollowSourceFormat != true;

        page.PageFormatId = format.Id;
        page.PageFormatSnapshot = format;
        page.PlacementMode = PagePlacementMode.PreserveDrawingSpace;
        page.FollowSourceFormat = true;
        return changed;
    }

    public static bool TryResolveSourceFormat(SheetPackageEntry entry, out PageFormatDefinition format)
    {
        if (entry.Format is not null && PageFormatSpecGeometry.Validate(entry.Format).Count == 0)
        {
            format = FromSpec(entry.Format);
            return true;
        }

        if (!string.IsNullOrWhiteSpace(entry.PageFormatId))
        {
            var catalogFormat = PageFormatCatalog.All.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, entry.PageFormatId, StringComparison.OrdinalIgnoreCase));
            if (catalogFormat is not null)
            {
                format = catalogFormat;
                return true;
            }
        }

        format = null!;
        return false;
    }

    public static PageFormatDefinition FromSpec(PageFormatSpec spec)
    {
        return new PageFormatDefinition
        {
            SpecVersion = spec.SpecVersion,
            Id = spec.Id,
            Name = string.IsNullOrWhiteSpace(spec.Name) ? spec.Id : spec.Name,
            Kind = ResolveKind(spec.Mode),
            Code = spec.Code,
            Orientation = spec.Orientation,
            BindEdge = spec.BindEdge,
            WidthMm = spec.WidthMm,
            HeightMm = spec.HeightMm,
            DrawingArea = FromRect(spec.DrawingArea),
            SheetTitleArea = FromRect(spec.SheetTitleArea),
            TitleBlockArea = FromRect(spec.TitleBlockArea),
            ShowBorder = spec.ShowBorder,
            ShowGrid = spec.ShowGrid,
            Revision = spec.Revision,
            ModuleColumns = spec.ModuleColumns,
            ModuleRows = spec.ModuleRows,
            HasHalfModule = spec.HasHalfModule,
            GeometryHash = spec.GeometryHash,
        };
    }

    private static PageFormatKind ResolveKind(string? mode)
    {
        if (string.Equals(mode, "Sketch", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(mode, "Concept", StringComparison.OrdinalIgnoreCase))
        {
            return PageFormatKind.Concept;
        }
        if (string.Equals(mode, "Blueprint", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(mode, "WorkingDrawing", StringComparison.OrdinalIgnoreCase))
        {
            return PageFormatKind.WorkingDrawing;
        }
        if (string.Equals(mode, "Document", StringComparison.OrdinalIgnoreCase))
        {
            return PageFormatKind.Document;
        }
        if (string.Equals(mode, "Cover", StringComparison.OrdinalIgnoreCase))
        {
            return PageFormatKind.Cover;
        }
        return PageFormatKind.WorkingDrawing;
    }

    private static PageRectMm FromRect(PageRectSpec? rect) => rect is null
        ? new PageRectMm()
        : new PageRectMm { X = rect.X, Y = rect.Y, Width = rect.Width, Height = rect.Height };
}

public static class PdfSourcePageFormatFactory
{
    public const string SourceCode = "SOURCE";
    public const string CustomCode = "CUSTOM";

    private static readonly IReadOnlyDictionary<string, (double Width, double Height)> StandardSizes =
        new Dictionary<string, (double Width, double Height)>(StringComparer.OrdinalIgnoreCase)
        {
            ["A4"] = (210, 297),
            ["A3"] = (297, 420),
            ["A2"] = (420, 594),
            ["A1"] = (594, 841),
            ["A0"] = (841, 1189),
        };

    public static IReadOnlyList<string> StandardCodes { get; } =
        ["A4", "A3", "A2", "A1", "A0"];

    public static PageFormatDefinition Create(
        string? code,
        string? orientation,
        string? bindEdge,
        double customWidthMm = 0,
        double customHeightMm = 0)
    {
        string normalizedCode = NormalizeCode(code);
        string normalizedOrientation = NormalizeOrientation(orientation);
        string normalizedBindEdge = NormalizeBindEdge(bindEdge);
        (double width, double height) = ResolveDimensions(
            normalizedCode,
            normalizedOrientation,
            customWidthMm,
            customHeightMm);
        BuildingArchitectureConceptPageRegions regions =
            BuildingArchitectureConceptPageLayout.Calculate(
                width,
                height,
                normalizedBindEdge);
        string dimensionKey =
            $"{width:0.###}x{height:0.###}".Replace('.', '-');

        return new PageFormatDefinition
        {
            Id = $"pdf-source-{normalizedCode.ToLowerInvariant()}-" +
                 $"{normalizedOrientation.ToLowerInvariant()}-" +
                 $"{normalizedBindEdge.ToLowerInvariant()}-{dimensionKey}",
            Name = normalizedCode == CustomCode
                ? $"PDF custom - {width:0.##} x {height:0.##} mm"
                : $"PDF - {normalizedCode} {normalizedOrientation.ToLowerInvariant()}",
            Kind = PageFormatKind.Concept,
            Code = normalizedCode,
            Orientation = normalizedOrientation,
            BindEdge = normalizedBindEdge,
            WidthMm = width,
            HeightMm = height,
            DrawingArea = regions.DrawingArea,
            SheetTitleArea = regions.SheetTitleArea,
            TitleBlockArea = regions.TitleBlockArea,
            ShowBorder = true,
            ShowGrid = false,
            Revision = 1,
        };
    }

    /// <summary>
    /// Creates the Studio frame that best matches a PDF source page. Standard
    /// ISO sizes retain their familiar name; other page sizes remain exact as
    /// a custom Studio format rather than being silently resized.
    /// </summary>
    public static PageFormatDefinition CreateForSource(
        double widthMm,
        double heightMm,
        string? bindEdge = "LEFT")
    {
        double width = IsUsableDimension(widthMm) ? widthMm : 420;
        double height = IsUsableDimension(heightMm) ? heightMm : 297;
        string orientation = height > width ? "PORTRAIT" : "LANDSCAPE";
        double shortEdge = Math.Min(width, height);
        double longEdge = Math.Max(width, height);

        foreach ((string code, (double standardWidth, double standardHeight)) in StandardSizes)
        {
            if (Math.Abs(shortEdge - Math.Min(standardWidth, standardHeight)) <= 5 &&
                Math.Abs(longEdge - Math.Max(standardWidth, standardHeight)) <= 5)
            {
                return Create(code, orientation, bindEdge);
            }
        }

        return Create(CustomCode, orientation, bindEdge, width, height);
    }

    public static bool IsPdfSourceFormat(PageFormatDefinition? format) =>
        format is not null &&
        format.Id.StartsWith("pdf-source-", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeCode(string? code)
    {
        string value = (code ?? "").Trim().ToUpperInvariant();
        if (string.Equals(value, CustomCode, StringComparison.OrdinalIgnoreCase))
            return CustomCode;
        return StandardSizes.ContainsKey(value) ? value : "A3";
    }

    private static string NormalizeOrientation(string? orientation) =>
        string.Equals(
            orientation?.Trim(),
            "PORTRAIT",
            StringComparison.OrdinalIgnoreCase)
            ? "PORTRAIT"
            : "LANDSCAPE";

    private static string NormalizeBindEdge(string? bindEdge)
    {
        string value = (bindEdge ?? "").Trim().ToUpperInvariant();
        return value is "TOP" or "RIGHT" or "BOTTOM" ? value : "LEFT";
    }

    private static (double Width, double Height) ResolveDimensions(
        string code,
        string orientation,
        double customWidthMm,
        double customHeightMm)
    {
        (double width, double height) = code == CustomCode
            ? (
                Math.Clamp(customWidthMm, 100, 3000),
                Math.Clamp(customHeightMm, 100, 3000))
            : StandardSizes[code];
        double shortEdge = Math.Min(width, height);
        double longEdge = Math.Max(width, height);
        return orientation == "PORTRAIT"
            ? (shortEdge, longEdge)
            : (longEdge, shortEdge);
    }

    private static bool IsUsableDimension(double value) =>
        double.IsFinite(value) && value is >= 100 and <= 3000;
}
