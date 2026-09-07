namespace ErkS.Platform.Core.Tests;

/// <summary>
/// The order a building's drawings run in.
///
/// One building's sheets arrive from two products at once - AutoCAD sends the
/// floor plans, Revit the sections and elevations - and each numbers its own
/// set from one. Grouped by source, the same building would read differently
/// depending on which of two people pressed export first.
///
/// The client's rule: «энэ тохиолдолд хуудасны төрлөөр студио дарааллаа
/// хадгална … студио Байгуулалтын хуудаснуудыг огтлол болон нүүр талуудын
/// өмнө оруулдаг.» Studio orders because it is the only side that sees both.
///
/// 🔴 THESE TESTS WERE REWRITTEN WHEN THE RANK STOPPED BEING A LIST. They used
/// to assert six hard-coded Mongolian strings against six hard-coded numbers,
/// which meant they passed happily while a SECOND matcher - the album
/// template's - accepted a wider vocabulary and disagreed with them. A sheet
/// declaring «floor-plans» got the right slot and the wrong rank, and nothing
/// here could see it, because these tests only ever asked the list about words
/// the list already knew.
///
/// What is asserted now is the PROPERTY the client stated - plans, then
/// sections, then elevations - and the agreement between the rank and the slot,
/// which is the thing that was actually broken.
/// </summary>
public sealed class BuildingPageTypeOrderTests
{
    private static AlbumDefinition Concept()
    {
        var definition = new AlbumDefinition();
        BuildingArchitectureConceptAlbumTemplate.Ensure(definition);
        return definition;
    }

    [Fact]
    public void PlansComeBeforeSectionsWhichComeBeforeElevations()
    {
        // The client's sentence, as a check. It survives the move because it is
        // a statement about ORDER, not about particular numbers.
        AlbumDefinition definition = Concept();

        Assert.True(
            BuildingPageTypeOrder.Of(definition, "Давхрын байгуулалт") <
            BuildingPageTypeOrder.Of(definition, "Огтлол"));
        Assert.True(
            BuildingPageTypeOrder.Of(definition, "Огтлол") <
            BuildingPageTypeOrder.Of(definition, "Нүүр тал"));
        Assert.True(
            BuildingPageTypeOrder.Of(definition, "Нүүр тал") <
            BuildingPageTypeOrder.Of(definition, "Харагдах байдал"));
    }

    [Fact]
    public void THERankAgreesWithTheSLOTTheSheetIsPlacedIn()
    {
        // 🔴 THE ASSERTION THE WHOLE CHANGE EXISTS FOR. Rank and slot are now
        // one answer; this fails the moment they become two again.
        AlbumDefinition definition = Concept();

        foreach (string kind in new[]
        {
            "Давхрын байгуулалт",
            "Огтлол",
            "Нүүр тал",
            "Харагдах байдал",
        })
        {
            AlbumCompositionItem? slot =
                BuildingArchitectureConceptAlbumTemplate.FindSourceSlot(definition, kind, null, null);
            Assert.NotNull(slot);
            Assert.Equal(slot!.Order, BuildingPageTypeOrder.Of(definition, kind));
        }
    }

    [Fact]
    public void ASlotIDRanksTheSAMEAsTheKindThatSlotHolds()
    {
        // The defect that motivated the change, stated directly. AutoCAD and
        // Revit both declare a sheet's kind, and one of the forms they use is
        // the slot id itself. Under the old list that form was unrecognised and
        // the sheet fell to the end of its building - while the album placed it
        // under the correct heading.
        AlbumDefinition definition = Concept();

        Assert.Equal(
            BuildingPageTypeOrder.Of(definition, "Давхрын байгуулалт"),
            BuildingPageTypeOrder.Of(definition, "floor-plans"));
        Assert.Equal(
            BuildingPageTypeOrder.Of(definition, "Огтлол"),
            BuildingPageTypeOrder.Of(definition, "sections"));

        // ...and it is not merely that both are "unclassified".
        Assert.NotEqual(BuildingPageTypeOrder.Unclassified, BuildingPageTypeOrder.Of(definition, "floor-plans"));
    }

