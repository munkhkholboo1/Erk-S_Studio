using System.Globalization;
using ErkS.Platform.Contracts;
using ErkS.Platform.Core;

namespace ErkS.Platform.Pdf;

/// <summary>One legend row placed in its band, in millimetres from the band's top-left.</summary>
internal sealed record GeneralPlanLegendItem(
    double XMm,
    double YMm,
    double WidthMm,
    SheetInfoLegendRow Row,
    string QuantityText);

internal sealed record GeneralPlanLegendLayout(
    IReadOnlyList<GeneralPlanLegendItem> Items,
    int Overflow);

internal sealed record GeneralPlanTextRow(string Label, string Value);

internal sealed record GeneralPlanScheduleRow(
    string Number,
    string Name,
    string Floors,
    string Stage,
    string Count);

/// <summary>
/// What a general-plan sheet's information bands say, from its
/// `erks-sheet-info` document - as data, so it is tested without rendering.
/// </summary>
/// <remarks>
/// The owner's reference sheets (MJ_AP ZZ, TKK ZZ, GM-ZZ-2026) carry, besides
/// the drawing: a legend with quantities (ТАНИХ ТЭМДЭГ), the ТЭЗ summary, the
/// building schedule (ТОДОРХОЙЛОЛТ) and the basis (ҮНДЭСЛЭЛ). Erin's sheets
/// had none of it; the bands were drawn empty.
/// </remarks>
internal static class GeneralPlanSheetInfoLayout
{
    public const double TitleHeightMm = 4.5;
    public const double RowHeightMm = 3.8;
    public const double MinimumColumnWidthMm = 68;
    public const double SwatchWidthMm = 8;

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>
    /// Columns of rows under one title. Rows that do not fit are counted in
    /// <see cref="GeneralPlanLegendLayout.Overflow"/>, never silently dropped.
    /// </summary>
    public static GeneralPlanLegendLayout Legend(
        IReadOnlyList<SheetInfoLegendRow>? rows,
        double widthMm,
        double heightMm)
    {
        IReadOnlyList<SheetInfoLegendRow> list = rows ?? [];
        int perColumn = Math.Max(0, (int)Math.Floor((heightMm - TitleHeightMm + 0.0001) / RowHeightMm));
        int columns = Math.Max(1, (int)Math.Floor((widthMm + 0.0001) / MinimumColumnWidthMm));
        double columnWidth = widthMm / columns;
        int capacity = perColumn * columns;

        var items = new List<GeneralPlanLegendItem>();
        for (int index = 0; index < Math.Min(capacity, list.Count); index++)
        {
            SheetInfoLegendRow row = list[index];
            int column = index / perColumn;
            int line = index % perColumn;
            items.Add(new GeneralPlanLegendItem(
                column * columnWidth,
                TitleHeightMm + line * RowHeightMm,
                columnWidth,
                row,
                Quantity(row.Quantity, row.Unit)));
        }

        return new GeneralPlanLegendLayout(items, list.Count - items.Count);
    }

    public const double ScheduleRowHeightMaxMm = 3.4;
    public const double ScheduleRowHeightMinMm = 2.5;

    /// <summary>
    /// The schedule's row pitch: as tall as it can be up to 3.4 mm, shrinking to
    /// fit every building (header included) but never below 2.5 mm.
    /// </summary>
    public static double ScheduleRowHeight(double heightMm, int buildingCount) =>
        Math.Clamp(heightMm / (buildingCount + 1), ScheduleRowHeightMinMm, ScheduleRowHeightMaxMm);

    /// <summary>How many buildings the schedule shows; the rest are named in the build's warnings.</summary>
    public static int ScheduleCapacity(double heightMm, int buildingCount) =>
        Math.Max(0, (int)Math.Floor((heightMm + 0.0001) / ScheduleRowHeight(heightMm, buildingCount)) - 1);

