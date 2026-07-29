using ErkS.Platform.Core;

namespace ErkS.Studio;

internal sealed record StudioPdfPageEditCloudDecision(
    bool Allowed,
    string ReasonCode,
    string Message,
    string ComponentCode,
    StudioWorkspaceOperation BuildOperation);

internal enum StudioPdfPageEditAlbumRoute
{
    FullLocalBuild,
    CanonicalPatchOrDefer,
}

internal sealed record StudioPdfPageEditAlbumRouteDecision(
    StudioPdfPageEditAlbumRoute Route,
    int CloudOnlyComponentCount,
    string ReasonCode);

/// <summary>
/// Keeps a PDF page-format edit inside the exact account/device source stream.
/// The resulting source component can replace its canonical Cloud slot without
/// granting access to any collaborator component whose payload is not local.
/// </summary>
internal static class StudioPdfPageEditCloudPolicy
{
    public static StudioPdfPageEditCloudDecision Resolve(
        ProjectWorkspace project,
        ProjectDesignSource? source,
        string? currentAccountEmail,
        string? currentDeviceFingerprint,
        bool hasVerifiedPayload)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (source is null)
        {
            return Denied(
                "pdf_page_edit_source_missing",
                "PDF хуудасны локал эх үүсвэр төслөөс олдсонгүй.");
        }

        if (!StudioAuxiliarySourceLocalityPolicy.IsCloudLinked(project))
            return Allowed("");

        ProjectSourceEditAuthority authority =
            ProjectCloudSyncAuthority.ResolveSource(
                project,
                source,
                currentAccountEmail);
        if (!authority.CanEdit)
        {
            return Denied(
                "pdf_page_edit_source_authority_denied",
                authority.Message);
        }
        if (!StudioRuntimeSourceScope.IsAuthorizedLocal(
                project,
                source,
                currentAccountEmail,
                currentDeviceFingerprint,
                _ => hasVerifiedPayload))
        {
            return Denied(
                "pdf_page_edit_source_not_local",
                "PDF эх үүсвэр энэ бүртгэл/төхөөрөмжийн баталгаатай локал payload биш. Cloud component read-only хэвээр үлдлээ.");
        }
        if (string.IsNullOrWhiteSpace(authority.OwnerEmail) ||
            string.IsNullOrWhiteSpace(authority.SourceKey))
        {
            return Denied(
                "pdf_page_edit_source_identity_missing",
                "PDF эх үүсвэрийн immutable owner/source identity бүрэн биш тул Cloud component шинэчлэхийг зогсоолоо.");
        }

        return Allowed(
            StudioAlbumComponentIdentity.SourceCode(
                authority.OwnerEmail,
                authority.SourceKey));
    }

    public static StudioPdfPageEditAlbumRouteDecision ResolveAlbumRoute(
        ProjectWorkspace project,
        string? currentAccountEmail,
        string? currentDeviceFingerprint,
        Func<ProjectDesignSource, bool>? hasVerifiedPayload = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (!StudioAuxiliarySourceLocalityPolicy.IsCloudLinked(project))
            return FullLocalBuild();

        hasVerifiedPayload ??=
            StudioLocalSourceBindingPolicy.HasVerifiedPayload;
        IEnumerable<(string Owner, string SourceKey)> componentIdentities =
            (project.Cloud.SharedAlbumComponents ?? [])
            .Where(component =>
                component.ComponentKind.Equals(
                    StudioAlbumComponentIdentity.SourceComponentKind,
                    StringComparison.OrdinalIgnoreCase) &&
                !IsRetired(component.Status))
            .Select(component => (
                Owner: Normalize(component.OwnerEmail),
                SourceKey: Normalize(component.SourceKey)));
        IEnumerable<(string Owner, string SourceKey)> registeredIdentities =
            (project.Cloud.SharedSources ?? [])
            .Where(source =>
                source.SheetCount > 0 &&
                !IsRetired(source.Status))
            .Select(source => (
                Owner: StudioSharedSourceProjection.ImmutableOwner(source),
                SourceKey: Normalize(source.SourceKey)));
        List<(string Owner, string SourceKey)> sourceComponents =
            componentIdentities
            .Concat(registeredIdentities)
            .Where(identity =>
                identity.Owner.Length > 0 &&
                identity.SourceKey.Length > 0)
            .Distinct()
            .ToList();
        int cloudOnlyCount = sourceComponents.Count(identity =>
            !project.Sources.Any(source =>
                ProjectCloudSyncMetadata.CloudSourceKey(source).Equals(
                    identity.SourceKey,
                    StringComparison.OrdinalIgnoreCase) &&
                ProjectCloudSyncMetadata.CloudOwnerEmail(source).Equals(
                    identity.Owner,
                    StringComparison.OrdinalIgnoreCase) &&
                StudioRuntimeSourceScope.IsAuthorizedLocal(
                    project,
                    source,
                    currentAccountEmail,
                    currentDeviceFingerprint,
                    hasVerifiedPayload)));
        return cloudOnlyCount == 0
            ? FullLocalBuild()
            : new StudioPdfPageEditAlbumRouteDecision(
                StudioPdfPageEditAlbumRoute.CanonicalPatchOrDefer,
                cloudOnlyCount,
                "pdf_page_edit_cloud_components_require_canonical");
    }

    private static StudioPdfPageEditCloudDecision Allowed(
        string componentCode) =>
        new(
            true,
            "",
            "",
            componentCode,
            StudioWorkspaceOperation.LocalPdfPageEdit);

    private static StudioPdfPageEditCloudDecision Denied(
        string reasonCode,
        string message) =>
        new(
            false,
            reasonCode,
            message,
            "",
            StudioWorkspaceOperation.LocalPdfPageEdit);

    private static StudioPdfPageEditAlbumRouteDecision FullLocalBuild() =>
        new(
            StudioPdfPageEditAlbumRoute.FullLocalBuild,
            0,
            "");

    private static bool IsRetired(string? status)
    {
        string normalized = (status ?? "").Trim();
        return normalized.Equals("Retired", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("Removed", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("Deleted", StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string? value) =>
        (value ?? "").Trim().ToLowerInvariant();
}
