using ErkS.Platform.Core;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

public sealed class StudioAlbumComponentManifestNormalizerTests
{
    [Fact]
    public void ExactDuplicateSingletonCode_KeepsOnePhysicalCopy()
    {
        ProjectWorkspace project = ProjectWithBuilding();
        StudioCloudAlbumSection[] manifest =
        [
            Section(ProjectCloudSyncMetadata.CoverComponentCode, 0, [1]),
            Section(ProjectCloudSyncMetadata.CoverComponentCode, 0, [2]),
            Section("generated:table-of-contents", 5_000, [3]),
        ];

        StudioAlbumComponentManifestNormalizationPlan plan =
            StudioAlbumComponentManifestNormalizer.CreatePlan(
                project,
                manifest,
                EmptySourceOrder());

        Assert.Equal(3, plan.OriginalSlots.Count);
        Assert.Equal([1], plan.OriginalSlots[0].PageNumbers);
        Assert.Equal([2], plan.OriginalSlots[1].PageNumbers);
        Assert.Equal(2, plan.TargetManifest.Count);
        StudioCloudAlbumSection cover = Assert.Single(
            plan.TargetManifest,
            item => item.Code == ProjectCloudSyncMetadata.CoverComponentCode);
        Assert.Equal([1], cover.PageNumbers);
        StudioCloudAlbumSection contents = Assert.Single(
            plan.TargetManifest,
            item => item.Code == "generated:table-of-contents");
        Assert.Equal([2], contents.PageNumbers);
        Assert.Single(plan.RemovedCodes);
        Assert.True(plan.RequiresPdfRewrite);
    }

    [Fact]
    public void SingleMultiPageComponent_PreservesEveryPhysicalPage()
    {
        ProjectWorkspace project = ProjectWithBuilding();
        StudioCloudAlbumSection[] manifest =
        [
            Section(ProjectCloudSyncMetadata.CompanyLicenseComponentCode, 20_000, [1, 2, 3]),
        ];

        StudioAlbumComponentManifestNormalizationPlan plan =
            StudioAlbumComponentManifestNormalizer.CreatePlan(
                project,
                manifest,
                EmptySourceOrder());

        StudioCloudAlbumSection license = Assert.Single(plan.TargetManifest);
        Assert.Equal([1, 2, 3], license.PageNumbers);
        Assert.Empty(plan.RemovedCodes);
        Assert.False(plan.RequiresPdfRewrite);
    }

    [Fact]
    public void BuildingSubCoverAliases_KeepOneCanonicalPageAndRemoveTheOther()
    {
        ProjectWorkspace project = ProjectWithBuilding();
        const string canonical =
            "generated:building-sub-cover:studio-building:building-2";
        const string alias =
            "generated:building-sub-cover:package-building:name:Apartment";
        string sourceCode = StudioAlbumComponentIdentity.SourceSliceCode(
            "architect@erks.local",
            "building-source",
            "studio-building:building-2",
            "floor-plans");
        StudioCloudAlbumSection[] manifest =
        [
            Section(alias, 200_000, [1]),
            Section(canonical, 200_000, [2]),
            SourceSection(sourceCode, "architect@erks.local", "building-source", [3]),
        ];

        StudioAlbumComponentManifestNormalizationPlan plan =
            StudioAlbumComponentManifestNormalizer.CreatePlan(
                project,
                manifest,
                EmptySourceOrder());

        Assert.Equal(2, plan.TargetManifest.Count);
        Assert.Equal(canonical, plan.TargetManifest[0].Code);
        Assert.Equal([1], plan.TargetManifest[0].PageNumbers);
        Assert.Equal([2], plan.TargetManifest[1].PageNumbers);
        Assert.Equal([alias], plan.RemovedCodes);
        Assert.Equal(canonical, plan.CanonicalCodeByRetainedCode[canonical]);
        Assert.True(plan.RequiresPdfRewrite);
    }

