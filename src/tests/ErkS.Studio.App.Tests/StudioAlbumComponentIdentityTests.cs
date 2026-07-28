using ErkS.Platform.Core;
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

    [Fact]
    public void LegacySnapshot_CoversEveryExistingCloudPage()
    {
        StudioCloudAlbumSection section =
            StudioAlbumComponentIdentity.CreateLegacySnapshotSection(32);

        Assert.Equal(
            StudioAlbumComponentIdentity.LegacySnapshotComponentCode,
            section.Code);
        Assert.Equal(
            StudioAlbumComponentIdentity.LegacySnapshotComponentKind,
            section.ComponentKind);
        Assert.Equal(Enumerable.Range(1, 32), section.PageNumbers);
        Assert.False(
            StudioAlbumComponentIdentity.HasNoAssignedPages([section]));
    }

    [Fact]
    public void EmptyLegacyRows_HaveNoAssignedPages()
    {
        StudioCloudAlbumSection[] sections =
        [
            new() { Code = "generated:cover", PageNumbers = [] },
            new() { Code = "source:legacy", PageNumbers = [] },
        ];

        Assert.True(
            StudioAlbumComponentIdentity.HasNoAssignedPages(sections));
    }

    [Fact]
    public void ExistingCollaboratorSource_IsResolvedWithoutLocalProjectSource()
    {
        const string owner = "collaborator@erks.local";
        const string sourceKey = "pdf-source-42";
        string code = StudioAlbumComponentIdentity.SourceSliceCode(
            owner,
            sourceKey,
            "studio-building:building-3",
            "plans");
        StudioCloudAlbumSection[] existing =
        [
            new()
            {
                Code = code,
                OwnerEmail = owner,
                SourceKey = sourceKey,
                ComponentKind = StudioAlbumComponentIdentity.SourceComponentKind,
            },
        ];

        bool resolved = StudioAlbumComponentIdentity.TryResolveExistingSource(
            "source:local-mirror-id|album-slice|studio-building:building-3|plans",
            sourceKey,
            existing,
            out StudioCloudAlbumSection? actual);

        Assert.True(resolved);
        Assert.Same(existing[0], actual);
    }

    [Fact]
    public void SameSourceKeyFromTwoContributors_IsNotResolvedByKeyAlone()
    {
        const string sourceKey = "shared-source-key";
        StudioCloudAlbumSection[] existing =
        [
            new()
            {
                Code = StudioAlbumComponentIdentity.SourceCode(
                    "architect-a@erks.local",
                    sourceKey),
                OwnerEmail = "architect-a@erks.local",
                SourceKey = sourceKey,
                ComponentKind = StudioAlbumComponentIdentity.SourceComponentKind,
            },
            new()
            {
                Code = StudioAlbumComponentIdentity.SourceCode(
                    "architect-b@erks.local",
                    sourceKey),
                OwnerEmail = "architect-b@erks.local",
                SourceKey = sourceKey,
                ComponentKind = StudioAlbumComponentIdentity.SourceComponentKind,
            },
        ];

        bool resolved = StudioAlbumComponentIdentity.TryResolveExistingSource(
            "source:local-mirror-id",
            sourceKey,
            existing,
            out StudioCloudAlbumSection? actual);

        Assert.False(resolved);
        Assert.Null(actual);
    }

    [Theory]
    [InlineData("studio-building:building-2")]
    [InlineData("package-building:id:building-2")]
    [InlineData("package-building:name:Apartment")]
    public void BuildingAliases_ResolveToOneCanonicalSectionKey(string alias)
    {
        ProjectWorkspace project = ProjectWithBuilding();

        string actual = StudioAlbumComponentIdentity.CanonicalBuildingSectionKey(
            project,
            alias);

        Assert.Equal("studio-building:building-2", actual);
    }

    [Theory]
    [InlineData("studio-building:building-2")]
    [InlineData("package-building:id:building-2")]
    [InlineData("package-building:name:Apartment")]
    public void BuildingSubCoverAliases_ResolveToOneCanonicalComponentCode(string alias)
    {
        ProjectWorkspace project = ProjectWithBuilding();
        string legacyCode =
            ProjectCloudSyncMetadata.BuildingSubCoverComponentCodePrefix + alias;

        string actual = StudioAlbumComponentIdentity.CanonicalBuildingSubCoverCode(
            project,
            legacyCode);

        Assert.Equal(
            "generated:building-sub-cover:studio-building:building-2",
            actual);
    }

    [Fact]
    public void CanonicalBuildingAlias_ProducesTheSameSourceSliceCodeAcrossDevices()
    {
        ProjectWorkspace project = ProjectWithBuilding();
        string firstSection =
            StudioAlbumComponentIdentity.CanonicalBuildingSectionKey(
                project,
                "package-building:name:Apartment");
        string secondSection =
            StudioAlbumComponentIdentity.CanonicalBuildingSectionKey(
                project,
                "studio-building:building-2");

        string first = StudioAlbumComponentIdentity.SourceSliceCode(
            "architect@erks.local",
            "building-source",
            firstSection,
            "plans");
        string second = StudioAlbumComponentIdentity.SourceSliceCode(
            "architect@erks.local",
            "building-source",
            secondSection,
            "plans");

        Assert.Equal(first, second);
    }

    [Fact]
    public void ExistingSourceSliceAlias_IsMigratedWithoutChangingItsOwnerOrSource()
    {
        ProjectWorkspace project = ProjectWithBuilding();
        string alias = StudioAlbumComponentIdentity.SourceSliceCode(
            "architect@erks.local",
            "building-source",
            "package-building:name:Apartment",
            "plans");
        string expected = StudioAlbumComponentIdentity.SourceSliceCode(
            "architect@erks.local",
            "building-source",
            "studio-building:building-2",
            "plans");

        string actual = StudioAlbumComponentIdentity.CanonicalComponentCode(
            project,
            alias);

        Assert.Equal(expected, actual);
        Assert.Equal(
            StudioAlbumComponentIdentity.BaseSourceCode(alias),
            StudioAlbumComponentIdentity.BaseSourceCode(actual));
    }

    private static ProjectWorkspace ProjectWithBuilding() => new()
    {
        BuildingGroups =
        [
            new ProjectBuildingGroup
            {
                Id = "building-2",
                Name = "Apartment",
                Order = 2,
            },
        ],
    };
}
