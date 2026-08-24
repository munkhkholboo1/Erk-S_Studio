using ErkS.Platform.Core;

namespace ErkS.Studio;

internal static class StudioCompanyProfileMapper
{
    public static CompanyProfile FromOrganization(StudioCloudOrganization cloud)
    {
        ArgumentNullException.ThrowIfNull(cloud);
        var profile = new CompanyProfile
        {
            OrganizationId = cloud.OrganizationId,
            Name = cloud.LegalName,
            DisplayName = cloud.DisplayName,
            ShortName = cloud.ShortName,
            RegistrationNumber = cloud.RegistrationNumber,
            LegalEntityType = cloud.LegalEntityType,
            LegalForm = cloud.LegalForm,
            ActivityDirections = [.. (cloud.ActivityDirections ?? [])],
            RegisteredAtUtc = cloud.RegisteredAtUtc,
            OfficialRepresentativeName = cloud.OfficialRepresentativeName,
            RegistrySource = cloud.RegistrySource,
            RegistrySourceUrl = cloud.RegistrySourceUrl,
            RegistryCheckedAtUtc = cloud.RegistryCheckedAtUtc,
            OrganizationType = cloud.OrganizationType,
            Status = cloud.Status,
            VerificationStatus = cloud.VerificationStatus,
            RegisteredCity = cloud.RegisteredCity,
            Address = cloud.Address,
            PhoneNumbers = [.. (cloud.PhoneNumbers ?? [])],
            Phone = cloud.PhoneNumbers?.FirstOrDefault() ?? "",
            Email = cloud.Email,
            WebSite = cloud.Website,
            LicenseScope = cloud.LicenseScope,
            LicenseNumber = cloud.LicenseNumber,
            DesignRepresentativeTitle = FirstValue(cloud.DesignRepresentativeTitle, cloud.DirectorTitle),
            DesignRepresentativeName = FirstValue(cloud.DesignRepresentativeName, cloud.DirectorName),
            DirectorTitle = FirstValue(cloud.DesignRepresentativeTitle, cloud.DirectorTitle),
            DirectorName = FirstValue(cloud.DesignRepresentativeName, cloud.DirectorName),
            LogoScale = cloud.LogoScale,
            LogoOffsetX = cloud.LogoOffsetX,
            LogoOffsetY = cloud.LogoOffsetY,
            UpdatedAtUtc = cloud.UpdatedAtUtc,
        };
        AddDesignRepresentativeSigner(profile);
        profile.Normalize();
        return profile;
    }

    public static CompanyProfile FromRenderProfile(StudioCloudOrganizationRenderProfile cloud)
    {
        ArgumentNullException.ThrowIfNull(cloud);
        var profile = new CompanyProfile
        {
            OrganizationId = cloud.OrganizationId,
            Name = cloud.LegalName,
            DisplayName = cloud.DisplayName,
            ShortName = cloud.ShortName,
            RegistrationNumber = cloud.RegistrationNumber,
            LegalEntityType = cloud.LegalEntityType,
            LegalForm = cloud.LegalForm,
            ActivityDirections = [.. (cloud.ActivityDirections ?? [])],
            RegisteredAtUtc = cloud.RegisteredAtUtc,
            OfficialRepresentativeName = cloud.OfficialRepresentativeName,
            RegistrySource = cloud.RegistrySource,
            RegistrySourceUrl = cloud.RegistrySourceUrl,
            RegistryCheckedAtUtc = cloud.RegistryCheckedAtUtc,
            OrganizationType = "DesignCompany",
            RegisteredCity = cloud.RegisteredCity,
            Address = cloud.Address,
            Phone = cloud.Phone,
            PhoneNumbers = string.IsNullOrWhiteSpace(cloud.Phone) ? [] : [cloud.Phone],
            Email = cloud.Email,
            WebSite = cloud.Website,
            LicenseScope = cloud.LicenseScope,
            LicenseNumber = cloud.LicenseNumber,
            DesignRepresentativeTitle = FirstValue(cloud.DesignRepresentativeTitle, cloud.DirectorTitle),
            DesignRepresentativeName = FirstValue(cloud.DesignRepresentativeName, cloud.DirectorName),
            DirectorTitle = FirstValue(cloud.DesignRepresentativeTitle, cloud.DirectorTitle),
            DirectorName = FirstValue(cloud.DesignRepresentativeName, cloud.DirectorName),
            LogoScale = cloud.LogoScale,
            LogoOffsetX = cloud.LogoOffsetX,
            LogoOffsetY = cloud.LogoOffsetY,
        };
        profile.RegistrationCertificateDocuments = ToDocuments(
            cloud.RegistrationCertificateDocuments,
            ProjectDocumentCategories.CompanyRegistrationCertificate);
        profile.DesignLicenseDocuments = ToDocuments(
            cloud.DesignLicenseDocuments,
            ProjectDocumentCategories.CompanyDesignLicense);
        AddDesignRepresentativeSigner(profile);
        profile.Normalize();
        return profile;
    }

