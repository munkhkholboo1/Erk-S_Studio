using ErkS.Platform.Core;

namespace ErkS.Platform.Pdf;

/// <summary>A front-matter table as data, in millimetres from its frame's top-left.</summary>
internal sealed record WorkingDrawingFrontMatterTable(
    IReadOnlyList<WorkingTitleBlockCell> Cells,
    IReadOnlyList<WorkingTitleBlockLine> Lines,
    int Overflow);

/// <summary>
/// The three tables of the working drawings' 01 «ЕРӨНХИЙ ХЭСЭГ», laid out in
/// the frames Revit reserves there. The writer only paints them.
/// </summary>
/// <remarks>
/// 🔴 MEASURED FROM THE OWNER'S OWN SHEET: Batnasan 01, Arch_BN_SHEET_V1_
/// Corrected_Working page 1 (582 × 420), read at 70 dpi on 2026-10-20 - the
/// list is ≈103 mm wide with number | title | mark at ≈14 | 70 | 16 %, a title
/// row and a two-line header of ≈9 mm each over rows of ≈4.9 mm; the
/// composition is mark | section | count, the indicators № | indicator | unit
/// | value. The frame's size is Revit's; rows are an even split of what is
/// left, never taller than <see cref="MaxRowMm"/>.
/// </remarks>
internal static class WorkingDrawingFrontMatterLayout
{
    public const string DrawingListTitle = "Зургийн жагсаалт";
    public const string NoteTitle = "ТАЙЛБАР БИЧИГ";
    public const string CompositionTitle = "Ажлын зургийн иж бүрдлийн жагсаалт";
    public const string IndicatorsTitle = "Техник эдийн засгийн үзүүлэлт";

    public const double MaxRowMm = 7;
    // 2.5 rather than 3: the indicators frame Revit reserves (46 mm) took 12
    // rows at 3 mm and the 13th was dropped (PFR, Batnasan, 2026-10-20); the
    // text still fits at its 1.6 mm floor.
    public const double MinRowMm = 2.5;
    private const double HeaderMm = 8;

    public sealed record ListRow(string Number, string Title, string Mark);

    /// <summary>
    /// How many lines a text takes in a column (text, text height mm, column
    /// width mm). With one, a name too long for its column wraps and its row
    /// grows instead of the name shrinking out of sight (owner, 2026-10-20:
    /// «хуудасны нэр багтахгүй бол мөр нэмэгдэж байгаа зэрэг нь мэдэгддэг
    /// байх»). Without one, every row is one line.
    /// </summary>
    public delegate int LineCounter(string text, double textHeightMm, double widthMm);

    public static WorkingDrawingFrontMatterTable DrawingList(
        double widthMm,
        double heightMm,
        IReadOnlyList<ListRow> rows,
        string title = DrawingListTitle,
        LineCounter? measure = null)
    {
        double titleHeight = Math.Min(HeaderMm, heightMm / 4);
        double header = Math.Min(HeaderMm, heightMm / 4);
        double[] columns = [widthMm * 0.14, widthMm * 0.70, widthMm * 0.16];
        return Table(
            widthMm,
            heightMm,
            titleHeight,
            title,
            header,
            ["Хуудасны дугаар", "Хуудасны нэр", "Зургийн марк"],
            columns,
            rows.Select(row => new[] { row.Number, row.Title, row.Mark }).ToList(),
            leftAlignedColumn: 1,
            measure: measure);
    }

    public static WorkingDrawingFrontMatterTable Composition(
        double widthMm,
        double heightMm,
        IReadOnlyList<WorkingDrawingCompositionRow> rows,
        Func<string, int?> albumCountOfMark,
        LineCounter? measure = null)
    {
        double header = Math.Min(HeaderMm, heightMm / 3);
        double[] columns = [widthMm * 0.15, widthMm * 0.62, widthMm * 0.23];
        List<string[]> values = rows
            .Select(row => new[]
            {
                row.Mark,
                row.Name,
                row.SheetCount.Length > 0
                    ? row.SheetCount
                    : albumCountOfMark(row.Mark)?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "",
            })
            .ToList();
        // The title spans mark and section; the count column names itself.
        var table = Table(
            widthMm,
            heightMm,
            0,
            "",
            header,
            ["", "", "Хуудасны тоо"],
            columns,
            values,
            leftAlignedColumn: 1,
            headerSpanTitle: CompositionTitle,
            headerSpanColumns: 2,
            measure: measure);
        return table;
    }

    public static WorkingDrawingFrontMatterTable Indicators(
        double widthMm,
        double heightMm,
        IReadOnlyList<WorkingDrawingIndicatorRow> rows,
        LineCounter? measure = null)
    {
        double header = Math.Min(HeaderMm, heightMm / 3);
        double[] columns = [widthMm * 0.12, widthMm * 0.53, widthMm * 0.15, widthMm * 0.20];
        return Table(
            widthMm,
            heightMm,
            0,
            "",
            header,
            ["", "", "Хэмжих нэгж", "Хэмжээ"],
            columns,
            rows.Select(row => new[] { row.Number, row.Name, row.Unit, row.Value }).ToList(),
            leftAlignedColumn: 1,
            headerSpanTitle: IndicatorsTitle,
            headerSpanColumns: 2,
            measure: measure);
    }

