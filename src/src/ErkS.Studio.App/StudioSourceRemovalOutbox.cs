using ErkS.Platform.Core;
using System.Net.Http;

namespace ErkS.Studio;

internal sealed record StudioSourceLocalRemovalCommit(
    ProjectLocalAlbumComponentClaim Claim,
    int RemovedAlbumPageCount);

internal enum StudioSourceRegistryResolutionStatus
{
    Exact,
    Missing,
    Ambiguous,
}

internal sealed record StudioSourceRegistryResolution(
    StudioSourceRegistryResolutionStatus Status,
    ProjectCloudSourceReference? Source)
{
    public bool IsExact =>
        Status == StudioSourceRegistryResolutionStatus.Exact &&
        Source is not null;
}

internal enum StudioSourceRemoteRetirementStatus
{
    DeferredOffline,
    DeferredFailure,
    Confirmed,
}

internal sealed record StudioSourceRemoteRetirementResult(
    StudioSourceRemoteRetirementStatus Status,
    Exception? Error = null)
{
    public bool Confirmed =>
        Status == StudioSourceRemoteRetirementStatus.Confirmed;
}

/// <summary>
/// Durable, account/device-scoped source retirement intent. The registry row
/// is represented by a tombstone before the local source is removed. The
/// local source and its album pages are removed immediately; Cloud retirement
/// is best-effort and an offline/failing request remains pending for Sync.
/// </summary>
internal static class StudioSourceRemovalOutbox
{
    public static StudioSourceRegistryResolution ResolveRegistrySource(
        ProjectWorkspace project,
        ProjectDesignSource source)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(source);

        string sourceKey =
            ProjectCloudSyncMetadata.CloudSourceKey(source).Trim();
        string immutableOwner =
            StudioLocalSourceBindingPolicy.ResolveLegacyImmutableOwner(
                project,
                source);
        if (string.IsNullOrWhiteSpace(sourceKey) ||
            string.IsNullOrWhiteSpace(immutableOwner))
        {
            return new StudioSourceRegistryResolution(
                StudioSourceRegistryResolutionStatus.Missing,
                null);
        }

        ProjectCloudSourceReference[] matches =
            (project.Cloud.SharedSources ?? [])
            .Where(candidate =>
                !string.Equals(
                    candidate.Status,
                    "Retired",
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    candidate.SourceKey,
                    sourceKey,
                    StringComparison.OrdinalIgnoreCase) &&
                StudioSharedSourceProjection.ImmutableOwner(candidate).Equals(
                    immutableOwner,
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matches.Length > 1)
        {
            return new StudioSourceRegistryResolution(
                StudioSourceRegistryResolutionStatus.Ambiguous,
                null);
        }
        if (matches.Length == 0 ||
            string.IsNullOrWhiteSpace(matches[0].SourceId))
        {
            return new StudioSourceRegistryResolution(
                StudioSourceRegistryResolutionStatus.Missing,
                null);
        }

        return new StudioSourceRegistryResolution(
            StudioSourceRegistryResolutionStatus.Exact,
            matches[0]);
    }

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
        StudioSourceRegistryResolution registryResolution =
            ResolveRegistrySource(project, source);
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
            !registryResolution.IsExact ||
            !registryResolution.Source!.SourceId.Equals(
                registrySource.SourceId,
                StringComparison.OrdinalIgnoreCase) ||
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

    public static StudioSourceLocalRemovalCommit StageAndRemoveLocal(
        ProjectWorkspace project,
        ProjectDesignSource source,
        ProjectCloudSourceReference registrySource,
        string? currentAccountEmail,
        string? currentDeviceFingerprint,
        bool hasVerifiedPayload,
        Action persistStagedClaim,
        Func<ProjectDesignSource, int> removeLocalSource,
        DateTimeOffset? requestedAtUtc = null)
    {
        ArgumentNullException.ThrowIfNull(persistStagedClaim);
        ArgumentNullException.ThrowIfNull(removeLocalSource);

        ProjectLocalAlbumComponentClaim claim = Stage(
            project,
            source,
            registrySource,
            currentAccountEmail,
            currentDeviceFingerprint,
            hasVerifiedPayload,
            requestedAtUtc);

        // Persist the tombstone before removing the local row. If the second
        // persistence step fails, Sync can still finish the staged retirement
        // without resurrecting the source on the server.
        persistStagedClaim();
        int removedPageCount = removeLocalSource(source);
        return new StudioSourceLocalRemovalCommit(
            claim,
            removedPageCount);
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

    public static bool IsRegistryMirrorStaged(
        ProjectWorkspace project,
        ProjectCloudSourceReference registrySource,
        string? currentAccountEmail,
        string? currentDeviceFingerprint)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(registrySource);
        if (string.IsNullOrWhiteSpace(registrySource.SourceId))
            return false;

        return Pending(
                project,
                currentAccountEmail,
                currentDeviceFingerprint)
            .Any(claim => claim.RegistrySourceId.Equals(
                registrySource.SourceId,
                StringComparison.OrdinalIgnoreCase));
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

    public static async Task<StudioSourceRemoteRetirementResult>
        TryConfirmRegistryRetirementAsync(
            ProjectWorkspace project,
            ProjectLocalAlbumComponentClaim claim,
            string? currentAccountEmail,
            string? currentDeviceFingerprint,
            bool canContactCloud,
            Func<string, Task> retireAsync,
            Action? validateContext = null)
    {
        if (!canContactCloud)
        {
            return new StudioSourceRemoteRetirementResult(
                StudioSourceRemoteRetirementStatus.DeferredOffline);
        }

        try
        {
            _ = await ConfirmRegistryRetirementAsync(
                project,
                claim,
                currentAccountEmail,
                currentDeviceFingerprint,
                retireAsync,
                validateContext);
            return new StudioSourceRemoteRetirementResult(
                StudioSourceRemoteRetirementStatus.Confirmed);
        }
        catch (Exception exception) when (
            exception is StudioAccountException or HttpRequestException or
                TaskCanceledException)
        {
            return new StudioSourceRemoteRetirementResult(
                StudioSourceRemoteRetirementStatus.DeferredFailure,
                exception);
        }
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
