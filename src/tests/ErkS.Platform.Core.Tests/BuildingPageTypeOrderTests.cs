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
/// The category names are what AutoCAD and Revit both declare, confirmed word
/// for word with both products before this was written. A change on either
/// side breaks these rather than a printed album.
/// </summary>
public sealed class BuildingPageTypeOrderTests
{
    [Fact]
    public void PlansComeBeforeSectionsWhichComeBeforeElevations()
    {
        // The client's sentence, as a check.
        Assert.True(
            BuildingPageTypeOrder.Of("Давхрын байгуулалт") <
            BuildingPageTypeOrder.Of("Огтлол"));
        Assert.True(
            BuildingPageTypeOrder.Of("Огтлол") <
            BuildingPageTypeOrder.Of("Нүүр тал"));
    }

    [Fact]
    public void TheWholeOrderIsStrictlyIncreasing()
    {
        string[] order =
        [
            "Давхрын байгуулалт",
            "Огтлол",
            "Нүүр тал",
            "Харагдах байдал",
            "Ерөнхий хэсэг",
            "Ерөнхий төлөвлөгөө",
        ];

        for (int index = 1; index < order.Length; index++)
        {
            Assert.True(
                BuildingPageTypeOrder.Of(order[index - 1]) < BuildingPageTypeOrder.Of(order[index]),
                $"{order[index - 1]} must precede {order[index]}");
        }
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
        Assert.Equal(BuildingPageTypeOrder.Unclassified, BuildingPageTypeOrder.Of(contentKind));
        Assert.True(BuildingPageTypeOrder.IsUnclassified(contentKind));
        Assert.True(BuildingPageTypeOrder.Of("Ерөнхий төлөвлөгөө") < BuildingPageTypeOrder.Of(contentKind));
    }

    [Fact]
    public void SurroundingSpaceDoesNotChangeADrawingsPlace()
    {
        // Two products write this string; neither should have to think about
        // trailing spaces.
        Assert.Equal(
            BuildingPageTypeOrder.Of("Огтлол"),
            BuildingPageTypeOrder.Of("  Огтлол  "));
    }

    [Fact]
    public void RevitsSectionsAndAutoCadsPlansInterleaveByKindNotBySource()
    {
        // The situation the rule exists for, stated as the client described
        // it: plans from one product, sections and elevations from another.
        (string Product, string Kind)[] arrived =
        [
            ("Revit", "Огтлол"),
            ("Revit", "Нүүр тал"),
            ("AutoCAD", "Давхрын байгуулалт"),
        ];

        string[] ordered = [.. arrived
            .OrderBy(sheet => BuildingPageTypeOrder.Of(sheet.Kind))
            .Select(sheet => sheet.Kind)];

        Assert.Equal(["Давхрын байгуулалт", "Огтлол", "Нүүр тал"], ordered);
    }
}
