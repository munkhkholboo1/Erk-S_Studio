using ErkS.Platform.Core;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

public sealed class CloudSyncPreviewPlannerTests
{
    [Fact]
    public void LegacyPendingCanonicalMetadataIsNeverUploadedByCloudSync()
    {
        ProjectWorkspace project = CloudProject("ProjectAdmin");
        project.Cloud.PendingProjectInformation = new PendingProjectInformationUpdate
        {
            Name = "Updated project",
            QueuedAtUtc = DateTimeOffset.UtcNow,
        };
        project.Foundation.DesignCompany.AssignmentSource = "StudioCloudPending";

        CloudSyncPreviewPlan plan = CloudSyncPreviewPlanner.Build(
            project,
            "admin@example.com",
            "WORKSTATION · abc12345",
            new StudioCloudProjectRefreshResult(false, null));

        Assert.False(plan.AuthorizeProjectInformation);
        Assert.False(plan.AuthorizeCompanyAssignment);
        Assert.DoesNotContain(plan.Uploads, item =>
            item.Code is "project-information" or "company-assignment");
        Assert.Contains(plan.Blocked, item => item.Code == "project-information");
        Assert.Contains(plan.Blocked, item => item.Code == "company-assignment");
    }

    [Fact]
    public void NonAdminCanonicalMetadataRemainsBlockedAndPending()
    {
        ProjectWorkspace project = CloudProject("Architect");
        project.Cloud.PendingProjectInformation = new PendingProjectInformationUpdate
        {
            Name = "Unauthorized local edit",
            QueuedAtUtc = DateTimeOffset.UtcNow,
        };
        project.Cloud.CanonicalTitleBlockPending = true;

        CloudSyncPreviewPlan plan = CloudSyncPreviewPlanner.Build(
            project,
            "architect@example.com",
            "LAPTOP · def67890",
            new StudioCloudProjectRefreshResult(false, null));

        Assert.False(plan.AuthorizeProjectInformation);
        Assert.False(plan.AuthorizeCanonicalTitleBlock);
        Assert.DoesNotContain(plan.Uploads, item =>
            item.Code is "project-information" or "canonical-title-block");
        Assert.Contains(plan.Blocked, item => item.Code == "project-information");
        Assert.Contains(plan.Blocked, item => item.Code == "canonical-title-block");
        Assert.True(plan.HasBlockedPendingChanges);
    }

    [Fact]
    public void PendingProjectInformationBlocksCanonicalTitleBlockPublication()
    {
        ProjectWorkspace project = CloudProject("ProjectAdmin");
        project.Cloud.PendingProjectInformation = new PendingProjectInformationUpdate
        {
            Name = "Unaccepted local draft",
            BaseConcurrencyToken = "server-token-before-edit",
            QueuedAtUtc = DateTimeOffset.UtcNow,
        };
        project.Cloud.CanonicalTitleBlockPending = true;

        CloudSyncPreviewPlan plan = CloudSyncPreviewPlanner.Build(
            project,
            "admin@example.com",
            "WORKSTATION · canonical-guard",
            new StudioCloudProjectRefreshResult(false, null));

        Assert.False(plan.AuthorizeCanonicalTitleBlock);
        Assert.DoesNotContain(
            plan.Uploads,
            item => item.Code == "canonical-title-block");
        Assert.Contains(
            plan.Blocked,
            item => item.Code == "canonical-title-block");
    }

    [Fact]
    public void ConceptContributorCanPublishSharedBuildingOrderWithoutAdminMetadataRights()
    {
        ProjectWorkspace project = CloudProject("Architect");
        project.Cloud.CurrentUserScopes = ["concept.read", "concept.write"];
        project.Cloud.BuildingCompositionPending = true;
        project.Cloud.CanonicalTitleBlockPending = true;

        CloudSyncPreviewPlan plan = CloudSyncPreviewPlanner.Build(
            project,
            "architect@example.com",
            "LAPTOP · abc12345",
            new StudioCloudProjectRefreshResult(false, null));

        Assert.True(plan.AuthorizeBuildingComposition);
        Assert.Contains(plan.Uploads, item => item.Code == "building-composition");
        Assert.False(plan.AuthorizeCanonicalTitleBlock);
        Assert.Contains(plan.Blocked, item => item.Code == "canonical-title-block");
    }

