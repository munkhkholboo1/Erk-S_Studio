using System.Text;
using ErkS.Platform.Core;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// What the user found on 2026-09-07: «төслийн хаягийг сонгож хадгалчихаад
/// байхад хадгалагдахгүй байна. буцаагаад нээхэд буцаад хоосон болчихоод
/// байна».
///
/// Measured before anything was changed. The newest project file on disk had an
/// all-empty siteLocation, so the value was not being WRITTEN - which ruled out
/// two of the three candidates immediately and left two real causes, both of
/// which are fixed here.
/// </summary>
public sealed class SiteLocationRoundTripTests
{
    private sealed class Catalogue : IAdministrativeUnitCatalogue
    {
        private readonly List<AdministrativeUnit> units =
        [
            new("511", AdministrativeUnits.Capital, "", "Улаанбаатар", "Дүүрэг", true),
            new("51101", AdministrativeUnits.District, "511", "Багануур", "Хороо", true),
            new("5110151", AdministrativeUnits.Khoroo, "51101", "1-р хороо", ""),
        ];

        public DateTimeOffset? AsOfUtc { get; } = new(2026, 9, 6, 8, 0, 0, TimeSpan.Zero);

        public string UnavailableReasonMn => "";

        public IReadOnlyList<AdministrativeUnit> ChildrenOf(string? parentUnitCode) =>
            units.Where(unit => unit.ParentUnitCode == (parentUnitCode ?? "").Trim()).ToList();
    }

    private static AdministrativeUnitPicker Picker() => new(new Catalogue());

    [Fact]
    public void APROVINCEAloneSurvivesSaveAndReopen()
    {
        // 🔴 THE DEFECT. ToLocation returned an empty location unless all three
        // levels were chosen, so somebody who picked an aimag and pressed save
        // had their choice thrown away by the code that was meant to store it -
        // silently, and they found out by reopening the project.
        var picker = Picker();
        picker.ChooseProvince(picker.ProvinceChoices().Units.Single());

        ProjectSiteLocation saved = picker.ToLocation();
        Assert.Equal("511", saved.ProvinceCode);
        Assert.Equal("Улаанбаатар", saved.ProvinceName);

        var reopened = Picker();
        reopened.Restore(saved);
        Assert.Equal("511", reopened.Province?.UnitCode);

        // Kept and still not usable: nothing downstream acts on a partial
        // location, which was the only thing the discard was ever protecting.
        Assert.False(saved.IsChosen);
        // 🔴 THE RULE CHANGED, DELIBERATELY. A partial choice now composes
        // what it has. «Орхон аймаг» is a real place and blanking it threw
        // away something entered on purpose; the shared vectors require the
        // province-only case to print. IsChosen is unchanged and still means
        // "complete enough to reason about".
        Assert.Equal("Улаанбаатар хот", saved.CoverLine());
    }

    [Fact]
    public void TWOLevelsSurviveToo()
    {
        var picker = Picker();
        picker.ChooseProvince(picker.ProvinceChoices().Units.Single());
        picker.ChooseDistrict(picker.DistrictChoices().Units.Single());

        ProjectSiteLocation saved = picker.ToLocation();
        Assert.Equal("51101", saved.DistrictCode);
        Assert.Equal("Багануур", saved.DistrictName);
        Assert.Equal("", saved.WardCode);

        var reopened = Picker();
        reopened.Restore(saved);
        Assert.Equal("511", reopened.Province?.UnitCode);
        Assert.Equal("51101", reopened.District?.UnitCode);
        Assert.Null(reopened.Ward);
    }

    [Fact]
    public void AWHOLEChoiceStillRoundTripsWithItsHeadingAndVersion()
    {
        var picker = Picker();
        picker.ChooseProvince(picker.ProvinceChoices().Units.Single());
        picker.ChooseDistrict(picker.DistrictChoices().Units.Single());
        picker.ChooseWard(picker.WardChoices().Units.Single());

        ProjectSiteLocation saved = picker.ToLocation();

        Assert.True(saved.IsChosen);
        Assert.Equal("5110151", saved.WardCode);
        Assert.Equal("Хороо", saved.WardLabelMn);
        Assert.Equal(new DateTimeOffset(2026, 9, 6, 8, 0, 0, TimeSpan.Zero), saved.CatalogueAsOfUtc);
        Assert.Equal("Улаанбаатар хот, Багануур дүүрэг, 1-р хороо", saved.CoverLine());

        var reopened = Picker();
        reopened.Restore(saved);
        Assert.Equal("5110151", reopened.Ward?.UnitCode);
    }

