using System.Globalization;

namespace ErkS.Platform.Core;

/// <summary>
/// Working drawings are numbered by mark: every mark starts again at 1 -
/// ЕХ-1, ЕХ-2, ЕТ-1…ЕТ-4, БА-1…, ТХ-1… - and the corner table, the drawing
/// list and the note all carry the same number.
/// </summary>
/// <remarks>
/// 🔴 OWNER, 2026-10-20 (through the Master): «ЕХ-1-2, ЕТ-1-4, БА-1-... гэх
/// мэтээр дугаарлах ёстой. жишээ зурган дээр яг ийм байгаа. экспертүүд яг
/// ингэж хийхийг байнга шаарддаг». Batnasan printed ЕХ 00-01, ЕТ 02-05,
/// БА 11-57, ТХ 58-63: one running number and the Revit sheet numbers. The
/// source's number now only orders the sheets; it is not what prints. Concept
/// albums carry no mark and are not touched.
/// </remarks>
public static class WorkingDrawingSheetNumbering
{
    /// <summary>The general part: Studio's cover and list pages, and the 01.</summary>
    public const string GeneralPartMark = "ЕХ";

    public static bool Applies(AlbumDefinition? album) =>
        string.Equals(album?.TemplateId, BuildingWorkingDrawingAlbumTemplate.TemplateId, StringComparison.OrdinalIgnoreCase);

    /// <summary>The page's mark as it prints («БА», «ЕТ» …); blank when the source gave none.</summary>
    public static string MarkOf(string? discipline) =>
        (discipline ?? "").Trim().ToUpperInvariant();

    /// <summary>The number in the drawing list: «ЕТ-1»; a bare number when there is no mark.</summary>
    public static string Label(string? mark, string? number)
    {
        string value = (number ?? "").Trim();
        string prefix = MarkOf(mark);
        if (prefix.Length == 0 || value.Length == 0 ||
            value.StartsWith(prefix + "-", StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }
        return prefix + "-" + value;
    }

    /// <summary>
    /// A part's sheets in the order their source numbers them: one source's
    /// sheets stay together, where that source first appears, and run in
    /// their sheet-number order; a page whose number has no digits comes after
    /// the numbered ones, in its stored order.
    /// </summary>
    /// <remarks>
    /// 🔴 OWNER, 2026-10-20: «жишгийн дарааллаар 04 Өндөржилт, 05 Картограмм,
    /// 06 Ногоон болго». The DWG was renumbered and sent again - and the album
    /// kept 04 Ногоон, because a page kept the place it was first filed in.
    /// The source's number is what orders the sheets; the album's stored order
    /// only breaks ties.
    /// </remarks>
    public static List<AlbumBuildPage> OrderBySourceNumber(IReadOnlyList<AlbumBuildPage> pages)
    {
        var sourceRank = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (AlbumBuildPage page in pages)
        {
            string source = SourceKey(page);
            if (!sourceRank.ContainsKey(source))
                sourceRank[source] = sourceRank.Count;
        }

        return pages
            .Select((page, index) => (Page: page, Index: index))
            .OrderBy(item => sourceRank[SourceKey(item.Page)])
            .ThenBy(item => SheetNumberValue(item.Page.Sheet.Entry.Number) ?? double.MaxValue)
            .ThenBy(item => item.Index)
            .Select(item => item.Page)
            .ToList();
    }

    private static string SourceKey(AlbumBuildPage page) =>
        string.IsNullOrWhiteSpace(page.Sheet.SourceId)
            ? (page.Sheet.SourceIdentity ?? "").Trim()
            : page.Sheet.SourceId.Trim();

    /// <summary>«04» → 4, «ЕТ-05» → 5, «11a» → 11; null when the number has no digits.</summary>
    private static double? SheetNumberValue(string? number)
    {
        string text = (number ?? "").Trim();
        int start = -1;
        for (int i = 0; i < text.Length; i++)
        {
            if (char.IsDigit(text[i]))
            {
                start = i;
                break;
            }
        }
        if (start < 0)
            return null;
        int end = start;
        while (end < text.Length && char.IsDigit(text[end]))
            end++;
        return double.Parse(text[start..end], CultureInfo.InvariantCulture);
    }

    /// <summary>The n-th sheet of a mark, as it prints: «1», «2» …</summary>
    public static string Number(int ordinal) =>
        Math.Max(1, ordinal).ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Every source sheet's number as the album prints it: the source's own
    /// number (Revit «18») beside Studio's («БА-8»). A source writes its
    /// cross-references - a note's «see sheet …», a section head - from this,
    /// so they name the sheet the reader finds in the album (BN review,
    /// 2026-10-20, B: notes and section heads still cited Revit's numbers).
    /// Contract: _shared/sheet-reference-contract-2026-10-20.md.
    /// </summary>
    public static IReadOnlyList<SheetPrintNumber> PrintNumbers(AlbumBuildRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        bool marked = Applies(request.Project.Album);
        var result = new List<SheetPrintNumber>();
        foreach (AlbumBuildSection section in request.Sections)
        {
            foreach (AlbumBuildPage page in section.Pages)
            {
                if (page.Sheet is null)
                    continue;
                string mark = MarkOf(page.Sheet.Entry.Discipline);
                result.Add(new SheetPrintNumber(
                    page.Sheet.SourceId,
                    page.Sheet.Entry.SheetId,
                    (page.Sheet.Entry.Number ?? "").Trim(),
                    page.Title,
                    mark,
                    page.Number,
                    marked ? Label(mark, page.Number) : page.Number,
                    section.Title ?? ""));
            }
        }
        return result;
    }
}

/// <summary>A source sheet's own number and the number it prints under in the album.</summary>
public sealed record SheetPrintNumber(
    string SourceId,
    string SheetId,
    string SourceNumber,
    string SheetName,
    string Mark,
    string PrintNumber,
    string Label,
    string Section);
