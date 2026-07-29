using ErkS.Platform.Core;

namespace ErkS.Studio;

internal sealed record StudioCloudUnionPendingScope(
    IReadOnlyList<ProjectSourceSyncCandidate> Sources,
    IReadOnlyList<string> ComponentCodes);

/// <summary>
/// Limits a local Cloud-union preview to changes this exact signed-in
/// account/device may upload. A copied project mirror can retain pending flags,
/// but those flags never make its Cloud-only source payload locally editable.
/// </summary>
internal static class StudioCloudUnionPreviewScope
{
    public static StudioCloudUnionPendingScope Resolve(
        ProjectWorkspace project,
        string? currentAccountEmail,
        string? currentDeviceFingerprint,
        Func<ProjectDesignSource, bool>? hasVerifiedPayload = null,
        Func<ProjectFileReference, bool>? hasVerifiedDocumentPayload = null,
        Func<ProjectVisualizationImage, bool>? hasVerifiedVisualizationPayload = null)
    {
        ArgumentNullException.ThrowIfNull(project);

        IReadOnlyList<ProjectSourceSyncCandidate> pendingSources =
            StudioSourceUploadScope.AuthorizedLocal(
                project,
                ProjectCloudSyncMetadata.PendingSourcePackages(project),
                currentAccountEmail,
                currentDeviceFingerprint,
                hasVerifiedPayload);
        CloudSyncPreviewPlan authority = CloudSyncPreviewPlanner.Build(
            project,
            currentAccountEmail,
            "local-union-preview",
            new StudioCloudProjectRefreshResult(false, null),
            currentDeviceFingerprint,
            hasVerifiedPayload,
            hasVerifiedDocumentPayload,
            hasVerifiedVisualizationPayload);
        IReadOnlyList<string> componentCodes =
            ProjectCloudSyncMetadata.PendingAlbumComponents(project)
                .Where(authority.IsComponentAuthorized)
                .Where(code =>
                    StudioAuxiliarySourceLocalityPolicy.IsAlbumComponentAuthorized(
                        project,
                        code,
                        currentAccountEmail,
                        currentDeviceFingerprint,
                        hasVerifiedDocumentPayload ?? (static _ => false),
                        hasVerifiedVisualizationPayload ?? (static _ => false)))
                .ToList();

        return new StudioCloudUnionPendingScope(
            pendingSources,
            componentCodes);
    }
}