    [Fact]
    public void THECatalogueVersionIsRecordedEvenBeforeAnythingIsChosen()
    {
        // Which copy of the catalogue was on screen is a fact about the moment,
        // not about the completeness of the choice.
        Assert.Equal(
            new DateTimeOffset(2026, 9, 6, 8, 0, 0, TimeSpan.Zero),
            Picker().ToLocation().CatalogueAsOfUtc);
    }

    [Fact]
    public void NEITHERLowerPickerIsEverNAMELESS()
    {
        // The second thing the user met: three dropdowns, one labelled. Before a
        // province is chosen the catalogue cannot say what the next level is
        // called - Улаанбаатар has «Дүүрэг» under it, Архангай has «Сум» - so the
        // published heading is empty and the box was drawn with nothing above it.
        var waiting = new AdministrativeUnitChoices("", [], ParentIsChosen: false);

        Assert.Equal(
            SiteLocationLabels.DistrictProvisionalMn,
            SiteLocationLabels.DisplayHeadingFor(waiting, SiteLocationLabels.DistrictProvisionalMn));
        Assert.Contains("Сум", SiteLocationLabels.DistrictProvisionalMn, StringComparison.Ordinal);
        Assert.Contains("Дүүрэг", SiteLocationLabels.DistrictProvisionalMn, StringComparison.Ordinal);
    }

    [Fact]
    public void THEPublishedHeadingALWAYSWinsOverTheProvisionalOne()
    {
        // The provisional words are a placeholder, never a rule. The moment the
        // catalogue says «Дүүрэг» - or «Тосгон», or a word this build has never
        // seen - that is what shows.
        var chosen = new AdministrativeUnitChoices(
            "ZZZ-ТЕСТ-4471",
            [new AdministrativeUnit("5110151", "Khoroo", "51101", "1-р хороо", "")]);

        Assert.Equal(
            "ZZZ-ТЕСТ-4471",
            SiteLocationLabels.DisplayHeadingFor(chosen, SiteLocationLabels.WardProvisionalMn));
    }

    [Fact]
    public void THEProvisionalHeadingIsNotUsedWhereItWouldBeALIE()
    {
        // A level whose parent IS chosen and which lists nothing has a real
        // heading from the catalogue. If it somehow has none, showing «Баг /
        // Хороо» would invent a name for a level the catalogue never described.
        var emptyByData = new AdministrativeUnitChoices("", [], ParentIsChosen: true);

        Assert.Equal(
            "",
            SiteLocationLabels.DisplayHeadingFor(emptyByData, SiteLocationLabels.WardProvisionalMn));
    }

    [Fact]
    public void ANUnavailableCatalogueMustNotERASEAStoredChoice()
    {
        // 🔴 THE WORST OF THE THREE, and it was hiding behind the other two.
        //
        // The picker restores by looking each stored code up in the catalogue.
        // With no catalogue - refused, offline, still loading - nothing is
        // found, the picker holds nothing, and a save wrote that nothing over a
        // location chosen weeks earlier. The person would have had to notice a
        // picker quietly emptying itself to know it was coming.
        //
        // Measured on the picker: this is the state the save used to capture.
        var stored = new ProjectSiteLocation
        {
            ProvinceCode = "511",
            ProvinceName = "Улаанбаатар",
            DistrictCode = "51101",
            DistrictName = "Багануур",
            WardCode = "5110151",
            WardName = "1-р хороо",
            WardLabelMn = "Хороо",
        };

        var offline = new AdministrativeUnitPicker(new EmptyCatalogue());
        offline.Restore(stored);

        Assert.False(offline.CatalogueIsAvailable);
        Assert.Equal("", offline.ToLocation().ProvinceCode);

        // So the view may not simply take ToLocation() when the catalogue could
        // not answer - it keeps what was stored.
        string view = ReadAppSource("ShellView.SiteLocation.cs");
        int capture = view.IndexOf("private ProjectSiteLocation CaptureSiteLocationDraft()", StringComparison.Ordinal);
        Assert.True(capture > 0, "the capture method was not found");
        // Clamped: this method is the last thing in the file, so a fixed-length
        // slice runs off the end. The third time a source-reading test in this
        // repository has been broken by its own arithmetic rather than by the
        // thing it was checking.
        string body = view[capture..Math.Min(view.Length, capture + 700)];

        Assert.Contains("!sitePicker.CatalogueIsAvailable", body, StringComparison.Ordinal);
        Assert.Contains("return stored;", body, StringComparison.Ordinal);
    }

