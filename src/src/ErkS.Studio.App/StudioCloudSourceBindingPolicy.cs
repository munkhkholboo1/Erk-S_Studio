using ErkS.Platform.Core;

namespace ErkS.Studio;

/// <summary>
/// Determines which Cloud source streams may be bound to one local source.
/// SourceKey is portable and may collide across contributors, so duplicate
/// detection always uses immutable owner + SourceKey together.
/// </summary>
internal static class StudioCloudSourceBindingPolicy
{
    public static IReadOnlyList<StudioCloudSourcePackage> EligibleSources(
        ProjectWorkspace project,
        ProjectDesignSource bindingTarget,
        IEnumerable<StudioCloudSourcePackage> cloudSources,
        string? currentAccountEmail)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(bindingTarget);
        string current = NormalizeEmail(currentAccountEmail);
        return (cloudSources ?? [])
            .Where(cloudSource =>
                !string.IsNullOrWhiteSpace(cloudSource.SourceKey) &&
                NormalizeEmail(cloudSource.CustodianEmail).Equals(
                    current,
                    StringComparison.OrdinalIgnoreCase))
            .Where(cloudSource => !(project.Sources ?? []).Any(local =>
                !ReferenceEquals(local, bindingTarget) &&
                IsSameImmutableStream(
                    project,
                    local,
                    cloudSource,
                    current)))
            .ToList();
    }

    public static bool IsSameImmutableStream(
        ProjectWorkspace project,
        ProjectDesignSource localSource,
        StudioCloudSourcePackage cloudSource,
        string? currentAccountEmail)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(localSource);
        ArgumentNullException.ThrowIfNull(cloudSource);
        if (!ProjectCloudSyncMetadata.CloudSourceKey(localSource).Equals(
                (cloudSource.SourceKey ?? "").Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string localOwner =
            StudioLocalSourceBindingPolicy.ResolveLegacyImmutableOwner(
                project,
                localSource);
        string cloudOwner =
            ImmutableOwner(cloudSource, currentAccountEmail);
        return !string.IsNullOrWhiteSpace(localOwner) &&
            !string.IsNullOrWhiteSpace(cloudOwner) &&
            localOwner.Equals(
                cloudOwner,
                StringComparison.OrdinalIgnoreCase);
    }

    public static string ImmutableOwner(
        StudioCloudSourcePackage cloudSource,
        string? legacyFallbackAccountEmail)
    {
        ArgumentNullException.ThrowIfNull(cloudSource);
        string registeredBy = NormalizeEmail(cloudSource.RegisteredBy);
        return !string.IsNullOrWhiteSpace(registeredBy)
            ? registeredBy
            : NormalizeEmail(legacyFallbackAccountEmail);
    }

    private static string NormalizeEmail(string? value) =>
        (value ?? "").Trim().ToLowerInvariant();
}
