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
            entry.ContentKind,
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
