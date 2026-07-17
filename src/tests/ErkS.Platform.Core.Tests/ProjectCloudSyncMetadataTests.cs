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
