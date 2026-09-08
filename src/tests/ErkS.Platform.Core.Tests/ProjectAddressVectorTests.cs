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

        // Still true, and now for a simpler reason: the ward is printed
        // verbatim, so no suffix can appear at all.
        Assert.DoesNotContain("Жинст баг", location.CoverLine(), StringComparison.Ordinal);
        Assert.Contains("Завхан аймаг", location.CoverLine(), StringComparison.Ordinal);
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
    public void AWardIsPrintedEXACTLYAsStored()
    {
        // 🔴 THIS TEST ASSERTED THE OPPOSITE AN HOUR AGO, AND THE REVERSAL IS
        // THE POINT. I had found by mutation that the vectors could not see a
        // missing ward-label lookup, and concluded the lookup must therefore be
        // exercised. The gap was real; the conclusion was backwards.
        //
        // Measured on the catalogue: 1 852 of 1 853 ward names already contain
        // their word, so appending helps none of them. The single unit that
        // does not is «Хатгал тосгон» - and the label the picker sends for it
        // is «Баг», because the heading comes from the PARENT's level.
        // Appending would print «Хатгал тосгон баг»: a place that does not
        // exist, on a signed sheet.
        //
        // So the rule would have been right 1 852 times and wrong exactly
        // where it mattered. A minority of one was not an edge case to note -
        // it was the case that decided the rule.
        var location = new ProjectSiteLocation
        {
            ProvinceCode = "267",
            ProvinceName = "Хөвсгөл",
            DistrictCode = "26704",
            DistrictName = "Алаг-Эрдэнэ",
            WardCode = "2670401",
            WardName = "Хатгал тосгон",
            WardLabelMn = "Баг",
        };

        Assert.Equal("Хөвсгөл аймаг, Алаг-Эрдэнэ сум, Хатгал тосгон", location.CoverLine());
        Assert.DoesNotContain("тосгон баг", location.CoverLine(), StringComparison.OrdinalIgnoreCase);
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
