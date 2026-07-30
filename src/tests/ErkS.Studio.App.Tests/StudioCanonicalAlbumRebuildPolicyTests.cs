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
            ],
            manifest:
            [
                SourceSlice("building-a"),
                SourceSlice("building-b"),
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
    public void SourceTombstoneRemainsServerAuthoritativeAndIsNotSynthesizedByStudio()
    {
        StudioCloudAlbumSection source = SourceSlice("school");
        ProjectWorkspace project = new();
        StudioCloudProjectDetail cloud = CloudProject(
            pending: true,
            requiredVersion: 2,
            revisionVersion: 2,
            groups: [Group("school", "School", 1)],
            tombstones: [source.Code],
            manifest: [source]);

        StudioCanonicalAlbumRebuildResolution received =
            StudioCanonicalAlbumRebuildPolicy.Apply(project, cloud);
        StudioCanonicalAlbumRebuildResolution resolution =
            StudioCanonicalAlbumRebuildPolicy.ResolvePersisted(project);
        IReadOnlyList<StudioAlbumComponentUpload> uploads =
            StudioCanonicalAlbumRebuildPolicy.ApplyTombstoneUploads(
                resolution,
                [source],
                [
                    new StudioAlbumComponentUpload(
                        source.Code,
                        source.Label,
                        source.Order,
                        "",
                        Remove: true,
                        SourceKey: source.SourceKey,
                        ComponentKind: source.ComponentKind,
                        SectionKey: source.SectionKey,
                        SequenceKey: source.SequenceKey),
                ]);

        Assert.Empty(received.TombstoneCodes);
        Assert.Equal([source.Code], received.RejectedTombstoneCodes);
        Assert.Empty(resolution.TombstoneCodes);
        Assert.Equal([source.Code], resolution.RejectedTombstoneCodes);
        Assert.Empty(uploads);
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
            ],
            manifest: [SourceSlice("building-a")]);

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
    public void BuildingTombstones_DoNotProduceClientRemovalDescriptors()
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
                existingUploads: []);

        Assert.Empty(uploads);
    }

    [Fact]
    public void BuildingTombstone_DropsExistingStaleReplacement()
    {
        const string tombstoneCode =
            "generated:building-sub-cover:studio-building:building-a";
        StudioCanonicalAlbumRebuildResolution resolution = new(
            IsPending: true,
            RequiredBuildingCompositionVersion: 3,
            CurrentBuildingCompositionVersion: 2,
            PendingComponentCodes: [],
            TombstoneCodes: [tombstoneCode],
            RejectedTombstoneCodes: []);

        IReadOnlyList<StudioAlbumComponentUpload> uploads =
            StudioCanonicalAlbumRebuildPolicy.ApplyTombstoneUploads(
                resolution,
                [],
                [
                    new StudioAlbumComponentUpload(
                        tombstoneCode,
                        "Stale local render",
                        10,
                        "stale.pdf"),
                ]);

        Assert.Empty(uploads);
    }

    [Fact]
    public void FortyServerTombstones_LeaveOnlyOneRealClientUpload()
    {
        string[] tombstones = Enumerable.Range(1, 40)
            .Select(index =>
                "generated:building-sub-cover:studio-building:deleted-" +
                index.ToString("D2"))
            .ToArray();
        StudioCanonicalAlbumRebuildResolution resolution = new(
            IsPending: true,
            RequiredBuildingCompositionVersion: 3,
            CurrentBuildingCompositionVersion: 2,
            PendingComponentCodes: [],
            TombstoneCodes: tombstones,
            RejectedTombstoneCodes: []);
        StudioAlbumComponentUpload realUpload = new(
            "generated:building-sub-cover:studio-building:school",
            "School",
            10,
            "school.pdf");
        StudioAlbumComponentUpload staleUpload = new(
            tombstones[0],
            "Deleted",
            20,
            "deleted.pdf");

        IReadOnlyList<StudioAlbumComponentUpload> uploads =
            StudioCanonicalAlbumRebuildPolicy.ApplyTombstoneUploads(
                resolution,
                [],
                [staleUpload, realUpload]);

        StudioAlbumComponentUpload upload = Assert.Single(uploads);
        Assert.Same(realUpload, upload);
    }

    [Fact]
    public void PendingPlan_ExcludesEmptyGroupAndIncludesMissingReferencedSchoolCover()
    {
        ProjectWorkspace project = ProjectWithLocalPendingCover();
        project.Cloud.SharedAlbumComponents =
        [
            new ProjectCloudAlbumComponentReference
            {
                Code = StudioAlbumComponentIdentity.SourceSliceCode(
                    "architect@example.com",
                    "stale-source",
                    "studio-building:empty-apartment",
                    "floor-plans"),
                PageNumbers = [99],
                Status = "Available",
                OwnerEmail = "architect@example.com",
                SourceKey = "stale-source",
                ComponentKind =
                    StudioAlbumComponentIdentity.SourceComponentKind,
            },
        ];
        StudioCloudProjectDetail cloud = CloudProject(
            pending: true,
            requiredVersion: 5,
            revisionVersion: 4,
            groups:
            [
                Group("empty-apartment", "Empty apartment", 1),
                Group("school", "School", 2),
            ],
            tombstones: [],
            manifest: [SourceSlice("school")]);

        StudioCanonicalAlbumRebuildResolution resolution =
            StudioCanonicalAlbumRebuildPolicy.Resolve(project, cloud);

        Assert.Equal(
            ["generated:building-sub-cover:studio-building:school"],
            resolution.PendingComponentCodes);
        Assert.DoesNotContain(
            "generated:building-sub-cover:studio-building:empty-apartment",
            resolution.PendingComponentCodes);
    }

    [Fact]
    public void PendingSameCompositionVersion_RequestsOnlyMissingReferencedSubcovers()
    {
        ProjectWorkspace project = ProjectWithLocalPendingCover();
        StudioCloudProjectDetail cloud = CloudProject(
            pending: true,
            requiredVersion: 7,
            revisionVersion: 7,
            groups:
            [
                Group("apartment", "Apartment", 1),
                Group("service", "Service", 2),
                Group("school", "School", 3),
            ],
            tombstones: [],
            manifest:
            [
                SourceSlice("apartment"),
                SourceSlice("service"),
                SourceSlice("school"),
                Subcover("apartment"),
                Subcover("service"),
            ]);

        StudioCanonicalAlbumRebuildResolution resolution =
            StudioCanonicalAlbumRebuildPolicy.Resolve(project, cloud);

        Assert.Equal(
            ["generated:building-sub-cover:studio-building:school"],
            resolution.PendingComponentCodes);
    }

    [Fact]
    public void PendingSameCompositionVersion_DoesNotTreatInactiveSubcoverAsCurrent()
    {
        ProjectWorkspace project = ProjectWithLocalPendingCover();
        StudioCloudAlbumSection inactive = Subcover("school");
        inactive.Status = "Removed";
        inactive.PageNumbers = [];
        StudioCloudProjectDetail cloud = CloudProject(
            pending: true,
            requiredVersion: 7,
            revisionVersion: 7,
            groups: [Group("school", "School", 1)],
            tombstones: [],
            manifest:
            [
                SourceSlice("school"),
                inactive,
            ]);

        StudioCanonicalAlbumRebuildResolution resolution =
            StudioCanonicalAlbumRebuildPolicy.Resolve(project, cloud);

        Assert.Equal(
            ["generated:building-sub-cover:studio-building:school"],
            resolution.PendingComponentCodes);
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
        IReadOnlyList<string> tombstones,
        IReadOnlyList<StudioCloudAlbumSection>? manifest = null)
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
                            SectionManifest = manifest?.ToList() ?? [],
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

    private static StudioCloudAlbumSection SourceSlice(
        string buildingGroupId) => new()
    {
        Code = StudioAlbumComponentIdentity.SourceSliceCode(
            "architect@example.com",
            "autocad-source",
            $"studio-building:{buildingGroupId}",
            "floor-plans"),
        Label = buildingGroupId,
        Order = 10,
        PageNumbers = [1],
        Status = "Available",
        OwnerEmail = "architect@example.com",
        SourceKey = "autocad-source",
        ComponentKind = StudioAlbumComponentIdentity.SourceComponentKind,
    };

    private static StudioCloudAlbumSection Subcover(
        string buildingGroupId) => new()
    {
        Code =
            $"generated:building-sub-cover:studio-building:{buildingGroupId}",
        Label = buildingGroupId,
        Order = 10,
        PageNumbers = [1],
        Status = "Available",
        ComponentKind = StudioAlbumComponentIdentity.GeneratedComponentKind,
    };
}
