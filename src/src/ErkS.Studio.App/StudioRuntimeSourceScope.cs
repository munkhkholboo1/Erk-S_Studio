using ErkS.Platform.Contracts;
using ErkS.Platform.Core;
using System.IO;

namespace ErkS.Studio;

/// <summary>
/// Single admission boundary for device-local source runtime work. A source
/// may be watched, scanned, reconciled, or uploaded only when both Cloud
/// authority and the exact account/device payload binding are valid.
/// </summary>
/// <summary>
/// Whether a package may be taken into the project, and the reason when it may
/// not.
/// </summary>
internal sealed record PackageAdmission(ProjectDesignSource? Source, string Refusal)
{
    public static PackageAdmission Admitted(ProjectDesignSource source) => new(source, "");

    public static PackageAdmission Refused(string reason) => new(null, reason);

    public bool IsAdmitted => Source is not null;
}

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
        Func<ProjectDesignSource, bool>? hasVerifiedPayload = null) =>
        Admit(
            project,
            result,
            currentAccountEmail,
            currentDeviceFingerprint,
            hasVerifiedPayload).Source;

    /// <summary>
    /// Decides whether a verified package may be taken into this project, and
    /// says why not when it may not. A refused package is not a bad package, so
    /// nothing quarantines it - which means this reason is the only account the
    /// user will ever get, and losing it makes the delivery look as though it
    /// never arrived.
    /// </summary>
    public static PackageAdmission Admit(
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
            return PackageAdmission.Refused("Багц шалгалт давсангүй.");

        if (!string.IsNullOrWhiteSpace(manifest.ProjectId) &&
            !manifest.ProjectId.Trim().Equals(
                project.ProjectId.Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            return PackageAdmission.Refused("Багц өөр төслийнх байна.");
        }

        string sourceId = manifest.Source?.SourceId?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(sourceId))
            return PackageAdmission.Refused("Багцад эх үүсвэрийн дугаар алга байна.");
        ProjectDesignSource? source = (project.Sources ?? []).FirstOrDefault(
            candidate => candidate.Id.Equals(
                sourceId,
                StringComparison.OrdinalIgnoreCase));
        if (source is null)
        {
            return PackageAdmission.Refused(
                "Багцын эх үүсвэр энэ төсөлд бүртгэлгүй байна.");
        }
        if (!IsAuthorizedLocal(
                project,
                source,
                currentAccountEmail,
                currentDeviceFingerprint,
                hasVerifiedPayload))
        {
            return PackageAdmission.Refused(
                $"«{source.Name}» эх үүсвэр энэ бүртгэл эсвэл энэ төхөөрөмжид холбогдоогүй байна.");
        }

        return PackageBelongsToSourceInbox(source, result.ManifestPath)
            ? PackageAdmission.Admitted(source)
            : PackageAdmission.Refused(
                $"Багц «{source.Name}» эх үүсвэрийн inbox-оос ирээгүй байна.");
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