    public static IReadOnlyList<GeneralPlanTextRow> SummaryRows(IEnumerable<SheetInfoSummaryRow>? rows) =>
        (rows ?? [])
            .Select(row => new GeneralPlanTextRow((row.Label ?? "").Trim(), Quantity(row.Value, row.Unit)))
            .ToList();

    public static IReadOnlyList<GeneralPlanScheduleRow> ScheduleRows(IEnumerable<SheetInfoBuilding>? buildings) =>
        (buildings ?? [])
            .Select(building => new GeneralPlanScheduleRow(
                (building.No ?? "").Trim(),
                (building.Name ?? "").Trim(),
                (building.Floors ?? "").Trim(),
                StageText(building.Stage),
                building.Count.ToString(Invariant)))
            .ToList();

    /// <summary>
    /// ҮНДЭСЛЭЛ: Studio's own fact first, then the document's text, then a
    /// placeholder that reads as a form field - never an empty line.
    /// </summary>
    public static IReadOnlyList<string> BasisLines(AlbumProject project, SheetInfoDocument? info)
    {
        ArgumentNullException.ThrowIfNull(project);
        var lines = new List<string>();
        var keys = new List<string> { "land", "planningTask" };
        foreach (SheetInfoBasisLine line in info?.Basis ?? [])
        {
            if (!keys.Contains(line.Key))
                keys.Add(line.Key);
        }

        foreach (string key in keys)
        {
            string? text = StudioBasis(project, key) ??
                           (info?.Basis ?? []).FirstOrDefault(line => line.Key == key)?.Text?.Trim();
            lines.Add(string.IsNullOrWhiteSpace(text) ? Placeholder(key) : text!);
        }

        return lines;
    }

    public static string StageText(string? stage) => stage switch
    {
        "Planned" => "Төлөвлөж буй",
        "Existing" => "Одоо байгаа",
        "UnderConstruction" => "Баригдаж буй",
        "Demolish" => "Буулгах",
        _ => (stage ?? "").Trim(),
    };

    /// <summary>"25 237.9 м²" - thousands grouped with a space, at most one decimal unless the value needs two.</summary>
    public static string Quantity(double? value, string? unit)
    {
        if (value is not { } number || !double.IsFinite(number))
            return "";
        string format = Math.Abs(number - Math.Round(number, 1)) > 1e-9 ? "#,0.##" : "#,0.#";
        string text = number.ToString(format, Invariant).Replace(",", " ", StringComparison.Ordinal);
        string suffix = (unit ?? "").Trim();
        return suffix.Length == 0 ? text : text + " " + suffix;
    }

    private static string? StudioBasis(AlbumProject project, string key)
    {
        switch (key)
        {
            case "land":
            {
                string reference = (project.InitiationBasis?.LandReference ?? "").Trim();
                return reference.Length == 0 ? null : "Газар эзэмших эрх: " + reference;
            }
            case "planningTask":
            {
                string number = (project.PlanningTask?.AtdNumber ?? "").Trim();
                if (number.Length == 0)
                    return null;
                string date = project.PlanningTask!.IssuedAtUtc is { } issued
                    ? $", {issued.ToLocalTime():yyyy} оны {issued.ToLocalTime():MM} сарын {issued.ToLocalTime():dd}"
                    : "";
                return $"Архитектур төлөвлөлтийн даалгавар №{number}{date}";
            }
            default:
                return null;
        }
    }

    private static string Placeholder(string key) => key switch
    {
        "land" => "Газар эзэмших эрхийн гэрчилгээ №____, ____ оны __ сарын __",
        "planningTask" => "Архитектур төлөвлөлтийн даалгавар №____, ____ оны __ сарын __",
        _ => "____",
    };
}

