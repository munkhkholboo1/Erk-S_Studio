using System.IO;
using ErkS.Platform.Core;

namespace ErkS.Studio;

internal enum StudioWorkspaceOperation
{
    ExplicitAlbumEdit,
    LocalPdfPageEdit,
    SourceRefresh,
    CloudSync,
}

internal enum StudioCloudSyncPayload
{
    SourcePackage,
    AlbumComponent,
    BuildingComposition,
    ProjectInformation,
    OrganizationAssignment,
}

/// <summary>
/// Keeps source reconciliation and Cloud publication from observing different
/// halves of the same package callback cycle.
/// </summary>
internal static class StudioRefreshSyncOperationPolicy
{
    public static bool CanStartSourceRefresh(
        bool hasOpenProject,
        bool canEditProjectContent,
        bool projectAccessRefreshInProgress,
        bool sourceRefreshInProgress,
        bool cloudSyncInProgress) =>
        hasOpenProject &&
        canEditProjectContent &&
        !projectAccessRefreshInProgress &&
        !sourceRefreshInProgress &&
        !cloudSyncInProgress;

    public static bool CanContinueSourceRefresh(
        StudioOperationContext capturedContext,
        bool projectAccessRefreshInProgress,
        bool hasOpenProject,
        ProjectWorkspace? currentProject,
        string? currentProjectPath,
        StudioAccountSession? currentAccount,
        long workspaceEpoch,
        long accountEpoch)
    {
        ArgumentNullException.ThrowIfNull(capturedContext);
        return !projectAccessRefreshInProgress &&
            capturedContext.Matches(
                hasOpenProject,
                currentProject,
                currentProjectPath,
                currentAccount,
                workspaceEpoch,
                accountEpoch);
    }

    public static bool CanStartCloudSync(
        bool hasOpenProject,
        bool projectAccessRefreshInProgress,
        bool sourceRefreshInProgress,
        bool cloudSyncInProgress) =>
        hasOpenProject &&
        !projectAccessRefreshInProgress &&
        !sourceRefreshInProgress &&
        !cloudSyncInProgress;

    public static bool IsBusy(
        bool projectAccessRefreshInProgress,
        bool sourceRefreshInProgress,
        bool cloudSyncInProgress) =>
        projectAccessRefreshInProgress ||
        sourceRefreshInProgress ||
        cloudSyncInProgress;

    public static bool ShouldCollectProjectUi(StudioWorkspaceOperation operation) =>
        operation == StudioWorkspaceOperation.ExplicitAlbumEdit;

    public static bool ShouldReconcileLinkedProjectAssets(
        StudioWorkspaceOperation operation) =>
        operation == StudioWorkspaceOperation.ExplicitAlbumEdit;

    public static bool ShouldAttemptCloudUnionPreview(
        StudioWorkspaceOperation operation) =>
        operation != StudioWorkspaceOperation.SourceRefresh;

    public static bool ShouldFallbackToLocalAlbumBuild(
        StudioWorkspaceOperation operation,
        Exception exception)
    {
        if (operation is not (StudioWorkspaceOperation.SourceRefresh or
            StudioWorkspaceOperation.ExplicitAlbumEdit or
            StudioWorkspaceOperation.LocalPdfPageEdit))
        {
            return false;
        }

        // This policy is evaluated only around construction of the optional
        // Cloud-union preview. A rejected/stale canonical composition must not
        // prevent the same operation from building the verified local album.
        if (exception is AlbumBuildException)
            return true;

        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is NullReferenceException or InvalidDataException or IOException)
                return true;
        }

        return false;
    }

    public static bool CanUploadPersistedPayload(StudioCloudSyncPayload payload) =>
        payload is
            StudioCloudSyncPayload.SourcePackage or
            StudioCloudSyncPayload.AlbumComponent or
            StudioCloudSyncPayload.BuildingComposition;
}
