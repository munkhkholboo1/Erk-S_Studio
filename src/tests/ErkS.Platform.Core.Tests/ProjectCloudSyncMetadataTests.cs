using ErkS.Platform.Contracts;
using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

public sealed class ProjectCloudSyncMetadataTests
{
    [Fact]
    public void ExportPackageStaysPendingUntilManualSyncAcknowledgesIt()
    {
        ProjectWorkspace project = Project();
        ProjectDesignSource source = project.Sources.Single();
        var manifest = new SheetPackageManifest
        {
            SchemaVersion = 4,
            PackageId = Guid.Parse("d321526b-e797-4520-aa74-c5742b267eb8"),
            ExportedAtUtc = new DateTimeOffset(2026, 7, 17, 5, 0, 0, TimeSpan.Zero),
            WorkPackageId = "architecture",
            Source = new SheetPackageSource
            {
                SourceId = source.Id,
                Application = SheetSourceApplication.Revit,
                DocumentTitle = "Concept model",
                DocumentPath = @"C:\private\building.rvt",
            },
            Sheets = [new SheetPackageEntry { SheetId = "A-01", Sha256 = "sheet-hash" }],
        };

        ProjectCloudSyncMetadata.RecordPackage(project, source, manifest, "ABC123");

        ProjectSourceSyncCandidate candidate = Assert.Single(ProjectCloudSyncMetadata.PendingSourcePackages(project));
        Assert.Equal(ProjectSyncStatuses.Pending, project.Cloud.SyncStatus);
        Assert.Equal("source-1", candidate.SourceKey);
        Assert.Equal("Revit", candidate.SourceApplication);
        Assert.Equal("Concept model", candidate.SourceDocumentReference);
        Assert.DoesNotContain("private", candidate.SourceDocumentReference, StringComparison.OrdinalIgnoreCase);

        ProjectCloudSyncMetadata.MarkSourceSynced(candidate);

        Assert.Empty(ProjectCloudSyncMetadata.PendingSourcePackages(project));
        Assert.Single(ProjectCloudSyncMetadata.SourcePackages(project));
        Assert.True(ProjectCloudSyncMetadata.HasSourcePackageSnapshot(project));
    }

    [Fact]
    public void SourceLinkWithoutReceivedPackageRemainsAnEmptyTemplate()
    {
        ProjectWorkspace project = Project();

        Assert.Empty(ProjectCloudSyncMetadata.SourcePackages(project));
        Assert.False(ProjectCloudSyncMetadata.HasSourcePackageSnapshot(project));

        ProjectCloudSyncMetadata.MarkSynced(project, "", "", "etag-empty", DateTimeOffset.UtcNow);

        Assert.Equal(ProjectSyncStatuses.Synced, project.Cloud.SyncStatus);
        Assert.Equal("", project.Cloud.LastSyncedAlbumSha256);
        Assert.Equal("", project.Cloud.LastSyncedRevisionId);
    }

