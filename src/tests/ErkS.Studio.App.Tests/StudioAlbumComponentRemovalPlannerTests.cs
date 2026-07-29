using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

public sealed class StudioAlbumComponentRemovalPlannerTests
{
    [Fact]
    public void BaseSourceRemovalExpandsToAllOwnedSlicesOnly()
    {
        const string sourceKey = "same-source-key";
        string ownerABase =
            StudioAlbumComponentIdentity.SourceCode("a@example.com", sourceKey);
        string ownerAFirst = StudioAlbumComponentIdentity.SourceSliceCode(
            "a@example.com",
            sourceKey,
            "studio-building:building-1",
            "floor-plans");
        string ownerASecond = StudioAlbumComponentIdentity.SourceSliceCode(
            "a@example.com",
            sourceKey,
            "studio-building:building-2",
            "sections");
        string ownerB = StudioAlbumComponentIdentity.SourceSliceCode(
            "b@example.com",
            sourceKey,
            "studio-building:building-1",
            "floor-plans");

        IReadOnlyList<StudioCloudAlbumSection> removals =
            StudioAlbumComponentRemovalPlanner.FindMissingSourceComponents(
                [
                    Section(ownerAFirst),
                    Section(ownerB),
                    Section(ownerASecond),
                ],
                [ownerABase]);

        Assert.Equal(
            new[] { ownerAFirst, ownerASecond }.Order(StringComparer.OrdinalIgnoreCase),
            removals.Select(item => item.Code));
        Assert.DoesNotContain(removals, item =>
            item.Code.Equals(ownerB, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SliceRemovalDoesNotRemoveSiblingSlices()
    {
        const string owner = "a@example.com";
        const string sourceKey = "source";
        string first = StudioAlbumComponentIdentity.SourceSliceCode(
            owner,
            sourceKey,
            "studio-building:building-1",
            "floor-plans");
        string second = StudioAlbumComponentIdentity.SourceSliceCode(
            owner,
            sourceKey,
            "studio-building:building-1",
            "sections");

        StudioCloudAlbumSection removal = Assert.Single(
            StudioAlbumComponentRemovalPlanner.FindMissingSourceComponents(
                [Section(first), Section(second)],
                [first]));

        Assert.Equal(first, removal.Code);
    }

    private static StudioCloudAlbumSection Section(string code) => new()
    {
        Code = code,
        Label = code,
        ComponentKind = StudioAlbumComponentIdentity.SourceComponentKind,
    };
}
