using ErkS.Platform.Core;

namespace ErkS.Studio;

internal enum CompanyEditorMode
{
    View,
    Edit,
    Create,
}

internal static class CompanyEditorWorkflowPolicy
{
    public static bool IsEmptyLegacyDraft(CompanyCatalogEntry entry)
    {
        if (!entry.SyncStatus.Equals(CompanySyncStatuses.PendingCreate, StringComparison.OrdinalIgnoreCase) ||
            !entry.Profile.OrganizationId.StartsWith("local-", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        CompanyProfile profile = entry.Profile;
        return new[]
            {
                profile.Name,
                profile.DisplayName,
                profile.ShortName,
                profile.RegistrationNumber,
                profile.LegalEntityType,
                profile.LegalForm,
                profile.OfficialRepresentativeName,
                profile.RegisteredCity,
                profile.Address,
                profile.Phone,
                profile.Email,
                profile.WebSite,
                profile.LicenseScope,
                profile.LicenseNumber,
                // The editor's representative field writes the director, so
                // that is the one that tells an empty profile from a filled
                // one.
                profile.DirectorName,
                profile.LogoPath,
            }
            .All(string.IsNullOrWhiteSpace) &&
            profile.ActivityDirections.Count == 0 &&
            profile.PhoneNumbers.Count == 0 &&
            profile.RegistrationCertificateDocuments.Count == 0 &&
            profile.DesignLicenseDocuments.Count == 0;
    }
}