    [Fact]
    public void ConceptContributorCanPublishKnownBuildingSubCoverWithoutAdminMetadataRights()
    {
        ProjectWorkspace project = CloudProject("Architect");
        project.Cloud.CurrentUserScopes = ["concept.read", "concept.write"];
        project.BuildingGroups =
        [
            new ProjectBuildingGroup
            {
                Id = "school",
                Name = "Сургууль",
                Order = 3,
            },
        ];
        string schoolSubCover =
            ProjectCloudSyncMetadata.BuildingSubCoverComponentCode(
                project.BuildingGroups[0]);
        ProjectCloudSyncMetadata.MarkAlbumComponentsPending(
            project,
            [schoolSubCover, "generated:cover"]);

        CloudSyncPreviewPlan plan = CloudSyncPreviewPlanner.Build(
            project,
            "architect@example.com",
            "LAPTOP · school",
            new StudioCloudProjectRefreshResult(false, null));

        Assert.True(plan.IsComponentAuthorized(schoolSubCover));
        Assert.Contains(plan.Uploads, item => item.Code == schoolSubCover);
        Assert.False(plan.IsComponentAuthorized("generated:cover"));
        Assert.Contains(plan.Blocked, item => item.Code == "generated:cover");
    }

    [Fact]
    public void ConceptContributorCannotPublishSubCoverForUnknownBuilding()
    {
        ProjectWorkspace project = CloudProject("Architect");
        project.Cloud.CurrentUserScopes = ["concept.read", "concept.write"];
        const string unknownSubCover =
            "generated:building-sub-cover:studio-building:not-canonical";
        ProjectCloudSyncMetadata.MarkAlbumComponentsPending(
            project,
            [unknownSubCover]);

        CloudSyncPreviewPlan plan = CloudSyncPreviewPlanner.Build(
            project,
            "architect@example.com",
            "LAPTOP · unknown-building",
            new StudioCloudProjectRefreshResult(false, null));

        Assert.False(plan.IsComponentAuthorized(unknownSubCover));
        Assert.Contains(plan.Blocked, item => item.Code == unknownSubCover);
    }

    [Fact]
    public void ModifiedCloudProjectIsShownAsDownloadWithoutCreatingUpload()
    {
        ProjectWorkspace project = CloudProject("Architect");

        CloudSyncPreviewPlan plan = CloudSyncPreviewPlanner.Build(
            project,
            "architect@example.com",
            "LAPTOP · def67890",
            new StudioCloudProjectRefreshResult(true, null));

        Assert.False(plan.HasUploads);
        Assert.True(plan.HasDownloads);
        Assert.Contains(plan.Downloads, item => item.Code == "remote-project");
    }

    [Fact]
    public void RemoteCanonicalRebuildSignal_AuthorizesNewSubcoverAndTombstoneInSameSync()
    {
        ProjectWorkspace project = CloudProject("ProjectAdmin");
        const string currentRevisionId = "revision-before-composition";
        const string currentBuilding =
            "generated:building-sub-cover:studio-building:building-new";
        const string deletedBuilding =
            "generated:building-sub-cover:studio-building:building-deleted";
        var canonical = new StudioCloudProjectDetail
        {
            BuildingComposition = new StudioCloudBuildingComposition
            {
                Version = 3,
                Groups =
                [
                    new StudioCloudBuildingGroup
                    {
                        Id = "building-new",
                        Name = "New building",
                        Order = 1,
                    },
                ],
            },
            Albums =
            [
                new StudioCloudAlbum
                {
                    AlbumId = "concept-album",
                    AlbumType = ProjectWorkspace.BuildingArchitectureConcept,
                    CurrentRevisionId = currentRevisionId,
                    RequiredBuildingCompositionVersion = 3,
                    CanonicalRebuildPending = true,
                    PendingComponentTombstoneCodes = [deletedBuilding],
                    Revisions =
                    [
                        new StudioCloudAlbumRevision
                        {
                            RevisionId = currentRevisionId,
                            RevisionNumber = 4,
                            BuildingCompositionVersion = 2,
                            PageCount = 2,
                            SectionManifest =
                            [
                                new StudioCloudAlbumSection
                                {
                                    Code =
                                        StudioAlbumComponentIdentity.SourceSliceCode(
                                            "architect@example.com",
                                            "new-building-source",
                                            "studio-building:building-new",
                                            "floor-plans"),
                                    PageNumbers = [1],
                                    Status = "Available",
                                    OwnerEmail = "architect@example.com",
                                    SourceKey = "new-building-source",
                                    ComponentKind =
                                        StudioAlbumComponentIdentity.SourceComponentKind,
                                },
                            ],
                        },
                    ],
                },
            ],
        };

        CloudSyncPreviewPlan plan = CloudSyncPreviewPlanner.Build(
            project,
            "admin@example.com",
            "WORKSTATION · rebuild",
            new StudioCloudProjectRefreshResult(true, canonical));

        Assert.True(plan.IsComponentAuthorized(currentBuilding));
        Assert.True(plan.IsComponentAuthorized(deletedBuilding));
        Assert.Contains(plan.Uploads, item => item.Code == currentBuilding);
        Assert.Contains(plan.Uploads, item => item.Code == deletedBuilding);
        Assert.Contains(
            plan.Downloads,
            item => item.Code == "remote-album-rebuild-pending");
    }