    [Fact]
    public void SourceSliceAlias_PreservesContributorMetadataWhileCanonicalizingCode()
    {
        ProjectWorkspace project = ProjectWithBuilding();
        const string owner = "architect@erks.local";
        const string sourceKey = "building-source";
        string alias = StudioAlbumComponentIdentity.SourceSliceCode(
            owner,
            sourceKey,
            "package-building:name:Apartment",
            "sections");
        string expected = StudioAlbumComponentIdentity.SourceSliceCode(
            owner,
            sourceKey,
            "studio-building:building-2",
            "sections");
        StudioCloudAlbumSection source = SourceSection(
            alias,
            owner,
            sourceKey,
            [1]);

        StudioAlbumComponentManifestNormalizationPlan plan =
            StudioAlbumComponentManifestNormalizer.CreatePlan(
                project,
                [source],
                EmptySourceOrder());

        StudioCloudAlbumSection actual = Assert.Single(plan.TargetManifest);
        Assert.Equal(expected, actual.Code);
        Assert.Equal(owner, actual.OwnerEmail);
        Assert.Equal(sourceKey, actual.SourceKey);
        Assert.Equal(
            StudioAlbumComponentIdentity.SourceComponentKind,
            actual.ComponentKind);
        Assert.False(plan.RequiresPdfRewrite);
    }

    [Fact]
    public void InputRowOrder_DoesNotChangeCanonicalManifest()
    {
        ProjectWorkspace project = ProjectWithBuilding();
        string plans = StudioAlbumComponentIdentity.SourceSliceCode(
            "architect@erks.local",
            "building-source",
            "package-building:id:building-2",
            "floor-plans");
        string sections = StudioAlbumComponentIdentity.SourceSliceCode(
            "architect@erks.local",
            "building-source",
            "studio-building:building-2",
            "sections");
        StudioCloudAlbumSection[] manifest =
        [
            SourceSection(sections, "architect@erks.local", "building-source", [3]),
            Section("generated:cover", 0, [1]),
            SourceSection(plans, "architect@erks.local", "building-source", [2]),
        ];

        StudioAlbumComponentManifestNormalizationPlan first =
            StudioAlbumComponentManifestNormalizer.CreatePlan(
                project,
                manifest,
                EmptySourceOrder());
        StudioAlbumComponentManifestNormalizationPlan second =
            StudioAlbumComponentManifestNormalizer.CreatePlan(
                project,
                manifest.Reverse().ToArray(),
                EmptySourceOrder());

        Assert.Equal(
            first.TargetManifest.Select(ComponentSignature),
            second.TargetManifest.Select(ComponentSignature));
    }

    private static StudioCloudAlbumSection Section(
        string code,
        int order,
        int[] pages) => new()
    {
        Code = code,
        Label = code,
        Order = order,
        PageNumbers = pages,
        Status = "Available",
        ComponentKind = StudioAlbumComponentIdentity.GeneratedComponentKind,
    };

    private static StudioCloudAlbumSection SourceSection(
        string code,
        string owner,
        string sourceKey,
        int[] pages) => new()
    {
        Code = code,
        Label = sourceKey,
        Order = 0,
        PageNumbers = pages,
        Status = "Available",
        OwnerEmail = owner,
        SourceKey = sourceKey,
        ComponentKind = StudioAlbumComponentIdentity.SourceComponentKind,
    };

    private static string ComponentSignature(StudioCloudAlbumSection component) =>
        $"{component.Code}|{component.Order}|{string.Join(",", component.PageNumbers)}";

    private static IReadOnlyDictionary<string, int> EmptySourceOrder() =>
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    private static ProjectWorkspace ProjectWithBuilding() => new()
    {
        BuildingGroups =
        [
            new ProjectBuildingGroup
            {
                Id = "building-2",
                Name = "Apartment",
                Order = 1,
            },
        ],
    };
}
