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
/// 🔴 THE RANK IS DERIVED FROM THE COMPOSITION, NOT FROM A LIST KEPT HERE.
///
/// It used to be a table of six Mongolian strings, and that table was a SECOND
/// answer to a question the album template already answers. The two did not
/// accept the same words: the template's matcher takes a slot id
/// («floor-plans»), a declared kind («Давхрын байгуулалт»), a discipline or a
/// sheet name, while the table took only the six Mongolian strings. A sheet
/// declaring its kind as a slot id therefore landed in the RIGHT SLOT and got
/// the WRONG RANK - rank «unclassified», sorting it to the end of its building
/// while the album showed it under the correct heading. Nothing reported the
/// contradiction, because each half was individually behaving as written.
///
/// Asking the template and taking the slot's own Order removes the second list
/// entirely, so the two cannot drift apart again. That is the structural form
/// of the fix; keeping both lists in step by hand is the form that gets
/// forgotten.
///
/// WHAT CHANGED WHEN THIS MOVED, measured rather than assumed. The four
/// building kinds keep their exact relative order (ranks 1·2·3·4 became slot
/// Orders 9·10·11·12 - the same sequence). Two entries genuinely differ:
///
///   * «Ерөнхий төлөвлөгөө» ranked LAST in the table and is slot 8 in the
///     composition, so it now sorts BEFORE the building drawings when the two
///     ever meet in one group;
///   * «Ерөнхий хэсэг» has no source slot at all - it is a SECTION title
///     carried by two generated pages - so a source sheet declaring it is now
///     reported unclassified instead of quietly ranked fifth. That is the more
///     honest answer: there is nowhere in the composition for such a sheet to
///     go, and saying so is what lets someone fix it.
///
/// On the project this was measured against, both differences move ZERO pages:
/// no sorting group there mixes a general-plan kind with a building kind.
/// </summary>
public static class BuildingPageTypeOrder
{
    /// <summary>
    /// Where a drawing of this kind belongs among a building's pages. Lower
    /// comes first.
    /// </summary>
    /// <remarks>
    /// Anything the composition cannot place sorts last rather than into the
    /// middle. A sheet whose kind nobody declared must not push a known one out
    /// of position: placed at the end it is visible, and the numbering of
    /// everything before it is unchanged.
    /// </remarks>
    public static int Of(
        AlbumDefinition definition,
        string? contentKind,
        string? discipline = null,
        string? sheetName = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        AlbumCompositionItem? slot = BuildingArchitectureConceptAlbumTemplate.FindSourceSlot(
            definition,
            contentKind,
            discipline,
            sheetName);
        return slot?.Order ?? Unclassified;
    }

    /// <summary>
    /// The place a sheet gets when the composition has nowhere to put it -
    /// whether its kind was unknown, declared «Ангилаагүй», or names something
    /// this album has no slot for. Deliberately one rank for all of them:
    /// Studio cannot tell them apart, and pretending otherwise would put a
    /// guess into a printed set.
    ///
    /// int.MaxValue rather than a round number, so it cannot collide with a
    /// slot Order in a composition that grows.
    /// </summary>
    public const int Unclassified = int.MaxValue;

    /// <summary>
    /// Whether this album has nowhere to place a sheet of this kind.
    ///
    /// 🔴 THIS EXISTS TO BE SHOWN TO SOMEONE. For a long time it had no caller
    /// outside its own test: the rank was applied silently, and a person whose
    /// page sat at the end of a section had no way at all to learn why. A rule
    /// nobody asks is the same as a rule nobody wrote.
    /// </summary>
    public static bool IsUnclassified(
        AlbumDefinition definition,
        string? contentKind,
        string? discipline = null,
        string? sheetName = null) =>
        Of(definition, contentKind, discipline, sheetName) == Unclassified;
}
