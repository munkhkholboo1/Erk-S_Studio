using ErkS.Platform.Core;

namespace ErkS.Studio;

/// <summary>
/// Durable, account/device-scoped source retirement intent. The registry row
/// is retired first; only its acknowledgement allows the local mirror row and
/// canonical album component to be removed.
/// </summary>
internal static class StudioSourceRemovalOutbox
{
    public static ProjectLocalAlbumComponentClaim Stage(
        ProjectWorkspace project,
        ProjectDesignSource source,
        ProjectCloudSourceReference registrySource,
        string? currentAccountEmail,
        string? currentDeviceFingerprint,
        bool hasVerifiedPayload,
        DateTimeOffset? requestedAtUtc = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(registrySource);

        string account = Normalize(currentAccountEmail);
        string device = Normalize(currentDeviceFingerprint);
        string immutableOwner =
            StudioSharedSourceProjection.ImmutableOwner(registrySource);
        string sourceImmutableOwner =
            StudioLocalSourceBindingPolicy.ResolveLegacyImmutableOwner(
                project,
                source);
        ProjectSourceEditAuthority authority =
            ProjectCloudSyncAuthority.ResolveSource(
                project,
                source,
                account);
        string sourceKey = ProjectCloudSyncMetadata.CloudSourceKey(source).Trim();
        if (string.IsNullOrWhiteSpace(account) ||
            string.IsNullOrWhiteSpace(device) ||
            string.IsNullOrWhiteSpace(immutableOwner) ||
            !sourceImmutableOwner.Equals(
                immutableOwner,
                StringComparison.OrdinalIgnoreCase) ||
            !authority.CanEdit ||
            !authority.OwnerEmail.Equals(
                account,
                StringComparison.OrdinalIgnoreCase) ||
            !registrySource.SourceKey.Equals(
                sourceKey,
                StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(registrySource.SourceId) ||
            !StudioLocalSourceBindingPolicy.IsLocal(
                source,
                account,
                device,
                hasVerifiedPayload))
        {
            throw new InvalidOperationException(
                "Source retirement requires the exact owner, device, verified payload, and Cloud registry row.");
        }

        string componentCode =
            StudioAlbumComponentIdentity.SourceCode(
                immutableOwner,
                sourceKey);
        ProjectCloudSyncMetadata.MarkAlbumComponentPendingForBinding(
            project,
            componentCode,
            account,
            device,
            isRemoval: true,
            claimedAtUtc: requestedAtUtc,
            registrySourceId: registrySource.SourceId);
        return ProjectCloudSyncMetadata.PendingAlbumComponentClaim(
                project,
                componentCode,
                account,
                device)
            ?? throw new InvalidOperationException(
                "Source retirement claim could not be persisted.");
    }

    public static IReadOnlyList<ProjectLocalAlbumComponentClaim> Pending(
        ProjectWorkspace project,
        string? currentAccountEmail,
        string? currentDeviceFingerprint)
    {
        ArgumentNullException.ThrowIfNull(project);
        string account = Normalize(currentAccountEmail);
        string device = Normalize(currentDeviceFingerprint);
        if (string.IsNullOrWhiteSpace(account) ||
            string.IsNullOrWhiteSpace(device))
        {
            return [];
        }

        return (project.Cloud.PendingAlbumComponentClaims ?? [])
            .Where(claim =>
                claim.IsRemoval &&
                !string.IsNullOrWhiteSpace(claim.RegistrySourceId) &&
                claim.OwnerEmail.Equals(
                    account,
                    StringComparison.OrdinalIgnoreCase) &&
                claim.DeviceFingerprint.Equals(
                    device,
                    StringComparison.OrdinalIgnoreCase) &&
                IsOwnedSourceCode(claim.ComponentCode))
            .OrderBy(claim => claim.ClaimedAtUtc)
            .ThenBy(claim => claim.ComponentCode, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static bool IsStaged(
        ProjectWorkspace project,
        string componentCode,
        string? currentAccountEmail,
        string? currentDeviceFingerprint)
    {
        ArgumentNullException.ThrowIfNull(project);
        string account = Normalize(currentAccountEmail);
        string device = Normalize(currentDeviceFingerprint);
        ProjectLocalAlbumComponentClaim? claim =
            ProjectCloudSyncMetadata.PendingAlbumComponentClaim(
                project,
                componentCode,
                account,
                device);
        return claim is
        {
            IsRemoval: true,
            RegistrySourceId.Length: > 0
        } &&
            IsOwnedSourceCode(claim.ComponentCode);
    }

    public static bool IsSourceStaged(
        ProjectWorkspace project,
        ProjectDesignSource source,
        string? currentAccountEmail,
        string? currentDeviceFingerprint)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(source);
        string owner = StudioLocalSourceBindingPolicy.ResolveLegacyImmutableOwner(
            project,
            source);
        if (string.IsNullOrWhiteSpace(owner))
            return false;
        return IsStaged(
            project,
            StudioAlbumComponentIdentity.SourceCode(
                owner,
                ProjectCloudSyncMetadata.CloudSourceKey(source)),
            currentAccountEmail,
            currentDeviceFingerprint);
    }

    public static ProjectDesignSource? ResolveLocalSource(
        ProjectWorkspace project,
        ProjectLocalAlbumComponentClaim claim)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(claim);
        string baseCode =
            StudioAlbumComponentIdentity.BaseSourceCode(claim.ComponentCode);
        return project.Sources.FirstOrDefault(source =>
        {
            string owner =
                StudioLocalSourceBindingPolicy.ResolveLegacyImmutableOwner(
                    project,
                    source);
            return !string.IsNullOrWhiteSpace(owner) &&
                StudioAlbumComponentIdentity.SourceCode(
                    owner,
                    ProjectCloudSyncMetadata.CloudSourceKey(source))
                .Equals(baseCode, StringComparison.OrdinalIgnoreCase);
        });
    }

    public static void ApplyRegistryRetirement(
        ProjectWorkspace project,
        ProjectLocalAlbumComponentClaim claim)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(claim);
        project.Cloud.SharedSources.RemoveAll(source =>
            source is not null &&
            source.SourceId.Equals(
                claim.RegistrySourceId,
                StringComparison.OrdinalIgnoreCase));
    }

