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

        int cloudOnlyCount =
            StudioCloudAlbumLocalityPolicy.CloudOnlySourceComponentCount(
                project,
                currentAccountEmail,
                currentDeviceFingerprint,
                hasVerifiedPayload);
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

}
