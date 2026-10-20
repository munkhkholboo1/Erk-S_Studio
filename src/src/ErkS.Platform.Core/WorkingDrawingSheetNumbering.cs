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

    /// <summary>The n-th sheet of a mark, as it prints: «1», «2» …</summary>
    public static string Number(int ordinal) =>
        Math.Max(1, ordinal).ToString(CultureInfo.InvariantCulture);
}
