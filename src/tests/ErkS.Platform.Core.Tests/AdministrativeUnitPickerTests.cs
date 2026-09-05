using System.Text.Json;
using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// The cascading location picker, exercised against the rows SRV published.
///
/// The fixture READS the contract rather than copying it. A copied fixture is a
/// second statement of the same facts, and the day the contract changes it goes
/// on asserting the old ones - green, and describing a catalogue that no longer
/// exists.
///
/// Four paths, because two of them are where every natural rule goes wrong:
/// the capital, an ordinary aimag, a CITY inside an aimag whose wards are
/// «баг», and a ward called «тосгон» - a third word under a heading that says
/// something else.
/// </summary>
public sealed class AdministrativeUnitPickerTests
{
    private sealed class ContractCatalogue : IAdministrativeUnitCatalogue
    {
        private readonly List<AdministrativeUnit> units = [];

        public ContractCatalogue()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            string? contract = null;
            while (directory is not null && contract is null)
            {
                string candidate = Path.Combine(
                    directory.FullName,
                    "_shared",
                    "mongolia-admin-divisions-contract-2026-09-06.json");
                if (File.Exists(candidate))
                    contract = candidate;
                directory = directory.Parent;
            }

            Assert.True(contract is not null, "the administrative divisions contract was not found");

            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(contract!));
            JsonElement rows = document.RootElement.GetProperty("realRows");
            foreach (JsonProperty branch in rows.EnumerateObject())
            {
                if (branch.Value.ValueKind != JsonValueKind.Array)
                    continue;
                foreach (JsonElement row in branch.Value.EnumerateArray())
                {
                    units.Add(new AdministrativeUnit(
                        row.GetProperty("unitCode").GetString() ?? "",
                        row.GetProperty("level").GetString() ?? "",
                        row.TryGetProperty("parentUnitCode", out JsonElement parent) &&
                            parent.ValueKind == JsonValueKind.String
                            ? parent.GetString() ?? ""
                            : "",
                        row.GetProperty("nameMn").GetString() ?? "",
                        row.TryGetProperty("childPickerLabelMn", out JsonElement label) &&
                            label.ValueKind == JsonValueKind.String
                            ? label.GetString() ?? ""
                            : ""));
                }
            }

