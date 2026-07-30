namespace ErkS.Studio;

internal static class StudioCanonicalAlbumPreviewPolicy
{
    public static StudioCanonicalAlbumPreviewDecision Resolve(
        StudioCanonicalAlbumRebuildResolution rebuild,
        bool hasVerifiedServerRevision)
    {
        ArgumentNullException.ThrowIfNull(rebuild);

        return new StudioCanonicalAlbumPreviewDecision(
            CanDisplay: hasVerifiedServerRevision,
            IsRebuildPending: rebuild.IsPending,
            IsCanonicalComplete:
                hasVerifiedServerRevision &&
                !rebuild.IsPending);
    }
}

internal sealed record StudioCanonicalAlbumPreviewDecision(
    bool CanDisplay,
    bool IsRebuildPending,
    bool IsCanonicalComplete);
