using ErkS.Platform.Core;

namespace ErkS.Platform.Pdf;

/// <summary>
/// A working sheet's «ЗӨВШӨӨРӨЛЦСӨН» table as data: a title row across the
/// frame, then one row per section engineer - mark | name | signature - in
/// millimetres from the frame's top-left.
/// </summary>
/// <remarks>
/// The frame is the source's (Revit reserves it above the corner table and
/// says where in the manifest); its height and row count are Revit's decision,
/// so rows here are an even split of that height with one more for the title.
/// The owner's Batnasan sheets measured 6 rows of 5 mm under a 5 mm title.
///
/// The title follows the owner's own hand-drawn Batnasan table. The Eco Garden
/// reference labels the same block «Зөвшилцсөн»; the owner has not chosen
/// between them, so the word lives in one place.
/// </remarks>
internal static class ConsultationTableLayout
{
    public const string Title = "ЗӨВШӨӨРӨЛЦСӨН";

    private const double MarkShare = 0.15;
    private const double NameShare = 0.40;

    /// <summary>
    /// Where the table is drawn: immediately left of the corner table Studio
    /// draws, bottom edges aligned - the source decides only its size.
    /// </summary>
    /// <remarks>
    /// 🔴 BATNASAN, 2026-10-20 (owner: «зөвшөөрөлцсөн хүснэгт хуудас бүр дээр
    /// байрлал нь алдаатай байна»). The Revit frames came 3 mm below the
    /// corner table and 1 mm into it (crossing the sheet frame), and on some
    /// sheets above-right of it, overlapping its top. The corner table is
    /// Studio's, so the table beside it is placed from it: the owner's rule is
    /// «булангийн хүснэгтийн зүүн талын ЗӨВШӨӨРӨЛЦСӨН блок».
    /// </remarks>
    public static PageRectMm Place(PageRectMm corner, double widthMm, double heightMm)
    {
        ArgumentNullException.ThrowIfNull(corner);
        double width = Math.Max(0, widthMm);
        double height = Math.Max(0, heightMm);
        return new PageRectMm
        {
            X = corner.X - width,
            Y = corner.Y + corner.Height - height,
            Width = width,
            Height = height,
        };
    }

    /// <summary>Engineers the frame has no row for; the build reports them.</summary>
    public static int Overflow(int rows, IReadOnlyCollection<WorkingDrawingSectionConsentEntry>? sections) =>
        Math.Max(0, (sections?.Count ?? 0) - Math.Max(0, rows));

    public static IReadOnlyList<WorkingTitleBlockLine> Lines(double widthMm, double heightMm, int rows)
    {
        var lines = new List<WorkingTitleBlockLine>();
        if (rows <= 0 || widthMm <= 0 || heightMm <= 0)
            return lines;

        double rowHeight = heightMm / (rows + 1);
        for (int row = 1; row <= rows; row++)
            lines.Add(new(0, rowHeight * row, widthMm, rowHeight * row));
        double markRight = widthMm * MarkShare;
        double nameRight = widthMm * (MarkShare + NameShare);
        lines.Add(new(markRight, rowHeight, markRight, heightMm));
        lines.Add(new(nameRight, rowHeight, nameRight, heightMm));
        return lines;
    }

    public static IReadOnlyList<WorkingTitleBlockCell> Cells(
        double widthMm,
        double heightMm,
        int rows,
        IReadOnlyList<WorkingDrawingSectionConsentEntry>? sections)
    {
        var cells = new List<WorkingTitleBlockCell>();
        if (rows <= 0 || widthMm <= 0 || heightMm <= 0)
            return cells;

        double rowHeight = heightMm / (rows + 1);
        double textHeight = Math.Clamp(rowHeight * 0.45, 1.5, 2.5);
        double markWidth = widthMm * MarkShare;
        double nameWidth = widthMm * NameShare;
        cells.Add(new(0, 0, widthMm, rowHeight, Title, textHeight, Bold: true));

        IReadOnlyList<WorkingDrawingSectionConsentEntry> list = sections ?? [];
        for (int index = 0; index < Math.Min(rows, list.Count); index++)
        {
            WorkingDrawingSectionConsentEntry entry = list[index];
            double y = rowHeight * (index + 1);
            cells.Add(new(0, y, markWidth, rowHeight, (entry?.SectionMark ?? "").Trim(), textHeight, Bold: true));
            cells.Add(new(markWidth, y, nameWidth, rowHeight, (entry?.PersonName ?? "").Trim(), textHeight,
                Align: WorkingTitleBlockAlign.Left));
        }
        return cells;
    }
}