            Assert.NotEmpty(units);
        }

        public DateTimeOffset? AsOfUtc { get; } = new(2026, 9, 6, 0, 0, 0, TimeSpan.Zero);

        public IReadOnlyList<AdministrativeUnit> ChildrenOf(string? parentUnitCode) =>
            units
                .Where(unit => unit.ParentUnitCode.Equals(
                    (parentUnitCode ?? "").Trim(),
                    StringComparison.Ordinal))
                .ToList();
    }

    private static AdministrativeUnitPicker Picker() => new(new ContractCatalogue());

    private static AdministrativeUnit Unit(AdministrativeUnitChoices choices, string code) =>
        Assert.Single(choices.Units, unit => unit.UnitCode == code);

    [Fact]
    public void TheCapitalOffersДҮҮРЭГThenХОРОО()
    {
        AdministrativeUnitPicker picker = Picker();
        picker.ChooseProvince(Unit(picker.ProvinceChoices(), "511"));

        Assert.Equal("Дүүрэг", picker.DistrictChoices().LabelMn);
        picker.ChooseDistrict(Unit(picker.DistrictChoices(), "51101"));
        Assert.Equal("Хороо", picker.WardChoices().LabelMn);
    }

    [Fact]
    public void AnAimagOffersСУМThenБАГ()
    {
        AdministrativeUnitPicker picker = Picker();
        picker.ChooseProvince(Unit(picker.ProvinceChoices(), "183"));

        Assert.Equal("Сум", picker.DistrictChoices().LabelMn);
        picker.ChooseDistrict(Unit(picker.DistrictChoices(), "18301"));
        Assert.Equal("Баг", picker.WardChoices().LabelMn);
    }

    [Fact]
    public void ACITYInsideAnAimagOffersБАГ_NotХОРОО()
    {
        // Orkhon's centre is Erdenet - a city, and its wards are «баг». This is
        // the path that "capital means khoroo" gets wrong, and it gets it wrong
        // only here and in Darkhan, so a test built from the capital and one
        // ordinary aimag stays green over it.
        AdministrativeUnitPicker picker = Picker();
        picker.ChooseProvince(Unit(picker.ProvinceChoices(), "261"));
        picker.ChooseDistrict(Unit(picker.DistrictChoices(), "26101"));

        Assert.Equal("Баг", picker.WardChoices().LabelMn);
        Assert.Equal("Сум", picker.DistrictChoices().LabelMn);
    }

    [Fact]
    public void AWardMayBeCalledSomethingTheHeadingDoesNotSay()
    {
        // «Хатгал тосгон» under a heading that reads «Баг». The heading and the
        // unit's own name are separate facts, and flattening either into the
        // other renames a place on a printed sheet.
        AdministrativeUnitPicker picker = Picker();
        picker.ChooseProvince(new AdministrativeUnit("267", "Aimag", "", "Хөвсгөл", "Сум"));
        picker.ChooseDistrict(new AdministrativeUnit("26704", "Sum", "267", "Алаг-Эрдэнэ", "Баг"));

        AdministrativeUnitChoices wards = picker.WardChoices();
        Assert.Equal("Баг", wards.LabelMn);
        Assert.Contains(wards.Units, unit => unit.NameMn == "Хатгал тосгон");
    }

    [Fact]
    public void ALabelThisBuildHasNEVERSeenIsUsedAsGiven()
    {
        // 🔴 THE CASE THAT SEPARATES "read the label" FROM "work it out".
        //
        // The four real paths do NOT separate them: today the capital's wards
        // are «Хороо» and everything else is «Баг», so `province is capital ?
        // "Хороо" : "Баг"` answers all four correctly. Sabotaging the picker to
        // that branch left every one of them green - the tests named the rule
        // and did not hold it, the same shape found in the parent-code check an
        // hour earlier.
        //
        // A third word is what tells them apart. The catalogue already carries
        // one place whose ward is a тосгон, and it may carry more tomorrow -
        // and the whole reason the label travels in the data is that Studio
        // should print it without being rebuilt.
        AdministrativeUnitPicker picker = Picker();
        picker.ChooseProvince(new AdministrativeUnit("267", "Aimag", "", "Хөвсгөл", "Сум"));
        picker.ChooseDistrict(new AdministrativeUnit("26704", "Sum", "267", "Алаг-Эрдэнэ", "Тосгон"));

        Assert.Equal("Тосгон", picker.WardChoices().LabelMn);
    }

    [Fact]
    public void TheStoredLabelIsTheOneTheWardWasChosenUNDER()
    {
        // Same separation, carried into what is written to the project: the
        // label is copied from the district that offered it, so an unfamiliar
        // word survives into the file and onto the sheet.
        AdministrativeUnitPicker picker = Picker();
        picker.ChooseProvince(new AdministrativeUnit("267", "Aimag", "", "Хөвсгөл", "Сум"));
        picker.ChooseDistrict(new AdministrativeUnit("26704", "Sum", "267", "Алаг-Эрдэнэ", "Тосгон"));
        picker.ChooseWard(Unit(picker.WardChoices(), "2670461"));

        Assert.Equal("Тосгон", picker.ToLocation().WardLabelMn);
        Assert.Equal("Хатгал тосгон", picker.ToLocation().WardName);
    }

    [Fact]
    public void ChangingTheProvinceCLEARSWhatWasBelowIt()
    {
        // Leaving a district from the previous province in place is how one
        // location comes to name two regions - and the suggestion rules would
        // then read whichever of them they happened to ask for.
        AdministrativeUnitPicker picker = Picker();
        picker.ChooseProvince(Unit(picker.ProvinceChoices(), "511"));
        picker.ChooseDistrict(Unit(picker.DistrictChoices(), "51101"));
        picker.ChooseWard(Unit(picker.WardChoices(), "5110151"));
        Assert.NotNull(picker.Ward);

        picker.ChooseProvince(Unit(picker.ProvinceChoices(), "183"));

        Assert.Null(picker.District);
        Assert.Null(picker.Ward);
        Assert.Equal("", picker.ToLocation().ProvinceCode);
    }

    [Fact]
    public void AChoiceFromTheWrongParentIsREFUSED()
    {
        AdministrativeUnitPicker picker = Picker();
        picker.ChooseProvince(Unit(picker.ProvinceChoices(), "511"));

        picker.ChooseDistrict(new AdministrativeUnit("18301", "Sum", "183", "Өлгий", "Баг"));

        Assert.Null(picker.District);
    }

    [Fact]
    public void ACompleteChoiceCarriesTheLabelAndTheCatalogueVersion()
    {
        AdministrativeUnitPicker picker = Picker();
        picker.ChooseProvince(Unit(picker.ProvinceChoices(), "261"));
        picker.ChooseDistrict(Unit(picker.DistrictChoices(), "26101"));
        picker.ChooseWard(Unit(picker.WardChoices(), "2610151"));

        ProjectSiteLocation location = picker.ToLocation();

        Assert.True(location.IsChosen);
        Assert.Equal("Орхон", location.ProvinceName);
        Assert.Equal("1-р баг, Зэст", location.WardName);
        // The heading it was chosen under - «Баг», from a city.
        Assert.Equal("Баг", location.WardLabelMn);
        Assert.Equal(new DateTimeOffset(2026, 9, 6, 0, 0, 0, TimeSpan.Zero), location.CatalogueAsOfUtc);
    }

    [Fact]
    public void AnIncompleteChoiceStoresNOTHING()
    {
        AdministrativeUnitPicker picker = Picker();
        picker.ChooseProvince(Unit(picker.ProvinceChoices(), "511"));
        picker.ChooseDistrict(Unit(picker.DistrictChoices(), "51101"));

        Assert.False(picker.ToLocation().IsChosen);
    }

    [Fact]
    public void ReopeningAStoredLocationMatchesOnCODE_NotName()
    {
        // The stored name is what the album printed; the catalogue may have
        // renamed the unit since. Matching on the name would fail in exactly
        // the case the stored name exists for.
        AdministrativeUnitPicker picker = Picker();
        picker.Restore(new ProjectSiteLocation
        {
            ProvinceCode = "511",
            ProvinceName = "ХУУЧИН НЭР",
            DistrictCode = "51101",
            DistrictName = "ХУУЧИН НЭР",
            WardCode = "5110151",
            WardName = "ХУУЧИН НЭР",
        });

        Assert.Equal("Улаанбаатар", picker.Province?.NameMn);
        Assert.Equal("Багануур", picker.District?.NameMn);
        Assert.Equal("1-р хороо", picker.Ward?.NameMn);
    }

    [Fact]
    public void TheWardListIsOrderedNumerically()
    {
        AdministrativeUnitPicker picker = Picker();
        picker.ChooseProvince(Unit(picker.ProvinceChoices(), "511"));
        picker.ChooseDistrict(Unit(picker.DistrictChoices(), "51101"));

        IReadOnlyList<AdministrativeUnit> wards = picker.WardChoices().Units;

        Assert.Equal(
            wards.Select(unit => unit.NameMn).OrderBy(name => name, AdministrativeUnitNameComparer.Instance),
            wards.Select(unit => unit.NameMn));
    }
}
