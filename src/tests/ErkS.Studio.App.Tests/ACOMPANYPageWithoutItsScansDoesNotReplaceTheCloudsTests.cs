using ErkS.Platform.Core;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// Erin, 2026-10-31: R1-R63 carried the company certificate (1 page) and design licence
/// (4 pages) scanned. The owner's desktop had neither scan, redrew both as «Хуулбар
/// оруулаагүй» and sent them: R64 fell from 44 pages to 41 and every member saw the
/// placeholder. A device without the scans keeps the Cloud's pages.
/// </summary>
public sealed class ACOMPANYPageWithoutItsScansDoesNotReplaceTheCloudsTests
{
    private static ProjectWorkspace ProjectWithCloudPages()
    {
        var project = new ProjectWorkspace();
        project.Cloud.SharedAlbumComponents =
        [
            new ProjectCloudAlbumComponentReference
            {
                Code = ProjectCloudSyncMetadata.CompanyRegistrationComponentCode,
                Status = "Available",
            },
            new ProjectCloudAlbumComponentReference
            {
                Code = ProjectCloudSyncMetadata.CompanyLicenseComponentCode,
                Status = "Available",
            },
        ];
        return project;
    }

    [Fact]
    public void WithoutTheScansTheCloudPageStands()
    {
        ProjectWorkspace project = ProjectWithCloudPages();

        Assert.True(CloudSyncPreviewPlanner.OrganizationDocumentsMissingHere(
            project, ProjectCloudSyncMetadata.CompanyRegistrationComponentCode, _ => true));
        Assert.True(CloudSyncPreviewPlanner.OrganizationDocumentsMissingHere(
            project, ProjectCloudSyncMetadata.CompanyLicenseComponentCode, _ => true));
    }

    [Fact]
    public void AScanTheServerHoldsButThisDeviceHasNotFetchedIsMissing()
    {
        ProjectWorkspace project = ProjectWithCloudPages();
        project.Foundation.DesignCompany.OrganizationSnapshot.DesignLicenseDocuments.Add(
            new ProjectFileReference { ServerDocumentId = "d1", IsAvailable = false, IsCloudPlaceholder = true });

        Assert.True(CloudSyncPreviewPlanner.OrganizationDocumentsMissingHere(
            project, ProjectCloudSyncMetadata.CompanyLicenseComponentCode, _ => true));
    }

    [Fact]
    public void WithTheScansHereThePageIsSent()
    {
        ProjectWorkspace project = ProjectWithCloudPages();
        project.Foundation.DesignCompany.OrganizationSnapshot.RegistrationCertificateDocuments.Add(
            new ProjectFileReference { RelativePath = "assets/organization-documents/a.jpg", IsAvailable = true });

        Assert.False(CloudSyncPreviewPlanner.OrganizationDocumentsMissingHere(
            project, ProjectCloudSyncMetadata.CompanyRegistrationComponentCode, _ => true));
    }

    [Fact]
    public void AProjectWithNoCloudPageYetStillGetsOne()
    {
        var project = new ProjectWorkspace();

        Assert.False(CloudSyncPreviewPlanner.OrganizationDocumentsMissingHere(
            project, ProjectCloudSyncMetadata.CompanyRegistrationComponentCode, _ => true));
    }

    [Fact]
    public void OtherComponentsAreNotTouched()
    {
        ProjectWorkspace project = ProjectWithCloudPages();

        Assert.False(CloudSyncPreviewPlanner.OrganizationDocumentsMissingHere(
            project, ProjectCloudSyncMetadata.CoverComponentCode, _ => true));
    }
}
