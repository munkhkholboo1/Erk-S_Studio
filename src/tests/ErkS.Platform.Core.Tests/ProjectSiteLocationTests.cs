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
        // 🔴 CHANGED DELIBERATELY. IsChosen still says "not a complete
        // answer" - that is unchanged and is what the guards read. What
        // changed is that the LINE now prints what was chosen: a province
        // alone is a real place, and blanking it threw away something the
        // person entered on purpose. The shared vectors require this
        // («province-only» → «Орхон аймаг»).
        Assert.Equal("Улаанбаатар хот", location.CoverLine());
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
        Assert.Equal("Улаанбаатар хот, Багануур дүүрэг, 1-р хороо", location.CoverLine());
    }

    [Fact]
    public void AWardThatDoesNotBelongToTheChosenDistrictIsKEPTAndREFUSED()
    {
        // Two different writers left a record that cannot be true. It must not
        // reach the suggestion rules - a unit from somewhere else puts ANOTHER
        // ORGANISATION'S NAME on a signed document - and the old answer to that
        // was to delete the ward, its name and its heading at load.
        //
        // Deleting what somebody stored is not one of the honest answers. This
        // is: the values stay, the chain refuses to be used, and the record says
        // why. Nothing downstream can act on it either way; the difference is
        // whether the person still has what they entered.
        var location = new ProjectSiteLocation
        {
            ProvinceCode = "511",
            ProvinceName = "Улаанбаатар",
            DistrictCode = "51101",
            DistrictName = "Багануур",
            WardCode = "1830151",
            WardName = "1-р баг, Хуст арал",
        };

        location.Normalize();

        Assert.Equal("1830151", location.WardCode);
        Assert.Equal("1-р баг, Хуст арал", location.WardName);
        Assert.Equal("51101", location.DistrictCode);

        Assert.False(location.ChainHoldsTogether);
        Assert.False(location.IsChosen);
        Assert.Equal("", location.CoverLine());
        Assert.Contains("1830151", location.ProblemMn, StringComparison.Ordinal);
        Assert.Contains("Багануур", location.ProblemMn, StringComparison.Ordinal);
    }

    [Fact]
    public void ADistrictOutsideItsProvinceIsKEPTAndREFUSED()
    {
        var location = new ProjectSiteLocation
        {
            ProvinceCode = "511",
            ProvinceName = "Улаанбаатар",
            DistrictCode = "18301",
            DistrictName = "Өлгий",
            WardCode = "1830151",
        };

        location.Normalize();

        Assert.Equal("18301", location.DistrictCode);
        Assert.Equal("1830151", location.WardCode);
        Assert.False(location.IsChosen);
        Assert.Equal("", location.CoverLine());

        // The district is named, not the ward: the ward sits correctly under the
        // district, and reporting the innermost mismatch would send the reader
        // to the one level that is fine.
        Assert.Contains("18301", location.ProblemMn, StringComparison.Ordinal);
        Assert.DoesNotContain("1830151", location.ProblemMn, StringComparison.Ordinal);
    }

    [Fact]
    public void TheREFUSALDoesNotNeedAnybodyToCallAnything()
    {
        // 🔴 THE REASON THIS MOVED. The rule lived in Normalize, which nothing
        // outside the tests ever called - written, tested, green and unreachable.
        // So the deletion never ran, and neither did the protection: a stored
        // chain that cannot be true was loaded exactly as written and PRINTED on
        // a cover.
        //
        // Asked without calling Normalize at all, which is how every caller in
        // the program meets this object.
        var untouched = new ProjectSiteLocation
        {
            ProvinceCode = "511",
            ProvinceName = "Улаанбаатар",
            DistrictCode = "18301",
            DistrictName = "Өлгий",
            WardCode = "1830151",
            WardName = "1-р баг, Хуст арал",
        };

        Assert.False(untouched.IsChosen);
        Assert.Equal("", untouched.CoverLine());
        Assert.NotEqual("", untouched.ProblemMn);
    }

    [Fact]
    public void ALocationThatHOLDSTogetherSaysNothingIsWrong()
    {
        // The negative control. A rule that reports a problem for everything is
        // the same as a rule that reports it for nothing.
        var sound = new ProjectSiteLocation
        {
            ProvinceCode = "511",
            ProvinceName = "Улаанбаатар",
            DistrictCode = "51101",
            DistrictName = "Багануур",
            WardCode = "5110151",
            WardName = "1-р хороо",
        };

        Assert.True(sound.ChainHoldsTogether);
        Assert.True(sound.IsChosen);
        Assert.Equal("", sound.ProblemMn);
    }

    [Fact]
    public void LOADINGAProjectActuallyTrimsTheStoredLocation()
    {
        // 🔴 THE STEP THAT WAS OWNED BY NOBODY. Normalize existed on this class
        // and the loader normalised the client snapshot, the workflow and the
        // company - but never the location. Written, tested, green, unreachable.
        //
        // A sabotage run proved the point: deleting the loader's call to it broke
        // no test at all, which is how it went missing in the first place. Asked
        // through the STORE, because asking the object would pass either way.
        string path = Path.Combine(
            Path.GetTempPath(),
            "erk-s-site-location-" + Guid.NewGuid().ToString("N") + ".erksalbum");
        var project = new AlbumProject();
        project.InitiationBasis.SiteLocation = new ProjectSiteLocation
        {
            ProvinceCode = " 511 ",
            ProvinceName = " Улаанбаатар ",
            DistrictCode = " 51101 ",
            WardCode = " 5110151 ",
            WardLabelMn = " Хороо ",
        };

        try
        {
            AlbumProjectStore.Save(project, path);
            ProjectSiteLocation loaded = AlbumProjectStore.Load(path).InitiationBasis.SiteLocation;

            Assert.Equal("511", loaded.ProvinceCode);
            Assert.Equal("Улаанбаатар", loaded.ProvinceName);
            Assert.Equal("51101", loaded.DistrictCode);
            Assert.Equal("5110151", loaded.WardCode);
            Assert.Equal("Хороо", loaded.WardLabelMn);
            Assert.True(loaded.IsChosen);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void TheBadRecordIsNotTheFIRSTOne()
    {
        // A checker that looks at the head of a list and reports on the whole of
        // it passes every fixture whose first entry is the interesting one - SRV
        // walked into exactly that trap today, on their side of the same wire.
        //
        // So the inconsistent record sits FOURTH of five, and the sound ones
        // around it have to stay sound: a rule that says "problem" for
        // everything is the same as one that says it for nothing.
        ProjectSiteLocation[] stored =
        [
            Sound("511", "51101", "5110151"),
            Sound("183", "18301", "1830151"),
            Sound("261", "26101", "2610151"),
            new ProjectSiteLocation
            {
                ProvinceCode = "511",
                ProvinceName = "Улаанбаатар",
                DistrictCode = "18301",
                DistrictName = "Өлгий",
                WardCode = "1830151",
                WardName = "1-р баг, Хуст арал",
            },
            Sound("267", "26704", "2670461"),
        ];

        bool[] holds = stored.Select(location => location.ChainHoldsTogether).ToArray();

        Assert.Equal([true, true, true, false, true], holds);
        Assert.Equal(1, stored.Count(location => location.ProblemMn.Length > 0));
        Assert.Equal(4, stored.Count(location => location.IsChosen));

        // And the one that failed kept everything it was given.
        Assert.Equal("1830151", stored[3].WardCode);
        Assert.Equal("1-р баг, Хуст арал", stored[3].WardName);

        static ProjectSiteLocation Sound(string province, string district, string ward) => new()
        {
            ProvinceCode = province,
            ProvinceName = "аймаг",
            DistrictCode = district,
            DistrictName = "сум",
            WardCode = ward,
            WardName = "баг",
        };
    }

    [Fact]
    public void AnIncompleteChoiceIsNotACCUSEDOfBeingInconsistent()
    {
        // Half-filled is the ordinary state of a form somebody is still filling
        // in. Telling them their record contradicts itself while they are two
        // clicks into it would be this rule inventing a defect.
        var halfway = new ProjectSiteLocation
        {
            ProvinceCode = "511",
            ProvinceName = "Улаанбаатар",
        };

        Assert.True(halfway.ChainHoldsTogether);
        Assert.False(halfway.IsChosen);
        Assert.Equal("", halfway.ProblemMn);
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
    public void TheCHAINIsCheckedAgainstThePublishedParents()
    {
        // The catalogue prints parentUnitCode even though the prefix gives it
        // away, and the reason is that two readers deriving the same rule can
        // derive it differently. Where the rows are in hand, the published
        // parent is what is asked; the prefix rule stays as a second, separate
        // opinion for the stored project, which keeps codes and nothing else.
        var orkhon = new AdministrativeUnit("261", AdministrativeUnits.Aimag, "", "Орхон", "Сум");
        var bayanUndur = new AdministrativeUnit("26101", AdministrativeUnits.Sum, "261", "Баян-Өндөр", "Баг");
        var zest = new AdministrativeUnit("2610151", AdministrativeUnits.Bag, "26101", "1-р баг, Зэст", "");

        Assert.True(AdministrativeUnits.ChainIsConsistent(orkhon, bayanUndur, zest));

        var elsewhere = new AdministrativeUnit("1830151", AdministrativeUnits.Bag, "18301", "1-р баг, Хуст арал", "");
        Assert.False(AdministrativeUnits.ChainIsConsistent(orkhon, bayanUndur, elsewhere));

        // THE CASE THAT SEPARATES THE TWO SOURCES. A first version of this test
        // used rows whose prefixes and published parents agreed, so deriving
        // the prefix instead passed it - the test named the rule and did not
        // hold it. Here the row's code says one district and its published
        // parent says another, which is exactly the disagreement the catalogue
        // prints parentUnitCode to make visible.
        var disagreeing = new AdministrativeUnit(
            "2610151",
            AdministrativeUnits.Bag,
            ParentUnitCode: "18301",
            "1-р баг, Зэст",
            "");
        Assert.Equal("26101", AdministrativeUnits.ParentCodeOf(disagreeing.UnitCode));
        Assert.False(AdministrativeUnits.ChainIsConsistent(orkhon, bayanUndur, disagreeing));
    }

    [Fact]
    public void ERDENETSWardsAreBAG_AndNothingInTheCodeDecidesThat()
    {
        // Orkhon's centre is a city, and its wards are «баг». Any rule of the
        // shape "city means khoroo" answers this wrong - and answers it wrong
        // only here and in Darkhan, so a test built from Ulaanbaatar and one
        // aimag would have stayed green over it.
        var bayanUndur = new AdministrativeUnit(
            "26101",
            AdministrativeUnits.Sum,
            "261",
            "Баян-Өндөр",
            "Баг",
            HasChildren: true);

        // TWO SEPARATE FACTS, and this test used to conflate them: HasChildren
        // was derived from the label being non-empty, so «has a name for its
        // children» and «has children» were the same statement. Three real sums
        // are the counter-example - «Баг» with no bags published - and the
        // fixture now has to say which it means.
        Assert.Equal("Баг", bayanUndur.ChildPickerLabelMn);
        Assert.True(bayanUndur.HasChildren);
    }

    [Fact]
    public void ATHIRDLabelSurvivesUntouched()
    {
        // The catalogue is allowed to grow a word this build has never seen.
        // Collapsing an unfamiliar label to whichever of two is nearer would
        // rename a place on a printed sheet, and would do it silently.
        var invented = new AdministrativeUnit("99901", AdministrativeUnits.Sum, "999", "Шинэ", "Тосгон");
        var location = new ProjectSiteLocation
        {
            ProvinceCode = "999",
            ProvinceName = "Шинэ аймаг",
            DistrictCode = "99901",
            DistrictName = "Шинэ",
            WardCode = "9990151",
            WardName = "Хатгал тосгон",
            WardLabelMn = invented.ChildPickerLabelMn,
        };

        location.Normalize();

        Assert.Equal("Тосгон", location.WardLabelMn);
        Assert.Equal("Хатгал тосгон", location.WardName);
        Assert.True(location.IsChosen);
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
