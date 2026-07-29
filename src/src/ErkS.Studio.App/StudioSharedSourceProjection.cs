using ErkS.Platform.Core;

namespace ErkS.Studio;

/// <summary>
/// Rehydrates the server source registry used by album ordering. Component
/// identity is always based on the immutable registrant; custody is operational
/// authority and must not rename an existing album component.
/// </summary>
internal static class StudioSharedSourceProjection
{
    public static IReadOnlyList<StudioCloudSourcePackage> Create(
        IEnumerable<ProjectCloudSourceReference> sharedSources) =>
        (sharedSources ?? [])
        .Where(source =>
            !string.IsNullOrWhiteSpace(source.SourceKey) &&
            !string.IsNullOrWhiteSpace(ImmutableOwner(source)))
        .Select(source => new StudioCloudSourcePackage
        {
            SourceId = source.SourceId,
            SourceKey = source.SourceKey,
            SourceApplication = source.SourceApplication,
            SourceDocumentReference = source.SourceDocumentReference,
            ManifestId = source.ManifestId,
            ContentHash = source.ContentHash,
            SheetCount = source.SheetCount,
            Status = source.Status,
            RegisteredBy = ImmutableOwner(source),
            RegisteredAtUtc = source.RegisteredAtUtc,
            CustodianEmail = FirstNonEmpty(
                source.CustodianEmail,
                source.OwnerEmail),
        })
        .ToList();

    public static string ImmutableOwner(ProjectCloudSourceReference source) =>
        FirstNonEmpty(source.RegisteredBy, source.OwnerEmail)
            .Trim()
            .ToLowerInvariant();

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";
}