    private sealed class EmptyCatalogue : IAdministrativeUnitCatalogue
    {
        public DateTimeOffset? AsOfUtc => null;

        public string UnavailableReasonMn => "Жагсаалт татагдсангүй.";

        public IReadOnlyList<AdministrativeUnit> ChildrenOf(string? parentUnitCode) => [];
    }

    [Fact]
    public void AFAILEDFetchSaysSoWhereThePickersAre()
    {
        // The durable half of the 403. The header that unblocked it is a
        // mitigation - Cloudflare scores the whole client and can refuse again
        // tomorrow on some other signal - so what has to hold is that a failed
        // fetch is VISIBLE within seconds instead of looking like an empty
        // country.
        var picker = new AdministrativeUnitPicker(new EmptyCatalogue());

        Assert.False(picker.CatalogueIsAvailable);
        Assert.Equal("Жагсаалт татагдсангүй.", picker.UnavailableMessageMn);

        // And the view shows exactly that, without composing its own sentence.
        string view = ReadAppSource("ShellView.SiteLocation.cs");
        Assert.Contains("siteLocationMessage.Text = sitePicker.UnavailableMessageMn;", view, StringComparison.Ordinal);
    }

    [Fact]
    public void STUDIOTellsTheServerWhoItIs()
    {
        // 🔴 THE ROOT CAUSE OF BOTH COMPLAINTS, and it was not in this code at
        // all. erk-s.mn sits behind Cloudflare, which refuses a request with no
        // User-Agent (error 1010) - and .NET's HttpClient sends none. So the
        // catalogue answered 403, the pickers stayed empty, nothing could be
        // chosen, and nothing was stored.
        //
        // Isolated against production by sending one header at a time: no
        // headers → 403, Accept alone → 403, User-Agent alone → 200 with 2217
        // units.
        Assert.StartsWith("ErkS-Studio/", StudioHttpIdentity.UserAgent, StringComparison.Ordinal);

        using var client = new System.Net.Http.HttpClient();
        StudioHttpIdentity.Identify(client);
        Assert.NotEmpty(client.DefaultRequestHeaders.UserAgent);

        // Applied once - a second call must not stack a second product token.
        StudioHttpIdentity.Identify(client);
        Assert.Single(client.DefaultRequestHeaders.UserAgent);
    }

    [Theory]
    [InlineData("1.2.3+abc.def", "ErkS-Studio/1.2.3+abc.def")]
    [InlineData("1.2.3 dev build", "ErkS-Studio/1.2.3-dev-build")]
    [InlineData("", "ErkS-Studio")]
    public void AVersionThatIsNotALegalHeaderValueDoesNotThrowAtTheFirstREQUEST(
        string version,
        string expected)
    {
        // A development version can carry a space or build metadata. Finding
        // that out when the first call throws would look like a network fault.
        Assert.Equal(expected, StudioHttpIdentity.Sanitise("ErkS-Studio/" + version).TrimEnd('/'));
    }

    [Fact]
    public void BOTHHttpClientsCarryTheIdentity()
    {
        // The account service and the catalogue build their own clients. One of
        // them remembering is how the next 403 goes unexplained.
        string account = ReadAppSource("StudioAccountService.cs");
        string catalogue = ReadAppSource("StudioAdministrativeUnitCatalogue.cs");

        Assert.Contains("StudioHttpIdentity.Identify(httpClient);", account, StringComparison.Ordinal);
        Assert.Contains("StudioHttpIdentity.Identify(client);", catalogue, StringComparison.Ordinal);
    }

    private static string ReadAppSource(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(
                directory.FullName, "src", "src", "ErkS.Studio.App", fileName);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate, Encoding.UTF8);
            directory = directory.Parent;
        }

        Assert.Fail(fileName + " was not found; this test reads it from source");
        return "";
    }
}
