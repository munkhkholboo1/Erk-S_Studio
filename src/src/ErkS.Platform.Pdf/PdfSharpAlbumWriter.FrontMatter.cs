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
            .Select(plan => new WorkingDrawingFrontMatterLayout.ListRow(
                WorkingDrawingSheetNumbering.Label(FrontMatterMark, plan.Number),
                plan.Title,
                FrontMatterMark))
            .Concat(sections
                .SelectMany(section => section.Pages)
                .Select(page => new WorkingDrawingFrontMatterLayout.ListRow(
                    WorkingDrawingSheetNumbering.Label(page.Sheet.Entry.Discipline, page.Number),
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
        // Nothing of the front matter - paper, moved parts, blocks - reaches
        // the sheet's own lines: the drawing frame, the sheet title strip and
        // the corner table stay as the chrome drew them (owner, 2026-10-20:
        // «хуудасны зурааснууд яагаад арилаад байна вэ»).
        gfx.IntersectClip(FrontMatterClip(WorkingDrawingPageLayout.Resolve(buildPage.Format)));
        // The frames the source reserved are covered wherever the blocks end up:
        // they are reservations, like the corner table's.
        var reserved = new Dictionary<string, PageRectSpec>(StringComparer.Ordinal);
        foreach (SheetFrontMatterArea frame in frames)
        {
            if (frame.Area is null)
                continue;
            reserved[frame.Kind] = frame.Area;
            gfx.DrawRectangle(paperBrush, ReservedFrameCover(frame.Area, 0, 0));
        }

        DrawSourceMoves(gfx, buildPage.Sheet!, matter.SourceMoves, paperBrush, reserved.Values.ToList());
        DrawFrontMatterBlocks(gfx, FrontMatterBlocks(buildPage, reserved, matter), rows, matter, warnings, number, paperBrush);
    }

    /// <summary>
    /// The front matter alone, as it will print, on a page of the 01's size -
    /// for the layout editor to show the real text while the blocks are
    /// arranged (owner, 2026-10-20: «хэмжээг тохируулахад текст ямар байдалтай
    /// болох нь мэдэгдэхгүй байх нь эрсдэлтэй»). Returns what the build would
    /// say about it - a row that did not fit, a note cut short.
    /// </summary>
    public static IReadOnlyList<string> WriteFrontMatterPreview(
        AlbumBuildRequest request,
        AlbumBuildPage frontMatterPage,
        WorkingDrawingFrontMatter matter,
        IReadOnlyList<FrontMatterBlockLayout> blocks,
        string outputPath)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(frontMatterPage);
        var warnings = new List<string>();
        IReadOnlyList<ConceptGeneratedPagePlan> generated =
            BuildingArchitectureConceptGeneratedPagePlanner.Create(request.Project);
        IReadOnlyList<WorkingDrawingFrontMatterLayout.ListRow> rows =
            FrontMatterListRows(generated, request.Sections);
        using var document = new PdfDocument();
        PdfPage page = document.AddPage();
        page.Width = XUnit.FromMillimeter(Math.Max(10, frontMatterPage.Sheet.Entry.WidthMm));
        page.Height = XUnit.FromMillimeter(Math.Max(10, frontMatterPage.Sheet.Entry.HeightMm));
        using (XGraphics gfx = XGraphics.FromPdfPage(page))
        {
            DrawFrontMatterBlocks(
                gfx,
                blocks,
                rows,
                matter ?? new WorkingDrawingFrontMatter(),
                warnings,
                frontMatterPage.Number,
                new XSolidBrush(XColor.FromArgb(254, 254, 254)));
        }
        document.Save(outputPath);
        return warnings;
    }

    private static void DrawFrontMatterBlocks(
        XGraphics gfx,
        IEnumerable<FrontMatterBlockLayout> blocks,
        IReadOnlyList<WorkingDrawingFrontMatterLayout.ListRow> rows,
        WorkingDrawingFrontMatter matter,
        List<string> warnings,
        string number,
        XBrush paperBrush)
    {
        var borderPen = new XPen(XColors.Black, Mm(0.35));
        var finePen = new XPen(XColors.Black, Mm(0.10));
        WorkingDrawingFrontMatterLayout.LineCounter measure = (text, heightMm, widthMm) =>
            WrapCoverText(gfx, text, CreateCoverFont(heightMm, false, WorkingDrawingFontName), Mm(widthMm)).Count;
        foreach (FrontMatterBlockLayout block in blocks)
        {
            var rect = new XRect(Mm(block.X), Mm(block.Y), Mm(block.Width), Mm(block.Height));
            gfx.DrawRectangle(paperBrush, rect);
            var area = new PageRectSpec { X = block.X, Y = block.Y, Width = block.Width, Height = block.Height };
            switch (block.Kind)
            {
                case SheetFrontMatterKinds.DrawingList:
                    DrawDrawingListColumns(gfx, block, rows, borderPen, finePen, warnings, number, measure);
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
                            mark => CountOfMark(rows, mark),
                            AlbumCarriedCount(rows),
                            measure),
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
                        WorkingDrawingFrontMatterLayout.Indicators(area.Width, area.Height, matter.Indicators, measure),
                        borderPen,
                        finePen,
                        warnings,
                        number,
                        WorkingDrawingFrontMatterLayout.IndicatorsTitle);
                    break;
                case SheetFrontMatterKinds.ExplanatoryNote:
                    DrawExplanatoryNote(gfx, rect, matter.ExplanatoryNote, warnings, number, block.Columns, block.ColumnGapMm);
                    break;
            }
        }
    }

    /// <summary>
    /// The parts of the sheet's own drawing the owner moved: each one's old
    /// place is cleared, then the same vector page is drawn again shifted,
    /// clipped to the part's new place. The source PDF is not touched.
    /// </summary>
    /// <summary>The sheet's lines are kept clear of the front matter by this much.</summary>
    internal const double ChromeClearanceMm = 0.4;

    /// <summary>
    /// Where the front matter may draw: inside the drawing frame below the
    /// sheet title strip, outside the corner table, each kept
    /// <see cref="ChromeClearanceMm"/> clear of their lines.
    /// </summary>
    internal static XGraphicsPath FrontMatterClip(WorkingDrawingPageRegions regions)
    {
        PageRectMm inner = regions.EtalonInnerFrame;
        double c = ChromeClearanceMm;
        double left = inner.X + c;
        double top = WorkingDrawingFrontMatterPlacement.InnerTop(regions) + c;
        double right = inner.X + inner.Width - c;
        double bottom = inner.Y + inner.Height - c;
        var path = new XGraphicsPath { FillMode = XFillMode.Alternate };
        path.AddRectangle(new XRect(Mm(left), Mm(top), Mm(Math.Max(0, right - left)), Mm(Math.Max(0, bottom - top))));
        PageRectMm corner = regions.TitleBlockArea;
        double cl = Math.Max(left, corner.X - c);
        double ct = Math.Max(top, corner.Y - c);
        double cr = Math.Min(right, corner.X + corner.Width + c);
        double cb = Math.Min(bottom, corner.Y + corner.Height + c);
        if (cr > cl && cb > ct)
            path.AddRectangle(new XRect(Mm(cl), Mm(ct), Mm(cr - cl), Mm(cb - ct)));
        return path;
    }

    /// <summary>
    /// A reserved frame's paper, a little wider than the frame so the source's
    /// outline - drawn on the frame's edge - goes under it too.
    /// </summary>
    internal const double ReservedFrameBleedMm = 0.5;

    private static XRect ReservedFrameCover(PageRectSpec frame, double offsetX, double offsetY) => new(
        Mm(frame.X + offsetX - ReservedFrameBleedMm),
        Mm(frame.Y + offsetY - ReservedFrameBleedMm),
        Mm(frame.Width + 2 * ReservedFrameBleedMm),
        Mm(frame.Height + 2 * ReservedFrameBleedMm));

    private static void DrawSourceMoves(
        XGraphics gfx,
        SheetRecord sheet,
        IReadOnlyList<SourceRegionMove> moves,
        XBrush paperBrush,
        IReadOnlyList<PageRectSpec>? reservedFrames = null)
    {
        List<SourceRegionMove> active = (moves ?? [])
            .Where(move => move.Width >= 1 && move.Height >= 1 &&
                           (Math.Abs(move.OffsetX) > 0.01 || Math.Abs(move.OffsetY) > 0.01))
            .ToList();
        if (active.Count == 0 || !File.Exists(sheet.PdfPath))
            return;

        using XPdfForm form = XPdfForm.FromFile(sheet.PdfPath);
        form.PageNumber = Math.Max(1, sheet.Entry.PdfPageNumber);
        double width = Math.Max(1, form.PointWidth);
        double height = Math.Max(1, form.PointHeight);
        PdfRectangle mediaBox = form.Page?.MediaBox ??
                                new PdfRectangle(new XPoint(0, 0), new XPoint(width, height));

        foreach (SourceRegionMove move in active)
            gfx.DrawRectangle(paperBrush, new XRect(Mm(move.X), Mm(move.Y), Mm(move.Width), Mm(move.Height)));

        foreach (SourceRegionMove move in active)
        {
            var target = new XRect(Mm(move.X + move.OffsetX), Mm(move.Y + move.OffsetY), Mm(move.Width), Mm(move.Height));
            XGraphicsState state = gfx.Save();
            gfx.IntersectClip(target);
            gfx.DrawRectangle(paperBrush, target);
            XRect desired = new(Mm(move.OffsetX), Mm(move.OffsetY), width, height);
            gfx.DrawImage(form, CalculatePdfSharpFormDrawRectangle(desired, width, height, mediaBox.X1, mediaBox.Y1));
            // A part moved with a reserved frame's outline in it does not
            // carry the outline along (owner, 2026-10-20: «ийм илүү дутуу
            // зурааснуудыг байхгүй болгоорой»).
            foreach (PageRectSpec frame in reservedFrames ?? [])
                gfx.DrawRectangle(paperBrush, ReservedFrameCover(frame, move.OffsetX, move.OffsetY));
            gfx.Restore(state);
        }
    }

    /// <summary>
    /// Where the 01's blocks stand: the owner's arrangement, else the default
    /// (WorkingDrawingFrontMatterPlacement) - computed from the same corner
    /// table the sheet chrome draws.
    /// </summary>
    internal static IReadOnlyList<FrontMatterBlockLayout> FrontMatterBlocks(
        AlbumBuildPage buildPage,
        IReadOnlyDictionary<string, PageRectSpec> reserved,
        WorkingDrawingFrontMatter matter)
    {
        WorkingDrawingPageRegions regions = WorkingDrawingPageLayout.Resolve(buildPage.Format);
        double innerTop = WorkingDrawingFrontMatterPlacement.InnerTop(regions);
        return WorkingDrawingFrontMatterPlacement.Resolve(
            reserved,
            regions.TitleBlockArea,
            innerTop,
            matter.Layout);
    }

    /// <summary>
    /// The drawing list set in the block's columns: the rows are shared out
    /// evenly, every column carries its own header, the title heads the first.
    /// </summary>
    private static void DrawDrawingListColumns(
        XGraphics gfx,
        FrontMatterBlockLayout block,
        IReadOnlyList<WorkingDrawingFrontMatterLayout.ListRow> rows,
        XPen borderPen,
        XPen finePen,
        List<string> warnings,
        string number,
        WorkingDrawingFrontMatterLayout.LineCounter? measure = null)
    {
        int columns = Math.Clamp(block.Columns, 1, FrontMatterBlockLayout.MaxColumns);
        double columnWidth = block.ColumnWidthMm;
        IReadOnlyList<WorkingDrawingFrontMatterTable> tables =
            WorkingDrawingFrontMatterLayout.DrawingListColumns(columnWidth, block.Height, rows, columns, measure);
        for (int column = 0; column < tables.Count; column++)
        {
            double x = block.X + column * (columnWidth + block.ColumnGapMm);
            var rect = new XRect(Mm(x), Mm(block.Y), Mm(columnWidth), Mm(block.Height));
            PaintFrontMatterTable(
                gfx,
                rect,
                tables[column],
                borderPen,
                finePen,
                warnings,
                number,
                WorkingDrawingFrontMatterLayout.DrawingListTitle);
        }
    }

    /// <summary>
    /// A mark's pages in the album; null for a mark the album does not carry,
    /// which then stays blank rather than reading as zero.
    /// </summary>
    /// <summary>
    /// The count a composition row prints for a mark this album carries: the
    /// whole album for its main mark (the set the album is - БА carries the
    /// album's ЕХ, ЕТ and ТХ pages too), the mark's own pages for any other.
    /// A typed count is the owner's for sets the album does not carry; for its
    /// own the album is the fact (BN review, 2026-10-20, C4: «БА 59» stayed
    /// after the album grew to 61).
    /// </summary>
    private static Func<string, int?> AlbumCarriedCount(IReadOnlyList<WorkingDrawingFrontMatterLayout.ListRow> rows)
    {
        string? main = rows
            .GroupBy(row => (row.Mark ?? "").Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Key.Length > 0 && group.Key != WorkingDrawingSheetNumbering.GeneralPartMark)
            .OrderByDescending(group => group.Count())
            .Select(group => group.Key)
            .FirstOrDefault();
        return mark =>
        {
            string wanted = (mark ?? "").Trim();
            if (wanted.Length == 0)
                return null;
            if (main is not null && wanted.Equals(main, StringComparison.OrdinalIgnoreCase))
                return rows.Count;
            return CountOfMark(rows, wanted);
        };
    }

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
        string number,
        int columns = 1,
        double columnGapMm = 0)
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
        text = PrintableNote(text);

        double pad = Mm(1);
        int columnCount = Math.Clamp(columns, 1, FrontMatterBlockLayout.MaxColumns);
        double gap = Mm(Math.Max(0, columnGapMm));
        double columnWidth = Math.Max(Mm(5), (rect.Width - gap * (columnCount - 1)) / columnCount);
        double width = columnWidth - pad * 2;
        double columnHeight = rect.Height - pad * 2;
        foreach (double size in new[] { 2.5, 2.2, 2.0, MinNoteTextMm })
        {
            List<(string Text, bool Bold, double Gap)> lines = NoteLines(gfx, text, width, size);
            double lineHeight = Mm(CoverLineHeightMm(size));
            bool fits = FlowNote(lines, lineHeight, columnHeight, columnCount, null) == lines.Count;
            if (!fits && size > MinNoteTextMm)
                continue;

            XFont regular = CreateCoverFont(size, false, WorkingDrawingFontName);
            XFont bold = CreateCoverFont(size, true, WorkingDrawingFontName);
            int drawnLines = FlowNote(lines, lineHeight, columnHeight, columnCount, (line, column, offset) =>
                gfx.DrawString(
                    line.Text,
                    line.Bold ? bold : regular,
                    XBrushes.Black,
                    new XRect(rect.Left + column * (columnWidth + gap) + pad, rect.Top + pad + offset, width, lineHeight),
                    XStringFormats.CenterLeft));
            if (drawnLines < lines.Count)
            {
                warnings.Add(
                    $"{number}: «{WorkingDrawingFrontMatterLayout.NoteTitle}» хүрээнд " +
                    $"{lines.Count - drawnLines} мөр багтсангүй.");
            }
            return;
        }
    }

    /// <summary>
    /// The note as it prints: a «⚠» left for the owner to fill becomes the
    /// placeholder «……». The sheet font has no ⚠ glyph, so it printed as «□»
    /// (PFR, Batnasan, 2026-10-20); a blank to fill is written «……» on a
    /// Mongolian sheet. The build warning still counts the ⚠ in the source.
    /// </summary>
    /// <summary>
    /// Pours the lines down the columns in turn; a block's gap at the head of
    /// a column is dropped. Returns how many lines found a place.
    /// </summary>
    private static int FlowNote(
        List<(string Text, bool Bold, double Gap)> lines,
        double lineHeight,
        double columnHeight,
        int columns,
        Action<(string Text, bool Bold, double Gap), int, double>? draw)
    {
        int column = 0;
        double y = 0;
        int placed = 0;
        foreach (var line in lines)
        {
            double gapBefore = y > 0 ? line.Gap : 0;
            if (y + gapBefore + lineHeight > columnHeight + 0.01)
            {
                column++;
                y = 0;
                gapBefore = 0;
                if (column >= columns)
                    break;
            }
            y += gapBefore;
            draw?.Invoke(line, column, y);
            y += lineHeight;
            placed++;
        }
        return placed;
    }

    internal static string PrintableNote(string text) =>
        (text ?? "").Replace("⚠", "……", StringComparison.Ordinal);

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
