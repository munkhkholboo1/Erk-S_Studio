using System.Windows.Media;
using ErkS.Platform.Core;

namespace ErkS.Studio;

/// <summary>
/// What a sheet comment may be, said the same way the server says it. The kinds
/// and the two states are the program's own rule and travel with a release; the
/// words inside a comment are the participants'.
///
/// A comment anchors to the sheet, not to a page number. An album is rebuilt,
/// re-ordered and merged constantly - a comment that pointed at "page 7" would
/// be pointing at a different drawing by the afternoon.
/// </summary>
internal static class StudioSheetCommentRules
{
    public const string KindNote = "Note";
    public const string KindChangeRequired = "ChangeRequired";
    public const string KindApproved = "Approved";

    public const string StatusOpen = "Open";
    public const string StatusResolved = "Resolved";

    public const int MaximumBodyLength = 4000;

    /// <summary>The kinds in the order they are offered and read.</summary>
    public static IReadOnlyList<string> Kinds { get; } =
    [
        KindChangeRequired,
        KindNote,
        KindApproved,
    ];

    public static string KindLabel(string? kind) => Normalize(kind) switch
    {
        KindChangeRequired => "Засах шаардлагатай",
        KindApproved => "Зөвшөөрсөн",
        _ => "Тайлбар",
    };

    public static string StatusLabel(string? status) =>
        IsResolved(status) ? "Шийдэгдсэн" : "Нээлттэй";

    /// <summary>
    /// A colour per kind, used for the pin on the drawing and for the chip in
    /// the list, so the same comment is recognisable in both.
    /// </summary>
    public static Brush KindBrush(string? kind) => Normalize(kind) switch
    {
        KindChangeRequired => StudioTheme.DangerBrush,
        KindApproved => StudioTheme.SuccessBrush,
        _ => StudioTheme.AccentBrush,
    };

    public static string Normalize(string? kind)
    {
        string value = (kind ?? "").Trim();
        return Kinds.FirstOrDefault(item =>
            item.Equals(value, StringComparison.OrdinalIgnoreCase)) ?? KindNote;
    }

    public static bool IsResolved(string? status) =>
        (status ?? "").Trim().Equals(StatusResolved, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The durable name of the page a comment is placed on.
    ///
    /// A drawing is named by its sheet key, which the sheet keeps across
    /// re-exports of the same drawing. A page the album generates - a cover, a
    /// drawing list, a visualization - has no sheet, and is named by the key its
    /// own plan carries, which is just as durable. Both outlive a rebuild, a
    /// re-order and a change of format, which is the lifetime a comment needs.
    /// </summary>
    public static string PageIdentity(SheetRecord? sheet, string? generatedKey = null)
    {
        string key = (sheet?.Key ?? "").Trim();
        if (key.Length > 0)
            return "sheet:" + key.ToLowerInvariant();

        string generated = (generatedKey ?? "").Trim();
        return generated.Length == 0 ? "" : "generated:" + generated.ToLowerInvariant();
    }

    /// <summary>How the page is named to a reader, at the time of writing.</summary>
    public static string PageLabel(string? number, string? title)
    {
        string page = (number ?? "").Trim();
        string name = (title ?? "").Trim();
        if (page.Length == 0)
            return name;
        return name.Length == 0 ? page : page + " · " + name;
    }

    public static string CleanBody(string? value)
    {
        string body = (value ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        return body.Length <= MaximumBodyLength ? body : body[..MaximumBodyLength].TrimEnd();
    }

    /// <summary>
    /// The order the list reads in, matching the server: still open before
    /// settled, the more demanding kind first, then oldest first.
    /// </summary>
    public static IEnumerable<StudioSheetComment> InReadingOrder(
        IEnumerable<StudioSheetComment> comments) =>
        comments
            .OrderBy(item => IsResolved(item.Status) ? 1 : 0)
            .ThenBy(item => Kinds.ToList().IndexOf(Normalize(item.Kind)))
            .ThenBy(item => item.CreatedAtUtc);
}
