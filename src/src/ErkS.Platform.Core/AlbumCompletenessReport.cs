using ErkS.Platform.Contracts;

namespace ErkS.Platform.Core;

/// <summary>One page the album could not place, and where it came from.</summary>
public sealed record UnplacedAlbumPage(
    string SheetKey,
    string SheetNumber,
    string SheetName,
    string DeclaredKind,
    SheetSourceApplication Application)
{
    /// <summary>
    /// The program this sheet was drawn in, named the way the person who drew
    /// it would name it. This is not decoration: it is the whole difference
    /// between "something is wrong" and "go here and fix it".
    /// </summary>
    public string ApplicationMn => Application switch
    {
        SheetSourceApplication.AutoCad => "AutoCAD",
        SheetSourceApplication.Revit => "Revit",
        SheetSourceApplication.CityGen => "CityGen",
        SheetSourceApplication.Pdf => "PDF",
        _ => "гараар нэмсэн",
    };

    public string LabelMn =>
        string.IsNullOrWhiteSpace(SheetNumber) ? SheetName : $"{SheetNumber} · {SheetName}";
}

/// <summary>A section of the album that has no page in it.</summary>
public sealed record EmptyAlbumSlot(string Number, string Title);

/// <summary>
/// What this album knows about itself but was not saying.
///
/// 🔴 THE COMPLAINT THIS ANSWERS WAS NOT "THE ORDER IS WRONG". It was «яг
/// яахаар нь зөв нь үүсээд байгаа нь мэдэгдэхгүй. буруу нь яагаад үүсээд
/// байгаа нь мэдэгдэхгүй байна» - the person could not find out WHY. Traced to
/// their own project, one page explained three separate complaints: a sheet
/// created by AutoCAD's new-sheet command, never named and never given a
/// category, which the album therefore could not place and put at the end of
/// its section. Every fact needed to say so was already in memory. Nothing
/// asked for it: the rank that decided the page's fate had no caller outside
/// its own test, and the composition's fill count was shown as «10/13» with no
/// way to learn which three were the missing ones.
///
/// 🔴 EVERY LINE HERE NAMES THE NEXT ACTION. "3 pages unclassified" is half a
/// sentence - it tells someone they have a problem and not what to do with it.
/// Each unplaced page therefore carries the program it was drawn in, so the
/// message can end where the fix is: give it a category in AutoCAD, and it
/// lands in the right place by itself.
/// </summary>
public sealed record AlbumCompletenessReport(
    IReadOnlyList<UnplacedAlbumPage> UnplacedPages,
    IReadOnlyList<EmptyAlbumSlot> EmptySlots,
    int FilledSlotCount,
    int TotalSlotCount)
{
    public bool HasSomethingToSay => UnplacedPages.Count > 0 || EmptySlots.Count > 0;

    /// <summary>
    /// The unplaced pages, as one sentence that ends with the fix.
    ///
    /// Empty when there is nothing to report - a message on every visit is how
    /// people learn to stop reading messages.
    /// </summary>
    public string UnplacedNoticeMn
    {
        get
        {
            if (UnplacedPages.Count == 0)
                return "";

            IReadOnlyList<string> programs = UnplacedPages
                .Select(page => page.ApplicationMn)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();

            string examples = string.Join(
                ", ",
                UnplacedPages.Take(3).Select(page => "«" + page.LabelMn + "»"));
            string more = UnplacedPages.Count > 3
                ? $" ба бусад {UnplacedPages.Count - 3}"
                : "";

            return
                $"{UnplacedPages.Count} хуудсын зургийн төрөл тодорхойгүй тул " +
                $"хэсгийнхээ төгсгөлд орлоо: {examples}{more}. " +
                $"{string.Join(" / ", programs)} дээр эдгээр хуудсанд зургийн төрөл " +
                "өгөөд дахин илгээвэл өөрсдөө зөв байрандаа орно.";
        }
    }

    /// <summary>
    /// Which sections are empty, BY NAME.
    ///
    /// The count alone («бүрдэл 10/13») is a number a person cannot act on:
    /// they can see that three are missing and not which three, so they cannot
    /// tell whether it matters. The names are already known here.
    /// </summary>
    public string EmptySlotsNoticeMn
    {
        get
        {
            if (EmptySlots.Count == 0)
                return "";

            string names = string.Join(
                ", ",
                EmptySlots.Select(slot => $"{slot.Number} {slot.Title}"));
            return
                $"Альбомын бүрдэл {FilledSlotCount}/{TotalSlotCount}. " +
                $"Хуудас ороогүй хэсэг: {names}.";
        }
    }

    /// <summary>
    /// Builds the report from what the album and library already hold.
    /// </summary>
    /// <remarks>
    /// GENERATED SLOTS ARE NOT COUNTED AS MISSING. Studio draws those itself at
    /// build time and they hold no page beforehand, so calling them empty would
    /// report a problem that does not exist - and burying three real gaps among
    /// four false ones is the same as reporting nothing.
    /// </remarks>
    public static AlbumCompletenessReport Create(
        AlbumDefinition definition,
        SheetLibrary library)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(library);

        var unplaced = new List<UnplacedAlbumPage>();
        foreach (AlbumPageDefinition page in definition.Pages)
        {
            SheetRecord? sheet = library.FindVerified(page.SheetKey);
            SheetPackageEntry entry = sheet?.Entry ?? new SheetPackageEntry();
            string kind = AlbumPageSourceMetadata.ResolveContentKind(page, entry);

            if (!BuildingPageTypeOrder.IsUnclassified(
                    definition,
                    kind,
                    entry.Discipline,
                    entry.Name))
            {
                continue;
            }

            unplaced.Add(new UnplacedAlbumPage(
                page.SheetKey,
                entry.Number ?? "",
                string.IsNullOrWhiteSpace(entry.Name) ? page.SheetKey : entry.Name,
                kind,
                sheet?.Source.Application ?? SheetSourceApplication.Manual));
        }

        var sourceSlots = definition.Composition
            .Where(item => item.Kind == AlbumCompositionKind.SourceSlot)
            .OrderBy(item => item.Order)
            .ToList();
        var occupied = definition.Pages
            .Select(page => page.TemplateSlotId ?? "")
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var empty = sourceSlots
            .Where(slot => !occupied.Contains(slot.Id))
            .Select(slot => new EmptyAlbumSlot(slot.Number, slot.Title))
            .ToList();

        return new AlbumCompletenessReport(
            unplaced,
            empty,
            sourceSlots.Count - empty.Count,
            sourceSlots.Count);
    }
}
