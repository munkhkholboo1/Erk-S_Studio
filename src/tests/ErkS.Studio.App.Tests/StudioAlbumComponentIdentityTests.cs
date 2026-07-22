using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

public sealed class StudioAlbumComponentIdentityTests
{
    [Fact]
    public void SameAtdSourceKeyFromTwoContributorsProducesDistinctStableCodes()
    {
        string first = StudioAlbumComponentIdentity.SourceCode(
            "architect-a@erks.local",
            StudioAlbumComponentIdentity.AtdSourceKey);
        string retry = StudioAlbumComponentIdentity.SourceCode(
            "ARCHITECT-A@ERKS.LOCAL",
            StudioAlbumComponentIdentity.AtdSourceKey);
        string second = StudioAlbumComponentIdentity.SourceCode(
            "architect-b@erks.local",
            StudioAlbumComponentIdentity.AtdSourceKey);

        Assert.Equal(first, retry);
        Assert.NotEqual(first, second);
        Assert.True(StudioAlbumComponentIdentity.IsOwnedSourceCode(first));
        Assert.True(StudioAlbumComponentIdentity.IsOwnedSourceCode(second));
    }
}
