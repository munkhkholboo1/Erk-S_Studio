using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// Erin, 2026-10-31: the certificate and licence scans reached the server after the album was
/// published with «Хуулбар оруулаагүй» pages. They downloaded onto the desktop and the pages drew
/// right there, yet sync had nothing to send, so the Cloud kept the placeholders. A device that
/// holds every scan of a company page sends that page once per set of scans.
/// </summary>
public sealed class ACOMPANYPageIsSentOnceItsScansArriveTests
{
    private static ProjectWorkspace ProjectWith(params string[] licenceShas)
    {
        var project = new ProjectWorkspace();
        foreach (string sha in licenceShas)
        {
            project.Foundation.DesignCompany.OrganizationSnapshot.DesignLicenseDocuments.Add(
                new ProjectFileReference { IsAvailable = true, Sha256 = sha });
        }
        return project;
    }

    [Fact]
    public void ScansThatArriveMarkTheirPageOnce()
    {
        ProjectWorkspace project = ProjectWith("aa", "bb");

        Assert.True(ProjectCloudSyncMetadata.MarkCompanyPagesWhoseCopiesArrived(project, _ => true));
        Assert.Contains(
            ProjectCloudSyncMetadata.CompanyLicenseComponentCode,
            ProjectCloudSyncMetadata.PendingAlbumComponents(project));
        Assert.DoesNotContain(
            ProjectCloudSyncMetadata.CompanyRegistrationComponentCode,
            ProjectCloudSyncMetadata.PendingAlbumComponents(project));

        project.Cloud.PendingAlbumComponentCodes.Clear();
        Assert.False(ProjectCloudSyncMetadata.MarkCompanyPagesWhoseCopiesArrived(project, _ => true));
        Assert.Empty(ProjectCloudSyncMetadata.PendingAlbumComponents(project));
    }

    [Fact]
    public void ANewScanMarksThePageAgain()
    {
        ProjectWorkspace project = ProjectWith("aa");
        ProjectCloudSyncMetadata.MarkCompanyPagesWhoseCopiesArrived(project, _ => true);
        project.Cloud.PendingAlbumComponentCodes.Clear();

        project.Foundation.DesignCompany.OrganizationSnapshot.DesignLicenseDocuments[0].Sha256 = "cc";

        Assert.True(ProjectCloudSyncMetadata.MarkCompanyPagesWhoseCopiesArrived(project, _ => true));
    }

    [Fact]
    public void APageMissingAScanHereIsNotMarked()
    {
        ProjectWorkspace project = ProjectWith("aa", "bb");
        project.Foundation.DesignCompany.OrganizationSnapshot.DesignLicenseDocuments[1].IsAvailable = false;

        Assert.False(ProjectCloudSyncMetadata.MarkCompanyPagesWhoseCopiesArrived(project, _ => true));
        Assert.False(ProjectCloudSyncMetadata.MarkCompanyPagesWhoseCopiesArrived(ProjectWith("aa"), _ => false));
        Assert.False(ProjectCloudSyncMetadata.MarkCompanyPagesWhoseCopiesArrived(new ProjectWorkspace(), _ => true));
    }
}
