using System.Text.Json;
using ErkS.Platform.Core;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// The organisation's certificate and licence, arriving from the server.
///
/// Somebody uploads these into their organisation once. Until this contract
/// existed they lived only on the machine of whoever added them, so a
/// colleague opening the same project found the certificate page empty - and
/// the album told them, in effect, that they had not uploaded it. They had.
///
/// The JSON below is the sample the server team produced from their own
/// implementation, kept verbatim, so a change on their side breaks this test
/// rather than a client somewhere.
/// </summary>
public sealed class CloudOrganizationDocumentMappingTests
{
    private const string RenderProfileJson = """
        {
          "organizationId": "org-1",
          "legalName": "Монгол Архитектур Дизайн",
          "registrationCertificateDocuments": [
            {
              "documentId": "9f2c4b1e7a8d4f0c9b3e5a2d6c7f8e10",
              "category": "RegistrationCertificate",
              "title": "Улсын бүртгэлийн гэрчилгээ",
              "originalFileName": "gerchilgee.pdf",
              "contentType": "application/pdf",
              "sizeBytes": 482913,
              "pageCount": 1,
              "sha256": "3B0C9A64",
              "contentUrl": "/api/cloud-era/v1/projects/c59b2a4c/design-organization/documents/9f2c4b1e/content",
              "updatedAtUtc": "2026-08-24T10:40:00+00:00"
            }
          ],
          "designLicenseDocuments": [
            {
              "documentId": "aa11bb22cc33dd44ee55ff6677889900",
              "category": "DesignLicense",
              "title": "Тусгай зөвшөөрөл",
              "originalFileName": "zovshoorol.png",
              "contentType": "image/png",
              "sizeBytes": 91234,
              "pageCount": 0,
              "sha256": "77AA11BB",
              "contentUrl": "/api/cloud-era/v1/projects/c59b2a4c/design-organization/documents/aa11bb22/content"
            }
          ]
        }
        """;

    private static CompanyProfile Mapped()
    {
        StudioCloudOrganizationRenderProfile? cloud =
            JsonSerializer.Deserialize<StudioCloudOrganizationRenderProfile>(
                RenderProfileJson,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(cloud);
        return StudioCompanyProfileMapper.FromRenderProfile(cloud!);
    }

    [Fact]
    public void BothDocumentsArriveUnderTheCategoriesTheAlbumLooksFor()
    {
        CompanyProfile profile = Mapped();

        Assert.Equal(
            ProjectDocumentCategories.CompanyRegistrationCertificate,
            Assert.Single(profile.RegistrationCertificateDocuments).Category);
        Assert.Equal(
            ProjectDocumentCategories.CompanyDesignLicense,
            Assert.Single(profile.DesignLicenseDocuments).Category);
    }

    [Fact]
    public void TheServersDocumentIdIsKept()
    {
        // It is how the file is asked for later.
        Assert.Equal(
            "9f2c4b1e7a8d4f0c9b3e5a2d6c7f8e10",
            Mapped().RegistrationCertificateDocuments[0].ServerDocumentId);
    }

    [Fact]
    public void AnUncountedDocumentKeepsItsZeroRatherThanBeingGivenAPageCount()
    {
        // 0 means "the server could not count these", not "no pages". Turning
        // it into 1 here would throw away the distinction the two teams agreed
        // to keep.
        Assert.Equal(0, Mapped().DesignLicenseDocuments[0].PageCount);
    }

    [Fact]
    public void ADocumentTheDeviceHasNotFetchedIsNotClaimedAsAvailable()
    {
        // The album draws from a file on disk. Marking these available would
        // send it looking for one that is not there.
        Assert.All(
            Mapped().RegistrationCertificateDocuments.Concat(Mapped().DesignLicenseDocuments),
            document =>
            {
                Assert.False(document.IsAvailable);
                Assert.True(document.IsCloudPlaceholder);
            });
    }

    [Fact]
    public void AnImageDocumentIsCarriedAsFaithfullyAsAPdf()
    {
        // The user's own certificates were uploaded as images, and the album's
        // renderer draws images perfectly well.
        ProjectFileReference licence = Mapped().DesignLicenseDocuments[0];

        Assert.Equal("image/png", licence.ContentType);
        Assert.Equal("zovshoorol.png", licence.OriginalFileName);
    }

    [Fact]
    public void AServerThatPredatesTheFieldIsNotAnError()
    {
        // Which is every server until the next deploy.
        StudioCloudOrganizationRenderProfile? cloud =
            JsonSerializer.Deserialize<StudioCloudOrganizationRenderProfile>(
                """{ "organizationId": "org-1", "legalName": "Тест" }""",
                new JsonSerializerOptions(JsonSerializerDefaults.Web));

        CompanyProfile profile = StudioCompanyProfileMapper.FromRenderProfile(cloud!);

        Assert.Empty(profile.RegistrationCertificateDocuments);
        Assert.Empty(profile.DesignLicenseDocuments);
    }

    [Fact]
    public void ADocumentWithNoIdIsDropped()
    {
        // There would be no way to ask for its content, so carrying it would
        // only produce a page that can never be filled.
        StudioCloudOrganizationRenderProfile? cloud =
            JsonSerializer.Deserialize<StudioCloudOrganizationRenderProfile>(
                """
                { "organizationId": "org-1",
                  "registrationCertificateDocuments": [ { "title": "Гэрчилгээ" } ] }
                """,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Empty(StudioCompanyProfileMapper.FromRenderProfile(cloud!).RegistrationCertificateDocuments);
    }
}
