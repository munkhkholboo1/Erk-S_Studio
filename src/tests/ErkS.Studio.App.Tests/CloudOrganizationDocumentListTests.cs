using System.Text.Json;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// The organisation list, read for the fingerprints of what the server already
/// holds.
///
/// Without them the uploader has nothing to compare against and would send the
/// same certificate on every sync - and the server caps each category at five,
/// so a few syncs would fill the organisation with copies of one scan.
/// </summary>
public sealed class CloudOrganizationDocumentListTests
{
    private static readonly JsonSerializerOptions Options =
        new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };

    [Fact]
    public void TheFingerprintsOfWhatTheServerHoldsAreRead()
    {
        const string json = """
            {
              "organizationId": "org-1",
              "legalName": "Монгол Архитектур Дизайн",
              "canManage": true,
              "registrationCertificateDocuments": [
                { "documentId": "d1", "sha256": "AAA", "contentType": "application/pdf" }
              ],
              "designLicenseDocuments": [
                { "documentId": "d2", "sha256": "BBB", "contentType": "image/png" }
              ]
            }
            """;

        StudioCloudOrganization? organization =
            JsonSerializer.Deserialize<StudioCloudOrganization>(json, Options);

        Assert.NotNull(organization);
        Assert.Equal("AAA", Assert.Single(organization!.RegistrationCertificateDocuments).Sha256);
        Assert.Equal("BBB", Assert.Single(organization.DesignLicenseDocuments).Sha256);
    }

    [Fact]
    public void AnOrganisationWithNoDocumentsIsNotAnError()
    {
        // Which is most of them, until people start uploading.
        StudioCloudOrganization? organization = JsonSerializer.Deserialize<StudioCloudOrganization>(
            """{ "organizationId": "org-1", "legalName": "Тест" }""",
            Options);

        Assert.NotNull(organization);
        Assert.Empty(organization!.RegistrationCertificateDocuments);
        Assert.Empty(organization.DesignLicenseDocuments);
    }
}
