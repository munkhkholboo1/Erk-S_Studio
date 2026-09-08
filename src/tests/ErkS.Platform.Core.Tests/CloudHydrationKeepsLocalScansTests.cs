using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// 🔴 THE BRANCH NOBODY TESTED. The existing coverage for this merge passes an
/// EMPTY cloud document list - which is the case where nothing can be
/// re-downloaded, so it proves nothing about the expensive path. The missing
/// lock was a test with a NON-EMPTY list.
///
/// What was happening: the cloud sends a description of the organisation's
/// scans, minted with IsAvailable = false because nothing has been fetched yet.
/// That clone replaced the project's snapshot wholesale, clearing the flag on
/// scans this device downloaded long ago. The download loop skips a document
/// only when the file exists AND the record says it is available - so with the
/// flag cleared the skip could never hold, and every sync fetched every scan
/// again, in full.
/// </summary>
public sealed class CloudHydrationKeepsLocalScansTests
{
    private const string OrganizationId = "org-1";

    [Fact]
    public void ASCANAlreadyOnThisDeviceSURVIVESCloudHydration()
    {
        ProjectWorkspace project = ProjectWithDownloadedScan();

        ProjectCompanyAssignmentService.MergeCloudAssignment(
            project,
            OrganizationId,
            "Байгууллага",
            CloudProfileWithPlaceholder());

        ProjectFileReference document = Assert.Single(
            project.Foundation.DesignCompany.OrganizationSnapshot.RegistrationCertificateDocuments);

        Assert.True(document.IsAvailable, "the downloaded scan was demoted to a placeholder");
        Assert.False(document.IsCloudPlaceholder);
        Assert.Equal("assets/cert.pdf", document.RelativePath);
    }

    [Fact]
    public void ASCANThisDeviceNEVERHadStaysAPlaceholder()
    {
        // 🔴 THE POSITIVE CONTROL, and the correctness half. Carrying the flag
        // across for a document this device never fetched would tell the album
        // to draw a file that is not there. Only a scan the project already had
        // marked available may keep the flag.
        // 🔴 THE PREVIOUS SNAPSHOT HOLDS THE SAME DOCUMENT, MARKED NOT
        // AVAILABLE. An earlier version of this test used an EMPTY previous
        // list, which returns before the availability filter is ever consulted
        // - so deleting that filter left the test green. A mutation caught it.
        // A positive control has to reach the line it is controlling.
        ProjectWorkspace project = ProjectWithDownloadedScan();
        project.Foundation.DesignCompany.OrganizationSnapshot
            .RegistrationCertificateDocuments[0].IsAvailable = false;

        ProjectCompanyAssignmentService.MergeCloudAssignment(
            project,
            OrganizationId,
            "Байгууллага",
            CloudProfileWithPlaceholder());

        ProjectFileReference document = Assert.Single(
            project.Foundation.DesignCompany.OrganizationSnapshot.RegistrationCertificateDocuments);

        Assert.False(document.IsAvailable, "a scan that was never fetched must not claim to be here");
    }

    [Fact]
    public void ADIFFERENTScanDoesNotInheritAnotherScansAvailability()
    {
        // Identity is the hash. A new certificate replacing an old one must be
        // fetched, not assumed present because its predecessor was.
        ProjectWorkspace project = ProjectWithDownloadedScan();

        CompanyProfile cloud = CloudProfileWithPlaceholder();
        cloud.RegistrationCertificateDocuments[0].Sha256 = "bbbb";
        cloud.RegistrationCertificateDocuments[0].ServerDocumentId = "doc-2";

        ProjectCompanyAssignmentService.MergeCloudAssignment(
            project, OrganizationId, "Байгууллага", cloud);

        ProjectFileReference document = Assert.Single(
            project.Foundation.DesignCompany.OrganizationSnapshot.RegistrationCertificateDocuments);

        Assert.False(document.IsAvailable, "a different scan must be fetched");
    }

    [Fact]
    public void THECloudStaysAuthoritativeForWhatItACTUALLYKnows()
    {
        // Only the local whereabouts are carried over. Everything the cloud
        // genuinely knows - title, page count - must still come from the cloud,
        // or this turns into a way of pinning stale metadata forever.
        ProjectWorkspace project = ProjectWithDownloadedScan();

        CompanyProfile cloud = CloudProfileWithPlaceholder();
        cloud.RegistrationCertificateDocuments[0].Title = "Шинэ гарчиг";
        cloud.RegistrationCertificateDocuments[0].PageCount = 9;

        ProjectCompanyAssignmentService.MergeCloudAssignment(
            project, OrganizationId, "Байгууллага", cloud);

        ProjectFileReference document = Assert.Single(
            project.Foundation.DesignCompany.OrganizationSnapshot.RegistrationCertificateDocuments);

        Assert.Equal("Шинэ гарчиг", document.Title);
        Assert.Equal(9, document.PageCount);
        Assert.True(document.IsAvailable);
    }

    private static ProjectWorkspace ProjectWithDownloadedScan()
    {
        var project = new ProjectWorkspace();
        project.Foundation.DesignCompany.OrganizationId = OrganizationId;
        project.Foundation.DesignCompany.OrganizationName = "Байгууллага";
        project.Foundation.DesignCompany.OrganizationSnapshot = new CompanyProfile
        {
            OrganizationId = OrganizationId,
            Name = "Байгууллага",
            RegistrationCertificateDocuments =
            [
                new ProjectFileReference
                {
                    Title = "Гэрчилгээ",
                    Category = ProjectDocumentCategories.CompanyRegistrationCertificate,
                    Sha256 = "aaaa",
                    ServerDocumentId = "doc-1",
                    RelativePath = "assets/cert.pdf",
                    PageCount = 1,
                    IsAvailable = true,
                },
            ],
        };
        return project;
    }

    private static CompanyProfile CloudProfileWithPlaceholder() => new()
    {
        OrganizationId = OrganizationId,
        Name = "Байгууллага",
        RegistrationCertificateDocuments =
        [
            new ProjectFileReference
            {
                Title = "Гэрчилгээ",
                Category = ProjectDocumentCategories.CompanyRegistrationCertificate,
                Sha256 = "aaaa",
                ServerDocumentId = "doc-1",
                PageCount = 1,
                IsCloudPlaceholder = true,
                IsAvailable = false,
            },
        ],
    };
}
