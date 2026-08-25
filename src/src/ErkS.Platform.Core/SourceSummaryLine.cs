namespace ErkS.Platform.Core;

/// <summary>
/// The second line of a source row: the facts, with the ones the row already
/// shows elsewhere taken out.
/// </summary>
/// <remarks>
/// These were stored as one pipe-separated string built for a single-line
/// label - "Revit | Архитектур | Локал | Холбогдсон | Альбум #3". Read as a
/// row it gave every fact equal weight and ran off the edge of the column.
///
/// Two of those facts are now shown by other parts of the row: the kind badge
/// on the left, and the owner heading above the group. Repeating them here
/// spends line width on what the reader has already taken in - and width is
/// exactly what was short.
/// </remarks>
public static class SourceSummaryLine
{
    public static string Compose(
        string? detail,
        string? categoryLabel = null,
        string? application = null)
    {
        string category = (categoryLabel ?? "").Trim();
        string app = (application ?? "").Trim();

        return string.Join(
            " · ",
            (detail ?? "")
                .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                // An address is the owner's, and the group heading carries it.
                // It is also the longest thing on most rows.
                .Where(part => !part.Contains('@', StringComparison.Ordinal))
                .Where(part => category.Length == 0 ||
                    !part.Equals(category, StringComparison.OrdinalIgnoreCase))
                .Where(part => app.Length == 0 ||
                    !part.Equals(app, StringComparison.OrdinalIgnoreCase)));
    }
}
