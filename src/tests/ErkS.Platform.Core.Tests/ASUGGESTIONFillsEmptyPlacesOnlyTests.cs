using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// A suggestion fills empty places and never touches what somebody wrote.
///
/// 🔴 THE OWNER SET THE ORDER, NOT THE CONVENIENCE: «Онцгой байдлын ерөнхий
/// газар болон эрүүл мэндийн яаманд хандахаар бол СОНГОДОГ байна» - the
/// automatic sits UNDER the manual choice. Overwriting a typed row inverts that,
/// and the cost is work nobody can get back; clearing a row to ask again is one
/// extra step.
///
/// 🔴 AND NO PROVENANCE FLAG WAS ADDED. Marking rows «this came from a
/// suggestion» would put a new state in the schema and still not answer the real
/// question - an address that changes makes an old suggestion stale, and a flag
/// cannot tell stale from deliberate. «Only fill what is empty» settles it with
/// no state at all.
/// </summary>
public sealed class ASUGGESTIONFillsEmptyPlacesOnlyTests
{
    [Fact]
    public void ABLANKRowIsAPLACEAndNotAnOccupant()
    {
        // 🔴 THE DEFECT THIS FILE WAS WRITTEN FOR. The editor adds blank rows when
        // somebody presses «add»; counting those as occupants makes a table that
        // looks EMPTY on screen refuse a suggestion for having no room - an answer
        // nobody can make sense of while staring at two blank lines.
        Assert.True(OfficialsRosterFill.IsEmptyPlace(new ProjectApprovalEntry()));
        Assert.True(OfficialsRosterFill.IsEmptyPlace(
            new ProjectApprovalEntry { OrganizationName = "   " }));
        Assert.True(OfficialsRosterFill.IsEmptyPlace(null));

        // A person's name with no organisation is still an empty place: the sheet
        // prints an organisation, and a row without one has nothing to print.
        Assert.True(OfficialsRosterFill.IsEmptyPlace(
            new ProjectApprovalEntry { PersonName = "Хүн" }));

        Assert.False(OfficialsRosterFill.IsEmptyPlace(
            new ProjectApprovalEntry { OrganizationName = "Байгууллага" }));
    }

    [Fact]
    public void AFULLLookingTableOfBLANKSStillHasRoom()
    {
        // The two halves together: the count the plan uses comes from occupied
        // places, so two blank ХЯНАСАН rows do not exhaust its two places.
        var blanks = new List<ProjectApprovalEntry>
        {
            new(),
            new(),
        };

        Assert.Equal(0, OfficialsRosterFill.OccupiedCount(blanks));

        IReadOnlyList<OfficialsProposalOutcome> outcomes = OfficialsProposalPlan.For(
            OfficialsProposal.For(Directory(), [], blanks),
            [],
            blanks);

        Assert.Equal(
            OfficialsProposalSkip.None,
            Assert.Single(outcomes, outcome => outcome.Block == OfficialsBlock.ReviewedBy).Skip);
    }

    [Fact]
    public void SUGGESTIONSFillTheBlanksRatherThanGrowingTheList()
    {
        // Pressing «suggest» after adding two blank rows should fill those two,
        // not produce four - the list stays the length the person made it.
        IReadOnlyList<ProjectApprovalEntry> after = OfficialsRosterFill.Apply(
            [new ProjectApprovalEntry(), new ProjectApprovalEntry()],
            [Row("Нэгдүгээр"), Row("Хоёрдугаар")]);

        Assert.Equal(2, after.Count);
        Assert.Equal("Нэгдүгээр", after[0].OrganizationName);
        Assert.Equal("Хоёрдугаар", after[1].OrganizationName);
    }

    [Fact]
    public void AWRITTENRowIsNEVEROverwritten()
    {
        // 🔴 THE RULE ITSELF. A suggestion that replaced a typed row would invert
        // the order the owner set, and the loss would be silent - the person's
        // own words replaced by a directory's.
        IReadOnlyList<ProjectApprovalEntry> after = OfficialsRosterFill.Apply(
            [Row("Хүний бичсэн"), new ProjectApprovalEntry()],
            [Row("Саналаас")]);

        Assert.Equal("Хүний бичсэн", after[0].OrganizationName);
        Assert.Equal("Саналаас", after[1].OrganizationName);
    }

    [Fact]
    public void ADDITIONSAppendOnceThePlacesRunOut()
    {
        IReadOnlyList<ProjectApprovalEntry> after = OfficialsRosterFill.Apply(
            [Row("Байгаа")],
            [Row("Нэг"), Row("Хоёр")]);

        Assert.Equal(3, after.Count);
        Assert.Equal("Байгаа", after[0].OrganizationName);
        Assert.Equal("Нэг", after[1].OrganizationName);
        Assert.Equal("Хоёр", after[2].OrganizationName);
    }

    [Fact]
    public void AFILLEDPlaceKeepsTheROWSOwnIdentity()
    {
        // It is the same row on screen, now with something in it. A new identity
        // would make the editor treat it as a different row - losing focus, or
        // whatever else is keyed on it.
        var blank = new ProjectApprovalEntry();
        string id = blank.Id;

        IReadOnlyList<ProjectApprovalEntry> after =
            OfficialsRosterFill.Apply([blank], [Row("Дүүрлээ")]);

        Assert.Equal(id, Assert.Single(after).Id);
        Assert.Equal("Дүүрлээ", after[0].OrganizationName);
    }

    [Fact]
    public void NOTHINGToAddLeavesTheRosterExactlyAsItWas()
    {
        IReadOnlyList<ProjectApprovalEntry> rows = [Row("Нэг"), new ProjectApprovalEntry()];

        IReadOnlyList<ProjectApprovalEntry> after = OfficialsRosterFill.Apply(rows, []);

        Assert.Equal(2, after.Count);
        Assert.Equal("Нэг", after[0].OrganizationName);
        Assert.True(OfficialsRosterFill.IsEmptyPlace(after[1]));
    }

    [Fact]
    public void THEResultIsACopyAndTheInputIsUntouched()
    {
        var rows = new List<ProjectApprovalEntry> { Row("Анхны") };

        IReadOnlyList<ProjectApprovalEntry> after =
            OfficialsRosterFill.Apply(rows, [Row("Шинэ")]);

        after[0].OrganizationName = "Дараа нь засав";
        Assert.Equal("Анхны", rows[0].OrganizationName);
    }

    [Fact]
    public void ANEmptyRosterTakesTheSuggestionsInOrder()
    {
        IReadOnlyList<ProjectApprovalEntry> after =
            OfficialsRosterFill.Apply([], [Row("Нэг"), Row("Хоёр")]);

        Assert.Equal(["Нэг", "Хоёр"], after.Select(row => row.OrganizationName));
        Assert.Empty(OfficialsRosterFill.Apply(null, null));
    }

    private static ProjectApprovalEntry Row(string organizationName) => new()
    {
        OrganizationName = organizationName,
        PositionTitle = "Албан тушаал",
        PersonName = "Хүн",
    };

    /// <summary>A fixture. No source names real officials yet.</summary>
    private static IReadOnlyList<OfficialsDirectoryEntry> Directory() =>
    [
        new()
        {
            UnitCode = "01103",
            Kind = OfficialBodyKind.UrbanPlanning,
            OrganizationName = "Хот байгуулалтын алба",
            PositionTitle = "Мэргэжилтэн",
            PersonName = "Тест",
        },
    ];
}
