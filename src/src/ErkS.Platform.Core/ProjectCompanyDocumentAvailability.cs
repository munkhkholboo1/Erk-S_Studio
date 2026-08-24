namespace ErkS.Platform.Core;

/// <summary>
/// Why a company's registration certificate and design licence are not in this
/// project, when they are not.
///
/// The album prints "Хуулбар оруулаагүй" on the page that would have held the
/// scan, which is true of the page and reads, in Studio, as an accusation: you
/// did not upload it. Often somebody did - on their own machine, into their own
/// company library - and the platform has no way to carry it here. A colleague
/// who has done the work being told they have not is worse than being told
/// nothing, because they go and do it twice.
///
/// Studio cannot know whether a copy exists elsewhere. It knows only whether
/// the company came from the cloud, and that is enough to tell the difference
/// between "add one" and "one may exist that cannot reach you".
/// </summary>
public static class ProjectCompanyDocumentAvailability
{
    /// <summary>
    /// What to say about the missing documents, or null when there is nothing
    /// to say - either both are present, or no company is assigned at all and
    /// the missing paperwork is not the problem worth naming.
    /// </summary>
    public static string? Describe(ProjectWorkspace project)
    {
        ArgumentNullException.ThrowIfNull(project);

        CompanyProfile company = project.Foundation.DesignCompany.OrganizationSnapshot;
        if (string.IsNullOrWhiteSpace(company.Name) &&
            string.IsNullOrWhiteSpace(project.Foundation.DesignCompany.OrganizationName))
        {
            return null;
        }

        bool certificate = company.RegistrationCertificateDocuments.Count > 0;
        bool licence = company.DesignLicenseDocuments.Count > 0;
        if (certificate && licence)
            return null;

        string missing = (certificate, licence) switch
        {
            (false, false) => "Гэрчилгээ, тусгай зөвшөөрлийн хуулбар",
            (false, true) => "Гэрчилгээний хуулбар",
            _ => "Тусгай зөвшөөрлийн хуулбар",
        };

        // A company that arrived from the cloud is owned by somebody else, and
        // their copies live in their own library. Nothing carries them across
        // yet, so asking this user to add one would be asking them to redo work
        // that is already done.
        return IsCloudOrganization(project)
            ? $"{missing} энэ төхөөрөмжид алга. Энэ байгууллага Cloud ERA-гаас " +
              "ирсэн тул хуулбарыг нь эзэн нь өөрийн Studio-д оруулсан байж болно — " +
              "баримтыг төслүүд хооронд зөөх зам одоогоор байхгүй. Альбомд " +
              "хуудас нь хоосон гарна."
            : $"{missing} алга. Компанийн сангаас энэ байгууллагыг нээж хуулбарыг " +
              "нь оруулбал альбомд автоматаар орно.";
    }

    /// <summary>
    /// Whether the assigned company is one the cloud owns rather than one this
    /// device holds. An organisation id is what the cloud gives it.
    /// </summary>
    private static bool IsCloudOrganization(ProjectWorkspace project) =>
        project.Cloud.Origin.Equals(ProjectOrigins.Cloud, StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(project.Cloud.ServerProjectId) &&
        !string.IsNullOrWhiteSpace(
            project.Foundation.DesignCompany.OrganizationSnapshot.OrganizationId);
}
