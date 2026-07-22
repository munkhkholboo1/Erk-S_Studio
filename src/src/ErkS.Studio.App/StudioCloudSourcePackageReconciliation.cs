namespace ErkS.Studio;

internal static class StudioCloudSourcePackageReconciliation
{
    public static IReadOnlyList<StudioCloudSourcePackage> ActiveCanonical(
        IEnumerable<StudioCloudSourcePackage> sourcePackages)
    {
        List<StudioCloudSourcePackage> active = sourcePackages
            .Where(source => string.IsNullOrWhiteSpace(source.Status) ||
                source.Status.Equals("Registered", StringComparison.OrdinalIgnoreCase))
            .ToList();
        List<StudioCloudSourcePackage> keyed = active
            .Where(source => !string.IsNullOrWhiteSpace(source.SourceKey))
            .GroupBy(
                source => $"{(source.RegisteredBy ?? "").Trim()}\n{source.SourceKey.Trim()}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderBy(EffectiveTimestamp)
                .ThenBy(source => source.SourceId, StringComparer.OrdinalIgnoreCase)
                .Last())
            .ToList();

        List<StudioCloudSourcePackage> legacy = active
            .Where(source => string.IsNullOrWhiteSpace(source.SourceKey))
            .Where(source => !keyed.Any(current => IsSuccessorOfLegacySnapshot(source, current)))
            .GroupBy(
                source => $"{source.ManifestId}\n{source.ContentHash}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderBy(EffectiveTimestamp)
                .ThenBy(source => source.SourceId, StringComparer.OrdinalIgnoreCase)
                .Last())
            .ToList();

        return keyed
            .Concat(legacy)
            .OrderBy(EffectiveTimestamp)
            .ThenBy(source => source.SourceKey, StringComparer.OrdinalIgnoreCase)
            .ThenBy(source => source.SourceId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsSuccessorOfLegacySnapshot(
        StudioCloudSourcePackage legacy,
        StudioCloudSourcePackage current)
    {
        if (!legacy.SourceApplication.Equals(
                current.SourceApplication,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string legacyReference = NormalizeReference(legacy.SourceDocumentReference);
        string currentReference = NormalizeReference(current.SourceDocumentReference);
        if (string.IsNullOrWhiteSpace(legacyReference) ||
            !legacyReference.Equals(currentReference, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(legacy.RegisteredBy) &&
            !string.IsNullOrWhiteSpace(current.RegisteredBy) &&
            !legacy.RegisteredBy.Equals(current.RegisteredBy, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        DateTimeOffset legacyAt = EffectiveTimestamp(legacy);
        DateTimeOffset currentAt = EffectiveTimestamp(current);
        return legacyAt == default || currentAt == default || currentAt >= legacyAt;
    }

    private static string NormalizeReference(string? value)
    {
        string text = (value ?? "").Trim().Replace('\\', '/');
        int separator = text.LastIndexOf('/');
        return separator >= 0 ? text[(separator + 1)..].Trim() : text;
    }

    private static DateTimeOffset EffectiveTimestamp(StudioCloudSourcePackage source) =>
        source.RegisteredAtUtc != default
            ? source.RegisteredAtUtc
            : source.ExportedAtUtc;
}
