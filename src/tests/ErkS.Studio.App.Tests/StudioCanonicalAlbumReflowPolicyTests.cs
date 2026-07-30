using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

public sealed class StudioCanonicalAlbumReflowPolicyTests
{
    [Fact]
    public async Task ServerReflowWithSourceDeletionInvokesEmptyCasWithoutClientTombstone()
    {
        string sourceTombstone =
            StudioAlbumComponentIdentity.SourceSliceCode(
                "architect@example.com",
                "retired-source",
                "studio-building:school",
                "floor-plans");
        var album = new StudioCloudAlbum
        {
            CanonicalReflowRequired = true,
            PendingComponentTombstoneCodes = [sourceTombstone],
        };
        IReadOnlyList<StudioAlbumComponentUpload>? dispatched = null;
        var expected = new StudioCloudAlbumRevision
        {
            RevisionId = "canonical-reflow",
        };

        StudioCloudAlbumRevision? actual =
            await StudioCanonicalAlbumReflowPolicy.RequestIfRequiredAsync(
                album,
                uploads =>
                {
                    dispatched = uploads;
                    return Task.FromResult(expected);
                });

        Assert.Same(expected, actual);
        Assert.NotNull(dispatched);
        Assert.Empty(dispatched);
        Assert.True(
            StudioCanonicalAlbumReflowPolicy.ShouldDispatchComponentMerge(
                album,
                pendingSourceCount: 0,
                pendingComponentCount: 0));
    }

    [Fact]
    public async Task CleanAlbumDoesNotInvokeMetadataOnlyMerge()
    {
        var album = new StudioCloudAlbum();
        bool invoked = false;

        StudioCloudAlbumRevision? actual =
            await StudioCanonicalAlbumReflowPolicy.RequestIfRequiredAsync(
                album,
                _ =>
                {
                    invoked = true;
                    return Task.FromResult(new StudioCloudAlbumRevision());
                });

        Assert.Null(actual);
        Assert.False(invoked);
    }

    [Fact]
    public void ServerReflowSignalDispatchesMergeWithoutLocalDirtyState()
    {
        var album = new StudioCloudAlbum
        {
            CanonicalReflowRequired = true,
        };

        Assert.True(
            StudioCanonicalAlbumReflowPolicy.ShouldDispatchComponentMerge(
                album,
                pendingSourceCount: 0,
                pendingComponentCount: 0));
    }

    [Fact]
    public void CleanCanonicalAlbumDoesNotDispatchEmptyMerge()
    {
        var album = new StudioCloudAlbum();

        Assert.False(
            StudioCanonicalAlbumReflowPolicy.ShouldDispatchComponentMerge(
                album,
                pendingSourceCount: 0,
                pendingComponentCount: 0));
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(0, 1)]
    public void LocalDirtyStateStillDispatchesMerge(
        int pendingSourceCount,
        int pendingComponentCount)
    {
        var album = new StudioCloudAlbum();

        Assert.True(
            StudioCanonicalAlbumReflowPolicy.ShouldDispatchComponentMerge(
                album,
                pendingSourceCount,
                pendingComponentCount));
    }
}
