using ErkS.Platform.Core;
using System.Net.Http;

namespace ErkS.Studio;

internal sealed record StudioSourceLocalRemovalCommit(
    ProjectLocalAlbumComponentClaim Claim,
    int RemovedAlbumPageCount);

/// <summary>A removal whose registry retirement the server refused this time.</summary>
internal sealed record StudioSourceRetirementFailure(
    ProjectLocalAlbumComponentClaim Claim,
    string Reason);

/// <summary>What one pass over the pending removals did.</summary>
internal sealed record StudioSourceRetirementRun(
    IReadOnlyList<ProjectDesignSource> RetiredLocalSources,
    IReadOnlyList<StudioSourceRetirementFailure> Failures);

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
                IsThisDevicesClaim(claim.DeviceFingerprint, device) &&
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
            ClaimOfThisDevice(project, componentCode, account, device);
        return claim is
        {
            IsRemoval: true,
            RegistrySourceId.Length: > 0
        } &&
            IsOwnedSourceCode(claim.ComponentCode);
    }

    /// <summary>
    /// The pending claim for a component made by this account on THIS
    /// machine, under any name the machine has signed with.
    /// </summary>
    /// <remarks>
    /// 🔴 Erin's two removals of 2026-08-20 carry this machine's older-salt
    /// form; after a device key was adopted the machine answered with the key
    /// form, an exact match found nothing, and both removals stood blocked on
    /// every sync. The exact claim still wins when there is one.
    /// </remarks>
    public static ProjectLocalAlbumComponentClaim? ClaimOfThisDevice(
        ProjectWorkspace project,
        string componentCode,
        string? currentAccountEmail,
        string? currentDeviceFingerprint)
    {
        ArgumentNullException.ThrowIfNull(project);
        string code = (componentCode ?? "").Trim();
        string account = Normalize(currentAccountEmail);
        string device = Normalize(currentDeviceFingerprint);
        if (code.Length == 0 || account.Length == 0 || device.Length == 0)
            return null;

        return ProjectCloudSyncMetadata.PendingAlbumComponentClaim(project, code, account, device) ??
            (project.Cloud.PendingAlbumComponentClaims ?? []).FirstOrDefault(claim =>
                claim.ComponentCode.Equals(code, StringComparison.OrdinalIgnoreCase) &&
                claim.OwnerEmail.Equals(account, StringComparison.OrdinalIgnoreCase) &&
                IsThisDevicesClaim(claim.DeviceFingerprint, device));
    }

    /// <summary>
    /// Whether a claim's device is the caller's. Exact equality, or - when the
    /// caller is this machine - any form this machine can present. Widening
    /// acceptance, never ownership: a caller that is not this machine gains
    /// nothing, and another machine's claim is never this machine's.
    /// </summary>
    private static bool IsThisDevicesClaim(string? claimDevice, string currentDevice)
    {
        string claimed = Normalize(claimDevice);
        if (claimed.Length == 0 || currentDevice.Length == 0)
            return false;
        if (claimed.Equals(currentDevice, StringComparison.Ordinal))
            return true;

        IReadOnlyList<string> forms = StudioDeviceIdentity.AllFormsOfThisDevice;
        bool callerIsThisMachine = forms.Any(form => Normalize(form) == currentDevice);
        return callerIsThisMachine && forms.Any(form => Normalize(form) == claimed);
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
        try
        {
            await retireAsync(claim.RegistrySourceId);
        }
        catch (StudioAccountException exception) when (
            IsAlreadyRetired(exception) &&
            !ServerSnapshotStillLists(project, claim.RegistrySourceId))
        {
            // 🔴 THE ROW IS ALREADY GONE, WHICH IS WHAT THIS REMOVAL WANTS.
            // Erin's syncs stopped five times on 2026-10-19 with exactly this,
            // each in under a second and before anything else could be sent:
            // the DELETE for a registry row the server no longer has. Only
            // "that source package is not found" counts - a project the server
            // will not show, a refusal or a changed row still stop.
            //
            // 🔴 AND ONLY WHEN THE SERVER'S OWN SNAPSHOT AGREES. SRV measured
            // that this one 404 code stands for three things - a refused
            // permission, a missing project, a missing package. The code alone
            // cannot tell a removal already done from a removal refused; the
            // last snapshot the server sent can: a row it still lists is not
            // gone, and the sync stops as before.
        }
        validateContext?.Invoke();
        ApplyRegistryRetirement(project, claim);
        return local;
    }

    /// <summary>
    /// Retires every pending removal of this account on this machine, one at a
    /// time. A refusal leaves THAT removal pending and goes on to the next.
    /// </summary>
    /// <remarks>
    /// 🔴 AGREED WITH SRV, 2026-10-19. The first refused retirement used to
    /// stop the whole sync, and nothing else - not a page, not a cover - could
    /// go up behind it; that is how Erin stood still. A removal that is not
    /// retired stays blocked from the album by the plan (its registry row is
    /// still listed), so continuing cannot drop a source the server keeps.
    /// A changed account or project still stops everything: that exception is
    /// not one of these.
    /// </remarks>
    public static async Task<StudioSourceRetirementRun> RetirePendingAsync(
        ProjectWorkspace project,
        string? currentAccountEmail,
        string? currentDeviceFingerprint,
        Func<string, Task> retireAsync,
        Action? validateContext = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(retireAsync);
        var retired = new List<ProjectDesignSource>();
        var failures = new List<StudioSourceRetirementFailure>();
        foreach (ProjectLocalAlbumComponentClaim claim in
                 Pending(project, currentAccountEmail, currentDeviceFingerprint))
        {
            try
            {
                ProjectDesignSource? local = await ConfirmRegistryRetirementAsync(
                    project,
                    claim,
                    currentAccountEmail,
                    currentDeviceFingerprint,
                    retireAsync,
                    validateContext);
                if (local is not null)
                    retired.Add(local);
            }
            catch (Exception exception) when (
                exception is StudioAccountException or HttpRequestException or
                    TaskCanceledException or InvalidOperationException)
            {
                failures.Add(new StudioSourceRetirementFailure(claim, exception.Message));
            }
        }
        return new StudioSourceRetirementRun(retired, failures);
    }

    /// <summary>
    /// Whether this machine's staged removal of a component is still waiting
    /// for its registry row to be retired - the server's last snapshot still
    /// lists it.
    /// </summary>
    public static bool IsRegistryRetirementPending(
        ProjectWorkspace project,
        string componentCode,
        string? currentAccountEmail,
        string? currentDeviceFingerprint)
    {
        ArgumentNullException.ThrowIfNull(project);
        ProjectLocalAlbumComponentClaim? claim =
            ClaimOfThisDevice(project, componentCode, currentAccountEmail, currentDeviceFingerprint);
        return claim is { IsRemoval: true } &&
            ServerSnapshotStillLists(project, claim.RegistrySourceId);
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

    private static bool ServerSnapshotStillLists(ProjectWorkspace project, string registrySourceId) =>
        (project.Cloud.SharedSources ?? []).Any(source =>
            source is not null &&
            source.SourceId.Equals(registrySourceId, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(source.Status, "Removed", StringComparison.OrdinalIgnoreCase));

    private static bool IsAlreadyRetired(StudioAccountException exception) =>
        exception.StatusCode == System.Net.HttpStatusCode.NotFound &&
        string.Equals(exception.ErrorCode, "source_package_not_found", StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string? value) =>
        (value ?? "").Trim().ToLowerInvariant();
}
