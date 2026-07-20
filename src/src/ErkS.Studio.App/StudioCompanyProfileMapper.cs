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
        AddDesignRepresentativeSigner(profile);
        profile.Normalize();
        return profile;
    }

    public static StudioCloudOrganizationUpsertRequest ToUpsertRequest(CompanyProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        profile.Normalize();
        return new StudioCloudOrganizationUpsertRequest
        {
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