    [Fact]
    public void PendingAlbumComponentsPersistUntilTheirMergeIsAcknowledged()
    {
        string root = Path.Combine(Path.GetTempPath(), "erks-component-queue-tests", Guid.NewGuid().ToString("N"));
        string projectPath = Path.Combine(root, ProjectWorkspace.DefaultFileName);
        ProjectWorkspace project = Project();
        try
        {
            ProjectCloudSyncMetadata.MarkAlbumComponentsPending(
                project,
                [
                    ProjectCloudSyncMetadata.ApprovedAtdComponentCode,
                    ProjectCloudSyncMetadata.ApprovedAtdComponentCode,
                    ProjectCloudSyncMetadata.CoverComponentCode,
                ]);
            ProjectWorkspaceStore.Save(project, projectPath);

            ProjectWorkspace loaded = ProjectWorkspaceStore.Load(projectPath);
            Assert.Equal(2, ProjectCloudSyncMetadata.PendingAlbumComponents(loaded).Count);
            Assert.Equal(ProjectSyncStatuses.Pending, loaded.Cloud.SyncStatus);

            ProjectCloudSyncMetadata.MarkAlbumComponentsSynced(
                loaded,
                [ProjectCloudSyncMetadata.ApprovedAtdComponentCode]);

            Assert.Equal(
                [ProjectCloudSyncMetadata.CoverComponentCode],
                ProjectCloudSyncMetadata.PendingAlbumComponents(loaded));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuildingCompositionQueuesCurrentAndPreviouslySharedSubCovers()
    {
        ProjectWorkspace project = Project();
        project.BuildingGroups =
        [
            new ProjectBuildingGroup
            {
                Id = "building-a",
                Name = "Орон сууц A",
                Order = 1,
            },
        ];
        string currentCode =
            ProjectCloudSyncMetadata.BuildingSubCoverComponentCode(project.BuildingGroups[0]);
        string staleCode =
            ProjectCloudSyncMetadata.BuildingSubCoverComponentCodePrefix +
            "studio-building:removed-building";
        project.Cloud.SharedAlbumComponents =
        [
            new ProjectCloudAlbumComponentReference { Code = staleCode },
        ];

        ProjectCloudSyncMetadata.MarkBuildingCompositionPending(project);

        Assert.True(project.Cloud.BuildingCompositionPending);
        Assert.Equal(
            [currentCode, staleCode],
            ProjectCloudSyncMetadata.PendingAlbumComponents(project));
        Assert.Equal(ProjectSyncStatuses.Pending, project.Cloud.SyncStatus);
    }

    [Fact]
    public void LocalSourceCanRebindToCloudSourceWithoutPublishingNativePath()
    {
        ProjectWorkspace project = Project();
        ProjectDesignSource source = project.Sources.Single();
        source.NativeDocumentPath = @"E:\private\handover\building.rvt";
        var manifest = new SheetPackageManifest
        {
            SchemaVersion = 4,
            PackageId = Guid.NewGuid(),
            ExportedAtUtc = DateTimeOffset.UtcNow,
            Source = new SheetPackageSource
            {
                SourceId = source.Id,
                Application = SheetSourceApplication.Revit,
                DocumentTitle = "building.rvt",
                DocumentPath = source.NativeDocumentPath,
            },
            Sheets = [new SheetPackageEntry { SheetId = "A-01", Sha256 = "sheet-hash" }],
        };

        ProjectCloudSyncMetadata.BindToCloudSource(project, source, "cloud-source-42");
        ProjectCloudSyncMetadata.RecordPackage(project, source, manifest, "ABC123");

        ProjectSourceSyncCandidate candidate = Assert.Single(ProjectCloudSyncMetadata.SourcePackages(project));
        Assert.Equal("cloud-source-42", candidate.SourceKey);
        Assert.Equal("building.rvt", candidate.SourceDocumentReference);
        Assert.DoesNotContain("private", candidate.SourceDocumentReference, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("E:\\", candidate.SourceDocumentReference, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuiltAlbumHashControlsPendingAndSyncedState()
    {
        string root = Path.Combine(Path.GetTempPath(), "erks-cloud-sync-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "albums"));
        string projectPath = Path.Combine(root, ProjectWorkspace.DefaultFileName);
        string pdfPath = Path.Combine(root, "albums", "concept.pdf");
        File.WriteAllBytes(pdfPath, [0x25, 0x50, 0x44, 0x46]);
        ProjectWorkspace project = Project();

        try
        {
            ProjectCloudSyncMetadata.RecordBuiltAlbum(project, projectPath, pdfPath, 12, "A3 landscape");
            string hash = project.PrimaryAlbum.LastPdfSha256;

            Assert.Equal(ProjectSyncStatuses.Pending, project.Cloud.SyncStatus);
            Assert.Equal(12, project.PrimaryAlbum.LastPageCount);
            Assert.Equal("albums/concept.pdf", project.PrimaryAlbum.LastPdfPath);

            ProjectCloudSyncMetadata.MarkSynced(project, hash, "revision-1", "etag-1", DateTimeOffset.UtcNow);

            Assert.Equal(ProjectSyncStatuses.Synced, project.Cloud.SyncStatus);
            Assert.Equal(hash, project.Cloud.LastSyncedAlbumSha256);
            Assert.Equal("revision-1", project.Cloud.LastSyncedRevisionId);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuiltAlbum_CreatesLocalDraftRevisionWithPinnedFoundationAndSources()
    {
        string root = Path.Combine(Path.GetTempPath(), "erks-revision-build-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "albums"));
        string projectPath = Path.Combine(root, ProjectWorkspace.DefaultFileName);
        string pdfPath = Path.Combine(root, "albums", "concept.pdf");
        File.WriteAllBytes(pdfPath, [0x25, 0x50, 0x44, 0x46]);
        ProjectWorkspace project = Project();
        project.Foundation.Version = 7;
        project.Foundation.DesignCompany.OrganizationId = "company-snapshot-7";
        Guid packageId = Guid.Parse("45e08692-b0a4-4b82-88dc-e6e844a6bd52");
        ProjectCloudSyncMetadata.RecordPackage(project, project.Sources.Single(), new SheetPackageManifest
        {
            SchemaVersion = 4,
            PackageId = packageId,
            ExportedAtUtc = DateTimeOffset.UtcNow,
            Source = new SheetPackageSource { SourceId = "source-1", Application = SheetSourceApplication.Revit },
            Sheets = [new SheetPackageEntry { SheetId = "sheet-1", Sha256 = "hash" }],
        }, new string('c', 64));
        StudioAlbumDocument album = new();

        try
        {
            ProjectCloudSyncMetadata.RecordBuiltAlbum(
                project,
                album,
                projectPath,
                pdfPath,
                3,
                "A3 landscape",
                "architect@erks.local");

            DeliverableRevisionRecord revision = Assert.Single(album.Revisions);
            Assert.Equal(project.PrimaryAlbum.LastPdfSha256, revision.Sha256);
            Assert.Equal(7, revision.FoundationVersion);
            Assert.Equal("company-snapshot-7", revision.CompanySnapshotId);
            Assert.Equal([packageId.ToString("N")], revision.SourcePackageIds);
            Assert.Equal("architect@erks.local", revision.CreatedBy);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Conflict_DoesNotMarkProjectSyncedAndKeepsPendingLocalEdit()
    {
        ProjectWorkspace project = Project();
        var pending = new PendingProjectInformationUpdate
        {
            Name = "Local edit",
            Location = "Local address",
            QueuedAtUtc = DateTimeOffset.UtcNow,
        };

        ProjectCloudSyncMetadata.MarkConflict(
            project,
            pending,
            "server-token-2",
            "Server project changed.");

        Assert.Equal(ProjectSyncStatuses.Conflict, project.Cloud.SyncStatus);
        Assert.Same(pending, project.Cloud.PendingProjectInformation);
        Assert.Equal("server-token-2", project.Cloud.LastServerConcurrencyToken);
        Assert.Equal("Server project changed.", project.Cloud.LastSyncError);
        Assert.Null(project.Cloud.LastSyncedAtUtc);
    }

    [Fact]
    public void ServerHashMismatch_IsRejectedBeforeSyncAcknowledgement()
    {
        InvalidDataException error = Assert.Throws<InvalidDataException>(() =>
            ProjectCloudSyncMetadata.ValidateAlbumAcknowledgement(
                "expected-hash",
                "server-hash",
                "revision-1"));

        Assert.Contains("hash", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SourceRegistrationMismatch_IsRejectedBeforeSourceIsMarkedSynced()
    {
        InvalidDataException error = Assert.Throws<InvalidDataException>(() =>
            ProjectCloudSyncMetadata.ValidateSourceAcknowledgement(
                "manifest-1",
                "hash-1",
                "manifest-other",
                "hash-1"));

        Assert.Contains("manifest", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CurrentAccountRolesAndScopesRoundTripWithProjectMirror()
    {
        string root = Path.Combine(Path.GetTempPath(), "erks-cloud-role-tests", Guid.NewGuid().ToString("N"));
        string projectPath = Path.Combine(root, ProjectWorkspace.DefaultFileName);
        ProjectWorkspace project = Project();
        project.Cloud.CurrentUserRoles = ["Architect", "Engineer"];
        project.Cloud.CurrentUserScopes = ["concept.write", "album.create"];

        try
        {
            ProjectWorkspaceStore.Save(project, projectPath);
            ProjectWorkspace loaded = ProjectWorkspaceStore.Load(projectPath);

            Assert.Equal(["Architect", "Engineer"], loaded.Cloud.CurrentUserRoles);
            Assert.True(loaded.Cloud.HasScope("CONCEPT.WRITE"));
            Assert.True(loaded.Cloud.HasScope("album.create"));
            Assert.False(loaded.Cloud.HasScope("team.manage"));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CloudCheckAndRefreshAreTrackedSeparatelyFromPushSync()
    {
        ProjectWorkspace project = Project();
        DateTimeOffset checkedAt = new(2026, 7, 20, 4, 0, 0, TimeSpan.Zero);
        DateTimeOffset refreshedAt = checkedAt.AddMinutes(2);

        ProjectCloudSyncMetadata.MarkCloudChecked(project, checkedAt);

        Assert.Equal(checkedAt, project.Cloud.LastCloudCheckedAtUtc);
        Assert.Null(project.Cloud.LastCloudRefreshedAtUtc);
        Assert.Null(project.Cloud.LastSyncedAtUtc);

        ProjectCloudSyncMetadata.MarkCloudRefreshed(project, "server-token-2", refreshedAt);
        ProjectCloudSyncMetadata.RecordReceivedAlbum(
            project,
            "revision-7",
            7,
            "ABC123",
            "albums/cloud/album-r7.pdf");
        project.Cloud.LastReceivedClientLogoKey = "/client-logo?v=client-7";
        project.Cloud.LastReceivedDesignOrganizationLogoKey = "/design-logo?v=design-4";

        Assert.Equal(refreshedAt, project.Cloud.LastCloudCheckedAtUtc);
        Assert.Equal(refreshedAt, project.Cloud.LastCloudRefreshedAtUtc);
        Assert.Equal("server-token-2", project.Cloud.LastServerConcurrencyToken);
        Assert.Equal("revision-7", project.Cloud.LastReceivedAlbumRevisionId);
        Assert.Equal(7, project.Cloud.LastReceivedAlbumRevisionNumber);
        Assert.Equal("abc123", project.Cloud.LastReceivedAlbumSha256);
        Assert.Equal("albums/cloud/album-r7.pdf", project.Cloud.LastReceivedAlbumPdfPath);
        Assert.Equal("/client-logo?v=client-7", project.Cloud.LastReceivedClientLogoKey);
        Assert.Equal("/design-logo?v=design-4", project.Cloud.LastReceivedDesignOrganizationLogoKey);
        Assert.Null(project.Cloud.LastSyncedAtUtc);

        ProjectCloudSyncMetadata.ClearReceivedAlbum(project);

        Assert.Equal("", project.Cloud.LastReceivedAlbumRevisionId);
        Assert.Equal(0, project.Cloud.LastReceivedAlbumRevisionNumber);
        Assert.Equal("", project.Cloud.LastReceivedAlbumSha256);
        Assert.Equal("", project.Cloud.LastReceivedAlbumPdfPath);
    }

    private static ProjectWorkspace Project() => new()
    {
        Cloud = new ProjectCloudLink
        {
            Origin = ProjectOrigins.Cloud,
            ServerProjectId = "project-1",
            SyncStatus = ProjectSyncStatuses.Linked,
        },
        Sources = [new ProjectDesignSource { Id = "source-1", Kind = DesignSourceKind.Revit }],
        Deliverables = new ProjectDeliverables
        {
            Albums = [new ProjectAlbumRecord { IsPrimary = true }],
        },
    };
}
