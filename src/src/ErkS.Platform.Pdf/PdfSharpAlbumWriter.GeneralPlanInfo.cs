using ErkS.Platform.Contracts;
using ErkS.Platform.Core;
using PdfSharp.Drawing;

namespace ErkS.Platform.Pdf;

/// <summary>
/// The general-plan sheets' information bands, drawn from the package's
/// `erks-sheet-info` document (_shared/erks-sheet-info-contract-2026-10-19.md).
/// </summary>
/// <remarks>
/// master-plan: the information header's description column becomes three -
/// ҮНДЭСЛЭЛ | ТОДОРХОЙЛОЛТ | БАЙРШЛЫН СХЕМ - and the strip left of the corner
/// table carries ТАНИХ ТЭМДЭГ and ТЭЗ. traffic-scheme, landscaping: the strip
/// carries the legend. solar-study: the strip carries the norm text.
/// Without a document nothing here draws, and the sheet is what it was.
/// </remarks>
public sealed partial class PdfSharpAlbumWriter
{
    private const double SummaryWidthMm = 130;

    internal static string? GeneralPlanSlot(AlbumBuildPage buildPage)
    {
        string slot = (buildPage.Definition?.TemplateSlotId ?? "").Trim();
        if (slot.Length == 0)
            slot = (buildPage.Sheet?.Entry?.TemplateSlotId ?? "").Trim();
        return SheetInfoVocabulary.Slots.Contains(slot) ? slot : null;
    }

    /// <summary>The band between the drawing and the frame's foot, left of the corner table.</summary>
    internal static PageRectMm GeneralPlanStrip(BuildingArchitectureConceptPageRegions regions)
    {
        double top = regions.DrawingArea.Y + regions.DrawingArea.Height;
        double bottom = regions.Frame.Y + regions.Frame.Height;
        return new PageRectMm
        {
            X = regions.Frame.X,
            Y = top,
            Width = Math.Max(0, regions.TitleBlockArea.X - regions.Frame.X),
            Height = Math.Max(0, bottom - top),
        };
    }

