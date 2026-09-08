using System.Text.Json;
using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// The shared address vectors, executed here.
///
/// 🔴 TWO COMPOSERS EXIST AND BOTH MUST. Studio composes because an album has
/// to print with no network; the server composes because it cannot store the
/// composed text - that would be keeping a derived value as a fact, which the
/// contract forbids for good reasons. So "which one composes" has no answer,
/// and the real question is how they are kept from drifting.
///
/// Not by hope: by this file. The same vectors run in both suites, so a side
/// that changes its mind goes red in its OWN tests rather than producing a
/// different sentence on a printed sheet than the one the website shows.
///
/// The vectors are built from units that exist in the real catalogue. That
/// matters more than it sounds: an invented code lets two sides agree about a
/// place that does not exist, which proves nothing at all.
/// </summary>
public sealed class ProjectAddressVectorTests
{
    private sealed record VectorFile(List<Vector> Cases);

    private sealed record Vector(
        string Name,
        string? Note,
        VectorInput Input,
        string? ExpectFull,
        string? ExpectShort);

    private sealed record VectorInput(
        string? ProvinceCode,
        string? ProvinceName,
        string? DistrictCode,
        string? DistrictName,
        string? WardCode,
        string? WardName,
        string? WardLabelMn,
        string? AddressLine);

    public static TheoryData<string> VectorNames()
    {
        var names = new TheoryData<string>();
        foreach (Vector vector in Load().Cases)
            names.Add(vector.Name);
        return names;
    }

    [Theory]
    [MemberData(nameof(VectorNames))]
    public void THEComposerMatchesTheSharedVector(string name)
    {
        Vector vector = Load().Cases.Single(item => item.Name == name);
        ProjectSiteLocation location = LocationOf(vector.Input);
        string address = vector.Input.AddressLine ?? "";

        Assert.Equal(vector.ExpectFull, ProjectSiteAddress.Compose(location, address));
        Assert.Equal(vector.ExpectShort, ProjectSiteAddress.ComposeShort(location, address));
    }

    [Fact]
    public void EVERYVectorIsActuallyRun()
    {
        // 🔴 THE CONTROL FOR THE THEORY ABOVE. A vector file that failed to load
        // would produce zero cases and a green run - the shape of "the checker
        // was silently switched off" this codebase keeps producing. Seven is
        // what the file carries today; a changed count is a deliberate edit and
        // should be seen.
        Assert.Equal(7, Load().Cases.Count);
        Assert.All(Load().Cases, vector => Assert.False(string.IsNullOrWhiteSpace(vector.Name)));
    }

    [Fact]
    public void THELabelIsMatchedByCONTAINSRatherThanEndsWith()
    {
        // The server's first composer used EndsWith and printed «1-р баг, Жинст
        // баг» on 1 544 units - the label sits in the MIDDLE of those names.
        // Asserted directly so the reason survives even if that vector is one
        // day reworded.
        var location = new ProjectSiteLocation
        {
            ProvinceCode = "181",
            ProvinceName = "Завхан",
            DistrictCode = "18101",
            DistrictName = "Улиастай",
            WardCode = "1810101",
            WardName = "1-р баг, Жинст",
            WardLabelMn = "баг",
        };

        Assert.DoesNotContain("Жинст баг", location.CoverLine(), StringComparison.Ordinal);
    }

    [Fact]
    public void APARTIALChoiceStillPrintsWhatWasChosen()
    {
        // A province and nothing else is a real place. Refusing to print it
        // because the chain is incomplete would throw away what somebody
        // deliberately entered.
        var location = new ProjectSiteLocation
        {
            ProvinceCode = "265",
            ProvinceName = "Орхон",
        };

        Assert.False(location.IsChosen);
        Assert.Equal("Орхон аймаг", ProjectSiteAddress.Compose(location, ""));
    }

    private static ProjectSiteLocation LocationOf(VectorInput input) => new()
    {
        ProvinceCode = input.ProvinceCode ?? "",
        ProvinceName = input.ProvinceName ?? "",
        DistrictCode = input.DistrictCode ?? "",
        DistrictName = input.DistrictName ?? "",
        WardCode = input.WardCode ?? "",
        WardName = input.WardName ?? "",
        WardLabelMn = input.WardLabelMn ?? "",
    };

    private static VectorFile Load()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory, "contracts", "project-address-vectors.json");
        Assert.True(File.Exists(path), "the shared address vectors were not copied to the output: " + path);

        VectorFile? file = JsonSerializer.Deserialize<VectorFile>(
            File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(file);
        Assert.NotEmpty(file!.Cases);
        return file;
    }

    [Fact]
    public void AWardWhoseNameDoesNOTCarryItsLabelGetsOneAppended()
    {
        // 🔴 A GAP IN THE SHARED VECTORS, FOUND BY MUTATION. Deleting the label
        // lookup for the ward level left all seven vectors green - because
        // every one of them uses a real unit whose name ALREADY contains its
        // label («1-р хороо», «1-р баг, Зэст», «Хатгал тосгон»). Measured on the
        // catalogue: 1 852 of 1 853 ward names carry their own label.
        //
        // So the vectors cannot see whether the lookup works at all - they only
        // check that it does not fire wrongly. The remaining unit, and any
        // future one, needs this.
        var location = new ProjectSiteLocation
        {
            ProvinceCode = "511",
            ProvinceName = "Улаанбаатар",
            DistrictCode = "51116",
            DistrictName = "Сонгинохайрхан",
            WardCode = "5111651",
            WardName = "Зүүн салаа",
            WardLabelMn = "хороо",
        };

        Assert.Equal(
            "Улаанбаатар хот, Сонгинохайрхан дүүрэг, Зүүн салаа хороо",
            location.CoverLine());
    }

    [Fact]
    public void APROVINCEWhoseNameCarriesItsLabelIsNotDoubled()
    {
        // The other direction at the top level. 0 of 22 province names carry
        // theirs today, so this is the case that would appear the day one does.
        var location = new ProjectSiteLocation
        {
            ProvinceCode = "265",
            ProvinceName = "Орхон аймаг",
        };

        Assert.Equal("Орхон аймаг", location.CoverLine());
    }
}
