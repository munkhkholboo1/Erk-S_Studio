using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

public sealed class ProjectCanonicalSyncServiceTests
{
    [Fact]
    public void WebsiteCanonicalFieldsReplaceStudioMirrorWithoutReplacingDeliverables()
    {
        ProjectWorkspace project = Project();
        ProjectAlbumRecord album = project.PrimaryAlbum;
        ProjectDesignSource source = project.Sources.Single();

        bool changed = ProjectCanonicalSyncService.Apply(project, Snapshot());

        Assert.True(changed);
        Assert.Equal("project-1", project.ProjectId);
        Assert.Equal("ATD-2026-002", project.Identity.Code);
        Assert.Equal("Canonical website name", project.Identity.Name);
        Assert.Equal("Apartment and services", project.Identity.Description);
        Assert.Equal("Canonical client", project.Foundation.InitiationBasis.ClientName);
        Assert.Equal("Ulaanbaatar, Khan-Uul", project.Foundation.InitiationBasis.SiteAddress);
        Assert.Equal("parcel-1, parcel-2", project.Foundation.InitiationBasis.LandReference);
        Assert.Equal("Apartment and services", project.Foundation.InitiationBasis.Summary);
        Assert.Equal("Planning authority", project.Foundation.PlanningTask.IssuingAuthorityName);
        Assert.Equal(2, project.Foundation.Version);
        Assert.Same(album, project.PrimaryAlbum);
        Assert.Same(source, project.Sources.Single());
        Assert.Equal("server-token-2", project.Cloud.ServerSnapshot.ConcurrencyToken);
        Assert.Equal(18, project.Cloud.ServerSnapshot.Information.FloorsAboveGround);
        Assert.Equal(["restriction-1"], project.Cloud.ServerSnapshot.SiteAndLand.RestrictionReferences);
    }

    [Fact]
    public void ReapplyingSameCanonicalSnapshotDoesNotCreateAnotherFoundationVersion()
    {
        ProjectWorkspace project = Project();
        ProjectServerSnapshot snapshot = Snapshot();
        ProjectCanonicalSyncService.Apply(project, snapshot);
        int version = project.Foundation.Version;

        bool changed = ProjectCanonicalSyncService.Apply(project, snapshot);

        Assert.False(changed);
        Assert.Equal(version, project.Foundation.Version);
    }

    [Fact]
    public void PendingStudioInformationIsNotOverwrittenByAnOlderServerSnapshot()
    {
        ProjectWorkspace project = Project();
        project.Cloud.PendingProjectInformation = new PendingProjectInformationUpdate
        {
            Name = "Pending Studio name",
            ClientName = "Pending client",
            PlanningAuthorityName = "Pending authority",
            Location = "Pending address",
            BuildingPurpose = "Pending purpose",
            QueuedAtUtc = DateTimeOffset.UtcNow,
        };

        ProjectCanonicalSyncService.Apply(project, Snapshot());

        Assert.Equal("Pending Studio name", project.Identity.Name);
        Assert.Equal("Pending client", project.Foundation.InitiationBasis.ClientName);
        Assert.Equal("Pending address", project.Foundation.InitiationBasis.SiteAddress);
        Assert.Equal("Pending purpose", project.Foundation.InitiationBasis.Summary);
        Assert.Equal("Pending authority", project.Foundation.PlanningTask.IssuingAuthorityName);
        Assert.Equal("Canonical website name", project.Cloud.ServerSnapshot.Name);
        Assert.Equal("Apartment and services", project.Cloud.ServerSnapshot.Information.BuildingPurpose);
        Assert.NotNull(project.Cloud.PendingProjectInformation);

        project.Cloud.PendingProjectInformation = null;
        ProjectCanonicalSyncService.Apply(project, Snapshot());

        Assert.Equal("Canonical website name", project.Identity.Name);
        Assert.Equal("Canonical client", project.Foundation.InitiationBasis.ClientName);
    }

