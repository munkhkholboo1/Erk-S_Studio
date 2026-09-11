using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// The editor shows one district and saves all of them.
///
/// 🔴 THIS IS WHERE THIRTY-SIX ROWS BECOME FOUR. The screen holds the rows it is
/// showing; the save writes what the screen holds; every other district is gone
/// and nothing failed. The person finds out months later, on a sheet, when an
/// official they entered is not offered.
///
/// The rule is kept out of the window's assembly on purpose - a rule that lives
/// inside a screen is a rule nobody can check, and this project has paid for that
/// before.
/// </summary>
public sealed class EDITINGOneDistrictKeepsTheOthersTests
{
    private const string Bayangol = "01103";
    private const string Songino = "01104";
    private const string Khan = "01105";

    [Fact]
    public void REPLACINGOneDistrictLeavesEVERYOtherRowAlone()
    {
        IReadOnlyList<OfficialsDirectoryEntry> all =
        [
            Row(Bayangol, OfficialBodyKind.EmergencyManagement, "Баянгол ОБ"),
            Row(Songino, OfficialBodyKind.EmergencyManagement, "Сонгино ОБ"),
            Row(Songino, OfficialBodyKind.PublicHealth, "Сонгино ЭМ"),
            Row(Khan, OfficialBodyKind.UrbanPlanning, "Хан-Уул ХБ"),
        ];

        IReadOnlyList<OfficialsDirectoryEntry> after = OfficialsDirectoryEdit.ReplaceUnit(
            all,
            Songino,
            [Row(Songino, OfficialBodyKind.UrbanPlanning, "Сонгино ХБ")]);

        Assert.Equal(3, after.Count);
        Assert.Contains(after, entry => entry.OrganizationName == "Баянгол ОБ");
        Assert.Contains(after, entry => entry.OrganizationName == "Хан-Уул ХБ");
        Assert.Contains(after, entry => entry.OrganizationName == "Сонгино ХБ");
        Assert.DoesNotContain(after, entry => entry.OrganizationName == "Сонгино ОБ");
    }

    [Fact]
    public void THEEditedDistrictKeepsItsPlaceInTheFile()
    {
        // A save that reordered the file would make every save look like a change
        // to anything comparing two versions of it.
        IReadOnlyList<OfficialsDirectoryEntry> all =
        [
            Row(Bayangol, OfficialBodyKind.EmergencyManagement, "Эхний"),
            Row(Songino, OfficialBodyKind.EmergencyManagement, "Дунд"),
            Row(Khan, OfficialBodyKind.UrbanPlanning, "Сүүлийн"),
        ];

        IReadOnlyList<OfficialsDirectoryEntry> after = OfficialsDirectoryEdit.ReplaceUnit(
            all,
            Songino,
            [Row(Songino, OfficialBodyKind.PublicHealth, "Шинэ дунд")]);

        Assert.Equal("Эхний", after[0].OrganizationName);
        Assert.Equal("Шинэ дунд", after[1].OrganizationName);
        Assert.Equal("Сүүлийн", after[2].OrganizationName);
    }

    [Fact]
    public void ADISTRICTThatHadNoneGetsItsRowsAppended()
    {
        IReadOnlyList<OfficialsDirectoryEntry> after = OfficialsDirectoryEdit.ReplaceUnit(
            [Row(Bayangol, OfficialBodyKind.EmergencyManagement, "Байгаа")],
            Songino,
            [Row(Songino, OfficialBodyKind.PublicHealth, "Шинэ")]);

        Assert.Equal(2, after.Count);
        Assert.Equal("Байгаа", after[0].OrganizationName);
        Assert.Equal("Шинэ", after[1].OrganizationName);
    }

    [Fact]
    public void CLEARINGADistrictRemovesONLYThatDistrict()
    {
        IReadOnlyList<OfficialsDirectoryEntry> after = OfficialsDirectoryEdit.ReplaceUnit(
            [
                Row(Bayangol, OfficialBodyKind.EmergencyManagement, "Үлдэнэ"),
                Row(Songino, OfficialBodyKind.PublicHealth, "Арилна"),
            ],
            Songino,
            []);

        Assert.Equal("Үлдэнэ", Assert.Single(after).OrganizationName);
    }

    [Fact]
    public void NODISTRICTChosenChangesNOTHING()
    {
        // 🔴 BOTH WAYS OF GETTING THIS WRONG ARE SILENT. Appending under an empty
        // code files rows no lookup can reach; matching on empty would delete
        // every row that has no code. Refusing to write is the only honest answer.
        IReadOnlyList<OfficialsDirectoryEntry> all =
        [
            Row(Bayangol, OfficialBodyKind.EmergencyManagement, "Хэвээр"),
        ];

        foreach (string? unit in new[] { null, "", "   " })
        {
            IReadOnlyList<OfficialsDirectoryEntry> after = OfficialsDirectoryEdit.ReplaceUnit(
                all,
                unit,
                [Row(Songino, OfficialBodyKind.PublicHealth, "Орж болохгүй")]);

            Assert.Equal("Хэвээр", Assert.Single(after).OrganizationName);
        }
    }

