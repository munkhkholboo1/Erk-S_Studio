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
            // The server is the only place that knows whether an architect was
            // appointed, so a value that came from it - including an empty one,
            // which means nobody - is knowledge. Anything still sitting on this
            // device from before the split is not.
            //
            // One limitation, stated rather than hidden: a server old enough to
            // still mirror the two fields would hand back the director here and
            // this would call it knowledge. Production separates them and has a
            // test forbidding the old shape, so the case is theoretical today; a
            // server-declared marker would close it properly.
            DesignRepresentativeKnown = true,
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
            // The server is the only place that knows whether an architect was
            // appointed, so a value that came from it - including an empty one,
            // which means nobody - is knowledge. Anything still sitting on this
            // device from before the split is not.
            //
            // One limitation, stated rather than hidden: a server old enough to
            // still mirror the two fields would hand back the director here and
            // this would call it knowledge. Production separates them and has a
            // test forbidding the old shape, so the case is theoretical today; a
            // server-declared marker would close it properly.
            DesignRepresentativeKnown = true,
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
            // The architect is sent only when this device actually knows who
            // it is. Otherwise the profile still holds the pre-split residue -
            // the director's name in the architect's field - and sending it
            // under the flag would appoint every director as their own
            // company's chief architect, across every colleague on the
            // project. That is the automatic appointment we were told to
            // remove, spread through the cloud instead of one machine.
            //
            // With the flag off the server ignores the architect half and
            // edits only the director, so an unknowing client stays harmless
            // without needing to guess. Nothing here inspects whether the two
            // names match: one person can hold both roles, and treating that
            // as residue would quietly delete a real appointment.
            SupportsSeparateRepresentatives = profile.DesignRepresentativeKnown,
            DesignRepresentativeTitle = profile.DesignRepresentativeKnown
                ? profile.DesignRepresentativeTitle
                : profile.DirectorTitle,
            DesignRepresentativeName = profile.DesignRepresentativeKnown
                ? profile.DesignRepresentativeName
                : profile.DirectorName,
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
