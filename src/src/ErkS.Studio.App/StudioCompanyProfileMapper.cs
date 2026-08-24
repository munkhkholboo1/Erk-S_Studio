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
            // The two used to be one person read twice. They are separate on
            // the server now, and reading the design representative first put
            // the chief architect's name into the director field the moment
            // anyone appointed one. Each field reads its own value.
            //
            // Director keeps a fallback: snapshots written before the split
            // hold the director in both slots, so an old profile still finds
            // the name. The design representative gets no fallback on purpose
            // - filling it from the director is the automatic appointment we
            // were asked to remove, and an unappointed architect must read as
            // unappointed.
            DesignRepresentativeTitle = Clean(cloud.DesignRepresentativeTitle),
            DesignRepresentativeName = Clean(cloud.DesignRepresentativeName),
            DirectorTitle = FirstValue(cloud.DirectorTitle, cloud.DesignRepresentativeTitle),
            DirectorName = FirstValue(cloud.DirectorName, cloud.DesignRepresentativeName),
            LogoScale = cloud.LogoScale,
            LogoOffsetX = cloud.LogoOffsetX,
            LogoOffsetY = cloud.LogoOffsetY,
            UpdatedAtUtc = cloud.UpdatedAtUtc,
        };
        AddDirectorSigner(profile);
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
            // The two used to be one person read twice. They are separate on
            // the server now, and reading the design representative first put
            // the chief architect's name into the director field the moment
            // anyone appointed one. Each field reads its own value.
            //
            // Director keeps a fallback: snapshots written before the split
            // hold the director in both slots, so an old profile still finds
            // the name. The design representative gets no fallback on purpose
            // - filling it from the director is the automatic appointment we
            // were asked to remove, and an unappointed architect must read as
            // unappointed.
            DesignRepresentativeTitle = Clean(cloud.DesignRepresentativeTitle),
            DesignRepresentativeName = Clean(cloud.DesignRepresentativeName),
            DirectorTitle = FirstValue(cloud.DirectorTitle, cloud.DesignRepresentativeTitle),
            DirectorName = FirstValue(cloud.DirectorName, cloud.DesignRepresentativeName),
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
        AddDirectorSigner(profile);
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
            // Both halves carry the director, and both must keep carrying it
            // until this client declares supportsSeparateRepresentatives.
            //
            // The server ignores the design representative half from a client
            // that has not declared the flag, so sending the director in both
            // leaves the wire behaving exactly as it did. What changed is
            // where the value is read from: profile.DesignRepresentative* now
            // holds the appointed chief architect, and sending that as the
            // director would file the architect as the director - or, when
            // nobody is appointed, blank the director outright.
            //
            // Declaring the flag is held back deliberately. Once declared, an
            // empty design representative clears the stored one, so a client
            // that had not read the server's current value first would erase
            // an appointment made on the website. SRV has been asked whether
            // baseConcurrencyToken already prevents that.
            DesignRepresentativeTitle = profile.DirectorTitle,
            DesignRepresentativeName = profile.DirectorName,
            DirectorTitle = profile.DirectorTitle,
            DirectorName = profile.DirectorName,
            LogoScale = profile.LogoScale,
            LogoOffsetX = profile.LogoOffsetX,
            LogoOffsetY = profile.LogoOffsetY,
        };
    }

    /// <summary>
    /// The company's signing officer, which is the director.
    ///
    /// This read the design representative until the server told the two
    /// apart. Once a chief architect can be appointed, taking the signer from
    /// that field would put the architect's name on the line the album labels
    /// "Захирал".
    /// </summary>
    private static void AddDirectorSigner(CompanyProfile profile)
    {
        if (!string.IsNullOrWhiteSpace(profile.DirectorName))
        {
            profile.Signers.Add(new CompanySigner
            {
                Role = profile.DirectorTitle,
                FullName = profile.DirectorName,
            });
        }
    }

    private static string FirstValue(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";

    private static string Clean(string? value) => (value ?? "").Trim();
}
