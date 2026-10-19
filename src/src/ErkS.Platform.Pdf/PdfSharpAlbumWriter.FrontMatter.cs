using ErkS.Platform.Contracts;
using ErkS.Platform.Core;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace ErkS.Platform.Pdf;

/// <summary>
/// The working drawings' front matter, written into the frames the source
/// reserved on its own 01 «ЕРӨНХИЙ ХЭСЭГ» (owner, 2026-10-20).
/// </summary>
/// <remarks>
/// The list is the album as built - Studio's own pages and every sheet, with
/// the number Studio prints on it. The note, composition and indicators are
/// the album's <see cref="AlbumDefinition.FrontMatter"/>. Nothing here guesses
/// a value: what is missing stays blank, and the build says so.
/// </remarks>
public sealed partial class PdfSharpAlbumWriter
{
    private const string FrontMatterMark = "ЕХ";

    internal static IReadOnlyList<WorkingDrawingFrontMatterLayout.ListRow> FrontMatterListRows(
        IEnumerable<ConceptGeneratedPagePlan> generated,
        IEnumerable<AlbumBuildSection> sections) =>
        generated
            .Select(plan => new WorkingDrawingFrontMatterLayout.ListRow(plan.Number, plan.Title, FrontMatterMark))
            .Concat(sections
                .SelectMany(section => section.Pages)
                .Select(page => new WorkingDrawingFrontMatterLayout.ListRow(
                    page.Number,
                    page.Title,
                    (page.Sheet.Entry.Discipline ?? "").Trim())))
            .ToList();

    private static void DrawFrontMatter(
        PdfDocument document,
        int pageIndex,
        AlbumBuildRequest request,
        IReadOnlyList<ConceptGeneratedPagePlan> generated,
        AlbumBuildPage buildPage,
        List<string> warnings)
    {
        List<SheetFrontMatterArea>? frames = buildPage.Sheet?.Entry.FrontMatterAreas;
        if (frames is null || frames.Count == 0 || pageIndex < 0 || pageIndex >= document.PageCount)
            return;

        string number = buildPage.Number;
        PdfPage page = document.Pages[pageIndex];
        SheetPackageEntry entry = buildPage.Sheet!.Entry;
        if (Math.Abs(page.Width.Millimeter - entry.WidthMm) > 1 ||
            Math.Abs(page.Height.Millimeter - entry.HeightMm) > 1)
        {
            warnings.Add(
                $"{number}: хуудас эх хэмжээнээсээ өөр байрлалд зурагдсан тул ЕХ-ийн хүрээнд бичсэнгүй.");
            return;
        }

        WorkingDrawingFrontMatter matter = request.Project.Album.FrontMatter ?? new WorkingDrawingFrontMatter();
        IReadOnlyList<WorkingDrawingFrontMatterLayout.ListRow> rows =
            FrontMatterListRows(generated, request.Sections);
        var borderPen = new XPen(XColors.Black, Mm(0.35));
        var finePen = new XPen(XColors.Black, Mm(0.10));
        var paperBrush = new XSolidBrush(XColor.FromArgb(254, 254, 254));

        using XGraphics gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
        foreach (SheetFrontMatterArea frame in frames)
        {
            PageRectSpec? area = frame.Area;
            if (area is null)
                continue;
            var rect = new XRect(Mm(area.X), Mm(area.Y), Mm(area.Width), Mm(area.Height));
            gfx.DrawRectangle(paperBrush, rect);
            switch (frame.Kind)
            {
                case SheetFrontMatterKinds.DrawingList:
                    PaintFrontMatterTable(
                        gfx,
                        rect,
                        WorkingDrawingFrontMatterLayout.DrawingList(area.Width, area.Height, rows),
                        borderPen,
                        finePen,
                        warnings,
                        number,
                        WorkingDrawingFrontMatterLayout.DrawingListTitle);
                    break;
                case SheetFrontMatterKinds.AlbumComposition:
                    if (matter.Composition.Count == 0)
                        warnings.Add($"{number}: «{WorkingDrawingFrontMatterLayout.CompositionTitle}» бөглөөгүй.");
                    PaintFrontMatterTable(
                        gfx,
                        rect,
                        WorkingDrawingFrontMatterLayout.Composition(
                            area.Width,
                            area.Height,
                            matter.Composition,
                            mark => CountOfMark(rows, mark)),
                        borderPen,
                        finePen,
                        warnings,
                        number,
                        WorkingDrawingFrontMatterLayout.CompositionTitle);
                    break;
                case SheetFrontMatterKinds.TechnicalIndicators:
                    int blanks = WorkingDrawingFrontMatterLayout.BlankIndicatorCount(matter.Indicators);
                    if (matter.Indicators.Count == 0)
                        warnings.Add($"{number}: «{WorkingDrawingFrontMatterLayout.IndicatorsTitle}» бөглөөгүй.");
                    else if (blanks > 0)
                        warnings.Add(
                            $"{number}: «{WorkingDrawingFrontMatterLayout.IndicatorsTitle}»-д {blanks} утга хоосон ⚠.");
                    PaintFrontMatterTable(
                        gfx,
                        rect,
                        WorkingDrawingFrontMatterLayout.Indicators(area.Width, area.Height, matter.Indicators),
                        borderPen,
                        finePen,
                        warnings,
                        number,
                        WorkingDrawingFrontMatterLayout.IndicatorsTitle);
                    break;
                case SheetFrontMatterKinds.ExplanatoryNote:
                    DrawExplanatoryNote(gfx, rect, matter.ExplanatoryNote, warnings, number);
                    break;
            }
        }
    }

