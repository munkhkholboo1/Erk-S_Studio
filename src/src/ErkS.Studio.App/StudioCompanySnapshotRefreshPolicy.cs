using ErkS.Platform.Core;
using System.IO;

namespace ErkS.Studio;

internal static class StudioCompanySnapshotRefreshPolicy
{
    public static bool ShouldMarkAlbumDirty(
        bool snapshotChanged,
        StudioCompanySnapshotRefreshOrigin origin,
        bool albumRenderChanged) =>
        snapshotChanged &&
        (origin == StudioCompanySnapshotRefreshOrigin.LocalAssetReconciliation ||
         albumRenderChanged);

    public static bool HasAlbumRenderChanges(
        CompanyProfile previous,
        CompanyProfile current,
        Func<string, string> assetContentIdentity,
        string? previousLogoContentIdentity = null)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(assetContentIdentity);

        return HasAlbumRenderChanges(
            CaptureAlbumRenderIdentity(
                previous,
                assetContentIdentity,
                previousLogoContentIdentity),
            current,
            assetContentIdentity);
    }

    public static bool HasAlbumRenderChanges(
        string previousAlbumRenderIdentity,
        CompanyProfile current,
        Func<string, string> assetContentIdentity)
    {
        ArgumentNullException.ThrowIfNull(previousAlbumRenderIdentity);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(assetContentIdentity);

        return !previousAlbumRenderIdentity.Equals(
            CaptureAlbumRenderIdentity(current, assetContentIdentity),
            StringComparison.Ordinal);
    }

    public static string CaptureAlbumRenderIdentity(
        CompanyProfile source,
        Func<string, string> assetContentIdentity,
        string? logoContentIdentityOverride = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(assetContentIdentity);

        CompanyProfile profile = source.Clone();
        profile.Normalize();
        string logoContentIdentity = logoContentIdentityOverride is null
            ? ResolveAssetContentIdentity(
                profile.LogoPath,
                assetContentIdentity)
            : Normalize(logoContentIdentityOverride).ToLowerInvariant();
        (string Role, string Name) representative =
            ResolveCompanyRepresentative(profile);
        object[] registrationDocuments = AvailableDocumentRenderIdentities(
            profile.RegistrationCertificateDocuments,
            assetContentIdentity);
        object[] licenseDocuments = AvailableDocumentRenderIdentities(
            profile.DesignLicenseDocuments,
            assetContentIdentity);
        bool registrationNumberIsVisible =
            registrationDocuments.Length == 0 ||
            licenseDocuments.Length == 0;

        return System.Text.Json.JsonSerializer.Serialize(new
        {
            // PdfSharp writes the legal name to document metadata even when a
            // separate display name is printed on the pages.
            MetadataAuthor = Normalize(profile.Name),
            DisplayName = ResolveCompanyDisplayName(profile),
            LegalDisplayName = ResolveCompanyLegalDisplayName(profile),
            LogoFallbackMark = string.IsNullOrWhiteSpace(logoContentIdentity)
                ? ResolveCompanyMark(profile)
                : "",
            RegistrationNumber = registrationNumberIsVisible
                ? Normalize(profile.RegistrationNumber)
                : "",
            RepresentativeRole = representative.Role,
            RepresentativeName = representative.Name,
            LogoContent = logoContentIdentity,
            LogoScale = string.IsNullOrWhiteSpace(logoContentIdentity)
                ? 0d
                : profile.LogoScale,
            LogoOffsetX = string.IsNullOrWhiteSpace(logoContentIdentity)
                ? 0d
                : profile.LogoOffsetX,
            LogoOffsetY = string.IsNullOrWhiteSpace(logoContentIdentity)
                ? 0d
                : profile.LogoOffsetY,
            RegistrationDocuments = registrationDocuments,
            LicenseDocuments = licenseDocuments,
        });
    }

    private static object[] AvailableDocumentRenderIdentities(
        IEnumerable<ProjectFileReference>? documents,
        Func<string, string> assetContentIdentity) =>
        (documents ?? [])
        .Where(document => document is not null && document.IsAvailable)
        .Select(document =>
            DocumentRenderIdentity(
                document,
                assetContentIdentity))
        .ToArray();

    private static object DocumentRenderIdentity(
        ProjectFileReference document,
        Func<string, string> assetContentIdentity)
    {
        int pageCount = Math.Max(1, document.PageCount);
        return new
        {
            ContentIdentity = ResolveDocumentContentIdentity(
                document,
                assetContentIdentity,
                pageCount),
            PageCount = pageCount,
            OriginalFileName = pageCount == 1
                ? SafeFileName(document.OriginalFileName)
                : "",
        };
    }

    private static string ResolveDocumentContentIdentity(
        ProjectFileReference document,
        Func<string, string> assetContentIdentity,
        int pageCount)
    {
        string identity = Normalize(document.Sha256).ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(identity))
            return identity;

        identity = Normalize(document.ServerFileRevisionId).ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(identity))
            return identity;

        identity = ResolveAssetContentIdentity(
            document.RelativePath,
            assetContentIdentity);
        if (!string.IsNullOrWhiteSpace(identity))
            return identity;

        identity = ResolveAssetContentIdentity(
            document.LinkedSourcePath,
            assetContentIdentity);
        return string.IsNullOrWhiteSpace(identity)
            ? $"{Normalize(document.ContentType)}|{document.SizeBytes}|{pageCount}"
            : identity;
    }

    private static (string Role, string Name) ResolveCompanyRepresentative(
        CompanyProfile profile)
    {
        // The director, not the appointed architect - this pairs with the
        // album's own resolver, which labels the line "Захирал".
        if (!string.IsNullOrWhiteSpace(profile.DirectorName))
        {
            return (
                string.IsNullOrWhiteSpace(profile.DirectorTitle)
                    ? "Захирал"
                    : Normalize(profile.DirectorTitle),
                Normalize(profile.DirectorName));
        }

        CompanySigner? signer = profile.Signers.FirstOrDefault(candidate =>
                                    candidate.Role.Contains(
                                        "захирал",
                                        StringComparison.OrdinalIgnoreCase))
                                ?? profile.Signers.FirstOrDefault();
        return signer is null
            ? ("", "")
            : (
                string.IsNullOrWhiteSpace(signer.Role)
                    ? "Захирал"
                    : Normalize(signer.Role),
                Normalize(signer.FullName));
    }

    private static string ResolveCompanyDisplayName(CompanyProfile profile) =>
        !string.IsNullOrWhiteSpace(profile.DisplayName)
            ? Normalize(profile.DisplayName)
            : Normalize(profile.Name);

    private static string ResolveCompanyLegalDisplayName(
        CompanyProfile profile)
    {
        string name = !string.IsNullOrWhiteSpace(profile.Name)
            ? Normalize(profile.Name)
            : ResolveCompanyDisplayName(profile);
        string legalForm = Normalize(profile.LegalForm);
        if (string.IsNullOrWhiteSpace(legalForm) ||
            name.Contains(legalForm, StringComparison.OrdinalIgnoreCase))
        {
            return name;
        }

        return string.IsNullOrWhiteSpace(name)
            ? legalForm
            : $"{name} {legalForm}";
    }

    private static string ResolveCompanyMark(CompanyProfile profile) =>
        string.IsNullOrWhiteSpace(profile.ShortName)
            ? ResolveCompanyDisplayName(profile)
            : Normalize(profile.ShortName);

    private static string SafeFileName(string? value)
    {
        try
        {
            return Normalize(Path.GetFileName(Normalize(value)));
        }
        catch (ArgumentException)
        {
            return Normalize(value);
        }
    }

    private static string ResolveAssetContentIdentity(
        string? path,
        Func<string, string> assetContentIdentity)
    {
        string value = Normalize(path);
        if (string.IsNullOrWhiteSpace(value))
            return "";
        try
        {
            return Normalize(assetContentIdentity(value))
                .ToLowerInvariant();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                InvalidDataException)
        {
            return "";
        }
    }

    private static string Normalize(string? value) =>
        value?.Trim() ?? "";
}

internal enum StudioCompanySnapshotRefreshOrigin
{
    PassiveCatalogHydration,
    LocalAssetReconciliation,
}
