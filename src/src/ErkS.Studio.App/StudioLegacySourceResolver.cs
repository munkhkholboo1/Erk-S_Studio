using ErkS.Platform.Core;

namespace ErkS.Studio;

/// <summary>
/// Resolves pre-owner-qualified source identities without guessing. Source ids
/// take precedence because they identify a row directly; a legacy SourceKey is
/// accepted only when it maps to one unambiguous immutable source stream.
/// </summary>
internal static class StudioLegacySourceResolver
{
    public static ProjectDesignSource? Resolve(
        ProjectWorkspace project,
        string? identity)
    {
        ArgumentNullException.ThrowIfNull(project);
        string normalized = (identity ?? "").Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return null;

        ProjectDesignSource[] sourceKeyMatches = (project.Sources ?? [])
            .Where(source =>
                ProjectCloudSyncMetadata.CloudSourceKey(source).Equals(
                    normalized,
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (sourceKeyMatches.Length > 0)
            return ResolveUniqueSourceKey(project, normalized);

        ProjectDesignSource[] exactIdMatches = (project.Sources ?? [])
            .Where(source =>
                source.Id.Equals(
                    normalized,
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (exactIdMatches.Length == 1)
            return exactIdMatches[0];
        if (exactIdMatches.Length > 1)
            return null;

        return null;
    }

    public static ProjectDesignSource? ResolveUniqueSourceKey(
        ProjectWorkspace project,
        string? sourceKey)
    {
        ArgumentNullException.ThrowIfNull(project);
        string normalized = (sourceKey ?? "").Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return null;

        ProjectDesignSource[] matches = (project.Sources ?? [])
            .Where(source =>
                ProjectCloudSyncMetadata.CloudSourceKey(source).Equals(
                    normalized,
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();
        string[] immutableOwners = matches
            .Select(ProjectCloudSyncMetadata.CloudOwnerEmail)
            .Concat((project.Cloud?.SharedSources ?? [])
                .Where(source =>
                    !source.Status.Equals(
                        "Retired",
                        StringComparison.OrdinalIgnoreCase) &&
                    source.SourceKey.Equals(
                        normalized,
                        StringComparison.OrdinalIgnoreCase))
                .Select(StudioSharedSourceProjection.ImmutableOwner))
            .Where(owner => !string.IsNullOrWhiteSpace(owner))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (immutableOwners.Length > 1)
            return null;

        return matches.Length == 1
            ? matches[0]
            : null;
    }

    public static bool CanRetireUnqualifiedComponent(
        ProjectWorkspace project,
        string? sourceKey,
        string? selectedOwnerEmail)
    {
        ProjectDesignSource? source =
            ResolveUniqueSourceKey(project, sourceKey);
        if (source is null)
            return false;

        string owner =
            StudioLocalSourceBindingPolicy.ResolveLegacyImmutableOwner(
                project,
                source);
        return !string.IsNullOrWhiteSpace(owner) &&
            owner.Equals(
                (selectedOwnerEmail ?? "").Trim(),
                StringComparison.OrdinalIgnoreCase);
    }
}