    public static async Task<ProjectDesignSource?> ConfirmRegistryRetirementAsync(
        ProjectWorkspace project,
        ProjectLocalAlbumComponentClaim claim,
        string? currentAccountEmail,
        string? currentDeviceFingerprint,
        Func<string, Task> retireAsync,
        Action? validateContext = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(retireAsync);

        bool stillPending = Pending(
                project,
                currentAccountEmail,
                currentDeviceFingerprint)
            .Any(candidate =>
                candidate.ComponentCode.Equals(
                    claim.ComponentCode,
                    StringComparison.OrdinalIgnoreCase) &&
                candidate.ClaimToken.Equals(
                    claim.ClaimToken,
                    StringComparison.OrdinalIgnoreCase) &&
                candidate.RegistrySourceId.Equals(
                    claim.RegistrySourceId,
                    StringComparison.OrdinalIgnoreCase));
        if (!stillPending)
        {
            throw new InvalidOperationException(
                "Source retirement claim changed before it could be confirmed.");
        }

        ProjectDesignSource? local = ResolveLocalSource(project, claim);
        await retireAsync(claim.RegistrySourceId);
        validateContext?.Invoke();
        ApplyRegistryRetirement(project, claim);
        return local;
    }

    private static bool IsOwnedSourceCode(string? componentCode)
    {
        string code =
            StudioAlbumComponentIdentity.BaseSourceCode(componentCode ?? "");
        if (!StudioAlbumComponentIdentity.IsOwnedSourceCode(code))
            return false;
        string[] parts = code.Split(':', 3);
        return parts.Length == 3 &&
            !string.IsNullOrWhiteSpace(parts[1]) &&
            !string.IsNullOrWhiteSpace(parts[2]);
    }

    private static string Normalize(string? value) =>
        (value ?? "").Trim().ToLowerInvariant();
}