    [Theory]
    [InlineData("Ангилаагүй")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("something nobody declared")]
    public void ADrawingWithNoKnownKindSortsLast(string? contentKind)
    {
        // Not into the middle: a sheet whose kind nobody declared must not
        // push a known one out of position. At the end it is visible and
        // everything before it keeps its number.
        AlbumDefinition definition = Concept();

        Assert.Equal(BuildingPageTypeOrder.Unclassified, BuildingPageTypeOrder.Of(definition, contentKind));
        Assert.True(BuildingPageTypeOrder.IsUnclassified(definition, contentKind));
        Assert.True(
            BuildingPageTypeOrder.Of(definition, "Харагдах байдал") <
            BuildingPageTypeOrder.Of(definition, contentKind));
    }

    [Fact]
    public void UNCLASSIFIEDSortsAfterEVERYSlotTheCompositionHas()
    {
        // The sentinel used to be 99, which is fine until a composition grows
        // past 99 slots - at which point an unclassified sheet would sort into
        // the MIDDLE and nothing would say so. Asserted against the real
        // composition rather than trusted.
        AlbumDefinition definition = Concept();

        Assert.All(
            definition.Composition,
            slot => Assert.True(
                slot.Order < BuildingPageTypeOrder.Unclassified,
                $"slot {slot.Id} has order {slot.Order}, at or past the unclassified rank"));
    }

    [Fact]
    public void SurroundingSpaceDoesNotChangeADrawingsPlace()
    {
        // Two products write this string; neither should have to think about
        // trailing spaces.
        AlbumDefinition definition = Concept();

        Assert.Equal(
            BuildingPageTypeOrder.Of(definition, "Огтлол"),
            BuildingPageTypeOrder.Of(definition, "  Огтлол  "));
    }

    [Fact]
    public void CASEDoesNotChangeADrawingsPlaceEither()
    {
        // The composition stores its titles in capitals and the products send
        // mixed case. That the matcher is case-insensitive is load-bearing for
        // every Mongolian-titled sheet, so it is asserted rather than assumed.
        AlbumDefinition definition = Concept();

        Assert.Equal(
            BuildingPageTypeOrder.Of(definition, "ЕРӨНХИЙ ТӨЛӨВЛӨГӨӨ"),
            BuildingPageTypeOrder.Of(definition, "Ерөнхий төлөвлөгөө"));
        Assert.NotEqual(
            BuildingPageTypeOrder.Unclassified,
            BuildingPageTypeOrder.Of(definition, "Ерөнхий төлөвлөгөө"));
    }

    [Fact]
    public void RevitsSectionsAndAutoCadsPlansInterleaveByKindNotBySource()
    {
        // The situation the rule exists for, stated as the client described
        // it: plans from one product, sections and elevations from another.
        AlbumDefinition definition = Concept();

        (string Product, string Kind)[] arrived =
        [
            ("Revit", "Огтлол"),
            ("Revit", "Нүүр тал"),
            ("AutoCAD", "Давхрын байгуулалт"),
        ];

        string[] ordered = [.. arrived
            .OrderBy(sheet => BuildingPageTypeOrder.Of(definition, sheet.Kind))
            .Select(sheet => sheet.Kind)];

        Assert.Equal(["Давхрын байгуулалт", "Огтлол", "Нүүр тал"], ordered);
    }

    [Fact]
    public void MIXINGTheTwoVocabulariesStillInterleavesCorrectly()
    {
        // The real cross-product case now that both forms are accepted: one
        // product names the kind, the other names the slot. They must still
        // come out in the client's order.
        AlbumDefinition definition = Concept();

        string[] arrived = ["elevations", "Огтлол", "floor-plans"];
        string[] ordered = [.. arrived.OrderBy(kind => BuildingPageTypeOrder.Of(definition, kind))];

        Assert.Equal(["floor-plans", "Огтлол", "elevations"], ordered);
    }
}