    /// <summary>Indicators somebody named but nobody measured; the build reports them.</summary>
    public static int BlankIndicatorCount(IEnumerable<WorkingDrawingIndicatorRow>? rows) =>
        (rows ?? []).Count(row => !string.IsNullOrWhiteSpace(row.Name) && string.IsNullOrWhiteSpace(row.Value));

    private static WorkingDrawingFrontMatterTable Table(
        double widthMm,
        double heightMm,
        double titleMm,
        string title,
        double headerMm,
        IReadOnlyList<string> headers,
        IReadOnlyList<double> columns,
        IReadOnlyList<string[]> rows,
        int leftAlignedColumn,
        string headerSpanTitle = "",
        int headerSpanColumns = 0,
        LineCounter? measure = null)
    {
        var cells = new List<WorkingTitleBlockCell>();
        var lines = new List<WorkingTitleBlockLine>();
        if (widthMm <= 0 || heightMm <= 0)
            return new(cells, lines, rows.Count);

        double bodyTop = titleMm + headerMm;
        double available = Math.Max(0, heightMm - bodyTop);

        // A row is as many line slots as its wrapped name needs. The slot is
        // the frame shared out over all the slots, never taller than MaxRowMm
        // nor shorter than MinRowMm; the name is measured again at the text
        // height that slot gives.
        int[] lineCounts = rows.Select(_ => 1).ToArray();
        double rowHeight = MaxRowMm;
        double textHeight = 2.5;
        for (int pass = 0; pass < 3; pass++)
        {
            int slots = Math.Max(1, lineCounts.Sum());
            rowHeight = rows.Count == 0 ? MaxRowMm : Math.Clamp(available / slots, MinRowMm, MaxRowMm);
            textHeight = Math.Clamp(rowHeight * 0.5, 1.6, 2.5);
            if (measure is null || leftAlignedColumn < 0 || leftAlignedColumn >= columns.Count)
                break;
            for (int row = 0; row < rows.Count; row++)
            {
                string name = leftAlignedColumn < rows[row].Length ? (rows[row][leftAlignedColumn] ?? "").Trim() : "";
                lineCounts[row] = name.Length == 0
                    ? 1
                    : Math.Max(1, measure(name, textHeight, Math.Max(1, columns[leftAlignedColumn] - 1.2)));
            }
        }

        int drawn = 0;
        double used = 0;
        for (int row = 0; row < rows.Count; row++)
        {
            double height = rowHeight * lineCounts[row];
            if (used + height > available + 0.01)
                break;
            used += height;
            drawn++;
        }
        var rowTops = new double[drawn + 1];
        rowTops[0] = bodyTop;
        for (int row = 0; row < drawn; row++)
            rowTops[row + 1] = rowTops[row] + rowHeight * lineCounts[row];
        double tableBottom = rowTops[drawn];
        double headerText = Math.Clamp(headerMm * 0.32, 1.6, 2.5);

        // Outer frame and horizontal rules.
        lines.Add(new(0, 0, widthMm, 0));
        lines.Add(new(0, 0, 0, tableBottom));
        lines.Add(new(widthMm, 0, widthMm, tableBottom));
        if (titleMm > 0)
        {
            lines.Add(new(0, titleMm, widthMm, titleMm));
            cells.Add(new(0, 0, widthMm, titleMm, title, Math.Clamp(titleMm * 0.38, 1.8, 3), Bold: true));
        }
        lines.Add(new(0, bodyTop, widthMm, bodyTop));
        for (int row = 1; row <= drawn; row++)
            lines.Add(new(0, rowTops[row], widthMm, rowTops[row]));

        // Column rules: a spanning header title has none between the spanned columns.
        double x = 0;
        for (int column = 0; column < columns.Count - 1; column++)
        {
            x += columns[column];
            bool spanned = column < headerSpanColumns - 1;
            lines.Add(new(x, spanned ? bodyTop : titleMm, x, tableBottom));
        }

        if (headerSpanColumns > 0)
        {
            double spanWidth = columns.Take(headerSpanColumns).Sum();
            cells.Add(new(0, titleMm, spanWidth, headerMm, headerSpanTitle, headerText, Bold: true, Wrap: true));
        }
        x = 0;
        for (int column = 0; column < columns.Count; column++)
        {
            if (headers[column].Length > 0)
                cells.Add(new(x, titleMm, columns[column], headerMm, headers[column], headerText, Bold: true, Wrap: true));
            x += columns[column];
        }

        for (int row = 0; row < drawn; row++)
        {
            double y = rowTops[row];
            double height = rowTops[row + 1] - rowTops[row];
            x = 0;
            for (int column = 0; column < columns.Count; column++)
            {
                string text = column < rows[row].Length ? (rows[row][column] ?? "").Trim() : "";
                cells.Add(new(
                    x,
                    y,
                    columns[column],
                    height,
                    text,
                    textHeight,
                    Align: column == leftAlignedColumn ? WorkingTitleBlockAlign.Left : WorkingTitleBlockAlign.Center,
                    Wrap: column == leftAlignedColumn && lineCounts[row] > 1));
                x += columns[column];
            }
        }

        return new(cells, lines, rows.Count - drawn);
    }
}