    [Fact]
    public void ClearedServerFieldsCannotBeReintroducedByLegacyPendingMirror()
    {
        ProjectWorkspace project = CloudProject("ProjectAdmin");
        project.Cloud.PendingProjectInformation = new PendingProjectInformationUpdate
        {
            Name = "stale local project name",
            Location = "stale local address",
            BuildingPurpose = "stale local purpose",
            QueuedAtUtc = DateTimeOffset.UtcNow.AddDays(-10),
        };
        var canonical = new StudioCloudProjectDetail
        {
            Project = new StudioCloudProjectSummary
            {
                ProjectId = project.Cloud.ServerProjectId,
                Name = "",
                ConcurrencyToken = "server-token-after-clear",
            },
            ProjectInformation = new StudioCloudProjectInformation
            {
                Name = "",
                Location = "",
                BuildingPurpose = "",
            },
        };

        CloudSyncPreviewPlan plan = CloudSyncPreviewPlanner.Build(
            project,
            "admin@example.com",
            "LAPTOP · stale-mirror",
            new StudioCloudProjectRefreshResult(true, canonical));

        Assert.Contains(plan.Downloads, item => item.Code == "remote-project");
        Assert.DoesNotContain(
            plan.Uploads,
            item => item.Code == "project-information");
        Assert.False(plan.AuthorizeProjectInformation);
    }

    [Fact]
    public void ChangedRemoteSourceIsListedByStreamOwnerAndPageCount()
    {
        ProjectWorkspace project = CloudProject("Architect");
        project.Cloud.SharedSources.Add(new ProjectCloudSourceReference
        {
            SourceId = "source-1",
            SourceKey = "building-a-revit",
            SourceApplication = "Revit",
            SourceDocumentReference = "Building A.rvt",
            ManifestId = "manifest-old",
            ContentHash = "hash-old",
            SheetCount = 3,
            Status = "Registered",
            RegisteredBy = "designer@example.com",
            OwnerEmail = "designer@example.com",
        });
        var remoteDetail = new StudioCloudProjectDetail
        {
            DesignPackages =
            [
                new StudioCloudDesignPackage
                {
                    SourcePackages =
                    [
                        new StudioCloudSourcePackage
                        {
                            SourceId = "source-2",
                            SourceKey = "building-a-revit",
                            SourceApplication = "Revit",
                            SourceDocumentReference = @"D:\Models\Building A.rvt",
                            ManifestId = "manifest-new",
                            ContentHash = "hash-new",
                            SheetCount = 5,
                            Status = "Registered",
                            RegisteredBy = "designer@example.com",
                            CustodianEmail = "designer@example.com",
                        },
                    ],
                },
            ],
        };

        CloudSyncPreviewPlan plan = CloudSyncPreviewPlanner.Build(
            project,
            "architect@example.com",
            "LAPTOP · def67890",
            new StudioCloudProjectRefreshResult(true, remoteDetail));

        CloudSyncChangeItem sourceChange = Assert.Single(
            plan.Downloads,
            item => item.Code.StartsWith("remote-source:", StringComparison.Ordinal));
        Assert.Contains("Source шинэчлэгдсэн", sourceChange.Title);
        Assert.Contains("Building A.rvt", sourceChange.Title);
        Assert.Contains("designer@example.com", sourceChange.Detail);
        Assert.Contains("5 хуудас", sourceChange.Detail);
        Assert.Contains("native файл татахгүй", sourceChange.Detail);
    }

    [Fact]
    public void SameSourceKey_DoesNotAuthorizeAnotherRegistrantsSourceOrComponent()
    {
        const string sourceKey = "shared-key";
        const string ownerA = "architect-a@example.com";
        const string ownerB = "architect-b@example.com";
        ProjectWorkspace project = CloudProject("Architect");
        project.Cloud.SharedSources =
        [
            SharedSource(sourceKey, ownerA),
            SharedSource(sourceKey, ownerB),
        ];
        ProjectDesignSource sourceA = LocalSource("local-a", sourceKey, ownerA, "a");
        ProjectDesignSource sourceB = LocalSource("local-b", sourceKey, ownerB, "b");
        project.Sources = [sourceA, sourceB];
        StudioLocalSourceBindingPolicy.Bind(sourceA, ownerA, "device-a");
        StudioLocalSourceBindingPolicy.Bind(sourceB, ownerB, "device-b");
        string componentA = StudioAlbumComponentIdentity.SourceCode(ownerA, sourceKey);
        string componentB = StudioAlbumComponentIdentity.SourceCode(ownerB, sourceKey);
        ProjectCloudSyncMetadata.MarkAlbumComponentsPending(
            project,
            [componentA, componentB]);

        CloudSyncPreviewPlan plan = CloudSyncPreviewPlanner.Build(
            project,
            ownerA,
            "LAPTOP · owner-scope",
            new StudioCloudProjectRefreshResult(false, null),
            "device-a",
            _ => true);

        ProjectSourceSyncCandidate candidateA =
            ProjectCloudSyncMetadata.PendingSourcePackages(project)
                .Single(candidate => ReferenceEquals(candidate.Source, sourceA));
        ProjectSourceSyncCandidate candidateB =
            ProjectCloudSyncMetadata.PendingSourcePackages(project)
                .Single(candidate => ReferenceEquals(candidate.Source, sourceB));
        Assert.True(plan.IsSourceAuthorized(candidateA));
        Assert.False(plan.IsSourceAuthorized(candidateB));
        Assert.True(plan.IsComponentAuthorized(componentA));
        Assert.False(plan.IsComponentAuthorized(componentB));
        Assert.Contains(plan.Blocked, item => item.Code == componentB);
    }

