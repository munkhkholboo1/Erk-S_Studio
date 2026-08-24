namespace ErkS.Platform.Core;

/// <summary>
/// The order a building's drawings run in, regardless of which product drew
/// them.
///
/// One building's sheets can arrive from two places at once - AutoCAD sends
/// the floor plans, Revit sends the sections and elevations - and each numbers
/// its own set from one. Left to itself the album would group them by source,
/// so whichever package was registered first would come first, and the same
/// building would read differently depending on the order two people happened
/// to press export.
///
/// The client stated the rule: «энэ тохиолдолд хуудасны төрлөөр студио
/// дарааллаа хадгална … студио Байгуулалтын хуудаснуудыг огтлол болон нүүр
/// талуудын өмнө оруулдаг.» Ordering is Studio's job precisely because it is
/// the only side that sees both.
///
/// The names are the categories AutoCAD and Revit both declare, confirmed
/// word for word with both products before this was written.
/// </summary>
public static class BuildingPageTypeOrder
{
    /// <summary>
    /// Where a drawing of this kind belongs among a building's pages. Lower
    /// comes first.
    /// </summary>
    /// <remarks>
    /// Anything unrecognised sorts last rather than into the middle. A sheet
    /// whose kind nobody declared must not push a known one out of position:
    /// placed at the end it is visible, and the numbering of everything before
    /// it is unchanged.
    /// </remarks>
    public static int Of(string? contentKind)
    {
        string kind = (contentKind ?? "").Trim();
        return kind switch
        {
            "Давхрын байгуулалт" => 1,
            "Огтлол" => 2,
            "Нүүр тал" => 3,
            "Харагдах байдал" => 4,
            "Ерөнхий хэсэг" => 5,
            "Ерөнхий төлөвлөгөө" => 6,
            _ => Unclassified,
        };
    }

    /// <summary>
    /// The place a sheet gets when its kind is unknown or was declared
    /// «Ангилаагүй». Deliberately the same rank for both: Studio cannot tell
    /// the two apart, and pretending otherwise would put a guess into a
    /// printed set.
    /// </summary>
    public const int Unclassified = 99;

    /// <summary>Whether a sheet of this kind sorted to the end for want of a kind.</summary>
    public static bool IsUnclassified(string? contentKind) => Of(contentKind) == Unclassified;
}
