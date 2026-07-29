using ErkS.Platform.Core;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

public sealed class StudioCanonicalAlbumRebuildPolicyTests
{
    [Fact]
    public void PendingSignal_ProducesDeterministicSubcoverRefreshAndTombstonePlan()
    {
        ProjectWorkspace project = ProjectWithLocalPendingCover();
        StudioCloudProjectDetail cloud = CloudProject(
            pending: true,
            requiredVersion: 4,
            revisionVersion: 3,
            groups:
            [
                Group("building-b", "Building B", 2),
                Group("building-a", "Building A", 1),
            ],
            tombstones:
            [
                "GENERATED:BUILDING-SUB-COVER:STUDIO-BUILDING:deleted-b",
                "generated:building-sub-cover:studio-building:deleted-a",
                "generated:building-sub-cover:studio-building:deleted-a",
            ]);

        StudioCanonicalAlbumRebuildResolution resolution =
            StudioCanonicalAlbumRebuildPolicy.Resolve(project, cloud);

        Assert.True(resolution.IsPending);
        Assert.False(resolution.CanPresentCanonicalPdf);
        Assert.Equal(4, resolution.RequiredBuildingCompositionVersion);
        Assert.Equal(3, resolution.CurrentBuildingCompositionVersion);
        Assert.Equal(
            [
                "generated:building-sub-cover:studio-building:deleted-a",
                "generated:building-sub-cover:studio-building:deleted-b",
            ],
            resolution.TombstoneCodes);
        Assert.Equal(
            [
                "generated:building-sub-cover:studio-building:building-a",
                "generated:building-sub-cover:studio-building:building-b",
                "generated:building-sub-cover:studio-building:deleted-a",
                "generated:building-sub-cover:studio-building:deleted-b",
            ],
            resolution.PendingComponentCodes);
    }

    [Fact]
    public void InvalidServerTombstone_RemainsVisibleButCannotDeleteUnrelatedComponent()
    {
        ProjectWorkspace project = ProjectWithLocalPendingCover();
        StudioCloudProjectDetail cloud = CloudProject(
            pending: true,
            requiredVersion: 2,
            revisionVersion: 1,
            groups: [Group("building-a", "Building A", 1)],
            tombstones:
            [
                ProjectCloudSyncMetadata.CoverComponentCode,
                "generated:building-sub-cover:studio-building:deleted-a",
            ]);

        StudioCanonicalAlbumRebuildResolution resolution =
            StudioCanonicalAlbumRebuildPolicy.Resolve(project, cloud);

        Assert.True(resolution.IsPending);
        Assert.Equal(
            [ProjectCloudSyncMetadata.CoverComponentCode],
            resolution.RejectedTombstoneCodes);
        Assert.DoesNotContain(
            ProjectCloudSyncMetadata.CoverComponentCode,
            resolution.TombstoneCodes);
        Assert.Contains(
            "generated:building-sub-cover:studio-building:deleted-a",
            resolution.TombstoneCodes);
        Assert.Contains(
            "1 invalid tombstone ignored",
            StudioCanonicalAlbumRebuildPolicy.Describe(resolution));
    }

    [Fact]
    public void ApplyAndClearSignal_PreservesIndependentLocalPendingWork()
    {
        ProjectWorkspace project = ProjectWithLocalPendingCover();
        StudioCloudProjectDetail pending = CloudProject(
            pending: true,
            requiredVersion: 2,
            revisionVersion: 1,
            groups: [Group("building-a", "Building A", 1)],
            tombstones:
            [
                "generated:building-sub-cover:studio-building:deleted-a",
            ]);

        StudioCanonicalAlbumRebuildPolicy.Apply(project, pending);

        Assert.True(project.Cloud.CanonicalAlbumRebuildPending);
        Assert.Contains(
            "generated:building-sub-cover:studio-building:building-a",
            ProjectCloudSyncMetadata.PendingAlbumComponents(project));
        Assert.Contains(
            "generated:building-sub-cover:studio-building:deleted-a",
            ProjectCloudSyncMetadata.PendingAlbumComponents(project));
        Assert.Contains(
            ProjectCloudSyncMetadata.CoverComponentCode,
            ProjectCloudSyncMetadata.PendingAlbumComponents(project));

        StudioCloudProjectDetail current = CloudProject(
            pending: false,
            requiredVersion: 2,
            revisionVersion: 2,
            groups: [Group("building-a", "Building A", 1)],
            tombstones: []);
        StudioCanonicalAlbumRebuildPolicy.Apply(project, current);

        Assert.False(project.Cloud.CanonicalAlbumRebuildPending);
        Assert.Empty(project.Cloud.CanonicalAlbumRebuildComponentCodes);
        Assert.Empty(project.Cloud.CanonicalAlbumPendingComponentTombstoneCodes);
        Assert.Equal(
            [ProjectCloudSyncMetadata.CoverComponentCode],
            ProjectCloudSyncMetadata.PendingAlbumComponents(project));
    }

