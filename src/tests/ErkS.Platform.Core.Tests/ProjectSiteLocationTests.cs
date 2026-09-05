using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// The address stops being typed and starts being chosen.
///
/// The reason it matters more than an address usually would: the concurring-body
/// suggestion reads this field, so a misread aimag does not produce a wrong
/// address - it produces a DIFFERENT ORGANISATION'S NAME on a document somebody
/// signs. That is what turns "do not parse the old free text" from caution into
/// a rule.
/// </summary>
public sealed class ProjectSiteLocationTests
{
    [Fact]
    public void APartialChoiceCountsAsNoChoice()
    {
        // Half a location is not half an answer. Both the cover line and the
        // suggestion need all three levels, so anything less has to behave
        // exactly as nothing rather than as "nearly".
        var location = new ProjectSiteLocation
        {
            ProvinceCode = "511",
            ProvinceName = "Улаанбаатар",
        };

        Assert.False(location.IsChosen);
        Assert.Equal("", location.CoverLine());
    }

    [Fact]
    public void ACompleteChoiceBuildsTheCoverLineFromTheSTOREDNames()
    {
        var location = new ProjectSiteLocation
        {
            ProvinceCode = "511",
            ProvinceName = "Улаанбаатар",
            DistrictCode = "51101",
            DistrictName = "Багануур",
            WardCode = "5110151",
            WardName = "1-р хороо",
            WardLabelMn = "Хороо",
        };

        Assert.True(location.IsChosen);
        Assert.Equal("Улаанбаатар, Багануур-ийн 1-р хороо", location.CoverLine());
    }

    [Fact]
    public void AWardThatDoesNotBelongToTheChosenDistrictIsDROPPED()
    {
        // Two different writers left a record that cannot be true. Keeping the
        // ward because it "looks filled in" would let the suggestion rules read
        // a unit from somewhere else entirely.
        var location = new ProjectSiteLocation
        {
            ProvinceCode = "511",
            DistrictCode = "51101",
            WardCode = "1830151",
            WardName = "1-р баг, Хуст арал",
        };

        location.Normalize();

        Assert.Equal("", location.WardCode);
        Assert.Equal("", location.WardName);
        Assert.Equal("51101", location.DistrictCode);
    }

    [Fact]
    public void ADistrictOutsideItsProvinceTakesTheWardWithIt()
    {
        var location = new ProjectSiteLocation
        {
            ProvinceCode = "511",
            DistrictCode = "18301",
            WardCode = "1830151",
        };

        location.Normalize();

        Assert.Equal("", location.DistrictCode);
        Assert.Equal("", location.WardCode);
    }

    [Fact]
    public void TheWardLabelIsSTOREDBecauseItCannotBeDerived()
    {
        // Erdenet is a city and its wards are «баг». Any rule of the form
        // "capital means khoroo" is wrong there, so the heading a unit was
        // chosen under is copied and kept rather than worked out again later.
        var erdenet = new ProjectSiteLocation
        {
            ProvinceCode = "261",
            ProvinceName = "Орхон",
            DistrictCode = "26101",
            DistrictName = "Баян-Өндөр",
            WardCode = "2610151",
            WardName = "1-р баг, Зэст",
            WardLabelMn = "Баг",
        };

        Assert.Equal("Баг", erdenet.WardLabelMn);
        Assert.True(erdenet.IsChosen);
    }

    [Fact]
    public void ARollUpRowIsNotAPlaceABuildingCanStandIn()
    {
        // "Улсын дүн" and the regional sums are one- and two-digit rows. If
        // they reached a picker, somebody would eventually choose the national
        // total as their project's location.
        Assert.False(AdministrativeUnits.IsSelectableUnit("1"));
        Assert.False(AdministrativeUnits.IsSelectableUnit("51"));
        Assert.False(AdministrativeUnits.IsSelectableUnit(""));
        Assert.False(AdministrativeUnits.IsSelectableUnit("51101x"));
        Assert.True(AdministrativeUnits.IsSelectableUnit("511"));
        Assert.True(AdministrativeUnits.IsSelectableUnit("51101"));
        Assert.True(AdministrativeUnits.IsSelectableUnit("5110151"));
    }

    [Fact]
    public void TheParentIsThePrefix()
    {
        Assert.Equal("51101", AdministrativeUnits.ParentCodeOf("5110151"));
        Assert.Equal("511", AdministrativeUnits.ParentCodeOf("51101"));
        Assert.Equal("", AdministrativeUnits.ParentCodeOf("511"));
    }

    [Fact]
    public void TENSortsAfterTWO_TheCaseEveryPlainComparerGetsWrong()
    {
        // The example to test with, and not the one first reached for. Plain
        // text puts "10-р баг" ahead of "2-р баг" because '1' precedes '2' -
        // and it does so under every comparer, ordinal or culture-aware.
        var names = new List<string> { "26-р баг, Баян-Овоот", "10-р баг, Наран", "2-р баг, Оюут" };

        names.Sort(AdministrativeUnitNameComparer.Instance);

        Assert.Equal(
            new[] { "2-р баг, Оюут", "10-р баг, Наран", "26-р баг, Баян-Овоот" },
            names);
    }

    [Fact]
    public void PlainTextWouldHaveOrderedThatListWrongly()
    {
        // The comparer earns its place: written out, the naive answer differs.
        var names = new List<string> { "26-р баг, Баян-Овоот", "10-р баг, Наран", "2-р баг, Оюут" };
        names.Sort(StringComparer.Ordinal);

        Assert.Equal("10-р баг, Наран", names[0]);
    }

    [Fact]
    public void ANameWithNoNumberStillSortsSomewhereSensible()
    {
        // «Хатгал тосгон» - the one row that is neither bag nor khoroo. It has
        // no leading number and must not throw or vanish.
        var names = new List<string> { "Хатгал тосгон", "2-р баг", "10-р баг" };

        names.Sort(AdministrativeUnitNameComparer.Instance);

        Assert.Equal(3, names.Count);
        Assert.Equal("2-р баг", names[0]);
        Assert.Equal("10-р баг", names[1]);
    }

    [Fact]
    public void TheFreeTextAddressIsUNTOUCHEDByAChoice()
    {
        // The two live side by side. Parsing the typed line into the structured
        // fields is the one thing this design refuses to do, because names
        // repeat across the country and a wrong unit reaches a signed document
        // through the concurring-body suggestion.
        var basis = new ProjectInitiationBasis
        {
            SiteAddress = "Улаанбаатар хот, Баянгол дүүрэг, 29-р хороо",
        };

        Assert.False(basis.SiteLocation.IsChosen);
        Assert.Equal("Улаанбаатар хот, Баянгол дүүрэг, 29-р хороо", basis.SiteAddress);
    }
}
