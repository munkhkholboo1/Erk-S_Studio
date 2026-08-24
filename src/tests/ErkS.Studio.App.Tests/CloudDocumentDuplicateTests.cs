using ErkS.Platform.Core;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// The server counts document records per category, not distinct content, so
/// the same scan uploaded twice through the website is stored twice.
/// </summary>
public sealed class CloudDocumentDuplicateTests
{
    private static StudioCloudOrganizationDocument Doc(string id, string sha) => new()
    {
        DocumentId = id,
        Sha256 = sha,
        Title = "Байгууллагын гэрчилгээ",
        ContentType = "application/pdf",
        OriginalFileName = "gerchilgee.pdf",
    };

    [Fact]
    public void TheSameScanStoredTwiceBecomesOneAlbumDocument()
    {
        var cloud = new StudioCloudOrganizationRenderProfile
        {
            OrganizationId = "org-1",
            LegalName = "Эрк-С ХХК",
            RegistrationCertificateDocuments =
            [
                Doc("doc-a", "same-hash"),
                Doc("doc-b", "same-hash"),
            ],
        };

        CompanyProfile profile = StudioCompanyProfileMapper.FromRenderProfile(cloud);

        ProjectFileReference kept = Assert.Single(profile.RegistrationCertificateDocuments);
        Assert.Equal("doc-a", kept.ServerDocumentId);
    }

    [Fact]
    public void TwoGenuinelyDifferentScansBothSurvive()
    {
        var cloud = new StudioCloudOrganizationRenderProfile
        {
            OrganizationId = "org-1",
            LegalName = "Эрк-С ХХК",
            RegistrationCertificateDocuments =
            [
                Doc("doc-a", "hash-one"),
                Doc("doc-b", "hash-two"),
            ],
        };

        CompanyProfile profile = StudioCompanyProfileMapper.FromRenderProfile(cloud);

        Assert.Equal(2, profile.RegistrationCertificateDocuments.Count);
    }

    [Fact]
    public void DocumentsTheServerCouldNotHashKeepTheirOwnIdentity()
    {
        // With no hash there is nothing to compare, and collapsing them would
        // hide a real second scan.
        var cloud = new StudioCloudOrganizationRenderProfile
        {
            OrganizationId = "org-1",
            LegalName = "Эрк-С ХХК",
            RegistrationCertificateDocuments =
            [
                Doc("doc-a", ""),
                Doc("doc-b", ""),
            ],
        };

        CompanyProfile profile = StudioCompanyProfileMapper.FromRenderProfile(cloud);

        Assert.Equal(2, profile.RegistrationCertificateDocuments.Count);
    }
}
