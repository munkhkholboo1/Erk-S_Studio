using ErkS.Platform.Core;

namespace ErkS.Studio;

/// <summary>
/// Identifies canonical source components whose payload is not controlled by
/// this exact account/device. A local-only album must never replace those
/// components; they can only be retained by patching the canonical Cloud PDF.
/// </summary>
internal static class StudioCloudAlbumLocalityPolicy
{
    public static int CloudOnlySourceComponentCount(
        ProjectWorkspace project,
        string? currentAccountEmail,
        string? currentDeviceFingerprint,
        Func<ProjectDesignSource, bool>? hasVerifiedPayload = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (!StudioAuxiliarySourceLocalityPolicy.IsCloudLinked(project))
            return 0;

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

        return sourceComponents.Count(identity =>
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
    }

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
