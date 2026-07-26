using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

public sealed class ProjectCanonicalCrossDeviceAcceptanceTests
{
    [Fact]
    public void TwoDeviceMirrorsConvergeOnCanonicalMetadataWithoutLosingLocalSources()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "erks-cross-device-sync-tests",
            Guid.NewGuid().ToString("N"));
        string deviceAPath = Path.Combine(root, "device-a", ProjectWorkspace.DefaultFileName);
        string deviceBPath = Path.Combine(root, "device-b", ProjectWorkspace.DefaultFileName);
        ProjectWorkspace deviceA = LocalMirror(
            nativePath: @"D:\Design\Building-A.rvt",
            inboxFolder: @"D:\Studio\ATD-001\sources\revit\deliveries",
            lastPdfPath: @"albums\device-a-working.pdf");
        ProjectWorkspace deviceB = LocalMirror(
            nativePath: @"E:\Consultant\Building-A.dwg",
            inboxFolder: @"E:\Studio\ATD-001\sources\autocad\deliveries",
            lastPdfPath: @"albums\device-b-working.pdf");

        try
        {
            ProjectServerSnapshot first = CanonicalSnapshot(
                token: "token-1",
                name: "Initial canonical project",
                clientName: "Initial client",
                representativeName: "Initial representative");
            Assert.True(ProjectCanonicalSyncService.Apply(deviceA, first));
            Assert.True(ProjectCanonicalSyncService.Apply(deviceB, first));
            ProjectWorkspaceStore.Save(deviceA, deviceAPath);
            ProjectWorkspaceStore.Save(deviceB, deviceBPath);

            deviceA = ProjectWorkspaceStore.Load(deviceAPath);
            deviceB = ProjectWorkspaceStore.Load(deviceBPath);
            ProjectServerSnapshot updated = CanonicalSnapshot(
                token: "token-2",
                name: "Updated canonical project",
                clientName: "Updated client organization",
                representativeName: "Updated representative");

            Assert.True(ProjectCanonicalSyncService.Apply(deviceA, updated));
            Assert.True(ProjectCanonicalSyncService.Apply(deviceB, updated));
            ProjectWorkspaceStore.Save(deviceA, deviceAPath);
            ProjectWorkspaceStore.Save(deviceB, deviceBPath);
            deviceA = ProjectWorkspaceStore.Load(deviceAPath);
            deviceB = ProjectWorkspaceStore.Load(deviceBPath);

            AssertCanonicalMetadataMatches(deviceA, deviceB);
            Assert.Equal("Updated canonical project", deviceA.Identity.Name);
            Assert.Equal(
                ProjectClientTypes.Organization,
                deviceA.Foundation.InitiationBasis.ClientType);
            Assert.Equal(
                "Updated client organization",
                deviceA.Foundation.InitiationBasis.ClientName);
            Assert.Equal(
                "Updated representative",
                deviceA.Foundation.InitiationBasis.ClientRepresentativeName);
            Assert.Equal(
                "/api/cloud-era/v1/projects/project-1/foundation/client-logo",
                deviceA.Cloud.ServerSnapshot.Foundation.InitiationBasis.ClientLogoUrl);
            Assert.Equal("token-2", deviceA.Cloud.ServerSnapshot.ConcurrencyToken);

            Assert.Equal(@"D:\Design\Building-A.rvt", deviceA.Sources.Single().NativeDocumentPath);
            Assert.Equal(
                @"D:\Studio\ATD-001\sources\revit\deliveries",
                deviceA.Sources.Single().InboxFolder);
            Assert.Equal(@"albums\device-a-working.pdf", deviceA.PrimaryAlbum.LastPdfPath);
            Assert.Equal(@"E:\Consultant\Building-A.dwg", deviceB.Sources.Single().NativeDocumentPath);
            Assert.Equal(
                @"E:\Studio\ATD-001\sources\autocad\deliveries",
                deviceB.Sources.Single().InboxFolder);
            Assert.Equal(@"albums\device-b-working.pdf", deviceB.PrimaryAlbum.LastPdfPath);

            Assert.False(ProjectCanonicalSyncService.Apply(deviceA, updated));
            Assert.False(ProjectCanonicalSyncService.Apply(deviceB, updated));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static ProjectWorkspace LocalMirror(
        string nativePath,
        string inboxFolder,
        string lastPdfPath) => new()
    {
        ProjectId = "project-1",
        Identity = new ProjectIdentity
        {
            Code = "LOCAL-001",
            Name = "Local mirror",
        },
        Cloud = new ProjectCloudLink
        {
            Origin = ProjectOrigins.Cloud,
            ServerProjectId = "project-1",
            SyncStatus = ProjectSyncStatuses.Linked,
        },
        Foundation = new ProjectFoundation(),
        Sources =
        [
            new ProjectDesignSource
            {
                Id = "local-source",
                Name = Path.GetFileNameWithoutExtension(nativePath),
                Kind = nativePath.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase)
                    ? DesignSourceKind.Revit
                    : DesignSourceKind.AutoCad,
                NativeDocumentPath = nativePath,
                InboxFolder = inboxFolder,
            },
        ],
        Deliverables = new ProjectDeliverables
        {
            Albums =
            [
                new ProjectAlbumRecord
                {
                    Id = "primary-album",
                    IsPrimary = true,
                    LastPdfPath = lastPdfPath,
                },
            ],
        },
    };

    private static ProjectServerSnapshot CanonicalSnapshot(
        string token,
        string name,
        string clientName,
        string representativeName) => new()
    {
        ProjectId = "project-1",
        ProjectCode = "ATD-2026-001",
        Name = name,
        Status = "ConceptDesign",
        CurrentStage = ProjectWorkspace.ConceptDesignStage,
        ClientName = clientName,
        PlanningAuthorityName = "Planning authority",
        DesignOrganizationName = "Erk-S LLC",
        UpdatedAtUtc = new DateTimeOffset(2026, 7, 27, 4, 0, 0, TimeSpan.Zero),
        ConcurrencyToken = token,
        Information = new ProjectServerInformation
        {
            ProjectId = "project-1",
            ProjectCode = "ATD-2026-001",
            Name = name,
            Location = "Ulaanbaatar",
            BuildingPurpose = "Apartment",
        },
        Foundation = new ProjectServerFoundation
        {
            IsAvailable = true,
            Version = 3,
            InitiationBasis = new ProjectServerInitiationBasis
            {
                SourceType = "ATDRequest",
                RequestNumber = "REQ-001",
                ClientType = ProjectClientTypes.Organization,
                ClientName = clientName,
                ClientEmail = "client@example.test",
                ClientRepresentativePosition = "Director",
                ClientRepresentativeName = representativeName,
                ClientLogoUrl = "/api/cloud-era/v1/projects/project-1/foundation/client-logo",
                SiteAddress = "Ulaanbaatar",
                LandReference = "parcel-001",
                SourceOrganizationName = "Planning authority",
                Summary = "Apartment",
            },
            PlanningTask = new ProjectServerPlanningTask
            {
                AtdNumber = "ATD-001",
                IssuingAuthorityName = "Planning authority",
                Status = "Issued",
            },
        },
        SiteAndLand = new ProjectServerSiteAndLand
        {
            ParcelNumbers = ["parcel-001"],
            Addresses = ["Ulaanbaatar"],
        },
    };

    private static void AssertCanonicalMetadataMatches(
        ProjectWorkspace deviceA,
        ProjectWorkspace deviceB)
    {
        Assert.Equal(deviceA.ProjectId, deviceB.ProjectId);
        Assert.Equal(deviceA.Identity.Code, deviceB.Identity.Code);
        Assert.Equal(deviceA.Identity.Name, deviceB.Identity.Name);
        Assert.Equal(deviceA.Identity.Description, deviceB.Identity.Description);
        Assert.Equal(
            deviceA.Foundation.InitiationBasis.ClientType,
            deviceB.Foundation.InitiationBasis.ClientType);
        Assert.Equal(
            deviceA.Foundation.InitiationBasis.ClientName,
            deviceB.Foundation.InitiationBasis.ClientName);
        Assert.Equal(
            deviceA.Foundation.InitiationBasis.ClientRepresentativePosition,
            deviceB.Foundation.InitiationBasis.ClientRepresentativePosition);
        Assert.Equal(
            deviceA.Foundation.InitiationBasis.ClientRepresentativeName,
            deviceB.Foundation.InitiationBasis.ClientRepresentativeName);
        Assert.Equal(
            deviceA.Foundation.PlanningTask.AtdNumber,
            deviceB.Foundation.PlanningTask.AtdNumber);
        Assert.Equal(
            deviceA.Cloud.ServerSnapshot.ConcurrencyToken,
            deviceB.Cloud.ServerSnapshot.ConcurrencyToken);
    }
}