    /// <summary>
    /// The organisation's scans as the project sees them.
    ///
    /// They arrive as cloud placeholders: the server holds the file, this
    /// device does not have it, and the content URL says where to get it.
    /// Recording them as present but unfetched is what lets the album stop
    /// claiming nobody uploaded anything - which was the complaint. The
    /// certificate had been uploaded, into an organisation, by somebody else.
    ///
    /// A page count of 0 means the server could not count the faces, not that
    /// there are none. It is carried through unchanged; the renderer already
    /// draws the one page it can be sure of.
    /// </summary>
    private static List<ProjectFileReference> ToDocuments(
        IEnumerable<StudioCloudOrganizationDocument>? documents,
        string category) =>
        [.. (documents ?? [])
            .Where(document => document is not null)
            .Where(document => !string.IsNullOrWhiteSpace(document.DocumentId))
            .Select(document => new ProjectFileReference
            {
                Category = category,
                Title = document.Title,
                OriginalFileName = document.OriginalFileName,
                ContentType = document.ContentType,
                SizeBytes = document.SizeBytes,
                PageCount = document.PageCount,
                Sha256 = document.Sha256,
                ServerDocumentId = document.DocumentId,
                CloudSyncStatus = ProjectDocumentCloudSyncStatuses.Synced,
                IsCloudPlaceholder = true,
                // Nothing has been fetched onto this device yet, so the album
                // must not try to draw a file that is not there.
                IsAvailable = false,
                AddedAtUtc = document.UpdatedAtUtc ?? DateTimeOffset.UtcNow,
            })];

    public static StudioCloudOrganizationUpsertRequest ToUpsertRequest(CompanyProfile profile)
        => ToUpsertRequest(profile, "");

    public static StudioCloudOrganizationUpsertRequest ToUpsertRequest(
        CompanyProfile profile,
        string? baseConcurrencyToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        profile.Normalize();
        return new StudioCloudOrganizationUpsertRequest
        {
            BaseConcurrencyToken =
                (baseConcurrencyToken ?? "").Trim(),
            RegistryFieldsIncluded = true,
            LegalName = profile.Name,
            DisplayName = profile.DisplayName,
            ShortName = profile.ShortName,
            RegistrationNumber = profile.RegistrationNumber,
            LegalEntityType = profile.LegalEntityType,
            LegalForm = profile.LegalForm,
            ActivityDirections = [.. profile.ActivityDirections],
            RegisteredAtUtc = profile.RegisteredAtUtc,
            OfficialRepresentativeName = profile.OfficialRepresentativeName,
            OrganizationType = string.IsNullOrWhiteSpace(profile.OrganizationType) ? "DesignCompany" : profile.OrganizationType,
            RegisteredCity = profile.RegisteredCity,
            Address = profile.Address,
            PhoneNumbers = [.. profile.PhoneNumbers],
            Email = profile.Email,
            Website = profile.WebSite,
            LicenseScope = profile.LicenseScope,
            LicenseNumber = profile.LicenseNumber,
            DesignRepresentativeTitle = profile.DesignRepresentativeTitle,
            DesignRepresentativeName = profile.DesignRepresentativeName,
            // Keep legacy aliases on the wire; the Studio editor exposes only one representative section.
            DirectorTitle = profile.DesignRepresentativeTitle,
            DirectorName = profile.DesignRepresentativeName,
            LogoScale = profile.LogoScale,
            LogoOffsetX = profile.LogoOffsetX,
            LogoOffsetY = profile.LogoOffsetY,
        };
    }

    private static void AddDesignRepresentativeSigner(CompanyProfile profile)
    {
        if (!string.IsNullOrWhiteSpace(profile.DesignRepresentativeName))
        {
            profile.Signers.Add(new CompanySigner
            {
                Role = profile.DesignRepresentativeTitle,
                FullName = profile.DesignRepresentativeName,
            });
        }
    }

    private static string FirstValue(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";
}
