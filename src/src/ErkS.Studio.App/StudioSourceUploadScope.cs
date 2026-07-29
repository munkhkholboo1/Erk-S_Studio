using ErkS.Platform.Core;

namespace ErkS.Studio;

/// <summary>
/// Final defense before a source package is uploaded or treated as available
/// for a full-album decision. Authority and device-local possession are both
/// required; neither can substitute for the other.
/// </summary>
internal static class StudioSourceUploadScope
{
    public static IReadOnlyList<ProjectSourceSyncCandidate> AuthorizedLocal(
        ProjectWorkspace project,
        IEnumerable<ProjectSourceSyncCandidate> candidates,
        string? currentAccountEmail,
        string? currentDeviceFingerprint,
        Func<ProjectDesignSource, bool>? hasVerifiedPayload = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        return (candidates ?? [])
            .Where(candidate => StudioRuntimeSourceScope.IsAuthorizedLocal(
                    project,
                    candidate.Source,
                    currentAccountEmail,
                    currentDeviceFingerprint,
                    hasVerifiedPayload))
            .ToList();
    }
}
