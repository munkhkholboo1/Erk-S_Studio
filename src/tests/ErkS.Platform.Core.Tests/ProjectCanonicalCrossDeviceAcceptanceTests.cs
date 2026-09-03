using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

public sealed class ProjectCanonicalCrossDeviceAcceptanceTests
{
    [Fact]
    public void CopyingAWholeProjectToAnotherMachineBringsTheInboxWithIt()
    {
        // Copying the project folder to another computer used to leave the
        // inbox naming the machine it was registered on: the watcher created
        // that old path and waited there while the deliveries carried along sat
        // unread inside the copy, with no error to show for it.
        string root = Path.Combine(
            Path.GetTempPath(),
            "erks-project-portability-tests",
            Guid.NewGuid().ToString("N"));
        string originalFolder = Path.Combine(root, "machine-a", "ATD-001");
        string copiedFolder = Path.Combine(root, "machine-b", "ATD-001");
        string originalInbox = Path.Combine(originalFolder, "sources", "AutoCAD", "deliveries");
        Directory.CreateDirectory(originalFolder);
        Directory.CreateDirectory(copiedFolder);

        try
        {
            var project = new ProjectWorkspace();
            project.Sources.Add(new ProjectDesignSource
            {
                Name = "AutoCAD",
                Kind = DesignSourceKind.AutoCad,
                NativeDocumentPath = Path.Combine(originalFolder, "drawing.dwg"),
                InboxFolder = originalInbox,
            });
            string originalPath = Path.Combine(originalFolder, ProjectWorkspace.DefaultFileName);
            ProjectWorkspaceStore.Save(project, originalPath);

            // The copy: same bytes, new folder — exactly what a USB stick does.
            string copiedPath = Path.Combine(copiedFolder, ProjectWorkspace.DefaultFileName);
            File.Copy(originalPath, copiedPath);

            ProjectWorkspace copied = ProjectWorkspaceStore.Load(copiedPath);
            Assert.True(ProjectWorkspaceStore.RelocateSourceInboxesInsideProject(copied, copiedPath));
            ProjectWorkspaceStore.Save(copied, copiedPath);

            // Persisted, not only in memory: the exporting plugin reads this
            // same file to decide where to write, so a memory-only fix would
            // leave Studio watching one folder and AutoCAD writing to another.
            ProjectWorkspace reopened = ProjectWorkspaceStore.Load(copiedPath);
            ProjectDesignSource source = Assert.Single(reopened.Sources);
            Assert.True(ProjectWorkspacePaths.IsInside(copiedFolder, source.InboxFolder));
            Assert.Equal(originalInbox, source.Metadata["legacyExternalInbox"]);

            // The original is untouched by its own copy being opened.
            ProjectDesignSource stillHome = Assert.Single(
                ProjectWorkspaceStore.Load(originalPath).Sources);
            Assert.Equal(originalInbox, stillHome.InboxFolder);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void AMovedDrawingIsFoundAgainWhenExactlyOneCandidateExists()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "erks-relink-tests",
            Guid.NewGuid().ToString("N"));
        string folder = Path.Combine(root, "ATD-003");
        Directory.CreateDirectory(Path.Combine(folder, "drawings"));
        string moved = Path.Combine(folder, "drawings", "tower.dwg");
        File.WriteAllText(moved, "dwg");

        try
        {
            const string gonePath = @"C:/gone/machine-a/tower.dwg";
            var project = new ProjectWorkspace();
            project.Sources.Add(new ProjectDesignSource
            {
                Name = "AutoCAD",
                Kind = DesignSourceKind.AutoCad,
                NativeDocumentPath = gonePath,
            });
            string path = Path.Combine(folder, ProjectWorkspace.DefaultFileName);
            ProjectWorkspaceStore.Save(project, path);
            ProjectWorkspace loaded = ProjectWorkspaceStore.Load(path);

            Assert.True(ProjectWorkspaceStore.RelinkNativeDocumentsInsideProject(loaded, path));
            ProjectDesignSource source = Assert.Single(loaded.Sources);
            Assert.Equal(Path.GetFullPath(moved), source.NativeDocumentPath);
            // The old value is kept: it is the only clue to where it used to live.
            Assert.Equal(gonePath, source.Metadata["previousNativeDocumentPath"]);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TwoDrawingsWithTheSameNameAreNotGuessedBetween()
    {
        // Ambiguity must not be resolved silently: binding the project to the
        // wrong drawing is worse than leaving it honestly disconnected.
        string root = Path.Combine(
            Path.GetTempPath(),
            "erks-relink-tests",
            Guid.NewGuid().ToString("N"));
        string folder = Path.Combine(root, "ATD-004");
        Directory.CreateDirectory(Path.Combine(folder, "a"));
        Directory.CreateDirectory(Path.Combine(folder, "b"));
        File.WriteAllText(Path.Combine(folder, "a", "tower.dwg"), "dwg");
        File.WriteAllText(Path.Combine(folder, "b", "tower.dwg"), "dwg");

        try
        {
            var project = new ProjectWorkspace();
            project.Sources.Add(new ProjectDesignSource
            {
                Name = "AutoCAD",
                Kind = DesignSourceKind.AutoCad,
                NativeDocumentPath = @"C:/gone/tower.dwg",
            });
            string path = Path.Combine(folder, ProjectWorkspace.DefaultFileName);
            ProjectWorkspaceStore.Save(project, path);
            ProjectWorkspace loaded = ProjectWorkspaceStore.Load(path);

            Assert.False(ProjectWorkspaceStore.RelinkNativeDocumentsInsideProject(loaded, path));
            Assert.Equal(@"C:/gone/tower.dwg", Assert.Single(loaded.Sources).NativeDocumentPath);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void AProjectSittingWhereItBelongsIsNotRewritten()
    {
        // The other direction: without this the relocation could fire on every
        // project and the test above would still pass.
        string root = Path.Combine(
            Path.GetTempPath(),
            "erks-project-portability-tests",
            Guid.NewGuid().ToString("N"));
        string folder = Path.Combine(root, "ATD-002");
        Directory.CreateDirectory(folder);

        try
        {
            var project = new ProjectWorkspace();
            project.Sources.Add(new ProjectDesignSource
            {
                Name = "AutoCAD",
                Kind = DesignSourceKind.AutoCad,
                InboxFolder = Path.Combine(folder, "sources", "AutoCAD", "deliveries"),
            });
            string path = Path.Combine(folder, ProjectWorkspace.DefaultFileName);
            ProjectWorkspaceStore.Save(project, path);

            ProjectWorkspace loaded = ProjectWorkspaceStore.Load(path);
            Assert.False(ProjectWorkspaceStore.RelocateSourceInboxesInsideProject(loaded, path));
            Assert.DoesNotContain("legacyExternalInbox", Assert.Single(loaded.Sources).Metadata.Keys);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

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
