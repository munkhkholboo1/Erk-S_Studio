using System.Globalization;

namespace ErkS.Platform.Core;

/// <summary>
/// Builds the same joined A3-module paper geometry used by Erk-S Platform for
/// AutoCAD. Adjacent modules share a 12 mm joining band.
/// </summary>
public static class WorkingDrawingAlbumFormatFactory
{
    public const int MinimumModuleCount = 1;
    public const int MaximumModuleCount = 4;
    public const double A3WidthMm = 420d;
    public const double A3HeightMm = 297d;
    public const double JoinOverlapMm = 12d;

    public static PageFormatDefinition Create(
        int columns,
        int rows,
        string orientation = "LANDSCAPE",
        string bindEdge = "LEFT",
        bool hasHalfModule = false)
    {
        ValidateModuleCount(columns, nameof(columns));
        ValidateModuleCount(rows, nameof(rows));
        string normalizedOrientation = NormalizeOrientation(orientation);
        string normalizedBindEdge = NormalizeBindEdge(bindEdge);

        double naturalWidth = JoinedLength(A3WidthMm, columns) +
                              (hasHalfModule ? A3WidthMm / 2d - JoinOverlapMm : 0d);
        double naturalHeight = JoinedLength(A3HeightMm, rows);
        (double width, double height) = Orient(
            naturalWidth,
            naturalHeight,
            normalizedOrientation);

        double leftMargin = normalizedBindEdge == "LEFT" ? 15d : 5d;
        double rightMargin = normalizedBindEdge == "RIGHT" ? 15d : 5d;
        double topMargin = normalizedBindEdge == "TOP" ? 15d : 5d;
        double bottomMargin = normalizedBindEdge == "BOTTOM" ? 15d : 5d;
        const double etalonBand = WorkingDrawingPageLayout.EtalonBandMm;
        var drawingArea = new PageRectMm
        {
            X = leftMargin + etalonBand,
            Y = topMargin + etalonBand,
            Width = width - leftMargin - rightMargin - etalonBand * 2d,
            Height = height - topMargin - bottomMargin - etalonBand * 2d,
        };
        string code = CreateCode(columns, rows, hasHalfModule);
        string id = columns == 1 && rows == 1 && !hasHalfModule &&
                    normalizedOrientation == "LANDSCAPE" && normalizedBindEdge == "LEFT"
            ? PageFormatCatalog.WorkingDrawingA3LandscapeId
            : $"erks-working-a3-grid-{code.ToLowerInvariant()}-" +
              $"{normalizedOrientation.ToLowerInvariant()}-{normalizedBindEdge.ToLowerInvariant()}";
        var format = new PageFormatDefinition
        {
            SpecVersion = 1,
            Id = id,
            Name = $"Ажлын зураг · A3 {columns}×{rows} ({code})",
            Kind = PageFormatKind.WorkingDrawing,
            Code = code,
            Orientation = normalizedOrientation,
            BindEdge = normalizedBindEdge,
            WidthMm = width,
            HeightMm = height,
            DrawingArea = drawingArea,
            SheetTitleArea = new PageRectMm
            {
                X = drawingArea.X,
                Y = drawingArea.Y,
                Width = drawingArea.Width,
                Height = Math.Min(9d, drawingArea.Height),
            },
            TitleBlockArea = new PageRectMm
            {
                X = drawingArea.X + drawingArea.Width -
                    Math.Min(WorkingDrawingPageLayout.HorizontalTitleBlockWidthMm, drawingArea.Width),
                Y = drawingArea.Y + drawingArea.Height -
                    Math.Min(WorkingDrawingPageLayout.HorizontalTitleBlockHeightMm, drawingArea.Height),
                Width = Math.Min(
                    WorkingDrawingPageLayout.HorizontalTitleBlockWidthMm,
                    drawingArea.Width),
                Height = Math.Min(
                    WorkingDrawingPageLayout.HorizontalTitleBlockHeightMm,
                    drawingArea.Height),
            },
            ShowBorder = true,
            ShowGrid = true,
            Revision = 1,
            ModuleColumns = columns,
            ModuleRows = rows,
            HasHalfModule = hasHalfModule,
        };
        format.GeometryHash = CreateGeometryHash(format);
        return format;
    }

    public static PageFormatDefinition Resolve(AlbumDefinition album)
    {
        ArgumentNullException.ThrowIfNull(album);
        return PageFormatCatalog.IsUsable(album.GeneratedPageFormat) &&
               album.GeneratedPageFormat!.Kind == PageFormatKind.WorkingDrawing
            ? album.GeneratedPageFormat
            : Create(1, 1);
    }

    private static double JoinedLength(double moduleLength, int count) =>
        moduleLength * count - JoinOverlapMm * (count - 1);

    private static (double Width, double Height) Orient(
        double width,
        double height,
        string orientation) => orientation == "LANDSCAPE"
        ? (Math.Max(width, height), Math.Min(width, height))
        : (Math.Min(width, height), Math.Max(width, height));

    private static string CreateCode(int columns, int rows, bool hasHalfModule) =>
        $"{(char)('A' + columns - 1)}{(hasHalfModule ? "H" : "")}{rows}";

    private static string NormalizeOrientation(string? value) =>
        string.Equals(value, "PORTRAIT", StringComparison.OrdinalIgnoreCase)
            ? "PORTRAIT"
            : "LANDSCAPE";

    private static string NormalizeBindEdge(string? value)
    {
        string normalized = (value ?? "").Trim().ToUpperInvariant();
        return normalized is "TOP" or "RIGHT" or "BOTTOM" ? normalized : "LEFT";
    }

    private static void ValidateModuleCount(int value, string parameterName)
    {
        if (value is < MinimumModuleCount or > MaximumModuleCount)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                $"A3 module count must be between {MinimumModuleCount} and {MaximumModuleCount}.");
        }
    }

    private static string CreateGeometryHash(PageFormatDefinition format) => string.Join(
        '|',
        format.Code,
        format.Orientation,
        format.BindEdge,
        Format(format.WidthMm),
        Format(format.HeightMm),
        Format(format.DrawingArea.X),
        Format(format.DrawingArea.Y),
        Format(format.DrawingArea.Width),
        Format(format.DrawingArea.Height),
        Format(format.TitleBlockArea.X),
        Format(format.TitleBlockArea.Y),
        Format(format.TitleBlockArea.Width),
        Format(format.TitleBlockArea.Height));

    private static string Format(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);
}