    [Fact]
    public void LinkedMirrorCannotBeReboundToAnotherServerProject()
    {
        ProjectWorkspace project = Project();
        ProjectServerSnapshot snapshot = Snapshot();
        snapshot.ProjectId = "project-2";

        InvalidDataException error = Assert.Throws<InvalidDataException>(() =>
            ProjectCanonicalSyncService.Apply(project, snapshot));

        Assert.Contains("project-1", error.Message, StringComparison.Ordinal);
        Assert.Equal("project-1", project.Cloud.ServerProjectId);
    }

    [Fact]
    public void CanonicalSnapshotRoundTripsWithLocalMirror()
    {
        string root = Path.Combine(Path.GetTempPath(), "erks-canonical-sync-tests", Guid.NewGuid().ToString("N"));
        string projectPath = Path.Combine(root, ProjectWorkspace.DefaultFileName);
        ProjectWorkspace project = Project();
        ProjectCanonicalSyncService.Apply(project, Snapshot());

        try
        {
            ProjectWorkspaceStore.Save(project, projectPath);
            ProjectWorkspace loaded = ProjectWorkspaceStore.Load(projectPath);

            Assert.Equal("server-token-2", loaded.Cloud.ServerSnapshot.ConcurrencyToken);
            Assert.Equal("Apartment and services", loaded.Cloud.ServerSnapshot.Information.BuildingPurpose);
            Assert.Equal(["parcel-1", "parcel-2"], loaded.Cloud.ServerSnapshot.SiteAndLand.ParcelNumbers);
            Assert.Equal("project-1", loaded.Cloud.ServerProjectId);
            Assert.Equal("project-1", loaded.ProjectId);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static ProjectWorkspace Project() => new()
    {
        ProjectId = "project-1",
        Identity = new ProjectIdentity
        {
            Code = "OLD-001",
            Name = "Old local name",
            Description = "Old purpose",
        },
        Cloud = new ProjectCloudLink
        {
            Origin = ProjectOrigins.Cloud,
            ServerProjectId = "project-1",
            SyncStatus = ProjectSyncStatuses.Linked,
        },
        Foundation = new ProjectFoundation
        {
            Version = 1,
            InitiationBasis = new ProjectInitiationBasis
            {
                ClientName = "Old client",
                SiteAddress = "Old address",
                LandReference = "old-parcel",
                Summary = "Old purpose",
            },
            PlanningTask = new PlanningTaskInformation
            {
                IssuingAuthorityName = "Old authority",
            },
        },
        Sources = [new ProjectDesignSource { Id = "source-1", Kind = DesignSourceKind.Revit }],
        Deliverables = new ProjectDeliverables
        {
            Albums = [new ProjectAlbumRecord { Id = "album-1", IsPrimary = true }],
        },
    };

    private static ProjectServerSnapshot Snapshot() => new()
    {
        ProjectId = "project-1",
        ProjectCode = "ATD-2026-002",
        Name = "Canonical website name",
        Status = "ProjectCreated",
        CurrentStage = "ConceptDesign",
        ClientName = "Canonical client",
        PlanningAuthorityName = "Planning authority",
        DesignOrganizationName = "Design company",
        UpdatedAtUtc = new DateTimeOffset(2026, 7, 18, 4, 0, 0, TimeSpan.Zero),
        ConcurrencyToken = "server-token-2",
        Information = new ProjectServerInformation
        {
            ProjectId = "project-1",
            ProjectCode = "ATD-2026-002",
            Name = "Canonical website name",
            Location = "Ulaanbaatar, Khan-Uul",
            BuildingPurpose = "Apartment and services",
            Capacity = 120,
            CapacityUnit = "households",
            FootprintSquareMeters = 860,
            GrossFloorAreaSquareMeters = 12400,
            HeightMeters = 54,
            FloorsAboveGround = 18,
            FloorsBelowGround = 2,
        },
        SiteAndLand = new ProjectServerSiteAndLand
        {
            ParcelNumbers = ["parcel-1", "parcel-2"],
            Addresses = ["Ulaanbaatar, Khan-Uul"],
            RestrictionReferences = ["restriction-1"],
        },
    };
}
