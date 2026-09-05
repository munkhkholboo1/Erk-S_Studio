namespace ErkS.Platform.Core;

/// <summary>
/// Where the units come from. An interface so the picker can be finished and
/// tested before the server route exists: the fixture behind it today reads the
/// published contract, and the live catalogue replaces it without the picker
/// noticing.
/// </summary>
public interface IAdministrativeUnitCatalogue
{
    /// <summary>
    /// Which version of the catalogue this is, issued by the server. Repeated
    /// into a project when a location is chosen, never invented by the client:
    /// without it, a choice made from a three-month-old offline copy and one
    /// made against live data both say only "chosen today".
    /// </summary>
    DateTimeOffset? AsOfUtc { get; }

    /// <summary>
    /// The units directly under <paramref name="parentUnitCode"/>, or the top
    /// level when it is empty.
    /// </summary>
    IReadOnlyList<AdministrativeUnit> ChildrenOf(string? parentUnitCode);
}

/// <summary>One level of the cascade: what to call it, and what it offers.</summary>
/// <param name="LabelMn">
/// The heading, taken from the PARENT's published label. Never worked out from
/// the parent's kind: Erdenet and Darkhan are cities whose wards are called
/// «баг», so "capital means khoroo" is wrong exactly where nobody tests it.
/// </param>
public sealed record AdministrativeUnitChoices(
    string LabelMn,
    IReadOnlyList<AdministrativeUnit> Units);

/// <summary>
/// The cascading choice of a site's location: province, then district, then
/// ward.
///
/// It holds no UI. Everything that decides what a person may pick, what the
/// pickers are called and what happens when a higher level changes lives here,
/// where it can be tested against the real published rows - the same reasoning
/// that moved the corner-table and cover rules out of the drawing code.
///
/// THE ONE RULE THAT SHAPES ALL OF IT: no branch in this file asks what kind of
/// place it is looking at. Labels come from the data, and a label this build
/// has never seen passes through unchanged.
/// </summary>
public sealed class AdministrativeUnitPicker
{
    private readonly IAdministrativeUnitCatalogue catalogue;

    public AdministrativeUnitPicker(IAdministrativeUnitCatalogue catalogue)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        this.catalogue = catalogue;
    }

    public AdministrativeUnit? Province { get; private set; }

    public AdministrativeUnit? District { get; private set; }

    public AdministrativeUnit? Ward { get; private set; }

    /// <summary>
    /// Whether there is a catalogue to choose from at all.
    ///
    /// A real runtime state, not a development gap: the catalogue is served and
    /// cached, so a first run, an offline machine or a server that is down all
    /// arrive here. The view needs to say so plainly instead of showing three
    /// empty boxes, and the decision belongs on this side - a View that works
    /// out for itself when to hide something is where four of this platform's
    /// rules went to stop being testable.
    /// </summary>
    public bool CatalogueIsAvailable => ProvinceChoices().Units.Count > 0;

    /// <summary>
    /// What to tell somebody who cannot choose yet. Empty when they can.
    /// </summary>
    public string UnavailableMessageMn =>
        CatalogueIsAvailable
            ? ""
            : "Засаг захиргааны нэгжийн жагсаалт татагдаагүй байна. " +
              "Холбогдсоны дараа сонгох боломжтой болно; тэр хүртэл доорх " +
              "хаягийн мөрөнд бичнэ үү.";

    /// <summary>The top level. Its heading is fixed - nothing above it names it.</summary>
    public AdministrativeUnitChoices ProvinceChoices() =>
        new("Хот, аймаг", Sorted(catalogue.ChildrenOf("")));

    /// <summary>
    /// The second level, named by the province: «Дүүрэг» under the capital,
    /// «Сум» under an aimag - read, not decided.
    /// </summary>
    public AdministrativeUnitChoices DistrictChoices() =>
        Province is null
            ? new AdministrativeUnitChoices("", [])
            : new AdministrativeUnitChoices(
                Province.ChildPickerLabelMn,
                Sorted(catalogue.ChildrenOf(Province.UnitCode)));

    /// <summary>
    /// The third level, named by the district. This is where a city inside an
    /// aimag says «Баг» while the capital says «Хороо», and where a name like
    /// «Хатгал тосгон» appears under a heading that does not match it.
    /// </summary>
    public AdministrativeUnitChoices WardChoices() =>
        District is null
            ? new AdministrativeUnitChoices("", [])
            : new AdministrativeUnitChoices(
                District.ChildPickerLabelMn,
                Sorted(catalogue.ChildrenOf(District.UnitCode)));

    /// <summary>
    /// Choosing a province CLEARS what was below it. Leaving a district from
    /// the previous province in place is how a location ends up naming two
    /// different regions, and the suggestion rules would then read whichever
    /// one they happened to ask for.
    /// </summary>
    public void ChooseProvince(AdministrativeUnit? province)
    {
        Province = province;
        District = null;
        Ward = null;
    }

    public void ChooseDistrict(AdministrativeUnit? district)
    {
        District = district is not null &&
            Province is not null &&
            district.ParentUnitCode.Equals(Province.UnitCode, StringComparison.Ordinal)
            ? district
            : null;
        Ward = null;
    }

    public void ChooseWard(AdministrativeUnit? ward) =>
        Ward = ward is not null &&
            District is not null &&
            ward.ParentUnitCode.Equals(District.UnitCode, StringComparison.Ordinal)
            ? ward
            : null;

    /// <summary>
    /// Re-opens a stored location by CODE. Names are not consulted: they are
    /// what the project printed at the time, and the catalogue may have renamed
    /// the unit since - matching on them would fail exactly when a place was
    /// renamed, which is the case the stored name exists for.
    /// </summary>
    public void Restore(ProjectSiteLocation? location)
    {
        ChooseProvince(null);
        if (location is null || !location.IsChosen)
            return;

        ChooseProvince(Find(ProvinceChoices().Units, location.ProvinceCode));
        ChooseDistrict(Find(DistrictChoices().Units, location.DistrictCode));
        ChooseWard(Find(WardChoices().Units, location.WardCode));
    }

    /// <summary>
    /// What to store. Empty unless all three levels are chosen - a partial
    /// choice is not a partial answer, and every reader of this value needs the
    /// whole chain.
    /// </summary>
    public ProjectSiteLocation ToLocation()
    {
        if (!AdministrativeUnits.ChainIsConsistent(Province, District, Ward))
            return new ProjectSiteLocation();

        return new ProjectSiteLocation
        {
            ProvinceCode = Province!.UnitCode,
            ProvinceName = Province.NameMn,
            DistrictCode = District!.UnitCode,
            DistrictName = District.NameMn,
            WardCode = Ward!.UnitCode,
            WardName = Ward.NameMn,
            // The heading the ward was chosen UNDER, copied now because it
            // cannot be worked out later from anything stored here.
            WardLabelMn = District.ChildPickerLabelMn,
            CatalogueAsOfUtc = catalogue.AsOfUtc,
        };
    }

    private static AdministrativeUnit? Find(IReadOnlyList<AdministrativeUnit> units, string code) =>
        units.FirstOrDefault(unit => unit.UnitCode.Equals(code, StringComparison.Ordinal));

    private static IReadOnlyList<AdministrativeUnit> Sorted(IReadOnlyList<AdministrativeUnit> units) =>
        units
            .Where(unit => AdministrativeUnits.IsSelectableUnit(unit.UnitCode))
            .OrderBy(unit => unit.NameMn, AdministrativeUnitNameComparer.Instance)
            .ToList();
}
