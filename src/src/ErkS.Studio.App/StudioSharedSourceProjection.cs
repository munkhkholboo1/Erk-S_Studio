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
        // A source is kept when it has a SourceKey and an OWNER - and a seat is
        // an owner. The second test used to read ImmutableOwner, an email-only
        // chain, so once SRV started emptying registeredBy and custodianEmail
        // on bot-owned rows this filter dropped every one of them: sources a
        // seat had produced never reached album ordering at all. A bail-out of
        // that shape loses the whole package rather than one field.
        .Where(source =>
            !string.IsNullOrWhiteSpace(source.SourceKey) &&
            ProjectSourceOwnership.Of(source).Reference.Length > 0)
        .Select(source => new StudioCloudSourcePackage
        {
            SourceId = source.SourceId,
            SourceKey = source.SourceKey,
            SourceApplication = source.SourceApplication,
            SourcePurpose = StudioSourcePurpose.Normalize(
                source.SourcePurpose),
            SourceDocumentReference = source.SourceDocumentReference,
            ManifestId = source.ManifestId,
            ContentHash = source.ContentHash,
            SheetCount = source.SheetCount,
            Status = source.Status,
            SourceOwnerKind = source.SourceOwnerKind,
            SourceOwnerRef = source.SourceOwnerRef,
            RegisteredBy = ImmutableOwner(source),
            RegisteredAtUtc = source.RegisteredAtUtc,
            CustodianEmail = FirstNonEmpty(
                source.CustodianEmail,
                source.OwnerEmail),
        })
        .ToList();

    /// <summary>
    /// The immutable PERSON who registered this stream, lowercased for use as
    /// an identity key. Empty on a bot-owned source, which is the honest
    /// answer: no person registered it. Ask
    /// <see cref="ProjectSourceOwnership.Of"/> when the question is "who owns
    /// this", and use this only where a person's email is what is wanted.
    /// </summary>
    public static string ImmutableOwner(ProjectCloudSourceReference source) =>
        ProjectSourceOwnership.Of(source).IsPersonOwned
            ? FirstNonEmpty(source.RegisteredBy, source.OwnerEmail)
                .Trim()
                .ToLowerInvariant()
            : "";

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";
}
