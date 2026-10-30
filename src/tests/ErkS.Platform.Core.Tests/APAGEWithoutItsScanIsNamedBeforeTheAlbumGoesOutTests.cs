using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// Erin R64-R66 went out with the company certificate and licence pages printing «Хуулбар
/// оруулаагүй», and nobody was told (Мастер, 2026-10-31). A page that will print the
/// placeholder is named in the album warnings.
/// </summary>
public sealed class APAGEWithoutItsScanIsNamedBeforeTheAlbumGoesOutTests
{
    private static AlbumBuildRequest Request(CompanyProfile company) => new()
    {
        Project = new AlbumProject
        {
            Company = company,
            Album = BuildingArchitectureConceptAlbumTemplate.CreateDefinition("Concept album"),
        },
        Sections = [],
    };

    [Fact]
    public void BothMissingScansAreNamedOnceEach()
    {
        IReadOnlyList<string> warnings = MissingDocumentCopySurvey.Review(Request(new CompanyProfile { Name = "НАНУТО" }));

        Assert.Contains(warnings, line => line.Contains("гэрчилгээ", StringComparison.Ordinal));
        Assert.Contains(warnings, line => line.Contains("тусгай зөвшөөрөл", StringComparison.Ordinal));
        Assert.All(warnings, line => Assert.StartsWith(MissingDocumentCopySurvey.Prefix, line, StringComparison.Ordinal));
        Assert.Equal(warnings.Count, warnings.Distinct().Count());
    }

    [Fact]
    public void AScanThatIsHereIsNotNamed()
    {
        var company = new CompanyProfile { Name = "НАНУТО" };
        company.RegistrationCertificateDocuments.Add(new ProjectFileReference { IsAvailable = true, PageCount = 1 });

        IReadOnlyList<string> warnings = MissingDocumentCopySurvey.Review(Request(company));

        Assert.DoesNotContain(warnings, line => line.Contains("гэрчилгээ", StringComparison.Ordinal));
        Assert.Contains(warnings, line => line.Contains("тусгай зөвшөөрөл", StringComparison.Ordinal));
    }
}
