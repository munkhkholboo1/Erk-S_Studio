namespace ErkS.Studio;

internal static class StudioCanonicalAlbumReflowPolicy
{
    public static bool ShouldDispatchComponentMerge(
        StudioCloudAlbum album,
        int pendingSourceCount,
        int pendingComponentCount)
    {
        ArgumentNullException.ThrowIfNull(album);
        ArgumentOutOfRangeException.ThrowIfNegative(pendingSourceCount);
        ArgumentOutOfRangeException.ThrowIfNegative(pendingComponentCount);

        return album.CanonicalReflowRequired ||
            pendingSourceCount > 0 ||
            pendingComponentCount > 0;
    }

    public static async Task<StudioCloudAlbumRevision?> RequestIfRequiredAsync(
        StudioCloudAlbum album,
        Func<IReadOnlyList<StudioAlbumComponentUpload>,
            Task<StudioCloudAlbumRevision>> merge)
    {
        ArgumentNullException.ThrowIfNull(album);
        ArgumentNullException.ThrowIfNull(merge);

        if (!album.CanonicalReflowRequired)
            return null;

        return await merge([]);
    }
}
