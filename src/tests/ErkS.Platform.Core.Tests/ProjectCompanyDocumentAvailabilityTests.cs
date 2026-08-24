namespace ErkS.Platform.Core.Tests;

/// <summary>
/// Telling somebody they did not upload a document they did upload.
///
/// The album prints "Хуулбар оруулаагүй" where the scan would have gone. On
/// the page that is true. In Studio it reads as an accusation, and for a
/// company that came from the cloud it is usually wrong: the owner uploaded
/// the certificate into their own library, on their own machine, and nothing
/// carries it here. A colleague who has done the work being told they have not
/// goes and does it twice.
/// </summary>
public sealed class ProjectCompanyDocumentAvailabilityTests
{
    [Fact]
    public void ACloudCompanyMissingItsPapersIsNotBlamedOnThisUser()
    {
        string notice = Require(CloudProject());

        Assert.Contains("Cloud ERA-гаас", notice);
        Assert.Contains("эзэн нь", notice);
        Assert.DoesNotContain("оруулбал", notice);
    }

    [Fact]
    public void ALocalCompanyMissingItsPapersGetsTheActionThatWouldFixIt()
    {
        // Here the user really can do something, and saying so is the point.
        ProjectWorkspace project = CloudProject();
        project.Cloud.Origin = ProjectOrigins.Local;
        project.Foundation.DesignCompany.OrganizationSnapshot.OrganizationId = "";

        string notice = Require(project);

        Assert.Contains("Компанийн сангаас", notice);
        Assert.DoesNotContain("Cloud ERA-гаас", notice);
    }

    [Fact]
    public void ADocumentTheServerHoldsButThisDeviceLacksIsItsOwnState()
    {
        // The moment organisations start carrying their own papers, this is
        // what a colleague sees first. Calling it present leaves the album
        // page blank with nothing said; calling it absent tells them to upload
        // a file that is already uploaded.
        ProjectWorkspace project = CloudProject();
        project.Foundation.DesignCompany.OrganizationSnapshot.RegistrationCertificateDocuments.Add(
            new ProjectFileReference { IsAvailable = false, ServerDocumentId = "d1" });

        string notice = Require(project);

        Assert.Contains("серверт байгаа", notice);
        Assert.Contains("татагдаагүй", notice);
        Assert.DoesNotContain("Компанийн сангаас", notice);
    }

    [Fact]
    public void NothingIsSaidWhenBothDocumentsAreThere()
    {
        ProjectWorkspace project = CloudProject();
        CompanyProfile company = project.Foundation.DesignCompany.OrganizationSnapshot;
        company.RegistrationCertificateDocuments.Add(new ProjectFileReference { IsAvailable = true });
        company.DesignLicenseDocuments.Add(new ProjectFileReference { IsAvailable = true });

        Assert.Null(ProjectCompanyDocumentAvailability.Describe(project));
    }

    [Fact]
    public void NothingIsSaidWhenNoCompanyIsAssignedAtAll()
    {
        // Then the missing paperwork is not the thing worth naming, and a
        // notice about it would point at a problem the user cannot act on.
        var project = new ProjectWorkspace();

        Assert.Null(ProjectCompanyDocumentAvailability.Describe(project));
    }

    [Theory]
    [InlineData(true, false, "Тусгай зөвшөөрлийн хуулбар")]
    [InlineData(false, true, "Гэрчилгээний хуулбар")]
    [InlineData(false, false, "Гэрчилгээ, тусгай зөвшөөрлийн хуулбар")]
    public void OnlyTheMissingOneIsNamed(bool certificate, bool licence, string expected)
    {
        // Naming both when one is present sends the user looking for something
        // that is already there.
        ProjectWorkspace project = CloudProject();
        CompanyProfile company = project.Foundation.DesignCompany.OrganizationSnapshot;
        if (certificate)
            company.RegistrationCertificateDocuments.Add(new ProjectFileReference { IsAvailable = true });
        if (licence)
            company.DesignLicenseDocuments.Add(new ProjectFileReference { IsAvailable = true });

        Assert.StartsWith(expected, Require(project), StringComparison.Ordinal);
    }

    [Fact]
    public void ALocalCompanyInsideACloudProjectIsStillTheUsersToFill()
    {
        // Cloud project, but a company this device owns: no organisation id,
        // so nobody else holds the papers.
        ProjectWorkspace project = CloudProject();
        project.Foundation.DesignCompany.OrganizationSnapshot.OrganizationId = "";

        Assert.Contains("Компанийн сангаас", Require(project));
    }

    private static ProjectWorkspace CloudProject()
    {
        var project = new ProjectWorkspace();
        project.Cloud.Origin = ProjectOrigins.Cloud;
        project.Cloud.ServerProjectId = "c59b2a4ce1cd4657b025a826223c6a5a";
        project.Foundation.DesignCompany.OrganizationName = "Монгол Архитектур Дизайн";
        project.Foundation.DesignCompany.OrganizationSnapshot.Name = "Монгол Архитектур Дизайн";
        project.Foundation.DesignCompany.OrganizationSnapshot.OrganizationId = "org-1";
        return project;
    }

    private static string Require(ProjectWorkspace project)
    {
        string? notice = ProjectCompanyDocumentAvailability.Describe(project);
        Assert.NotNull(notice);
        return notice!;
    }
}
