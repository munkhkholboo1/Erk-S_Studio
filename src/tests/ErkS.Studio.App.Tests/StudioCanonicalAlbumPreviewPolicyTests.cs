namespace ErkS.Studio.App.Tests;

public sealed class StudioCanonicalAlbumPreviewPolicyTests
{
    [Fact]
    public void PendingRebuildStillAllowsLatestVerifiedServerRevision()
    {
        var rebuild = new StudioCanonicalAlbumRebuildResolution(
            IsPending: true,
            RequiredBuildingCompositionVersion: 7,
            CurrentBuildingCompositionVersion: 6,
            PendingComponentCodes:
            [
                "generated:building-sub-cover:studio-building:school",
            ],
            TombstoneCodes: [],
            RejectedTombstoneCodes: []);

        StudioCanonicalAlbumPreviewDecision decision =
            StudioCanonicalAlbumPreviewPolicy.Resolve(
                rebuild,
                hasVerifiedServerRevision: true);

        Assert.True(decision.CanDisplay);
        Assert.True(decision.IsRebuildPending);
        Assert.False(decision.IsCanonicalComplete);
    }

    [Fact]
    public void PendingRebuildWithoutServerRevisionCannotDisplayAlbum()
    {
        var rebuild = new StudioCanonicalAlbumRebuildResolution(
            IsPending: true,
            RequiredBuildingCompositionVersion: 7,
            CurrentBuildingCompositionVersion: 6,
            PendingComponentCodes:
            [
                "generated:building-sub-cover:studio-building:school",
            ],
            TombstoneCodes: [],
            RejectedTombstoneCodes: []);

        StudioCanonicalAlbumPreviewDecision decision =
            StudioCanonicalAlbumPreviewPolicy.Resolve(
                rebuild,
                hasVerifiedServerRevision: false);

        Assert.False(decision.CanDisplay);
        Assert.True(decision.IsRebuildPending);
        Assert.False(decision.IsCanonicalComplete);
    }

    [Fact]
    public void CurrentServerRevisionIsCanonicalComplete()
    {
        var rebuild = new StudioCanonicalAlbumRebuildResolution(
            IsPending: false,
            RequiredBuildingCompositionVersion: 7,
            CurrentBuildingCompositionVersion: 7,
            PendingComponentCodes: [],
            TombstoneCodes: [],
            RejectedTombstoneCodes: []);

        StudioCanonicalAlbumPreviewDecision decision =
            StudioCanonicalAlbumPreviewPolicy.Resolve(
                rebuild,
                hasVerifiedServerRevision: true);

        Assert.True(decision.CanDisplay);
        Assert.False(decision.IsRebuildPending);
        Assert.True(decision.IsCanonicalComplete);
    }
}
