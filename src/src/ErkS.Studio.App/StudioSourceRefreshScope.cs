using ErkS.Platform.Core;

namespace ErkS.Studio;

/// <summary>
/// Selects the source streams that the signed-in participant may refresh.
/// Foreign mirrors remain read-only even when their native/inbox folders are
/// still present on this workstation.
/// </summary>
internal static class StudioSourceRefreshScope
{
    public static IReadOnlyList<ProjectDesignSource> OwnedSources(
        ProjectWorkspace project,
        string? currentUserEmail)
    {
        ArgumentNullException.ThrowIfNull(project);
        return (project.Sources ?? [])
            .Where(source =>
                ProjectCloudSyncAuthority
                    .ResolveSource(project, source, currentUserEmail)
                    .CanEdit)
            .ToList();
    }

    public static IReadOnlyList<ProjectDesignSource> OwnedSources(
        ProjectWorkspace project,
        string? currentUserEmail,
        string? currentDeviceFingerprint,
        Func<ProjectDesignSource, bool>? hasVerifiedPayload = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        return StudioRuntimeSourceScope.AuthorizedSources(
            project,
            currentUserEmail,
            currentDeviceFingerprint,
            hasVerifiedPayload);
    }

    public static bool CanRefresh(
        ProjectWorkspace project,
        ProjectDesignSource source,
        string? currentUserEmail) =>
        ProjectCloudSyncAuthority
            .ResolveSource(project, source, currentUserEmail)
            .CanEdit;
}