    /// <summary>
    /// A mark's pages in the album; null for a mark the album does not carry,
    /// which then stays blank rather than reading as zero.
    /// </summary>
    private static int? CountOfMark(IReadOnlyList<WorkingDrawingFrontMatterLayout.ListRow> rows, string mark)
    {
        string wanted = (mark ?? "").Trim();
        if (wanted.Length == 0)
            return null;
        int count = rows.Count(row => row.Mark.Equals(wanted, StringComparison.OrdinalIgnoreCase));
        return count > 0 ? count : null;
    }

    private static void PaintFrontMatterTable(
        XGraphics gfx,
        XRect rect,
        WorkingDrawingFrontMatterTable table,
        XPen borderPen,
        XPen finePen,
        List<string> warnings,
        string number,
        string title)
    {
        foreach (WorkingTitleBlockLine line in table.Lines)
        {
            gfx.DrawLine(
                finePen,
                rect.Left + Mm(line.X0Mm),
                rect.Top + Mm(line.Y0Mm),
                rect.Left + Mm(line.X1Mm),
                rect.Top + Mm(line.Y1Mm));
        }
        PaintWorkingTitleBlockCells(gfx, table.Cells, rect, 1, 1);
        if (table.Overflow > 0)
            warnings.Add($"{number}: «{title}» хүрээнд {table.Overflow} мөр багтсангүй.");
    }

    /// <summary>
    /// The note flows down the frame: its title, then each block - a heading
    /// line in capitals set bold, a paragraph wrapped to the width. The text
    /// shrinks to fit, down to <see cref="MinNoteTextMm"/>; below that the
    /// rest is reported, never cut without a word.
    /// </summary>
    private const double MinNoteTextMm = 1.8;

    private static void DrawExplanatoryNote(
        XGraphics gfx,
        XRect rect,
        string? note,
        List<string> warnings,
        string number)
    {
        string text = (note ?? "").Replace("\r\n", "\n").Trim();
        if (text.Length == 0)
        {
            warnings.Add($"{number}: «{WorkingDrawingFrontMatterLayout.NoteTitle}» бичигдээгүй.");
            return;
        }
        int pending = text.Count(character => character == '⚠');
        if (pending > 0)
            warnings.Add($"{number}: тайлбар бичигт бөглөөгүй {pending} хэсэг ⚠ байна.");

        double pad = Mm(1);
        double width = rect.Width - pad * 2;
        foreach (double size in new[] { 2.5, 2.2, 2.0, MinNoteTextMm })
        {
            List<(string Text, bool Bold, double Gap)> lines = NoteLines(gfx, text, width, size);
            double lineHeight = Mm(CoverLineHeightMm(size));
            double total = lines.Sum(line => lineHeight + line.Gap);
            bool fits = total <= rect.Height - pad * 2;
            if (!fits && size > MinNoteTextMm)
                continue;

            XFont regular = CreateCoverFont(size, false, WorkingDrawingFontName);
            XFont bold = CreateCoverFont(size, true, WorkingDrawingFontName);
            double y = rect.Top + pad;
            int drawnLines = 0;
            foreach ((string line, bool isBold, double gap) in lines)
            {
                y += gap;
                if (y + lineHeight > rect.Bottom - pad + 0.01)
                    break;
                gfx.DrawString(
                    line,
                    isBold ? bold : regular,
                    XBrushes.Black,
                    new XRect(rect.Left + pad, y, width, lineHeight),
                    XStringFormats.CenterLeft);
                y += lineHeight;
                drawnLines++;
            }
            if (drawnLines < lines.Count)
            {
                warnings.Add(
                    $"{number}: «{WorkingDrawingFrontMatterLayout.NoteTitle}» хүрээнд " +
                    $"{lines.Count - drawnLines} мөр багтсангүй.");
            }
            return;
        }
    }

    private static List<(string Text, bool Bold, double Gap)> NoteLines(
        XGraphics gfx,
        string text,
        double width,
        double sizeMm)
    {
        XFont regular = CreateCoverFont(sizeMm, false, WorkingDrawingFontName);
        XFont bold = CreateCoverFont(sizeMm, true, WorkingDrawingFontName);
        double blockGap = Mm(sizeMm * 0.6);
        var lines = new List<(string, bool, double)> { (WorkingDrawingFrontMatterLayout.NoteTitle, true, 0) };
        foreach (string block in text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            bool first = true;
            foreach (string raw in block.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0)
                    continue;
                bool heading = IsNoteHeading(line);
                foreach (string wrapped in WrapCoverText(gfx, line, heading ? bold : regular, width))
                {
                    lines.Add((wrapped, heading, first ? blockGap : 0));
                    first = false;
                }
            }
        }
        return lines;
    }

    /// <summary>A line with letters, all of them capitals.</summary>
    private static bool IsNoteHeading(string line) =>
        line.Any(char.IsLetter) && line.Where(char.IsLetter).All(char.IsUpper);
}