/// <summary>
/// How a legend line sample is drawn, in paper millimetres: v1's thin line,
/// or v2's width, dash and heads - the movement scheme's lane band, dots and
/// entries as CGA draws them (owner, 2026-10-29: the movement legend follows
/// the scheme CGA sends).
/// </summary>
internal sealed record LegendLineStyle(
    double WidthMm,
    double[]? DashMm,
    bool HeadAtStart,
    bool HeadAtEnd,
    double HeadLengthMm,
    double HeadHalfWidthMm)
{
    public const double DefaultWidthMm = 0.35;

    /// <param name="boxHeightMm">The swatch's height: a band or a head never spills into the next row.</param>
    public static LegendLineStyle Of(SheetInfoSample sample, double boxHeightMm)
    {
        double width = Math.Min(sample.WidthMm ?? DefaultWidthMm, boxHeightMm * 0.6);
        bool both = sample.Heads == "both";
        bool end = both || sample.Heads == "end" || sample.Arrow == true;

        // CGA's lane head is 2.75 band widths long and 1.6 wide; a thin line
        // keeps v1's 1.6 x 2 mm head.
        double halfWidth = Math.Min(Math.Max(1.0, width * 0.8), boxHeightMm / 2);
        double length = Math.Max(1.6, width * 2.75);
        return new LegendLineStyle(width, sample.DashMm, both, end, length, halfWidth);
    }
}

/// <summary>
/// North on every general-plan sheet (owner, 2026-10-29: «ерөнхий төлөвлөгөөний
/// хуудаснууд дээр хойд зүгийн тэмдэглэгээ нэмээрэй»): where the mark stands and
/// which way it points, as data.
/// </summary>
internal static class GeneralPlanNorth
{
    public const double SizeMm = 16;
    public const double InsetMm = 4;

    /// <summary>What the solar sheet's note gives up at the strip's right end.</summary>
    public const double StripReserveMm = SizeMm + 6;

    /// <summary>Up the page: a general plan's model +Y is north.</summary>
    public const double UpDegrees = 90;

    /// <summary>
    /// Where north points on the paper (degrees from +X, counter-clockwise), from
    /// the largest viewport the AutoCAD platform measured - its twist already in.
    /// No viewports: up the page.
    /// </summary>
    public static double OnPaper(IReadOnlyList<SheetViewportGeometry>? viewports)
    {
        SheetViewportGeometry? largest = (viewports ?? [])
            .Where(view => double.IsFinite(view.NorthAngleDegrees) &&
                           view.PaperWidthMm > 0 && view.PaperHeightMm > 0)
            .OrderByDescending(view => view.PaperWidthMm * view.PaperHeightMm)
            .FirstOrDefault();
        if (largest is null)
            return UpDegrees;
        double angle = largest.NorthAngleDegrees % 360;
        return angle < 0 ? angle + 360 : angle;
    }

    /// <summary>
    /// The plan sheets: the drawing area's top-right corner. The solar sheet's
    /// drawing is a grid of small plans with their hours over them, so its mark
    /// stands at the right end of the strip beside the corner table.
    /// </summary>
    public static PageRectMm Area(string slot, PageRectMm drawingArea, PageRectMm strip)
    {
        if (slot == "solar-study" && strip.Height >= SizeMm * 0.6 && strip.Width > StripReserveMm * 2)
        {
            double size = Math.Min(SizeMm, strip.Height - 2);
            return new PageRectMm
            {
                X = strip.X + strip.Width - StripReserveMm + (StripReserveMm - size) / 2,
                Y = strip.Y + (strip.Height - size) / 2,
                Width = size,
                Height = size,
            };
        }

        return new PageRectMm
        {
            X = drawingArea.X + drawingArea.Width - SizeMm - InsetMm,
            Y = drawingArea.Y + InsetMm,
            Width = SizeMm,
            Height = SizeMm,
        };
    }

    /// <summary>The clockwise page rotation that turns an up-pointing mark to <paramref name="northOnPaper"/>.</summary>
    public static double PageRotation(double northOnPaper) => UpDegrees - northOnPaper;
}