    private static void DrawGeneralPlanStrip(
        XGraphics gfx,
        AlbumBuildPage buildPage,
        BuildingArchitectureConceptPageRegions regions,
        XPen borderPen,
        XPen finePen)
    {
        SheetInfoDocument? info = buildPage.Sheet?.SheetInfo;
        string? slot = GeneralPlanSlot(buildPage);
        if (info is null || slot is null)
            return;

        PageRectMm strip = GeneralPlanStrip(regions);
        if (strip.Width <= 1 || strip.Height <= 1)
            return;

        XRect stripRect = ToPoints(strip);
        gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(254, 254, 254)), stripRect);
        gfx.DrawRectangle(borderPen, stripRect);

        if (slot == "solar-study")
        {
            DrawElevationHeaderLabel(
                gfx,
                "ТАЙЛБАР",
                new XRect(stripRect.Left + Mm(2), stripRect.Top + Mm(0.5), stripRect.Width - Mm(4), Mm(4)));
            info.Notes.TryGetValue(slot, out string? note);
            DrawTopAlignedFittedText(
                gfx,
                note,
                new XRect(stripRect.Left + Mm(2), stripRect.Top + Mm(5), stripRect.Width - Mm(4), stripRect.Height - Mm(6)),
                BuildingArchitectureConceptPageLayout.CornerTextHeightMm,
                1.5,
                bold: false);
            return;
        }

        info.Legends.TryGetValue(slot, out List<SheetInfoLegendRow>? rows);
        PageRectMm legendArea = strip;
        if (slot == "master-plan" && info.Summary.Count > 0 && strip.Width > SummaryWidthMm * 2)
        {
            legendArea = new PageRectMm { X = strip.X, Y = strip.Y, Width = strip.Width - SummaryWidthMm, Height = strip.Height };
            var summaryArea = new PageRectMm
            {
                X = strip.X + strip.Width - SummaryWidthMm,
                Y = strip.Y,
                Width = SummaryWidthMm,
                Height = strip.Height,
            };
            gfx.DrawLine(borderPen, Mm(summaryArea.X), Mm(summaryArea.Y), Mm(summaryArea.X), Mm(summaryArea.Y + summaryArea.Height));
            DrawGeneralPlanSummary(gfx, info, summaryArea);
        }

        DrawGeneralPlanLegend(gfx, rows ?? [], legendArea, finePen);
    }

    private static void DrawGeneralPlanLegend(
        XGraphics gfx,
        IReadOnlyList<SheetInfoLegendRow> rows,
        PageRectMm area,
        XPen finePen)
    {
        const double inset = 1.5;
        var inner = new PageRectMm
        {
            X = area.X + inset,
            Y = area.Y + 0.5,
            Width = Math.Max(0, area.Width - inset * 2),
            Height = Math.Max(0, area.Height - 1),
        };
        DrawElevationHeaderLabel(
            gfx,
            "ТАНИХ ТЭМДЭГ",
            new XRect(Mm(inner.X), Mm(inner.Y), Mm(inner.Width), Mm(GeneralPlanSheetInfoLayout.TitleHeightMm)));

        GeneralPlanLegendLayout layout = GeneralPlanSheetInfoLayout.Legend(rows, inner.Width, inner.Height);
        double textSize = Mm(CoverFontEmSizeMm(1.9));
        foreach (GeneralPlanLegendItem item in layout.Items)
        {
            double x = inner.X + item.XMm;
            double y = inner.Y + item.YMm;
            double h = GeneralPlanSheetInfoLayout.RowHeightMm;
            var swatch = new XRect(Mm(x), Mm(y + 0.6), Mm(GeneralPlanSheetInfoLayout.SwatchWidthMm), Mm(h - 1.2));
            DrawLegendSample(gfx, item.Row.Sample, swatch);

            double textLeft = x + GeneralPlanSheetInfoLayout.SwatchWidthMm + 1.5;
            double quantityWidth = item.QuantityText.Length == 0 ? 0 : 22;
            DrawFittedText(
                gfx,
                item.Row.Label,
                Mm(textLeft),
                Mm(y),
                Mm(item.WidthMm - (textLeft - x) - quantityWidth - 1.5),
                Mm(h),
                textSize,
                false,
                XStringFormats.CenterLeft,
                WorkingDrawingFontName);
            if (quantityWidth > 0)
            {
                DrawFittedText(
                    gfx,
                    item.QuantityText,
                    Mm(x + item.WidthMm - quantityWidth - 1.5),
                    Mm(y),
                    Mm(quantityWidth),
                    Mm(h),
                    textSize,
                    false,
                    XStringFormats.CenterRight,
                    WorkingDrawingFontName);
            }
        }
    }

    private static void DrawLegendSample(XGraphics gfx, SheetInfoSample? sample, XRect box)
    {
        if (sample is null)
            return;

        XColor colour = ParseHexColour(sample.Color, XColors.Gray);
        XColor outline = ParseHexColour(sample.Outline, XColors.Black);
        switch (sample.Kind)
        {
            case "line":
            {
                var pen = new XPen(colour, Mm(0.35))
                {
                    DashStyle = sample.Dash switch
                    {
                        "dashed" => XDashStyle.Dash,
                        "dotted" => XDashStyle.Dot,
                        _ => XDashStyle.Solid,
                    },
                };
                double y = box.Top + box.Height / 2;
                gfx.DrawLine(pen, box.Left, y, box.Right, y);
                if (sample.Arrow == true)
                {
                    double size = Mm(1.0);
                    gfx.DrawPolygon(
                        new XSolidBrush(colour),
                        new[]
                        {
                            new XPoint(box.Right, y),
                            new XPoint(box.Right - size * 1.6, y - size),
                            new XPoint(box.Right - size * 1.6, y + size),
                        },
                        XFillMode.Winding);
                }
                break;
            }
            case "symbol":
            {
                double d = Math.Min(box.Width, box.Height);
                var circle = new XRect(box.Left + (box.Width - d) / 2, box.Top + (box.Height - d) / 2, d, d);
                gfx.DrawEllipse(new XPen(outline, Mm(0.15)), new XSolidBrush(colour), circle);
                break;
            }
            default:
            {
                bool solid = string.IsNullOrWhiteSpace(sample.Pattern) ||
                             string.Equals(sample.Pattern, "SOLID", StringComparison.OrdinalIgnoreCase);
                if (solid)
                {
                    gfx.DrawRectangle(new XSolidBrush(colour), box);
                }
                else
                {
                    // A patterned hatch: its lines in the hatch colour on white,
                    // the way the drawing itself shows it.
                    gfx.Save();
                    gfx.IntersectClip(box);
                    var line = new XPen(colour, Mm(0.12));
                    double step = Mm(0.9);
                    for (double offset = -box.Height; offset < box.Width; offset += step)
                        gfx.DrawLine(line, box.Left + offset, box.Bottom, box.Left + offset + box.Height, box.Top);
                    gfx.Restore();
                }
                gfx.DrawRectangle(new XPen(outline, Mm(0.12)), box);
                break;
            }
        }
    }

    private static XColor ParseHexColour(string? hex, XColor fallback)
    {
        string text = (hex ?? "").Trim();
        if (text.Length != 7 || text[0] != '#')
            return fallback;
        try
        {
            int value = Convert.ToInt32(text[1..], 16);
            return XColor.FromArgb((value >> 16) & 0xFF, (value >> 8) & 0xFF, value & 0xFF);
        }
        catch (FormatException)
        {
            return fallback;
        }
    }

    private static void DrawGeneralPlanSummary(XGraphics gfx, SheetInfoDocument info, PageRectMm area)
    {
        const double inset = 1.5;
        DrawElevationHeaderLabel(
            gfx,
            "ТЕХНИК ЭДИЙН ЗАСГИЙН ҮЗҮҮЛЭЛТ",
            new XRect(Mm(area.X + inset), Mm(area.Y + 0.5), Mm(area.Width - inset * 2), Mm(GeneralPlanSheetInfoLayout.TitleHeightMm)));
        IReadOnlyList<GeneralPlanTextRow> rows = GeneralPlanSheetInfoLayout.SummaryRows(info.Summary);
        double top = area.Y + 0.5 + GeneralPlanSheetInfoLayout.TitleHeightMm;
        int perColumn = Math.Max(1, (int)Math.Floor((area.Y + area.Height - top) / GeneralPlanSheetInfoLayout.RowHeightMm));
        int columns = (int)Math.Ceiling(rows.Count / (double)perColumn);
        double columnWidth = (area.Width - inset * 2) / Math.Max(1, columns);
        double textSize = Mm(CoverFontEmSizeMm(1.9));
        for (int index = 0; index < rows.Count; index++)
        {
            double x = area.X + inset + (index / perColumn) * columnWidth;
            double y = top + (index % perColumn) * GeneralPlanSheetInfoLayout.RowHeightMm;
            double valueWidth = Math.Min(22, columnWidth * 0.4);
            DrawFittedText(gfx, rows[index].Label, Mm(x), Mm(y), Mm(columnWidth - valueWidth - 1), Mm(GeneralPlanSheetInfoLayout.RowHeightMm),
                textSize, false, XStringFormats.CenterLeft, WorkingDrawingFontName);
            DrawFittedText(gfx, rows[index].Value, Mm(x + columnWidth - valueWidth - 1), Mm(y), Mm(valueWidth), Mm(GeneralPlanSheetInfoLayout.RowHeightMm),
                textSize, false, XStringFormats.CenterRight, WorkingDrawingFontName);
        }
    }

    /// <summary>
    /// The master-plan header's description column, as three: ҮНДЭСЛЭЛ |
    /// ТОДОРХОЙЛОЛТ | БАЙРШЛЫН СХЕМ. Returns false when the sheet carries no
    /// information, and the caller draws its ТАЙЛБАР as before.
    /// </summary>
    private static bool DrawMasterPlanHeaderColumns(
        XGraphics gfx,
        AlbumProject project,
        AlbumBuildPage buildPage,
        double xLeft,
        double xRight,
        double y0,
        double y1,
        XPen borderPen)
    {
        SheetInfoDocument? info = buildPage.Sheet?.SheetInfo;
        if (info is null || GeneralPlanSlot(buildPage) != "master-plan")
            return false;

        double width = xRight - xLeft;
        double xBasis = xLeft;
        double xSchedule = xLeft + width * 0.32;
        double xMap = xLeft + width * 0.72;
        gfx.DrawLine(borderPen, Mm(xSchedule), Mm(y0), Mm(xSchedule), Mm(y1));
        gfx.DrawLine(borderPen, Mm(xMap), Mm(y0), Mm(xMap), Mm(y1));

        const double padding = 2.5;
        const double heading = 5.0;

        DrawElevationHeaderLabel(gfx, "ҮНДЭСЛЭЛ",
            new XRect(Mm(xBasis + padding), Mm(y0 + 1.5), Mm(xSchedule - xBasis - padding * 2), Mm(heading)));
        IReadOnlyList<string> basis = GeneralPlanSheetInfoLayout.BasisLines(project, info);
        DrawNumberedParagraphs(
            gfx,
            basis,
            new XRect(Mm(xBasis + padding), Mm(y0 + heading + 2), Mm(xSchedule - xBasis - padding * 2), Mm(y1 - y0 - heading - 3)));

        DrawElevationHeaderLabel(gfx, "ТОДОРХОЙЛОЛТ",
            new XRect(Mm(xSchedule + padding), Mm(y0 + 1.5), Mm(xMap - xSchedule - padding * 2), Mm(heading)));
        DrawBuildingSchedule(
            gfx,
            GeneralPlanSheetInfoLayout.ScheduleRows(info.Buildings),
            new PageRectMm
            {
                X = xSchedule + padding,
                Y = y0 + heading + 2,
                Width = xMap - xSchedule - padding * 2,
                Height = y1 - y0 - heading - 3,
            });

        var mapPanel = new PageRectMm { X = xMap, Y = y0, Width = xRight - xMap, Height = y1 - y0 };
        var mapArea = new PageRectMm { X = xMap, Y = y0 + heading + 2, Width = xRight - xMap, Height = y1 - y0 - heading - 2 };
        DrawSiteContextMapPanel(gfx, project, project.SiteContext.LocationScheme, mapPanel, mapArea, "БАЙРШЛЫН СХЕМ");
        return true;
    }

    /// <summary>
    /// Numbered paragraphs, each wrapped and stacked at a line pitch that
    /// clears the font's full height - the cap-height pitch of the description
    /// helper overlaps Cyrillic lines once text has to wrap.
    /// </summary>
    private static void DrawNumberedParagraphs(XGraphics gfx, IReadOnlyList<string> paragraphs, XRect rect)
    {
        for (double heightMm = 2.2; heightMm >= 1.4; heightMm -= 0.1)
        {
            XFont font = CreateCornerFont(heightMm, bold: false);
            double pitch = Mm(heightMm * 1.75);
            var lines = new List<string>();
            for (int index = 0; index < paragraphs.Count; index++)
                lines.AddRange(WrapCoverText(gfx, $"{index + 1}. {paragraphs[index]}", font, rect.Width));
            if (lines.Count * pitch > rect.Height + 0.01 && heightMm > 1.45)
                continue;

            double y = rect.Top;
            foreach (string line in lines)
            {
                if (y + pitch > rect.Bottom + 0.01)
                    break;
                gfx.DrawString(line, font, XBrushes.Black, new XRect(rect.Left, y, rect.Width, pitch), XStringFormats.CenterLeft);
                y += pitch;
            }
            return;
        }
    }

    private static void DrawBuildingSchedule(
        XGraphics gfx,
        IReadOnlyList<GeneralPlanScheduleRow> rows,
        PageRectMm area)
    {
        // № | Нэр | Давхар | Төлөв | Тоо
        double[] shares = [0.10, 0.50, 0.14, 0.18, 0.08];
        string[] header = ["№", "Нэр", "Давхар", "Төлөв", "Тоо"];
        double textSize = Mm(CoverFontEmSizeMm(1.7));
        var finePen = new XPen(XColors.Black, Mm(0.08));
        double rowHeight = GeneralPlanSheetInfoLayout.ScheduleRowHeight(area.Height, rows.Count);
        int capacity = GeneralPlanSheetInfoLayout.ScheduleCapacity(area.Height, rows.Count);

        void Row(int index, IReadOnlyList<string> cells, bool bold)
        {
            double y = area.Y + index * rowHeight;
            double x = area.X;
            for (int column = 0; column < cells.Count; column++)
            {
                double w = area.Width * shares[column];
                DrawFittedText(gfx, cells[column], Mm(x + 0.5), Mm(y), Mm(w - 1), Mm(rowHeight),
                    textSize, bold, column == 1 ? XStringFormats.CenterLeft : XStringFormats.Center, WorkingDrawingFontName);
                x += w;
            }
            gfx.DrawLine(finePen, Mm(area.X), Mm(y + rowHeight), Mm(area.X + area.Width), Mm(y + rowHeight));
        }

        Row(0, header, bold: true);
        for (int index = 0; index < Math.Min(capacity, rows.Count); index++)
        {
            GeneralPlanScheduleRow row = rows[index];
            Row(index + 1, [row.Number, row.Name, row.Floors, row.Stage, row.Count], bold: false);
        }
    }

    /// <summary>Rows the bands could not hold, named in the build's warnings.</summary>
    internal static IEnumerable<string> GeneralPlanInformationWarnings(AlbumBuildPage buildPage)
    {
        SheetInfoDocument? info = buildPage.Sheet?.SheetInfo;
        string? slot = GeneralPlanSlot(buildPage);
        if (info is null || slot is null || !BuildingArchitectureConceptPageLayout.SupportsStudioChrome(buildPage.Format))
            yield break;

        bool header = PdfSourcePageStudioLayout.UsesInformationHeader(buildPage.Definition, buildPage.Sheet!.Entry);
        BuildingArchitectureConceptPageRegions regions =
            BuildingArchitectureConceptPageLayout.ResolveRegions(buildPage.Format, header);
        PageRectMm strip = GeneralPlanStrip(regions);
        if (slot != "solar-study" && info.Legends.TryGetValue(slot, out List<SheetInfoLegendRow>? rows))
        {
            double width = slot == "master-plan" && info.Summary.Count > 0 && strip.Width > SummaryWidthMm * 2
                ? strip.Width - SummaryWidthMm
                : strip.Width;
            int overflow = GeneralPlanSheetInfoLayout.Legend(rows, Math.Max(0, width - 3), Math.Max(0, strip.Height - 1)).Overflow;
            if (overflow > 0)
                yield return $"{buildPage.Number}: «ТАНИХ ТЭМДЭГ»-ийн {overflow} мөр зурваст багтсангүй.";
        }
        if (slot == "master-plan" && info.Refusals.Count > 0)
        {
            yield return $"{buildPage.Number}: CGA {info.Refusals.Count} хэсгийг хэмжиж чадсангүй, " +
                         "тоо хэмжээ дутуу байж болно: " + string.Join(", ", info.Refusals);
        }
        if (slot == "master-plan")
        {
            // The schedule column's height: the information band less its heading.
            double height = regions.InformationArea.Height - 8;
            int shown = GeneralPlanSheetInfoLayout.ScheduleCapacity(height, info.Buildings.Count);
            int missing = info.Buildings.Count - Math.Min(shown, info.Buildings.Count);
            if (missing > 0)
                yield return $"{buildPage.Number}: «ТОДОРХОЙЛОЛТ»-ын {missing} мөр багтсангүй.";
        }
    }
}