    [Fact]
    public void StagedSourceRemoval_SupersedesPackageUploadAndAuthorizesTombstone()
    {
        const string owner = "architect@example.com";
        const string device = "device-a";
        const string sourceKey = "retiring-source";
        ProjectWorkspace project = CloudProject("Architect");
        ProjectDesignSource source =
            LocalSource("local-retiring", sourceKey, owner, "a");
        StudioLocalSourceBindingPolicy.Bind(source, owner, device);
        project.Sources = [source];
        ProjectCloudSourceReference registry = SharedSource(sourceKey, owner);
        registry.SourceId = "registry-retiring";
        project.Cloud.SharedSources = [registry];
        StudioSourceRemovalOutbox.Stage(
            project,
            source,
            registry,
            owner,
            device,
            hasVerifiedPayload: true);
        string component =
            StudioAlbumComponentIdentity.SourceCode(owner, sourceKey);

        CloudSyncPreviewPlan plan = CloudSyncPreviewPlanner.Build(
            project,
            owner,
            "LAPTOP · removal",
            new StudioCloudProjectRefreshResult(false, null),
            device,
            _ => true);

        ProjectSourceSyncCandidate candidate =
            Assert.Single(ProjectCloudSyncMetadata.PendingSourcePackages(project));
        Assert.False(plan.IsSourceAuthorized(candidate));
        Assert.True(plan.IsComponentAuthorized(component));
        Assert.Contains(plan.Uploads, item => item.Code == component);
        Assert.DoesNotContain(
            plan.Uploads,
            item => item.Code == "source:" + sourceKey);
    }

    private static ProjectDesignSource LocalSource(
        string id,
        string sourceKey,
        string owner,
        string suffix)
    {
        var source = new ProjectDesignSource
        {
            Id = id,
            Kind = DesignSourceKind.Revit,
        };
        var project = new ProjectWorkspace { Sources = [source] };
        ProjectCloudSyncMetadata.BindToCloudSource(project, source, sourceKey);
        ProjectCloudSyncMetadata.BindCloudOwner(source, owner);
        ProjectCloudSyncMetadata.RecordPackage(
            project,
            source,
            new ErkS.Platform.Contracts.SheetPackageManifest
            {
                PackageId = Guid.Parse(
                    suffix == "a"
                        ? "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"
                        : "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                SchemaVersion = 4,
                ExportedAtUtc = new DateTimeOffset(2026, 7, 29, 0, 0, 0, TimeSpan.Zero),
                Source = new ErkS.Platform.Contracts.SheetPackageSource
                {
                    SourceId = id,
                    Application = ErkS.Platform.Contracts.SheetSourceApplication.Revit,
                    DocumentTitle = id + ".rvt",
                },
                Sheets =
                [
                    new ErkS.Platform.Contracts.SheetPackageEntry
                    {
                        SheetId = "sheet-" + suffix,
                        Sha256 = "sheet-hash-" + suffix,
                    },
                ],
            },
            "content-hash-" + suffix);
        return source;
    }

    private static ProjectCloudSourceReference SharedSource(
        string sourceKey,
        string owner) => new()
    {
        SourceId = Guid.NewGuid().ToString("N"),
        SourceKey = sourceKey,
        SourceApplication = "Revit",
        Status = "Registered",
        RegisteredBy = owner,
        OwnerEmail = owner,
    };

    private static ProjectWorkspace CloudProject(params string[] roles)
    {
        var project = new ProjectWorkspace();
        project.Identity.Code = "TEST-001";
        project.Cloud.Origin = ProjectOrigins.Cloud;
        project.Cloud.ServerProjectId = "server-project-1";
        project.Cloud.PermissionSnapshotAccountEmail =
            roles.Any(role => role.Equals(
                "ProjectAdmin",
                StringComparison.OrdinalIgnoreCase))
                ? "admin@example.com"
                : "architect@example.com";
        project.Cloud.CurrentUserRoles = [.. roles];
        return project;
    }
}