    [Fact]
    public void TombstoneUploads_AreRemoveOnlyDistinctAndSorted()
    {
        StudioCanonicalAlbumRebuildResolution resolution = new(
            IsPending: true,
            RequiredBuildingCompositionVersion: 3,
            CurrentBuildingCompositionVersion: 2,
            PendingComponentCodes: [],
            TombstoneCodes:
            [
                "generated:building-sub-cover:studio-building:building-b",
                "generated:building-sub-cover:studio-building:building-a",
                "generated:building-sub-cover:studio-building:building-a",
            ],
            RejectedTombstoneCodes: []);
        StudioCloudAlbumSection current = new()
        {
            Code = "generated:building-sub-cover:studio-building:building-b",
            Label = "Building B",
            Order = 20,
            ComponentKind = StudioAlbumComponentIdentity.GeneratedComponentKind,
        };

        IReadOnlyList<StudioAlbumComponentUpload> uploads =
            StudioCanonicalAlbumRebuildPolicy.ApplyTombstoneUploads(
                resolution,
                [current],
                existingUploads:
                [
                    new StudioAlbumComponentUpload(
                        "generated:building-sub-cover:studio-building:building-a",
                        "Stale local render",
                        10,
                        "stale.pdf"),
                ]);

        Assert.Collection(
            uploads,
            upload =>
            {
                Assert.Equal(
                    "generated:building-sub-cover:studio-building:building-a",
                    upload.Code);
                Assert.True(upload.Remove);
                Assert.Empty(upload.PdfPath);
            },
            upload =>
            {
                Assert.Equal(
                    "generated:building-sub-cover:studio-building:building-b",
                    upload.Code);
                Assert.Equal("Building B", upload.Label);
                Assert.Equal(20, upload.Order);
                Assert.True(upload.Remove);
                Assert.Empty(upload.PdfPath);
            });
    }

    private static ProjectWorkspace ProjectWithLocalPendingCover()
    {
        var project = new ProjectWorkspace();
        ProjectCloudSyncMetadata.MarkAlbumComponentsPending(
            project,
            [ProjectCloudSyncMetadata.CoverComponentCode]);
        return project;
    }

    private static StudioCloudProjectDetail CloudProject(
        bool pending,
        int requiredVersion,
        int revisionVersion,
        IReadOnlyList<StudioCloudBuildingGroup> groups,
        IReadOnlyList<string> tombstones)
    {
        const string revisionId = "revision-current";
        return new StudioCloudProjectDetail
        {
            BuildingComposition = new StudioCloudBuildingComposition
            {
                Version = requiredVersion,
                Groups = groups.ToList(),
            },
            Albums =
            [
                new StudioCloudAlbum
                {
                    AlbumId = "concept-album",
                    AlbumType = ProjectWorkspace.BuildingArchitectureConcept,
                    CurrentRevisionId = revisionId,
                    RequiredBuildingCompositionVersion = requiredVersion,
                    CanonicalRebuildPending = pending,
                    PendingComponentTombstoneCodes = tombstones.ToList(),
                    Revisions =
                    [
                        new StudioCloudAlbumRevision
                        {
                            RevisionId = revisionId,
                            RevisionNumber = 7,
                            BuildingCompositionVersion = revisionVersion,
                            PageCount = 3,
                        },
                    ],
                },
            ],
        };
    }

    private static StudioCloudBuildingGroup Group(
        string id,
        string name,
        int order) => new()
    {
        Id = id,
        Name = name,
        Order = order,
    };
}