    [Fact]
    public void AROWIsStampedWithTheDISTRICTBeingEditedNotItsOwn()
    {
        // 🔴 A ROW CARRYING ITS OWN CODE WOULD VANISH IN FRONT OF SOMEBODY. They
        // would type an official into Bayangol's screen, it would be filed under
        // another district, and it would disappear from the list they are looking
        // at - with nothing said.
        IReadOnlyList<OfficialsDirectoryEntry> after = OfficialsDirectoryEdit.ReplaceUnit(
            [],
            Bayangol,
            [Row(Khan, OfficialBodyKind.PublicHealth, "Өөр код авчирсан")]);

        Assert.Equal(Bayangol, Assert.Single(after).UnitCode);
    }

    [Fact]
    public void AROWWithNoOrganisationIsDroppedATTheEditorRatherThanTheReader()
    {
        // A row somebody started and left. The reader would refuse it anyway, and
        // refusing it here means the count on screen after a save matches what
        // was actually kept - no «saved» that quietly stored one row fewer.
        IReadOnlyList<OfficialsDirectoryEntry> after = OfficialsDirectoryEdit.ReplaceUnit(
            [],
            Bayangol,
            [
                Row(Bayangol, OfficialBodyKind.PublicHealth, "Бүрэн"),
                Row(Bayangol, OfficialBodyKind.PublicHealth, "   "),
            ]);

        Assert.Equal("Бүрэн", Assert.Single(after).OrganizationName);
    }

    [Fact]
    public void ROWSComeOutASCopiesSoAnAbandonedEditChangesNothing()
    {
        var all = new List<OfficialsDirectoryEntry>
        {
            Row(Bayangol, OfficialBodyKind.EmergencyManagement, "Анхны"),
        };

        IReadOnlyList<OfficialsDirectoryEntry> editing =
            OfficialsDirectoryEdit.RowsOf(all, Bayangol);
        editing[0].OrganizationName = "Засав, гэхдээ хадгалаагүй";

        Assert.Equal("Анхны", all[0].OrganizationName);
    }

    [Fact]
    public void THEReplacementIsACopyToo()
    {
        // The same hazard from the other end: a saved list that shared objects
        // with the editor would keep changing after the save.
        var rows = new List<OfficialsDirectoryEntry>
        {
            Row(Bayangol, OfficialBodyKind.EmergencyManagement, "Хадгалсан"),
        };

        IReadOnlyList<OfficialsDirectoryEntry> after =
            OfficialsDirectoryEdit.ReplaceUnit([], Bayangol, rows);

        rows[0].OrganizationName = "Дараа нь засав";
        Assert.Equal("Хадгалсан", Assert.Single(after).OrganizationName);
    }

    [Fact]
    public void THEUnitsWithOfficialsAreListedONCEAndInOrder()
    {
        IReadOnlyList<string> units = OfficialsDirectoryEdit.UnitsWithOfficials(
        [
            Row(Songino, OfficialBodyKind.EmergencyManagement, "Нэг"),
            Row(Bayangol, OfficialBodyKind.EmergencyManagement, "Хоёр"),
            Row(Songino, OfficialBodyKind.PublicHealth, "Гурав"),
        ]);

        Assert.Equal([Songino, Bayangol], units);
    }

    [Fact]
    public void ANEmptyDirectoryListsNoUnits()
    {
        Assert.Empty(OfficialsDirectoryEdit.UnitsWithOfficials(null));
        Assert.Empty(OfficialsDirectoryEdit.UnitsWithOfficials([]));
        Assert.Empty(OfficialsDirectoryEdit.RowsOf(null, Bayangol));
        Assert.Empty(OfficialsDirectoryEdit.RowsOf([], null));
    }

    [Fact]
    public void EVERYFieldSurvivesTheRoundTripThroughTheEditor()
    {
        // 🔴 A FIELD THE EDITOR DROPS IS A FIELD THAT DISAPPEARS ON THE FIRST
        // SAVE OF AN UNRELATED DISTRICT. Derived from a fully-populated row so a
        // field added later is not silently left behind.
        var full = new OfficialsDirectoryEntry
        {
            UnitCode = Bayangol,
            Kind = OfficialBodyKind.UrbanPlanning,
            OrganizationName = "Байгууллага",
            PositionTitle = "Албан тушаал",
            PersonName = "Хүн",
        };

        OfficialsDirectoryEntry after = Assert.Single(
            OfficialsDirectoryEdit.ReplaceUnit([], Bayangol, [full]));

        Assert.Equal(full.UnitCode, after.UnitCode);
        Assert.Equal(full.Kind, after.Kind);
        Assert.Equal(full.OrganizationName, after.OrganizationName);
        Assert.Equal(full.PositionTitle, after.PositionTitle);
        Assert.Equal(full.PersonName, after.PersonName);
    }

    private static OfficialsDirectoryEntry Row(
        string unitCode,
        OfficialBodyKind kind,
        string organizationName) => new()
        {
            UnitCode = unitCode,
            Kind = kind,
            OrganizationName = organizationName,
            PositionTitle = "Албан тушаал",
            PersonName = "Хүн",
        };
}
