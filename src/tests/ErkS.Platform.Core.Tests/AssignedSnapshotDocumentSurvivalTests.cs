using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// The organisation's scans reach a project from the cloud, and reach the
/// company library from this device. Refreshing one from the other must not
/// throw away what only the other one has.
/// </summary>
public sealed class AssignedSnapshotDocumentSurvivalTests
{
    private static ProjectWorkspace ProjectAssignedTo(string organizationId)
    {
        ProjectWorkspace project = ProjectWorkspaceStore.Create("TEST-1", "Тест төсөл");
        project.Foundation.DesignCompany.OrganizationId = organizationId;
        project.Foundation.DesignCompany.OrganizationName = "Эрк-С ХХК";
        project.Foundation.DesignCompany.OrganizationSnapshot = new CompanyProfile
        {
            OrganizationId = organizationId,
            Name = "Эрк-С ХХК",
        };
        return project;
    }

    private static ProjectFileReference CloudDocument(string id) => new()
    {
        Category = ProjectDocumentCategories.CompanyRegistrationCertificate,
        Title = "Байгууллагын гэрчилгээ",
        ServerDocumentId = id,
        Sha256 = "hash-" + id,
        ContentType = "application/pdf",
        RelativePath = $"assets/organization-documents/{id}.pdf",
        IsAvailable = true,
    };

    [Fact]
    public void ACloudFetchedCertificateSurvivesALibraryDrivenRefresh()
    {
        // The certificate came down from the server into the project. The
        // company library on this device has never held it: nobody here
        // uploaded it, and the organisation list does not carry documents.
        //
        // Refreshing the project's snapshot from that library replaces the
        // whole profile, so without care the certificate is dropped and the
        // album silently loses a page that was working a moment ago.
        ProjectWorkspace project = ProjectAssignedTo("org-1");
        project.Foundation.DesignCompany.OrganizationSnapshot.RegistrationCertificateDocuments =
        [
            CloudDocument("doc-1"),
        ];

        var libraryProfile = new CompanyProfile
        {
            OrganizationId = "org-1",
            Name = "Эрк-С ХХК",
            DirectorName = "О.Очир-Эрдэнэ",
        };

        ProjectCompanyAssignmentService.RefreshAssignedSnapshot(project, libraryProfile);

        ProjectFileReference kept = Assert.Single(
            project.Foundation.DesignCompany.OrganizationSnapshot.RegistrationCertificateDocuments);
        Assert.Equal("doc-1", kept.ServerDocumentId);
    }

    [Fact]
    public void ALibraryCertificateStillReachesAProjectThatHasNone()
    {
        // The other direction has to keep working: a scan added on this device
        // is how most projects got their certificate before the cloud carried
        // them at all.
        ProjectWorkspace project = ProjectAssignedTo("org-1");

        var libraryProfile = new CompanyProfile
        {
            OrganizationId = "org-1",
            Name = "Эрк-С ХХК",
            RegistrationCertificateDocuments =
            [
                new ProjectFileReference
                {
                    Category = ProjectDocumentCategories.CompanyRegistrationCertificate,
                    Title = "Байгууллагын гэрчилгээ",
                    Sha256 = "hash-local",
                    IsAvailable = true,
                },
            ],
        };

        ProjectCompanyAssignmentService.RefreshAssignedSnapshot(project, libraryProfile);

        ProjectFileReference kept = Assert.Single(
            project.Foundation.DesignCompany.OrganizationSnapshot.RegistrationCertificateDocuments);
        Assert.Equal("hash-local", kept.Sha256);
    }

    [Fact]
    public void TheSameScanFromBothSidesIsNotStoredTwice()
    {
        // A document uploaded from this device and then synced down again is
        // one scan, and the album would otherwise draw it on two pages.
        ProjectWorkspace project = ProjectAssignedTo("org-1");
        project.Foundation.DesignCompany.OrganizationSnapshot.RegistrationCertificateDocuments =
        [
            CloudDocument("doc-1"),
        ];

        var libraryProfile = new CompanyProfile
        {
            OrganizationId = "org-1",
            Name = "Эрк-С ХХК",
            RegistrationCertificateDocuments =
            [
                new ProjectFileReference
                {
                    Category = ProjectDocumentCategories.CompanyRegistrationCertificate,
                    Title = "Байгууллагын гэрчилгээ",
                    Sha256 = "hash-doc-1",
                    IsAvailable = true,
                },
            ],
        };

        ProjectCompanyAssignmentService.RefreshAssignedSnapshot(project, libraryProfile);

        Assert.Single(
            project.Foundation.DesignCompany.OrganizationSnapshot.RegistrationCertificateDocuments);
    }
}
