using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

public sealed class ProjectCanonicalSyncServiceTests
{
    [Fact]
    public void Apply_PreservesPendingProjectCodeWhileKeepingServerCodeInCloudSnapshot()
    {
        ProjectWorkspace project = Project();
        project.Cloud.PendingProjectInformation = new PendingProjectInformationUpdate
        {
            ProjectCode = "EG-2026-001",
            Name = "Pending project",
            BaseConcurrencyToken = "server-token-before-edit",
        };

        ProjectCanonicalSyncService.Apply(project, Snapshot());

        Assert.Equal("EG-2026-001", project.Identity.Code);
        Assert.Equal("ATD-2026-002", project.Cloud.CloudProjectCode);
        Assert.Equal("ATD-2026-002", project.Cloud.ServerSnapshot.ProjectCode);
    }

    [Fact]
    public void WebsiteCanonicalFieldsReplaceStudioMirrorWithoutReplacingDeliverables()
    {
        ProjectWorkspace project = Project();
        ProjectAlbumRecord album = project.PrimaryAlbum;
        ProjectDesignSource source = project.Sources.Single();
        ProjectServerSnapshot snapshot = Snapshot();
        snapshot.Foundation = SnapshotFoundation();

        bool changed = ProjectCanonicalSyncService.Apply(project, snapshot);

        Assert.True(changed);
        Assert.Equal("project-1", project.ProjectId);
        Assert.Equal("ATD-2026-002", project.Identity.Code);
        Assert.Equal("Canonical website name", project.Identity.Name);
        Assert.Equal("Apartment and services", project.Identity.Description);
        Assert.Equal(ProjectClientTypes.Organization, project.Foundation.InitiationBasis.ClientType);
        Assert.Equal("Canonical client", project.Foundation.InitiationBasis.ClientName);
        Assert.Equal("Director", project.Foundation.InitiationBasis.ClientRepresentativePosition);
        Assert.Equal("Client Representative", project.Foundation.InitiationBasis.ClientRepresentativeName);
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
    public void CanonicalSnapshotReplacesMirrorWhilePendingDraftRemainsSeparate()
    {
        ProjectWorkspace project = Project();
        project.Cloud.PendingProjectInformation = new PendingProjectInformationUpdate
        {
            Name = "Pending Studio name",
            ClientName = "Pending client",
            PlanningAuthorityName = "Pending authority",
            Location = "Pending address",
            BuildingPurpose = "Pending purpose",
            BaseConcurrencyToken = "server-token-before-edit",
            QueuedAtUtc = DateTimeOffset.UtcNow,
        };

        ProjectCanonicalSyncService.Apply(project, Snapshot());

        Assert.Equal("Canonical website name", project.Identity.Name);
        Assert.Equal("Canonical client", project.Foundation.InitiationBasis.ClientName);
        Assert.Equal("Ulaanbaatar, Khan-Uul", project.Foundation.InitiationBasis.SiteAddress);
        Assert.Equal("Apartment and services", project.Foundation.InitiationBasis.Summary);
        Assert.Equal("Planning authority", project.Foundation.PlanningTask.IssuingAuthorityName);
        Assert.Equal("Canonical website name", project.Cloud.ServerSnapshot.Name);
        Assert.Equal("Apartment and services", project.Cloud.ServerSnapshot.Information.BuildingPurpose);
        Assert.NotNull(project.Cloud.PendingProjectInformation);
        Assert.Equal(
            "Pending Studio name",
            project.Cloud.PendingProjectInformation.Name);
        Assert.Equal(
            "server-token-before-edit",
            project.Cloud.PendingProjectInformation.BaseConcurrencyToken);

        project.Cloud.PendingProjectInformation = null;
        ProjectCanonicalSyncService.Apply(project, Snapshot());

        Assert.Equal("Canonical website name", project.Identity.Name);
        Assert.Equal("Canonical client", project.Foundation.InitiationBasis.ClientName);
    }

    [Fact]
    public void CanonicalEmptyValuesClearMirrorWithoutDiscardingConflictDraft()
    {
        ProjectWorkspace project = Project();
        project.Cloud.PendingProjectInformation = new PendingProjectInformationUpdate
        {
            Name = "Stale partial name",
            ClientName = "Stale client",
            PlanningAuthorityName = "Stale authority",
            Location = "Stale location",
            BuildingPurpose = "Stale purpose",
            BaseConcurrencyToken = "server-token-before-clear",
            QueuedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
        };
        ProjectServerSnapshot cleared = Snapshot();
        cleared.Name = "";
        cleared.ClientName = "";
        cleared.PlanningAuthorityName = "";
        cleared.Information.Name = "";
        cleared.Information.Location = "";
        cleared.Information.BuildingPurpose = "";
        cleared.SiteAndLand.Addresses = ["Legacy address that must not return"];
        cleared.Foundation = SnapshotFoundation();
        cleared.Foundation.InitiationBasis.ClientName = "";
        cleared.Foundation.InitiationBasis.SiteAddress = "";
        cleared.Foundation.InitiationBasis.Summary = "";
        cleared.Foundation.PlanningTask.IssuingAuthorityName = "";
        cleared.ConcurrencyToken = "server-token-after-clear";

        ProjectCanonicalSyncService.Apply(project, cleared);

        // This used to assert that the mirror was emptied. It is not any more,
        // and the change is deliberate: with no earlier snapshot to compare
        // against there is no way to tell a value the server cleared from one
        // the user has only just typed, and clearing both is how a project's
        // information came to be wiped on every sync. Showing a stale value is
        // corrected by the next sync; destroying someone's work is not.
        //
        // What the test was really guarding is untouched: nothing returns from
        // the legacy fallback, the conflict draft survives, and the token moves
        // on. Once a snapshot has been recorded - which this call does - a
        // genuine clearing on the server is seen as a change and does apply.
        Assert.Equal("Old local name", project.Identity.Name);
        Assert.Equal("Old client", project.Foundation.InitiationBasis.ClientName);
        Assert.Equal("Old address", project.Foundation.InitiationBasis.SiteAddress);
        Assert.DoesNotContain(
            "Legacy address that must not return",
            project.Foundation.InitiationBasis.SiteAddress);
        Assert.Equal(
            "server-token-after-clear",
            project.Cloud.ServerSnapshot.ConcurrencyToken);
        Assert.Equal(
            "Stale partial name",
            project.Cloud.PendingProjectInformation!.Name);
        Assert.Equal(
            "server-token-before-clear",
            project.Cloud.PendingProjectInformation.BaseConcurrencyToken);
    }

    [Fact]
    public void EmptyLegacyPendingRecordDoesNotEraseCanonicalProjectInformation()
    {
        ProjectWorkspace project = Project();
        project.Cloud.PendingProjectInformation = new PendingProjectInformationUpdate
        {
            Foundation = new ProjectServerFoundationUpdate
            {
                IsAvailable = true,
                ClientType = ProjectClientTypes.Citizen,
            },
            QueuedAtUtc = DateTimeOffset.UtcNow.AddDays(-1),
        };
        ProjectServerSnapshot snapshot = Snapshot();
        snapshot.Foundation = SnapshotFoundation();

        bool changed = ProjectCanonicalSyncService.Apply(project, snapshot);

        Assert.True(changed);
        Assert.Null(project.Cloud.PendingProjectInformation);
        Assert.Equal("Canonical website name", project.Identity.Name);
        Assert.Equal("Ulaanbaatar, Khan-Uul", project.Foundation.InitiationBasis.SiteAddress);
        Assert.Equal(ProjectClientTypes.Organization, project.Foundation.InitiationBasis.ClientType);
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
    public void SharedSurfaceAndFoundationReplaceCanonicalMirrorFields()
    {
        ProjectWorkspace project = Project();
        ProjectServerSnapshot snapshot = Snapshot();
        snapshot.Surface = new ProjectServerSurface
        {
            SchemaVersion = "1.0",
            ProductName = "Erk-S Studio",
            Sections =
            [
                new ProjectServerSurfaceSection { Id = "archive", Label = "Архив", Order = 60 },
                new ProjectServerSurfaceSection { Id = "overview", Label = "Ерөнхий", Order = 10 },
            ],
        };
        snapshot.Foundation = new ProjectServerFoundation
        {
            IsAvailable = true,
            Version = 7,
            InitiationBasis = new ProjectServerInitiationBasis
            {
                SourceType = "ATDRequest",
                RequestNumber = "REQ-42",
                ClientType = ProjectClientTypes.GovernmentAuthority,
                ClientName = "Canonical client",
                ClientEmail = "client@example.test",
                ClientRepresentativePosition = "Department head",
                ClientRepresentativeName = "Authority Representative",
                ClientLogoUrl = "/api/cloud-era/v1/projects/project-1/foundation/client-logo",
                SiteAddress = "Canonical site",
                LandReference = "parcel-42",
                SourceOrganizationName = "Planning authority",
                Summary = "Canonical basis",
            },
            PlanningTask = new ProjectServerPlanningTask
            {
                AtdNumber = "ATD-42",
                IssuingAuthorityName = "Planning authority",
                Status = "Issued",
                Summary = "Canonical ATD",
                Requirements = ["Requirement A"],
            },
        };

        ProjectCanonicalSyncService.Apply(project, snapshot);

        Assert.Equal("REQ-42", project.Foundation.InitiationBasis.RequestNumber);
        Assert.Equal(ProjectClientTypes.GovernmentAuthority, project.Foundation.InitiationBasis.ClientType);
        Assert.Equal("client@example.test", project.Foundation.InitiationBasis.ClientEmail);
        Assert.Equal("Department head", project.Foundation.InitiationBasis.ClientRepresentativePosition);
        Assert.Equal("Authority Representative", project.Foundation.InitiationBasis.ClientRepresentativeName);
        Assert.Equal("ATD-42", project.Foundation.PlanningTask.AtdNumber);
        Assert.Equal(["Requirement A"], project.Foundation.PlanningTask.Requirements);
        Assert.Equal(7, project.Foundation.Version);
        Assert.Equal(["overview", "archive"], project.Cloud.ServerSnapshot.Surface.Sections.Select(item => item.Id));
    }

    [Fact]
    public void LegacyPendingRecordDoesNotEraseNewFoundationDetails()
    {
        ProjectWorkspace project = Project();
        project.Foundation.InitiationBasis.RequestNumber = "LOCAL-REQ";
        project.Foundation.PlanningTask.AtdNumber = "LOCAL-ATD";
        project.Cloud.PendingProjectInformation = new PendingProjectInformationUpdate
        {
            Name = "Pending name",
            ClientName = "Pending client",
            Location = "Pending location",
            BuildingPurpose = "Pending purpose",
            Foundation = new ProjectServerFoundationUpdate { IsAvailable = false },
        };

        ProjectCanonicalSyncService.Apply(project, Snapshot());

        Assert.Equal("LOCAL-REQ", project.Foundation.InitiationBasis.RequestNumber);
        Assert.Equal("LOCAL-ATD", project.Foundation.PlanningTask.AtdNumber);
    }

    [Fact]
    public void CanonicalEmptyFoundationValuesClearStaleLocalMirrorValues()
    {
        ProjectWorkspace project = Project();

        // These are stale *mirror* values, so they have to arrive the way a
        // mirror's values do - from an earlier snapshot. Setting them straight
        // onto the project would make them indistinguishable from something the
        // user had just typed, and a sync that cannot tell those apart is what
        // wiped a real project's information.
        ProjectServerSnapshot before = Snapshot();
        before.Foundation = SnapshotFoundation();
        before.Foundation.InitiationBasis.RequestNumber = "STALE-REQ";
        before.Foundation.InitiationBasis.ClientRepresentativePosition = "Stale position";
        before.Foundation.InitiationBasis.ClientRepresentativeName = "Stale representative";
        before.Foundation.PlanningTask.AtdNumber = "STALE-ATD";
        before.Foundation.PlanningTask.Summary = "Stale ATD summary";
        before.Foundation.PlanningTask.Requirements = ["Stale requirement"];
        ProjectCanonicalSyncService.Apply(project, before);

        ProjectServerSnapshot snapshot = Snapshot();
        snapshot.Foundation = SnapshotFoundation();
        snapshot.Foundation.InitiationBasis.RequestNumber = string.Empty;
        snapshot.Foundation.InitiationBasis.ClientRepresentativePosition = string.Empty;
        snapshot.Foundation.InitiationBasis.ClientRepresentativeName = string.Empty;
        snapshot.Foundation.InitiationBasis.ClientLogoUrl = string.Empty;
        snapshot.Foundation.PlanningTask.AtdNumber = string.Empty;
        snapshot.Foundation.PlanningTask.Summary = string.Empty;
        snapshot.Foundation.PlanningTask.Requirements = [];

        bool changed = ProjectCanonicalSyncService.Apply(project, snapshot);

        Assert.True(changed);
        Assert.Empty(project.Foundation.InitiationBasis.RequestNumber);
        Assert.Empty(project.Foundation.InitiationBasis.ClientRepresentativePosition);
        Assert.Empty(project.Foundation.InitiationBasis.ClientRepresentativeName);
        Assert.Empty(project.Foundation.PlanningTask.AtdNumber);
        Assert.Empty(project.Foundation.PlanningTask.Summary);
        Assert.Empty(project.Foundation.PlanningTask.Requirements);
    }

    [Fact]
    public void CanonicalSnapshotRoundTripsWithLocalMirror()
    {
        string root = Path.Combine(Path.GetTempPath(), "erks-canonical-sync-tests", Guid.NewGuid().ToString("N"));
        string projectPath = Path.Combine(root, ProjectWorkspace.DefaultFileName);
        ProjectWorkspace project = Project();
        ProjectServerSnapshot snapshot = Snapshot();
        snapshot.Foundation = SnapshotFoundation();
        ProjectCanonicalSyncService.Apply(project, snapshot);

        try
        {
            ProjectWorkspaceStore.Save(project, projectPath);
            ProjectWorkspace loaded = ProjectWorkspaceStore.Load(projectPath);

            Assert.Equal("server-token-2", loaded.Cloud.ServerSnapshot.ConcurrencyToken);
            Assert.Equal("Apartment and services", loaded.Cloud.ServerSnapshot.Information.BuildingPurpose);
            Assert.Equal("/api/cloud-era/v1/projects/project-1/foundation/client-logo",
                loaded.Cloud.ServerSnapshot.Foundation.InitiationBasis.ClientLogoUrl);
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

    [Fact]
    public void PendingDraftBaseConcurrencyTokenRoundTripsWithoutRebase()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "erks-pending-project-info-tests",
            Guid.NewGuid().ToString("N"));
        string projectPath = Path.Combine(root, ProjectWorkspace.DefaultFileName);
        ProjectWorkspace project = Project();
        project.Cloud.PendingProjectInformation = new PendingProjectInformationUpdate
        {
            Name = "Offline draft",
            BaseConcurrencyToken = "server-token-at-edit",
            QueuedAtUtc = new DateTimeOffset(2026, 7, 29, 3, 0, 0, TimeSpan.Zero),
        };

        try
        {
            ProjectWorkspaceStore.Save(project, projectPath);
            ProjectWorkspace loaded = ProjectWorkspaceStore.Load(projectPath);

            PendingProjectInformationUpdate pending =
                Assert.IsType<PendingProjectInformationUpdate>(
                    loaded.Cloud.PendingProjectInformation);
            Assert.Equal("Offline draft", pending.Name);
            Assert.Equal("server-token-at-edit", pending.BaseConcurrencyToken);
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

    private static ProjectServerFoundation SnapshotFoundation() => new()
    {
        IsAvailable = true,
        Version = 2,
        InitiationBasis = new ProjectServerInitiationBasis
        {
            SourceType = "ATDRequest",
            RequestNumber = "REQ-BASE",
            ClientType = ProjectClientTypes.Organization,
            ClientName = "Canonical client",
            ClientEmail = "client@example.test",
            ClientRepresentativePosition = "Director",
            ClientRepresentativeName = "Client Representative",
            ClientLogoUrl = "/api/cloud-era/v1/projects/project-1/foundation/client-logo",
            SiteAddress = "Ulaanbaatar, Khan-Uul",
            LandReference = "parcel-1, parcel-2",
            SourceOrganizationName = "Planning authority",
            Summary = "Apartment and services",
        },
        PlanningTask = new ProjectServerPlanningTask
        {
            IssuingAuthorityName = "Planning authority",
        },
    };
}
