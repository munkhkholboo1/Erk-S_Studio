using ErkS.Platform.Contracts;

namespace ErkS.Platform.Core;

/// <summary>
/// A delivered sheet that stands in for a page Studio composes itself.
/// </summary>
/// <remarks>
/// The contract says producers must not deliver these, and both producers now
/// filter them out by exact title. Exact matching is the right choice on their
/// side - the looser tests they could have used would have silently swallowed
/// unrelated sheets, one of them every elevation in the album - but it leaves
/// one thing open: a sheet a person named by hand, close but not identical.
/// "ЗУРГИЙН ЖАГСААЛТ ТАЙЛБАР БИЧИГ" without its comma passes every producer
/// filter and lands beside the page Studio drew.
///
/// That last gap can only be closed by the receiving side, so it is closed
/// here, and deliberately more loosely than the producers match: punctuation,
/// casing and repeated spaces are ignored, because those are exactly the ways a
/// hand-typed title differs from the canonical one.
///
/// The sheet is not rejected and does not vanish. It stays in the library and
/// in the source list, where its owner can see it; what it does not get is an
/// album page, because the album already has that page. Anything else recreates
/// the duplicate this exists to prevent.
/// </remarks>
public static class StudioComposedPageCollision
{
    /// <summary>
    /// The Studio-composed slot this entry would duplicate, or null when it
    /// duplicates none.
    /// </summary>
    public static AlbumCompositionItem? Find(AlbumDefinition? album, SheetPackageEntry? entry)
    {
        if (album is null || entry is null)
            return null;

        List<AlbumCompositionItem> composed = album.Composition
            .Where(item => item.Kind == AlbumCompositionKind.Generated)
            .ToList();
        if (composed.Count == 0)
            return null;

        // A producer that names the slot outright is the unambiguous case, and
        // the only one where no title comparison is needed.
        string declared = (entry.TemplateSlotId ?? "").Trim();
        if (declared.Length > 0)
        {
            AlbumCompositionItem? byId = composed.FirstOrDefault(item =>
                item.Id.Equals(declared, StringComparison.OrdinalIgnoreCase));
            if (byId is not null)
                return byId;
        }

        string name = Normalize(entry.Name);
        if (name.Length == 0)
            return null;

        return composed.FirstOrDefault(item => Normalize(item.Title).Equals(name, StringComparison.Ordinal));
    }

    /// <summary>What to tell the person who delivered it.</summary>
    public static string Describe(SheetPackageEntry entry, AlbumCompositionItem slot)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(slot);

        string number = (entry.Number ?? "").Trim();
        string label = number.Length > 0 ? $"{number} {entry.Name}".Trim() : (entry.Name ?? "").Trim();
        return $"«{label}» нь Studio-гийн өөрөө үүсгэдэг «{slot.Title}» хуудастай "
            + "давхцаж байгаа тул альбомд орсонгүй. Эх үүсвэрийн жагсаалтад хэвээр байна.";
    }

    /// <summary>
    /// Casing, punctuation and repeated spaces removed - the differences a
    /// title picks up when a person retypes it rather than copies it.
    /// </summary>
    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var builder = new System.Text.StringBuilder(value.Length);
        bool pendingSpace = false;
        foreach (char character in value.Trim().ToUpperInvariant())
        {
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (char.IsPunctuation(character) || char.IsSymbol(character))
                continue;

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }
}
