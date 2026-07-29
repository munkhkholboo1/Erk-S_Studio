using ErkS.Platform.Contracts;
using ErkS.Platform.Core;
using System.IO;

namespace ErkS.Studio;

/// <summary>
/// Single admission boundary for device-local source runtime work. A source
/// may be watched, scanned, reconciled, or uploaded only when both Cloud
/// authority and the exact account/device payload binding are valid.
/// </summary>
internal static class StudioRuntimeSourceScope
{
    public static IReadOnlyList<ProjectDesignSource> AuthorizedSources(
        ProjectWorkspace project,
        string? currentAccountEmail,
        string? currentDeviceFingerprint,
        Func<ProjectDesignSource, bool>? hasVerifiedPayload = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        hasVerifiedPayload ??=
            StudioLocalSourceBindingPolicy.HasVerifiedPayload;
        return (project.Sources ?? [])
            .Where(source => IsAuthorizedLocal(
                project,
                source,
                currentAccountEmail,
                currentDeviceFingerprint,
                hasVerifiedPayload))
            .ToList();
    }

    public static bool IsAuthorizedLocal(
        ProjectWorkspace project,
        ProjectDesignSource source,
        string? currentAccountEmail,
        string? currentDeviceFingerprint,
        Func<ProjectDesignSource, bool>? hasVerifiedPayload = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(source);
        hasVerifiedPayload ??=
            StudioLocalSourceBindingPolicy.HasVerifiedPayload;
        return ProjectCloudSyncAuthority.ResolveSource(
                   project,
                   source,
                   currentAccountEmail).CanEdit &&
               StudioLocalSourceBindingPolicy.IsLocal(
                   source,
                   currentAccountEmail,
                   currentDeviceFingerprint,
                   hasVerifiedPayload(source));
    }

    public static ProjectDesignSource? ResolvePackageSource(
        ProjectWorkspace project,
        SheetPackageLoadResult result,
        string? currentAccountEmail,
        string? currentDeviceFingerprint,
        Func<ProjectDesignSource, bool>? hasVerifiedPayload = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(result);
        SheetPackageManifest? manifest = result.Manifest;
        if (!result.IsLossless || manifest is null)
            return null;

        if (!string.IsNullOrWhiteSpace(manifest.ProjectId) &&
            !manifest.ProjectId.Trim().Equals(
                project.ProjectId.Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string sourceId = manifest.Source?.SourceId?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(sourceId))
            return null;
        ProjectDesignSource? source = (project.Sources ?? []).FirstOrDefault(
            candidate => candidate.Id.Equals(
                sourceId,
                StringComparison.OrdinalIgnoreCase));
        if (source is null ||
            !IsAuthorizedLocal(
                project,
                source,
                currentAccountEmail,
                currentDeviceFingerprint,
                hasVerifiedPayload))
        {
            return null;
        }

        return PackageBelongsToSourceInbox(source, result.ManifestPath)
            ? source
            : null;
    }

    private static bool PackageBelongsToSourceInbox(
        ProjectDesignSource source,
        string? manifestPath)
    {
        if (string.IsNullOrWhiteSpace(manifestPath))
            return false;
        try
        {
            string fullManifestPath = Path.GetFullPath(manifestPath);
            IEnumerable<string> inboxes = [source.InboxFolder];
            if (source.Metadata is not null &&
                source.Metadata.TryGetValue(
                    "LegacyInboxFolder",
                    out string? legacyInbox))
            {
                inboxes = inboxes.Append(legacyInbox);
            }

            return inboxes
                .Where(folder => !string.IsNullOrWhiteSpace(folder))
                .Any(folder => ProjectWorkspacePaths.IsInside(
                    folder,
                    fullManifestPath));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                ArgumentException or NotSupportedException)
        {
            return false;
        }
    }
}
