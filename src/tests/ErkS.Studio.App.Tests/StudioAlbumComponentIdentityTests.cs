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

    [Fact]
    public void SourceSliceCode_RoundTripsBuildingAndSheetTypeWithoutChangingSourceIdentity()
    {
        const string owner = "architect@erks.local";
        const string sourceKey = "shared-building-source";
        const string sectionKey = "studio-building:building-2";
        const string sequenceKey = "sections";

        string sourceCode = StudioAlbumComponentIdentity.SourceCode(owner, sourceKey);
        string sliceCode = StudioAlbumComponentIdentity.SourceSliceCode(
            owner,
            sourceKey,
            sectionKey,
            sequenceKey);

        Assert.NotEqual(sourceCode, sliceCode);
        Assert.Equal(
            sourceCode,
            StudioAlbumComponentIdentity.BaseSourceCode(sliceCode));
        Assert.True(StudioAlbumComponentIdentity.IsOwnedSourceCode(sliceCode));
        Assert.True(StudioAlbumComponentIdentity.TryGetSourceSlice(
            sliceCode,
            out string actualSection,
            out string actualSequence));
        Assert.Equal(sectionKey, actualSection);
        Assert.Equal(sequenceKey, actualSequence);
    }
}
