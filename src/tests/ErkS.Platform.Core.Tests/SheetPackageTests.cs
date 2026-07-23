using ErkS.Platform.Contracts;
using ErkS.Platform.Core;
using ErkS.Platform.Pdf;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using System.Text.Json.Nodes;

namespace ErkS.Platform.Core.Tests;

public sealed class SheetPackageTests : IDisposable
{
    private readonly string workDirectory =
        Path.Combine(Path.GetTempPath(), "erks-platform-tests", Guid.NewGuid().ToString("N"));

    public SheetPackageTests()
    {
        WindowsFontResolver.Register();
        Directory.CreateDirectory(workDirectory);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(workDirectory, recursive: true);
        }
        catch
        {
        }
    }

    [Fact]
    public void ManifestRoundTrip_VerifiesLossless()
    {
        var manifestPath = WriteSamplePackage(workDirectory, sheetCount: 2);

        var result = SheetPackageReader.Load(manifestPath);

        Assert.True(result.IsLossless, string.Join("; ", result.Issues));
        Assert.NotNull(result.Manifest);
        Assert.Equal(2, result.Manifest!.Sheets.Count);
        Assert.Equal("Description 1", result.Manifest.Sheets[0].SheetDescription);
        Assert.Equal("1:100", result.Manifest.Sheets[0].ScaleText);
        Assert.All(result.Manifest.Sheets, sheet => Assert.NotEmpty(sheet.Sha256));
    }

    [Fact]
    public void TamperedPdf_FailsVerification()
    {
        var manifestPath = WriteSamplePackage(workDirectory, sheetCount: 1);
        var pdfPath = Directory.GetFiles(workDirectory, "*.pdf").Single();
        File.AppendAllText(pdfPath, "tampered");

        var result = SheetPackageReader.Load(manifestPath);

        Assert.False(result.IsLossless);
        Assert.Contains(result.Issues, issue => issue.Contains("hash mismatch"));
    }

    [Fact]
    public void MissingPdf_FailsVerification()
    {
        var manifestPath = WriteSamplePackage(workDirectory, sheetCount: 1);
        File.Delete(Directory.GetFiles(workDirectory, "*.pdf").Single());

        var result = SheetPackageReader.Load(manifestPath);

        Assert.False(result.IsLossless);
        Assert.Contains(result.Issues, issue => issue.Contains("missing"));
    }

    [Fact]
    public void ProjectStore_RoundTrips()
    {
        var project = new AlbumProject
        {
            Name = "Туршилтын төсөл",
            Code = "ERKS-T-01",
            ServerProjectId = "cloud-project-01",
            ServerUrl = "http://127.0.0.1:5055",
            CloudProjectCode = "ERKS-CLOUD-01",
            ClientName = "Захиалагч",
            PlanningAuthorityName = "Хот байгуулалтын хэлтэс",
            DesignOrganizationName = "Эрк-С зураг төслийн компани",
            CloudStatus = "Linked",
            Company = new CompanyProfile { Name = "Эрк-С ХХК", Email = "info@erk-s.mn" },
            SourceFolders = [@"C:\drop"],
        };
        var stage = new ProjectStage
        {
            Code = "ConceptDesign",
            Name = "Загвар зураг",
            Status = "Active",
            AssignedOrganizationName = "Эрк-С зураг төслийн компани",
        };
        project.Stages.Add(stage);
        project.WorkPackages.Add(new ProjectWorkPackage
        {
            StageId = stage.Id,
            Code = "AR",
            Name = "Архитектур",
            Status = "Draft",
            AssignedOrganizationName = "Эрк-С зураг төслийн компани",
        });
        project.Documents.Add(new ProjectDocumentRecord
        {
            StageId = stage.Id,
            Type = "Album",
            Title = "Загвар зургийн альбом",
            Status = "Draft",
            OwnerOrganizationName = "Эрк-С зураг төслийн компани",
        });
        project.Album.Sections.Add(new AlbumSection { Title = "Архитектур", SheetKeys = ["a", "b"] });
        var path = Path.Combine(workDirectory, "test" + AlbumProject.FileExtension);

        AlbumProjectStore.Save(project, path);
        var loaded = AlbumProjectStore.Load(path);

        Assert.Equal(project.Name, loaded.Name);
        Assert.Equal("Эрк-С ХХК", loaded.Company.Name);
        Assert.Equal("cloud-project-01", loaded.ServerProjectId);
        Assert.Equal("ERKS-CLOUD-01", loaded.CloudProjectCode);
        Assert.Equal("Эрк-С зураг төслийн компани", loaded.DesignOrganizationName);
        Assert.Equal("Загвар зураг", loaded.Stages.Single().Name);
        Assert.Equal("Архитектур", loaded.WorkPackages.Single().Name);
        Assert.Equal("Загвар зургийн альбом", loaded.Documents.Single().Title);
        Assert.Equal(["a", "b"], loaded.Album.Sections.Single().SheetKeys);
    }

    [Fact]
    public void LocalProjectCatalog_UnifiesLegacyAndWorkspaceProjects()
    {
        var root = Path.Combine(workDirectory, "Studio Projects");
        var projectFolder = Path.Combine(root, "ATD-SIM-2026-002");
        var path = Path.Combine(projectFolder, "ATD-SIM-2026-002" + AlbumProject.FileExtension);
        var project = new AlbumProject
        {
            Name = "Cloud ERA туршилтын төсөл",
            Code = "ATD-SIM-2026-002",
            ServerProjectId = "6f51ce0b80a145f899b791158fe2a1de",
            CloudProjectCode = "ATD-SIM-2026-002",
            DesignOrganizationName = "Erk-S зураг төслийн компани",
            SourceFolders = [Path.Combine(projectFolder, "incoming-sheets")],
            OutputFolder = Path.Combine(projectFolder, "albums"),
        };
        AlbumProjectStore.Save(project, path);

        var catalog = new LocalProjectCatalog(root);
        var legacyItem = Assert.Single(catalog.ListProjects());
        Assert.True(legacyItem.IsLegacyProject);
        Assert.Equal(path, legacyItem.ProjectPath);
        Assert.Equal("ATD-SIM-2026-002", legacyItem.ProjectCode);
        Assert.Equal("Cloud ERA туршилтын төсөл", legacyItem.DisplayName);
        Assert.Equal("Erk-S зураг төслийн компани", legacyItem.DesignOrganization);
        Assert.Equal(ProjectOrigins.Cloud, legacyItem.Origin);

        var migration = LegacyAlbumProjectImporter.Import(path, persist: true);
        var workspaceItem = Assert.Single(catalog.ListProjects());
        Assert.False(workspaceItem.IsLegacyProject);
        Assert.Equal(migration.ProjectPath, workspaceItem.ProjectPath);
        Assert.Equal("6f51ce0b80a145f899b791158fe2a1de", workspaceItem.ProjectId);
        Assert.Equal("ATD-SIM-2026-002", workspaceItem.ProjectCode);
        Assert.True(File.Exists(path));
        Assert.True(File.Exists(migration.ProjectPath));
        Assert.True(File.Exists(migration.AlbumPath));
        Assert.Equal(project.SourceFolders.Single(), migration.Project.Sources.Single().InboxFolder);
        Assert.Equal("albums", migration.Project.PrimaryAlbum.OutputFolder);
    }

    [Fact]
    public void LocalProjectCatalog_IgnoresRenamedOldMirror()
    {
        string root = Path.Combine(workDirectory, "Studio Projects");
        string backupFolder = Path.Combine(root, "ATD-SIM-2026-002.old");
        string backupPath = Path.Combine(backupFolder, ProjectWorkspace.DefaultFileName);
        ProjectWorkspace project = ProjectWorkspaceStore.Create(
            "ATD-SIM-2026-002",
            "Cloud ERA backup mirror");
        project.Cloud.ServerProjectId = "6f51ce0b80a145f899b791158fe2a1de";
        project.Cloud.Origin = ProjectOrigins.Cloud;
        ProjectWorkspaceStore.Save(project, backupPath);

        var catalog = new LocalProjectCatalog(root);

        Assert.Empty(catalog.ListProjects());
        Assert.True(File.Exists(backupPath));
    }

    [Fact]
    public void ProjectWorkspaceStore_RoundTripsFoundationAndDeliverableIndex()
    {
        var project = ProjectWorkspaceStore.Create("ERKS-P-01", "Барилга архитектурын төсөл");
        project.Cloud = new ProjectCloudLink
        {
            Origin = ProjectOrigins.Cloud,
            ServerProjectId = "server-project-01",
            ServerUrl = "http://127.0.0.1:5055",
            CloudProjectCode = "ERKS-P-01",
            SyncStatus = ProjectSyncStatuses.Linked,
        };
        project.Foundation.InitiationBasis.ClientType = ProjectClientTypes.Organization;
        project.Foundation.InitiationBasis.ClientName = "Захиалагч ХХК";
        project.Foundation.InitiationBasis.ClientOrganizationSnapshot.LogoPath =
            "foundation/documents/client-logo/client.png";
        project.Foundation.PlanningTask.AtdNumber = "АТД-2026-01";
        project.Foundation.PlanningTask.IssuingAuthorityName = "Хот байгуулалтын газар";
        project.Foundation.DesignCompany.OrganizationSnapshot.Name = "Erk-S зураг төслийн компани";
        project.Foundation.DesignCompany.Members.Add(new ProjectMember
        {
            FullName = "Мөнххолбоо Энхбаатар",
            Email = "munkhkholboo@gmail.com",
            Roles = ["Major architect", "Architect"],
        });
        project.Deliverables.Reports.Add(new ProjectReportRecord
        {
            Title = "Загвар зургийн тайлан",
        });
        var path = Path.Combine(workDirectory, ProjectWorkspace.DefaultFileName);

        ProjectWorkspaceStore.Save(project, path);
        var loaded = ProjectWorkspaceStore.Load(path);

        Assert.Equal(ProjectClientTypes.Organization, loaded.Foundation.InitiationBasis.ClientType);
        Assert.Equal("Захиалагч ХХК", loaded.Foundation.InitiationBasis.ClientName);
        Assert.Equal(
            "foundation/documents/client-logo/client.png",
            loaded.Foundation.InitiationBasis.ClientOrganizationSnapshot.LogoPath);
        Assert.Equal("АТД-2026-01", loaded.Foundation.PlanningTask.AtdNumber);
        Assert.Equal("Хот байгуулалтын газар", loaded.Foundation.PlanningTask.IssuingAuthorityName);
        Assert.Equal("Erk-S зураг төслийн компани", loaded.DesignOrganizationName);
        Assert.Equal(["Major architect", "Architect"], loaded.Foundation.DesignCompany.Members.Single().Roles);
        Assert.Equal(ProjectWorkspace.ConceptAlbumRelativePath, loaded.PrimaryAlbum.DocumentPath);
        Assert.Equal("Загвар зургийн тайлан", loaded.Deliverables.Reports.Single().Title);
    }

    [Fact]
    public void ProjectWorkspaceStore_NormalizesLegacyNullCloudSourceEntries()
    {
        ProjectWorkspace project = ProjectWorkspaceStore.Create("LEGACY-CLOUD-NULL", "Legacy cloud mirror");
        string path = Path.Combine(workDirectory, "legacy-cloud-null", ProjectWorkspace.DefaultFileName);
        ProjectWorkspaceStore.Save(project, path);

        JsonObject json = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        JsonObject cloud = json["cloud"]!.AsObject();
        cloud["sharedSources"] = new JsonArray
        {
            null,
            new JsonObject
            {
                ["sourceId"] = "source-1",
                ["sourceKey"] = null,
                ["ownerEmail"] = null,
            },
        };
        cloud["sharedAlbumComponents"] = new JsonArray
        {
            null,
            new JsonObject
            {
                ["code"] = "source:legacy",
                ["componentKind"] = null,
                ["pageNumbers"] = null,
            },
        };
        File.WriteAllText(path, json.ToJsonString());

        ProjectWorkspace loaded = ProjectWorkspaceStore.Load(path);

        ProjectCloudSourceReference source = Assert.Single(loaded.Cloud.SharedSources);
        Assert.Equal("", source.SourceKey);
        Assert.Equal("", source.OwnerEmail);
        ProjectCloudAlbumComponentReference component = Assert.Single(loaded.Cloud.SharedAlbumComponents);
        Assert.Equal("", component.ComponentKind);
        Assert.Empty(component.PageNumbers);
    }

    [Fact]
    public void ProjectWorkspaceStore_CreatesDesignOrganizationProjectWithStageAssignment()
    {
        var request = new ProjectCreationRequest
        {
            Code = "STUDIO-DESIGN-01",
            Name = "Зураг төслийн байгууллагын төсөл",
            Description = "Барилга архитектурын загвар зураг",
            Channel = ProjectCreationChannels.Studio,
            InitiatorType = ProjectInitiatorTypes.DesignOrganization,
            InitiatorOrganizationId = "org-erk-s",
            InitiatorOrganizationName = "Erk-S зураг төслийн компани",
            ClientName = "З.Бат",
            SiteAddress = "Улаанбаатар хот",
        };

        var project = ProjectWorkspaceStore.Create(request);
        var path = Path.Combine(workDirectory, "design", ProjectWorkspace.DefaultFileName);
        ProjectWorkspaceStore.Save(project, path);
        var loaded = ProjectWorkspaceStore.Load(path);

        Assert.Equal(ProjectCreationChannels.Studio, loaded.Creation.Channel);
        Assert.Equal(ProjectInitiatorTypes.DesignOrganization, loaded.Creation.InitiatorType);
        Assert.Equal("Erk-S зураг төслийн компани", loaded.Creation.InitiatorOrganizationName);
        Assert.Equal("org-erk-s", loaded.Foundation.DesignCompany.OrganizationId);
        Assert.Equal("org-erk-s", loaded.Foundation.DesignCompany.OrganizationSnapshot.OrganizationId);
        Assert.Equal(ProjectInitiationSourceTypes.DesignOrganizationCreated, loaded.Foundation.InitiationBasis.SourceType);
        Assert.Equal("Erk-S зураг төслийн компани", loaded.DesignOrganizationName);
        Assert.Equal("StudioSelfCreated", loaded.Foundation.DesignCompany.AssignmentSource);
        Assert.NotNull(loaded.Foundation.DesignCompany.AssignedAtUtc);
        Assert.Equal("З.Бат", loaded.Foundation.InitiationBasis.ClientName);
        Assert.Equal(ProjectWorkspace.ConceptDesignStage, loaded.Identity.StageCode);
    }

    [Fact]
    public void ProjectWorkspaceStore_CreatesGovernmentProjectWithoutSelectingDesignCompany()
    {
        var project = ProjectWorkspaceStore.Create(new ProjectCreationRequest
        {
            Code = "STUDIO-GOV-01",
            Name = "Төрийн байгууллагын үүсгэсэн төсөл",
            InitiatorType = ProjectInitiatorTypes.GovernmentAuthority,
            InitiatorOrganizationName = "Хот байгуулалтын газар",
            ClientName = "З.Бат",
        });

        Assert.Equal(ProjectInitiatorTypes.GovernmentAuthority, project.Creation.InitiatorType);
        Assert.Equal(ProjectInitiationSourceTypes.GovernmentCreated, project.Foundation.InitiationBasis.SourceType);
        Assert.Equal("Хот байгуулалтын газар", project.Foundation.PlanningTask.IssuingAuthorityName);
        Assert.Equal("", project.DesignOrganizationName);
        Assert.Equal("", project.Foundation.DesignCompany.AssignmentSource);
        Assert.Null(project.Foundation.DesignCompany.AssignedAtUtc);
    }

    [Fact]
    public void ProjectWorkspaceStore_RestoresLegacySelfCreatedCompanyIdentityOnLoad()
    {
        var project = ProjectWorkspaceStore.Create(new ProjectCreationRequest
        {
            Code = "STUDIO-DESIGN-LEGACY",
            Name = "Legacy design project",
            InitiatorType = ProjectInitiatorTypes.DesignOrganization,
            InitiatorOrganizationId = "org-design-legacy",
            InitiatorOrganizationName = "Legacy Design LLC",
        });
        project.Foundation.DesignCompany.OrganizationId = "";
        project.Foundation.DesignCompany.OrganizationSnapshot.OrganizationId = "";
        var path = Path.Combine(workDirectory, "legacy-design", ProjectWorkspace.DefaultFileName);

        ProjectWorkspaceStore.Save(project, path);
        ProjectWorkspace loaded = ProjectWorkspaceStore.Load(path);

        Assert.Equal("org-design-legacy", loaded.Foundation.DesignCompany.OrganizationId);
        Assert.Equal("org-design-legacy", loaded.Foundation.DesignCompany.OrganizationSnapshot.OrganizationId);
    }

    [Fact]
    public void ProjectWorkspaceStore_InfersCreationForVersionOneCloudProject()
    {
        var project = ProjectWorkspaceStore.Create("OLD-CLOUD-01", "Хуучин Cloud төсөл");
        project.Cloud = new ProjectCloudLink
        {
            Origin = ProjectOrigins.Cloud,
            ServerProjectId = "server-project-01",
            SyncStatus = ProjectSyncStatuses.Linked,
        };
        project.Foundation.PlanningTask.IssuingAuthorityName = "Хот байгуулалтын газар";
        var path = Path.Combine(workDirectory, "old-cloud", ProjectWorkspace.DefaultFileName);
        ProjectWorkspaceStore.Save(project, path);

        var json = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        json["formatVersion"] = 1;
        json.Remove("creation");
        File.WriteAllText(path, json.ToJsonString());

        var loaded = ProjectWorkspaceStore.Load(path);

        Assert.Equal(ProjectCreationChannels.Server, loaded.Creation.Channel);
        Assert.Equal(ProjectInitiatorTypes.GovernmentAuthority, loaded.Creation.InitiatorType);
        Assert.Equal("Хот байгуулалтын газар", loaded.Creation.InitiatorOrganizationName);
        Assert.Equal(loaded.CreatedAtUtc, loaded.Creation.CreatedAtUtc);
    }

    [Fact]
    public void LegacyImporter_PreservesLegacyFileAndSeparatesAlbumDocument()
    {
        var projectFolder = Path.Combine(workDirectory, "legacy-project");
        var sourceFolder = Path.Combine(projectFolder, "incoming-sheets");
        var legacyPath = Path.Combine(projectFolder, "LEGACY-01" + AlbumProject.FileExtension);
        var legacy = new AlbumProject
        {
            Name = "Legacy project",
            Code = "LEGACY-01",
            ClientName = "client@example.test",
            SourceFolders = [sourceFolder],
            OutputFolder = Path.Combine(projectFolder, "albums"),
        };
        legacy.Participants.Add(new ProjectParticipant
        {
            FullName = "З.Бат",
            Email = "client@example.test",
            Role = "Client",
        });
        legacy.Participants.Add(new ProjectParticipant
        {
            FullName = "Х.Туяа",
            Email = "authority@example.test",
            Role = "Authority Specialist",
        });
        legacy.Participants.Add(new ProjectParticipant
        {
            FullName = "Архитектор",
            Email = "architect@example.test",
            Role = "Major architect",
        });
        legacy.Participants.Add(new ProjectParticipant
        {
            FullName = "Архитектор",
            Email = "architect@example.test",
            Role = "Architect",
        });
        legacy.Album.Title = "Legacy album";
        AlbumProjectStore.Save(legacy, legacyPath);
        var originalJson = File.ReadAllText(legacyPath);

        var result = LegacyAlbumProjectImporter.Import(legacyPath, persist: true);

        Assert.Equal(originalJson, File.ReadAllText(legacyPath));
        Assert.Equal(ProjectWorkspace.DefaultFileName, Path.GetFileName(result.ProjectPath));
        Assert.Equal("building-architecture-concept.erksalbum", Path.GetFileName(result.AlbumPath));
        Assert.Equal("Legacy album", result.Album.Definition.Title);
        Assert.Equal(result.Project.ProjectId, result.Album.ProjectId);
        Assert.DoesNotContain(result.ProjectPath, result.Project.PrimaryAlbum.DocumentPath);
        Assert.Equal("З.Бат", result.Project.Foundation.InitiationBasis.ClientName);
        Assert.Equal("client@example.test", result.Project.Foundation.InitiationBasis.ClientEmail);
        Assert.Equal("Х.Туяа", result.Project.Foundation.PlanningTask.AuthorityMembers.Single().FullName);
        Assert.Equal(["Major architect", "Architect"], result.Project.Foundation.DesignCompany.Members.Single().Roles);
    }

    [Fact]
    public void ProjectWorkspacePaths_RejectsContentOutsideProject()
    {
        var projectFolder = Path.Combine(workDirectory, "project");
        var projectPath = Path.Combine(projectFolder, ProjectWorkspace.DefaultFileName);
        Directory.CreateDirectory(projectFolder);

        var inside = ProjectWorkspacePaths.ResolveInsideProject(projectPath, "sources/revit/deliveries");

        Assert.True(ProjectWorkspacePaths.IsInside(projectFolder, inside));
        Assert.Throws<InvalidDataException>(() =>
            ProjectWorkspacePaths.ResolveInsideProject(projectPath, Path.Combine(workDirectory, "outside")));
    }

    [Fact]
    public void LegacyImporter_RejectsProjectOwnedAlbumDocument()
    {
        var albumPath = Path.Combine(workDirectory, "albums", "concept" + AlbumProject.FileExtension);
        StudioAlbumDocumentStore.Save(new StudioAlbumDocument { ProjectId = "project-01" }, albumPath);

        Assert.True(StudioAlbumDocumentStore.IsAlbumDocument(albumPath));
        Assert.Throws<InvalidDataException>(() => LegacyAlbumProjectImporter.Import(albumPath, persist: true));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(albumPath)!, ProjectWorkspace.DefaultFileName)));
    }

    [Fact]
    public void Library_LatestExportWins()
    {
        var library = new SheetLibrary();
        var older = WriteSamplePackage(Path.Combine(workDirectory, "old"), sheetCount: 1);
        var newer = WriteSamplePackage(Path.Combine(workDirectory, "new"), sheetCount: 1);

        var olderResult = SheetPackageReader.Load(older);
        olderResult.Manifest!.ExportedAtUtc = DateTimeOffset.UtcNow.AddHours(-1);
        var newerResult = SheetPackageReader.Load(newer);

        library.Absorb(newerResult);
        library.Absorb(olderResult);

        var record = Assert.Single(library.Snapshot());
        Assert.Equal(newerResult.Manifest!.PackageId, record.PackageId);
    }

    [Fact]
    public void Library_FullSnapshotRemovesDeletedSheetWithoutTouchingOtherSource()
    {
        var now = DateTimeOffset.UtcNow;
        var sourceAOld = WriteSourcePackage(
            Path.Combine(workDirectory, "source-a-old"),
            "source-a",
            ["A1", "A2", "A3"],
            SheetPackageScope.Delta,
            now.AddMinutes(-2));
        var sourceB = WriteSourcePackage(
            Path.Combine(workDirectory, "source-b"),
            "source-b",
            ["B1"],
            SheetPackageScope.FullSnapshot,
            now.AddMinutes(-1));
        var sourceANew = WriteSourcePackage(
            Path.Combine(workDirectory, "source-a-new"),
            "source-a",
            ["A1", "A2"],
            SheetPackageScope.FullSnapshot,
            now);
        var library = new SheetLibrary();

        library.Absorb(SheetPackageReader.Load(sourceAOld));
        library.Absorb(SheetPackageReader.Load(sourceB));
        var change = library.Absorb(SheetPackageReader.Load(sourceANew));
        library.Absorb(SheetPackageReader.Load(sourceAOld));

        Assert.True(change.FullSnapshotApplied);
        Assert.Single(change.RemovedSheetKeys);
        Assert.Equal(3, library.Snapshot().Count);
        Assert.DoesNotContain(library.Snapshot(), record => record.Entry.SheetId == "A3");
        Assert.Contains(library.Snapshot(), record => record.Entry.SheetId == "B1");
    }

    [Fact]
    public void Library_DeltaNeverDeletesUnselectedSheets()
    {
        var now = DateTimeOffset.UtcNow;
        var full = WriteSourcePackage(
            Path.Combine(workDirectory, "full"),
            "source-a",
            ["A1", "A2"],
            SheetPackageScope.FullSnapshot,
            now.AddMinutes(-1));
        var delta = WriteSourcePackage(
            Path.Combine(workDirectory, "delta"),
            "source-a",
            ["A1"],
            SheetPackageScope.Delta,
            now);
        var library = new SheetLibrary();

        library.Absorb(SheetPackageReader.Load(full));
        var change = library.Absorb(SheetPackageReader.Load(delta));

        Assert.Empty(change.RemovedSheetKeys);
        Assert.Equal(2, library.Snapshot().Count);
    }

    [Fact]
    public void Library_EmptyFullSnapshotIsValidAndRemovesAllSourceSheets()
    {
        var now = DateTimeOffset.UtcNow;
        var full = WriteSourcePackage(
            Path.Combine(workDirectory, "before-empty"),
            "source-a",
            ["A1", "A2"],
            SheetPackageScope.FullSnapshot,
            now.AddMinutes(-1));
        var empty = WriteSourcePackage(
            Path.Combine(workDirectory, "empty"),
            "source-a",
            [],
            SheetPackageScope.FullSnapshot,
            now);
        var emptyResult = SheetPackageReader.Load(empty);
        var library = new SheetLibrary();

        library.Absorb(SheetPackageReader.Load(full));
        var change = library.Absorb(emptyResult);

        Assert.True(emptyResult.IsLossless, string.Join("; ", emptyResult.Issues));
        Assert.Equal(2, change.RemovedSheetKeys.Count);
        Assert.Empty(library.Snapshot());
    }

    [Fact]
    public void Intake_ManifestSourceIdOverridesSharedFolderRegistration()
    {
        var root = Path.Combine(workDirectory, "shared-inbox");
        WriteSourcePackage(
            Path.Combine(root, "package-a"),
            "source-a",
            ["A1"],
            SheetPackageScope.Delta,
            DateTimeOffset.UtcNow.AddMinutes(-1));
        WriteSourcePackage(
            Path.Combine(root, "package-b"),
            "source-b",
            ["B1"],
            SheetPackageScope.Delta,
            DateTimeOffset.UtcNow);
        var library = new SheetLibrary();
        using var intake = new SheetIntakeService(library);

        intake.WatchFolder(root, "folder-default-source");
        var rescan = intake.Rescan();

        Assert.Equal(["source-a", "source-b"], library.Snapshot()
            .Select(record => record.SourceId)
            .OrderBy(value => value)
            .ToArray());
        Assert.Equal(2, rescan.ManifestCount);
        Assert.Equal(0, rescan.ChangedPackageCount);
    }

    [Fact]
    public void Intake_DeferredInitialScan_LoadsPackagesOnlyWhenRescanRuns()
    {
        var root = Path.Combine(workDirectory, "deferred-inbox");
        WriteSourcePackage(
            root,
            "source-current",
            ["A1"],
            SheetPackageScope.Delta,
            DateTimeOffset.UtcNow,
            projectId: "project-current");
        var library = new SheetLibrary();
        using var intake = new SheetIntakeService(library);

        intake.WatchFolder(
            root,
            "source-current",
            "project-current",
            scanExisting: false);

        Assert.Empty(library.Snapshot());
        SheetIntakeScanResult scan = intake.Rescan();
        Assert.Equal(1, scan.ManifestCount);
        Assert.Single(library.Snapshot());
    }

    [Fact]
    public void Intake_CurrentSnapshotScan_SkipsSupersededFullSnapshotFiles()
    {
        var root = Path.Combine(workDirectory, "current-snapshot-inbox");
        string oldManifestPath = WriteSourcePackage(
            Path.Combine(root, "old"),
            "source-current",
            ["A1", "A2"],
            SheetPackageScope.FullSnapshot,
            DateTimeOffset.UtcNow.AddMinutes(-1),
            projectId: "project-current");
        string currentManifestPath = WriteSourcePackage(
            Path.Combine(root, "current"),
            "source-current",
            ["A2"],
            SheetPackageScope.FullSnapshot,
            DateTimeOffset.UtcNow,
            projectId: "project-current");
        SheetPackageManifest oldManifest = SheetPackageReader.Load(oldManifestPath).Manifest!;
        string oldPdfPath = Path.Combine(
            Path.GetDirectoryName(oldManifestPath)!,
            oldManifest.Sheets[0].PdfFileName);
        var library = new SheetLibrary();
        using var intake = new SheetIntakeService(library);
        intake.WatchFolder(
            root,
            "source-current",
            "project-current",
            scanExisting: false);
        using FileStream oldPdfLock = File.Open(
            oldPdfPath,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None);

        SheetIntakeScanResult scan = intake.RescanCurrentSnapshots();

        Assert.Equal(1, scan.ManifestCount);
        Assert.Equal(1, scan.SkippedHistoricalManifestCount);
        Assert.Empty(intake.RejectedPackages);
        SheetRecord current = Assert.Single(library.Snapshot());
        Assert.Equal("A2", current.Entry.SheetId);
        Assert.Equal(
            SheetPackageReader.Load(currentManifestPath).Manifest!.PackageId,
            current.PackageId);
    }

    [Fact]
    public void Intake_CurrentSnapshotScan_AppliesNewerDeltasAfterLatestFullSnapshot()
    {
        var root = Path.Combine(workDirectory, "current-snapshot-with-delta");
        DateTimeOffset now = DateTimeOffset.UtcNow;
        WriteSourcePackage(
            Path.Combine(root, "old-full"),
            "source-current",
            ["A0"],
            SheetPackageScope.FullSnapshot,
            now.AddMinutes(-2));
        WriteSourcePackage(
            Path.Combine(root, "current-full"),
            "source-current",
            ["A1", "A2"],
            SheetPackageScope.FullSnapshot,
            now.AddMinutes(-1));
        WriteSourcePackage(
            Path.Combine(root, "new-delta"),
            "source-current",
            ["A2"],
            SheetPackageScope.Delta,
            now);
        var library = new SheetLibrary();
        using var intake = new SheetIntakeService(library);
        intake.WatchFolder(root, "source-current", scanExisting: false);

        SheetIntakeScanResult scan = intake.RescanCurrentSnapshots();

        Assert.Equal(2, scan.ManifestCount);
        Assert.Equal(1, scan.SkippedHistoricalManifestCount);
        Assert.Equal(["A1", "A2"], library.Snapshot()
            .Select(record => record.Entry.SheetId)
            .OrderBy(value => value)
            .ToArray());
    }

    [Fact]
    public void Intake_CurrentSnapshotScan_SilentlyHydratesAlreadyRecordedPackage()
    {
        var root = Path.Combine(workDirectory, "current-snapshot-checkpoint");
        string manifestPath = WriteSourcePackage(
            root,
            "source-current",
            ["A1", "A2"],
            SheetPackageScope.FullSnapshot,
            DateTimeOffset.UtcNow,
            projectId: "project-current");
        SheetPackageLoadResult verified = SheetPackageReader.Load(manifestPath);
        var checkpoint = new SheetPackageCheckpoint(
            "project-current",
            "source-current",
            verified.Manifest!.PackageId,
            verified.Manifest.ExportedAtUtc,
            verified.ManifestSha256);
        var library = new SheetLibrary();
        using var intake = new SheetIntakeService(library);
        int libraryChanged = 0;
        int packagesPublished = 0;
        library.Changed += () => libraryChanged++;
        intake.PackageProcessed += _ => packagesPublished++;
        intake.WatchFolder(
            root,
            "source-current",
            "project-current",
            scanExisting: false);

        SheetIntakeScanResult scan = intake.RescanCurrentSnapshots([checkpoint]);

        Assert.Equal(1, scan.ManifestCount);
        Assert.Equal(1, scan.SilentlyHydratedManifestCount);
        Assert.Equal(2, library.Snapshot().Count);
        Assert.Equal(0, libraryChanged);
        Assert.Equal(0, packagesPublished);
    }

    [Fact]
    public void Intake_CurrentSnapshotScan_RejectsChangedPdfBehindRecordedCheckpoint()
    {
        var root = Path.Combine(workDirectory, "current-snapshot-changed-pdf");
        string manifestPath = WriteSourcePackage(
            root,
            "source-current",
            ["A1"],
            SheetPackageScope.FullSnapshot,
            DateTimeOffset.UtcNow,
            projectId: "project-current");
        SheetPackageLoadResult verified = SheetPackageReader.Load(manifestPath);
        SheetPackageEntry entry = Assert.Single(verified.Manifest!.Sheets);
        string pdfPath = Path.Combine(Path.GetDirectoryName(manifestPath)!, entry.PdfFileName);
        File.AppendAllText(pdfPath, "changed after verification");
        var checkpoint = new SheetPackageCheckpoint(
            "project-current",
            "source-current",
            verified.Manifest.PackageId,
            verified.Manifest.ExportedAtUtc,
            verified.ManifestSha256);
        var library = new SheetLibrary();
        using var intake = new SheetIntakeService(library);
        intake.WatchFolder(
            root,
            "source-current",
            "project-current",
            scanExisting: false);

        SheetIntakeScanResult scan = intake.RescanCurrentSnapshots([checkpoint]);

        Assert.Equal(1, scan.RejectedPackageCount);
        Assert.Equal(0, scan.SilentlyHydratedManifestCount);
        Assert.Empty(library.Snapshot());
        Assert.Contains(intake.RejectedPackages, rejected => rejected.Issues.Any(issue =>
            issue.Contains("hash mismatch", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void Intake_CurrentSnapshotScan_PublishesPackageNewerThanCheckpoint()
    {
        var root = Path.Combine(workDirectory, "current-snapshot-newer-than-checkpoint");
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string oldManifestPath = WriteSourcePackage(
            Path.Combine(root, "old"),
            "source-current",
            ["A1"],
            SheetPackageScope.FullSnapshot,
            now.AddMinutes(-1),
            projectId: "project-current");
        WriteSourcePackage(
            Path.Combine(root, "current"),
            "source-current",
            ["A1", "A2"],
            SheetPackageScope.FullSnapshot,
            now,
            projectId: "project-current");
        SheetPackageLoadResult old = SheetPackageReader.Load(oldManifestPath);
        var checkpoint = new SheetPackageCheckpoint(
            "project-current",
            "source-current",
            old.Manifest!.PackageId,
            old.Manifest.ExportedAtUtc,
            old.ManifestSha256);
        var library = new SheetLibrary();
        using var intake = new SheetIntakeService(library);
        int packagesPublished = 0;
        intake.PackageProcessed += _ => packagesPublished++;
        intake.WatchFolder(
            root,
            "source-current",
            "project-current",
            scanExisting: false);

        SheetIntakeScanResult scan = intake.RescanCurrentSnapshots([checkpoint]);

        Assert.Equal(1, scan.ManifestCount);
        Assert.Equal(1, scan.SkippedHistoricalManifestCount);
        Assert.Equal(0, scan.SilentlyHydratedManifestCount);
        Assert.Equal(1, packagesPublished);
        Assert.Equal(["A1", "A2"], library.Snapshot()
            .Select(record => record.Entry.SheetId)
            .OrderBy(value => value)
            .ToArray());
    }

    [Fact]
    public void Intake_CurrentSnapshotScan_SkipsHistoricalForeignSourceInOwnedInbox()
    {
        var root = Path.Combine(workDirectory, "current-snapshot-foreign-source");
        WriteSourcePackage(
            Path.Combine(root, "owned"),
            "source-owned",
            ["A1"],
            SheetPackageScope.FullSnapshot,
            DateTimeOffset.UtcNow,
            projectId: "project-current");
        WriteSourcePackage(
            Path.Combine(root, "foreign"),
            "source-foreign",
            ["B1"],
            SheetPackageScope.FullSnapshot,
            DateTimeOffset.UtcNow.AddMinutes(1),
            projectId: "project-current");
        var library = new SheetLibrary();
        using var intake = new SheetIntakeService(library);
        intake.WatchFolder(
            root,
            "source-owned",
            "project-current",
            scanExisting: false);

        SheetIntakeScanResult scan = intake.RescanCurrentSnapshots();

        Assert.Equal(1, scan.ManifestCount);
        Assert.Equal(1, scan.SkippedForeignManifestCount);
        Assert.Equal(0, scan.SkippedHistoricalManifestCount);
        SheetRecord record = Assert.Single(library.Snapshot());
        Assert.Equal("source-owned", record.SourceId);
        Assert.Empty(intake.RejectedPackages);
    }

    [Fact]
    public void Intake_ProjectScopedFolderRejectsManifestFromAnotherProject()
    {
        var root = Path.Combine(workDirectory, "project-scoped-inbox");
        WriteSourcePackage(
            root,
            "source-a",
            ["A1"],
            SheetPackageScope.Delta,
            DateTimeOffset.UtcNow,
            projectId: "project-other");
        var library = new SheetLibrary();
        using var intake = new SheetIntakeService(library);

        intake.WatchFolder(root, "source-a", "project-current");

        Assert.Empty(library.Snapshot());
        Assert.Contains(intake.RejectedPackages, rejected => rejected.Issues.Any(issue =>
            issue.Contains("project-other", StringComparison.OrdinalIgnoreCase) &&
            issue.Contains("project-current", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void Intake_ProjectScopedFolderRejectsManifestFromAnotherSource()
    {
        var root = Path.Combine(workDirectory, "source-scoped-inbox");
        WriteSourcePackage(
            root,
            "source-other",
            ["A1"],
            SheetPackageScope.Delta,
            DateTimeOffset.UtcNow,
            projectId: "project-current");
        var library = new SheetLibrary();
        using var intake = new SheetIntakeService(library);

        intake.WatchFolder(root, "source-current", "project-current");

        Assert.Empty(library.Snapshot());
        Assert.Contains(intake.RejectedPackages, rejected => rejected.Issues.Any(issue =>
            issue.Contains("source-other", StringComparison.OrdinalIgnoreCase) &&
            issue.Contains("source-current", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void Intake_ProjectScopedFolderAcceptsMatchingOwnership()
    {
        var root = Path.Combine(workDirectory, "matching-scoped-inbox");
        WriteSourcePackage(
            root,
            "source-current",
            ["A1"],
            SheetPackageScope.Delta,
            DateTimeOffset.UtcNow,
            projectId: "project-current");
        var library = new SheetLibrary();
        using var intake = new SheetIntakeService(library);

        intake.WatchFolder(root, "source-current", "project-current");

        SheetRecord record = Assert.Single(library.Snapshot());
        Assert.Equal("source-current", record.SourceId);
        Assert.Empty(intake.RejectedPackages);
    }

    [Fact]
    public async Task Intake_UnwatchCancelsQueuedManifestBeforeAbsorb()
    {
        var root = Path.Combine(workDirectory, "cancelled-watcher-inbox");
        Directory.CreateDirectory(root);
        var library = new SheetLibrary();
        using var intake = new SheetIntakeService(library);
        intake.WatchFolder(root, "source-current", "project-current");

        WriteSourcePackage(
            root,
            "source-current",
            ["A1"],
            SheetPackageScope.Delta,
            DateTimeOffset.UtcNow,
            projectId: "project-current");
        await Task.Delay(100);
        intake.UnwatchFolder(root);
        library.Clear();
        await Task.Delay(500);

        Assert.Empty(library.Snapshot());
    }

    [Fact]
    public void AlbumCompose_ProducesCoverTocAndSheets()
    {
        var manifestPath = WriteSamplePackage(workDirectory, sheetCount: 3);
        var library = new SheetLibrary();
        library.Absorb(SheetPackageReader.Load(manifestPath));

        var project = new AlbumProject { Name = "Compose test" };
        project.Album.Title = "Иж бүрэн альбом";
        var outputPath = Path.Combine(workDirectory, "album.pdf");

        var builder = new AlbumBuilder(new PdfSharpAlbumWriter());
        var result = builder.Build(project, library, outputPath);

        Assert.True(File.Exists(outputPath));
        Assert.Equal(3, result.SheetCount);
        // Cover + TOC + 3 sheets.
        Assert.Equal(5, result.PageCount);
        Assert.Empty(result.Warnings);

        using var composed = PdfReader.Open(outputPath, PdfDocumentOpenMode.Import);
        Assert.Equal(5, composed.PageCount);
    }

    [Fact]
    public void AlbumCompose_SharedMultiPagePdfImportsOnlyEachReferencedPage()
    {
        string directory = Path.Combine(workDirectory, "shared-multi-page-album");
        Directory.CreateDirectory(directory);
        string pdfPath = Path.Combine(directory, "autocad-layouts.pdf");
        using (var source = new PdfDocument())
        {
            AddVectorPage(source, 210, 297, "Page 1");
            AddVectorPage(source, 420, 297, "Page 2");
            source.Save(pdfPath);
        }
        var manifest = new SheetPackageManifest
        {
            SchemaVersion = SheetPackageManifest.CurrentSchemaVersion,
            Source = new SheetPackageSource
            {
                SourceId = "autocad-shared-pdf",
                Application = SheetSourceApplication.AutoCad,
                DocumentPath = @"C:\sample\shared.dwg",
                DocumentTitle = "shared.dwg",
            },
            PackageScope = SheetPackageScope.FullSnapshot,
            Sheets =
            [
                new SheetPackageEntry
                {
                    SheetId = "layout-1",
                    Number = "01",
                    Name = "Portrait layout",
                    WidthMm = 210,
                    HeightMm = 297,
                    ContentWidthMm = 210,
                    ContentHeightMm = 297,
                    PdfFileName = "autocad-layouts.pdf",
                    PdfPageNumber = 1,
                },
                new SheetPackageEntry
                {
                    SheetId = "layout-2",
                    Number = "02",
                    Name = "Landscape layout",
                    WidthMm = 420,
                    HeightMm = 297,
                    ContentWidthMm = 420,
                    ContentHeightMm = 297,
                    PdfFileName = "autocad-layouts.pdf",
                    PdfPageNumber = 2,
                },
            ],
        };
        string manifestPath = SheetPackageWriter.Write(manifest, directory, "autocad-shared");
        var library = new SheetLibrary();
        SheetLibraryChange change = library.Absorb(SheetPackageReader.Load(manifestPath));
        Assert.False(change.Rejected, string.Join(" | ", change.Issues));

        var project = new AlbumProject { Name = "Shared PDF page mapping" };
        project.Album.IncludeCover = false;
        project.Album.IncludeTableOfContents = false;
        foreach (SheetRecord sheet in library.Snapshot().OrderBy(item => item.Entry.Number))
        {
            project.Album.Pages.Add(new AlbumPageDefinition
            {
                SheetKey = sheet.Key,
                PageFormatId = PageFormatCatalog.SourceAsIsId,
                PlacementMode = PagePlacementMode.FullPage,
            });
        }
        string outputPath = Path.Combine(directory, "album.pdf");

        AlbumBuildResult result = new AlbumBuilder(new PdfSharpAlbumWriter())
            .Build(project, library, outputPath);

        Assert.Equal(2, result.SheetCount);
        Assert.Equal(2, result.PageCount);
        using var composed = PdfReader.Open(outputPath, PdfDocumentOpenMode.Import);
        Assert.InRange(composed.Pages[0].Width.Millimeter, 209.5, 210.5);
        Assert.InRange(composed.Pages[1].Width.Millimeter, 419.5, 420.5);
    }

    [Fact]
    public void ConceptAlbumTemplate_CreatesStudioFrontMatterAndSourceSlots()
    {
        var definition = BuildingArchitectureConceptAlbumTemplate.CreateDefinition("Загвар зургийн альбум");

        Assert.Equal(BuildingArchitectureConceptAlbumTemplate.TemplateId, definition.TemplateId);
        Assert.Equal(13, definition.Composition.Count);
        Assert.Equal(4, definition.Composition.Count(item => item.Kind == AlbumCompositionKind.Generated));
        Assert.Equal(9, definition.Composition.Count(item => item.Kind == AlbumCompositionKind.SourceSlot));
        Assert.Equal(
            new[] { "00", "01", "02" },
            definition.Composition.Take(3).Select(item => item.Number));
        Assert.Equal(
            new[]
            {
                "НҮҮР ХУУДАС",
                "ЗУРАГ ТӨСӨЛ БОЛОВСРУУЛСАН БАЙГУУЛЛАГА",
                BuildingArchitectureConceptGeneratedPagePlanner.ApprovedPlanningTaskTitle,
            },
            definition.Composition.Take(3).Select(item => item.Title));
        Assert.False(definition.IncludeCover);
        Assert.False(definition.IncludeTableOfContents);
    }

    [Fact]
    public void ConceptPageFormat_MatchesRevitSketchA3Geometry()
    {
        var format = PageFormatCatalog.Resolve(PageFormatCatalog.ConceptA3LandscapeId);

        Assert.Equal("Arial", BuildingArchitectureConceptPageLayout.FontFamilyName);
        Assert.Equal(2.5, BuildingArchitectureConceptPageLayout.CoverBodyTextHeightMm);
        Assert.Equal(4.0, BuildingArchitectureConceptPageLayout.CoverProjectNameTextHeightMm);
        Assert.Equal(2.5, BuildingArchitectureConceptPageLayout.CornerTextHeightMm);
        Assert.InRange(BuildingArchitectureConceptPageLayout.CornerMinimumTextHeightMm, 1.0, 2.0);
        Assert.True(BuildingArchitectureConceptPageLayout.CornerLineHeightFactor > 1.0);
        Assert.Equal(30.0,
            BuildingArchitectureConceptPageLayout.CoverProcessedLogoRightMm -
            BuildingArchitectureConceptPageLayout.CoverProcessedLeftMm,
            3);
        Assert.Equal(58.7,
            BuildingArchitectureConceptPageLayout.CoverProcessedRoleRightMm -
            BuildingArchitectureConceptPageLayout.CoverProcessedLogoRightMm,
            3);
        Assert.Equal(41.75,
            BuildingArchitectureConceptPageLayout.CoverProcessedNameRightMm -
            BuildingArchitectureConceptPageLayout.CoverProcessedRoleRightMm,
            3);
        Assert.Equal(25.0,
            BuildingArchitectureConceptPageLayout.CoverTableRightMm -
            BuildingArchitectureConceptPageLayout.CoverProcessedNameRightMm,
            3);
        Assert.False(ProjectClientTypes.UsesLogo(ProjectClientTypes.Citizen));
        Assert.True(ProjectClientTypes.UsesLogo(ProjectClientTypes.Organization));
        Assert.True(ProjectClientTypes.UsesLogo(ProjectClientTypes.GovernmentAuthority));
        Assert.True(ProjectClientTypes.ShowsDirectClientName(ProjectClientTypes.Citizen));
        Assert.False(ProjectClientTypes.ShowsDirectClientName(ProjectClientTypes.Organization));
        Assert.False(ProjectClientTypes.ShowsDirectClientName(ProjectClientTypes.GovernmentAuthority));
        Assert.Equal(
            "Гүйцэтгэгч",
            BuildingArchitectureConceptPageLayout.CoverProcessedTopSectionTitle);
        Assert.Equal(
            "Захиалагч",
            BuildingArchitectureConceptPageLayout.CoverProcessedBottomSectionTitle);
        Assert.True(BuildingArchitectureConceptPageLayout.IsCanonical(format));
        Assert.Equal(420, format.WidthMm);
        Assert.Equal(297, format.HeightMm);
        Assert.Equal("LEFT", format.BindEdge);
        Assert.Equal((15d, 5d, 400d, 9d),
            (format.SheetTitleArea.X, format.SheetTitleArea.Y, format.SheetTitleArea.Width, format.SheetTitleArea.Height));
        Assert.Equal((15d, 14d, 400d, 250d),
            (format.DrawingArea.X, format.DrawingArea.Y, format.DrawingArea.Width, format.DrawingArea.Height));
        Assert.Equal((225d, 264d, 190d, 28d),
            (format.TitleBlockArea.X, format.TitleBlockArea.Y, format.TitleBlockArea.Width, format.TitleBlockArea.Height));

        var approvalTable = BuildingArchitectureConceptPageLayout.FromBottomLeft(
            68.275,
            93.86,
            351.725,
            169.86);
        Assert.Equal(68.275, approvalTable.X, 3);
        Assert.Equal(127.14, approvalTable.Y, 3);
        Assert.Equal(283.45, approvalTable.Width, 3);
        Assert.Equal(76, approvalTable.Height, 3);
    }

    [Theory]
    [InlineData(ProjectClientTypes.Citizen, "З.Бат", "Д.Төлөөлөгч", "З.Бат")]
    [InlineData(ProjectClientTypes.Organization, "Хуучин захиалагч", "Д.Төлөөлөгч", "Д.Төлөөлөгч")]
    [InlineData(ProjectClientTypes.GovernmentAuthority, "Хуучин захиалагч", "Д.Төлөөлөгч", "Д.Төлөөлөгч")]
    [InlineData(ProjectClientTypes.Organization, "Хуучин захиалагч", "", "")]
    public void ClientCoverPerson_UsesRepresentativeOnlyForNonCitizenClients(
        string clientType,
        string clientName,
        string representativeName,
        string expected)
    {
        Assert.Equal(
            expected,
            ProjectClientTypes.ResolveCoverPersonName(
                clientType,
                clientName,
                representativeName,
                "Legacy citizen"));
    }

    [Theory]
    [InlineData(ProjectClientTypes.Citizen, "З.Бат", "", "Иргэн")]
    [InlineData(ProjectClientTypes.Organization, "Захиалагч ХХК", "Захирал", "Захиалагч ХХК Захирал")]
    [InlineData(ProjectClientTypes.GovernmentAuthority, "Хот байгуулалтын газар", "Хэлтсийн дарга", "Хот байгуулалтын газар Хэлтсийн дарга")]
    [InlineData(ProjectClientTypes.Organization, "Захиалагч ХХК", "", "Захиалагч ХХК")]
    [InlineData(ProjectClientTypes.Organization, "", "Захирал", "Захирал")]
    public void ClientCoverRole_UsesOrganizationNameInsteadOfClientType(
        string clientType,
        string clientName,
        string representativePosition,
        string expected)
    {
        Assert.Equal(
            expected,
            ProjectClientTypes.ResolveCoverRole(
                clientType,
                clientName,
                representativePosition));
    }

    [Fact]
    public void StoredClientType_InfersLegacyOrganizationWithoutOverridingExplicitCitizen()
    {
        var organization = new CompanyProfile { OrganizationType = "ClientOrganization" };
        var authority = new CompanyProfile { OrganizationType = "GovernmentAuthority" };

        Assert.Equal(
            ProjectClientTypes.Organization,
            ProjectClientTypes.ResolveStoredType(null, organization));
        Assert.Equal(
            ProjectClientTypes.GovernmentAuthority,
            ProjectClientTypes.ResolveStoredType("", authority));
        Assert.Equal(
            ProjectClientTypes.Citizen,
            ProjectClientTypes.ResolveStoredType(ProjectClientTypes.Citizen, organization));
    }

    [Theory]
    [InlineData(ProjectClientTypes.Citizen, "Захиалагчийн нэр")]
    [InlineData(ProjectClientTypes.Organization, "Захиалагч байгууллагын нэр")]
    [InlineData(ProjectClientTypes.GovernmentAuthority, "Төрийн байгууллагын нэр")]
    public void ClientNameFieldLabel_ExplainsWhichNameIsRequired(string clientType, string expected)
    {
        Assert.Equal(expected, ProjectClientTypes.ClientNameFieldLabel(clientType));
    }

    [Fact]
    public void AssetDisplayName_HidesContentAddressedStorageName()
    {
        const string hash = "315283a9bc6a54bc0de1a5cbbea2ef83ebcb54caefbed8654c07e8bd80127f14";
        var document = new ProjectFileReference
        {
            Title = "Батлагдсан архитектур төлөвлөлтийн даалгавар",
            OriginalFileName = hash + ".pdf",
            RelativePath = "foundation/documents/approved-atd/" + hash + ".pdf",
        };

        Assert.Equal(
            "Батлагдсан архитектур төлөвлөлтийн даалгавар.pdf",
            ProjectAssetDisplayName.ForDocument(document));
        Assert.Equal(
            "АТД.pdf",
            ProjectAssetDisplayName.ForDocument(new ProjectFileReference
            {
                Title = document.Title,
                OriginalFileName = "АТД.pdf",
                RelativePath = document.RelativePath,
            }));
        Assert.Equal(
            "Захиалагчийн лого.jpg",
            ProjectAssetDisplayName.Resolve("", hash + ".jpg", "Захиалагчийн лого"));
    }

    [Fact]
    public void ConceptElevationPage_ReservesFiftyFiveMillimeterInformationBand()
    {
        var page = new AlbumPageDefinition
        {
            PageFormatId = PageFormatCatalog.ConceptA3LandscapeId,
            TemplateSlotId = "elevations",
        };
        var entry = new SheetPackageEntry
        {
            ContentKind = "Elevation",
            Name = "North facade",
        };

        PageFormatDefinition format = PageFormatCatalog.ResolveForConceptPage(page, entry);

        Assert.Equal(PageFormatCatalog.ConceptElevationA3LandscapeId, format.Id);
        Assert.Equal((15d, 60d, 400d, 9d),
            (format.SheetTitleArea.X, format.SheetTitleArea.Y, format.SheetTitleArea.Width, format.SheetTitleArea.Height));
        Assert.Equal((15d, 69d, 400d, 195d),
            (format.DrawingArea.X, format.DrawingArea.Y, format.DrawingArea.Width, format.DrawingArea.Height));
        Assert.Equal(55d, BuildingArchitectureConceptPageLayout.ElevationInformationArea.Height);
        Assert.Equal(125d, BuildingArchitectureConceptPageLayout.ElevationRoleColumnRightMm);
        Assert.Equal(180d, BuildingArchitectureConceptPageLayout.ElevationApprovalPanelRightMm);
        Assert.Equal(
            new[] { 180d },
            BuildingArchitectureConceptPageLayout.ElevationInformationDividerXMm);
        Assert.DoesNotContain(
            BuildingArchitectureConceptPageLayout.ElevationRoleColumnRightMm,
            BuildingArchitectureConceptPageLayout.ElevationInformationDividerXMm);
    }

    [Fact]
    public void ConceptA3PortraitPage_UsesTopBindingAndPortraitTitleBlock()
    {
        PageFormatDefinition standard = PageFormatCatalog.Resolve(
            PageFormatCatalog.ConceptA3PortraitTopId);
        var page = new AlbumPageDefinition
        {
            PageFormatId = PageFormatCatalog.ConceptA3PortraitTopId,
            TemplateSlotId = "elevations",
        };
        var entry = new SheetPackageEntry
        {
            ContentKind = "Elevation",
            Name = "Portrait facade",
        };

        PageFormatDefinition elevation = PageFormatCatalog.ResolveForConceptPage(page, entry);

        Assert.Equal((297d, 420d, "PORTRAIT", "TOP"),
            (standard.WidthMm, standard.HeightMm, standard.Orientation, standard.BindEdge));
        Assert.Equal((5d, 15d, 287d, 9d),
            (standard.SheetTitleArea.X, standard.SheetTitleArea.Y,
                standard.SheetTitleArea.Width, standard.SheetTitleArea.Height));
        Assert.Equal((5d, 24d, 287d, 363d),
            (standard.DrawingArea.X, standard.DrawingArea.Y,
                standard.DrawingArea.Width, standard.DrawingArea.Height));
        Assert.Equal((102d, 387d, 190d, 28d),
            (standard.TitleBlockArea.X, standard.TitleBlockArea.Y,
                standard.TitleBlockArea.Width, standard.TitleBlockArea.Height));
        Assert.Equal(PageFormatCatalog.ConceptElevationA3PortraitTopId, elevation.Id);
        Assert.Equal((5d, 70d, 287d, 9d),
            (elevation.SheetTitleArea.X, elevation.SheetTitleArea.Y,
                elevation.SheetTitleArea.Width, elevation.SheetTitleArea.Height));
        Assert.Equal((5d, 79d, 287d, 308d),
            (elevation.DrawingArea.X, elevation.DrawingArea.Y,
                elevation.DrawingArea.Width, elevation.DrawingArea.Height));
    }

    [Fact]
    public void ConceptGeneralPlanPage_UsesInformationBandAndSeparateTitleRow()
    {
        var page = new AlbumPageDefinition
        {
            PageFormatId = PageFormatCatalog.ConceptA3LandscapeId,
            TemplateSlotId = "master-plan",
        };
        var entry = new SheetPackageEntry
        {
            ContentKind = "Ерөнхий төлөвлөгөө",
            Name = "ЕРӨНХИЙ ТӨЛӨВЛӨГӨӨ",
        };

        PageFormatDefinition format = PageFormatCatalog.ResolveForConceptPage(page, entry);

        Assert.True(BuildingArchitectureConceptPageLayout.UsesInformationHeader(
            entry.ContentKind,
            entry.Name,
            page.TemplateSlotId));
        Assert.Equal((15d, 60d, 400d, 9d),
            (format.SheetTitleArea.X, format.SheetTitleArea.Y, format.SheetTitleArea.Width, format.SheetTitleArea.Height));
        Assert.Equal((15d, 69d, 400d, 195d),
            (format.DrawingArea.X, format.DrawingArea.Y, format.DrawingArea.Width, format.DrawingArea.Height));
    }

    [Theory]
    [InlineData("НАРНЫ ЭЭВЭРЛЭЛТ", "solar-study")]
    [InlineData("ХӨДӨЛГӨӨНИЙ СХЕМ", "traffic-scheme")]
    [InlineData("НОГООН БАЙГУУЛАМЖ", "landscaping")]
    public void OtherGeneralPlanSheets_KeepStandardTitleRow(
        string sheetName,
        string templateSlotId)
    {
        var page = new AlbumPageDefinition
        {
            PageFormatId = PageFormatCatalog.ConceptA3LandscapeId,
            TemplateSlotId = templateSlotId,
        };
        var entry = new SheetPackageEntry
        {
            ContentKind = "Ерөнхий төлөвлөгөө",
            Name = sheetName,
        };

        PageFormatDefinition format = PageFormatCatalog.ResolveForConceptPage(page, entry);

        Assert.False(BuildingArchitectureConceptPageLayout.UsesInformationHeader(
            entry.ContentKind,
            entry.Name,
            page.TemplateSlotId));
        Assert.Equal((15d, 5d, 400d, 9d),
            (format.SheetTitleArea.X, format.SheetTitleArea.Y, format.SheetTitleArea.Width, format.SheetTitleArea.Height));
        Assert.Equal((15d, 14d, 400d, 250d),
            (format.DrawingArea.X, format.DrawingArea.Y, format.DrawingArea.Width, format.DrawingArea.Height));
    }

    [Theory]
    [InlineData("Давхрын байгуулалт", "1-Р ДАВХРЫН БАЙГУУЛАЛТ", "floor-plans")]
    [InlineData("Байгуулалт", "1-Р ДАВХАР", "floor-plans")]
    [InlineData("Огтлол", "ОГТЛОЛ 1-1", "sections")]
    [InlineData("Нүүр тал", "НҮҮР ТАЛ X1-X3", "elevations")]
    [InlineData("Харагдах байдал", "ХАРАГДАХ БАЙДАЛ", "visualizations")]
    [InlineData("Ерөнхий төлөвлөгөө", "НОГООН БАЙГУУЛАМЖ", "landscaping")]
    public void ConceptAlbumTemplate_MatchesSourceMetadata(
        string contentKind,
        string name,
        string expectedSlotId)
    {
        var definition = BuildingArchitectureConceptAlbumTemplate.CreateDefinition("Concept");
        var entry = new SheetPackageEntry { ContentKind = contentKind, Name = name };

        var slot = BuildingArchitectureConceptAlbumTemplate.FindSourceSlot(definition, entry);

        Assert.NotNull(slot);
        Assert.Equal(expectedSlotId, slot.Id);
    }

    [Fact]
    public void ConceptAlbumTemplate_SourceGroupMetadataWinsOverConflictingSheetName()
    {
        var definition = BuildingArchitectureConceptAlbumTemplate.CreateDefinition("Concept");
        var entry = new SheetPackageEntry
        {
            ContentKind = "Огтлол",
            Name = "НҮҮР ТАЛ ГЭЖ БУРУУ НЭРЛЭСЭН ХУУДАС",
        };

        AlbumCompositionItem? slot =
            BuildingArchitectureConceptAlbumTemplate.FindSourceSlot(definition, entry);

        Assert.NotNull(slot);
        Assert.Equal("sections", slot.Id);
    }

    [Fact]
    public void ConceptAlbumSequence_GroupsSourceThenBuildingAndOwnsPageNumbers()
    {
        var officeManifestPath = WriteConceptSourcePackage(
            Path.Combine(workDirectory, "office"),
            "office-source",
            "Office.rvt",
            [
                ("office-a-plan", "21", "A 1-р давхрын байгуулалт", "Давхрын байгуулалт", "a", "А барилга"),
                ("office-b-plan", "31", "B 1-р давхрын байгуулалт", "Давхрын байгуулалт", "b", "Б барилга"),
                ("office-a-section", "25", "A огтлол", "Огтлол", "a", "А барилга"),
                ("office-b-section", "35", "B огтлол", "Огтлол", "b", "Б барилга"),
                ("office-a-elevation", "26", "A нүүр тал", "Нүүр тал", "a", "А барилга"),
                ("office-b-elevation", "36", "B нүүр тал", "Нүүр тал", "b", "Б барилга"),
            ]);
        var storageManifestPath = WriteConceptSourcePackage(
            Path.Combine(workDirectory, "storage"),
            "storage-source",
            "Storage.rvt",
            [
                ("storage-plan", "12", "Агуулахын байгуулалт", "Давхрын байгуулалт", "", ""),
                ("storage-section", "15", "Агуулахын огтлол", "Огтлол", "", ""),
                ("storage-elevation", "17", "Агуулахын нүүр тал", "Нүүр тал", "", ""),
            ]);
        var library = new SheetLibrary();
        library.Absorb(SheetPackageReader.Load(officeManifestPath), "office-source");
        library.Absorb(SheetPackageReader.Load(storageManifestPath), "storage-source");

        var definition = BuildingArchitectureConceptAlbumTemplate.CreateDefinition("Concept");
        var records = library.Snapshot().ToDictionary(record => record.Entry.SheetId, StringComparer.Ordinal);
        var deliberatelyMixedOrder = new[]
        {
            "office-a-plan", "office-b-plan", "storage-plan",
            "office-a-section", "office-b-section", "storage-section",
            "office-a-elevation", "office-b-elevation", "storage-elevation",
        };
        foreach (var sheetId in deliberatelyMixedOrder)
        {
            var record = records[sheetId];
            var slot = BuildingArchitectureConceptAlbumTemplate.FindSourceSlot(definition, record.Entry);
            definition.Pages.Add(new AlbumPageDefinition
            {
                SheetKey = record.Key,
                TemplateSlotId = slot?.Id ?? "",
                SectionId = BuildingArchitectureConceptAlbumTemplate.ResolveSectionId(definition, slot),
            });
        }

        var sources = new List<ProjectDesignSource>
        {
            new() { Id = "office-source", Kind = DesignSourceKind.Revit, NativeDocumentTitle = "Office.rvt" },
            new() { Id = "storage-source", Kind = DesignSourceKind.Revit, NativeDocumentTitle = "Storage.rvt" },
        };
        var project = new AlbumProject
        {
            Album = definition,
            DesignSources = sources,
        };
        int generatedPageCount = BuildingArchitectureConceptGeneratedPagePlanner.Create(project).Count;
        var sequence = BuildingArchitectureConceptAlbumSequencer.Create(
            definition,
            definition.Pages,
            library,
            sources,
            generatedPageCount);

        Assert.Equal(
            new[]
            {
                "office-a-plan", "office-a-section", "office-a-elevation",
                "office-b-plan", "office-b-section", "office-b-elevation",
                "storage-plan", "storage-section", "storage-elevation",
            },
            sequence.Select(item => item.Sheet!.Entry.SheetId));
        Assert.Equal(
            new[] { "10", "11", "12", "13", "14", "15", "16", "17", "18" },
            sequence.Select(item => item.Number));
        Assert.Equal("21", sequence[0].Sheet!.Entry.Number);
        Assert.Equal("Office.rvt · А барилга", sequence[0].SourceGroupTitle);
        Assert.Equal("Office.rvt · Б барилга", sequence[3].SourceGroupTitle);
        Assert.Equal("Storage.rvt", sequence[6].SourceGroupTitle);

        var request = AlbumBuilder.CreateRequest(project, library);
        var builtPages = request.Sections.SelectMany(section => section.Pages).ToList();
        Assert.Equal(sequence.Select(item => item.Sheet!.Entry.SheetId), builtPages.Select(item => item.Sheet.Entry.SheetId));
        Assert.Equal(sequence.Select(item => item.Number), builtPages.Select(item => item.Number));
        Assert.All(builtPages, page =>
        {
            Assert.True(BuildingArchitectureConceptPageLayout.IsCanonical(page.Format));
            Assert.Equal(PagePlacementMode.FullPage, page.Definition.PlacementMode);
        });
        Assert.Equal(
            new[] { "Office.rvt · А барилга", "Office.rvt · Б барилга", "Storage.rvt" },
            request.Sections.Select(section => section.Title));
    }

    [Fact]
    public void ConceptAlbumSequence_ComposesOneBuildingFromRevitAndAutoCadSources()
    {
        var autoCadManifestPath = WriteConceptSourcePackage(
            Path.Combine(workDirectory, "mixed-autocad"),
            "autocad-source",
            "MixedBuilding.dwg",
            [
                ("main-plan", "A-11", "Үндсэн барилгын байгуулалт", "Давхрын байгуулалт", "", ""),
                ("annex-plan", "A-21", "Туслах барилгын байгуулалт", "Давхрын байгуулалт", "", ""),
            ],
            SheetSourceApplication.AutoCad);
        var revitManifestPath = WriteConceptSourcePackage(
            Path.Combine(workDirectory, "mixed-revit"),
            "revit-source",
            "MixedBuilding.rvt",
            [
                ("main-section", "R-12", "Үндсэн барилгын огтлол", "Огтлол", "", ""),
                ("annex-section", "R-22", "Туслах барилгын огтлол", "Огтлол", "", ""),
                ("main-elevation", "R-13", "Үндсэн барилгын нүүр тал", "Нүүр тал", "", ""),
                ("annex-elevation", "R-23", "Туслах барилгын нүүр тал", "Нүүр тал", "", ""),
            ]);
        var library = new SheetLibrary();
        library.Absorb(SheetPackageReader.Load(autoCadManifestPath), "autocad-source");
        library.Absorb(SheetPackageReader.Load(revitManifestPath), "revit-source");

        var definition = BuildingArchitectureConceptAlbumTemplate.CreateDefinition("Concept");
        Dictionary<string, SheetRecord> records = library.Snapshot()
            .ToDictionary(record => record.Entry.SheetId, StringComparer.Ordinal);
        foreach (string sheetId in new[]
                 {
                     "main-plan", "annex-plan", "main-section",
                     "annex-section", "main-elevation", "annex-elevation",
                 })
        {
            SheetRecord record = records[sheetId];
            AlbumCompositionItem? slot =
                BuildingArchitectureConceptAlbumTemplate.FindSourceSlot(definition, record.Entry);
            definition.Pages.Add(new AlbumPageDefinition
            {
                SheetKey = record.Key,
                TemplateSlotId = slot?.Id ?? "",
                SectionId = BuildingArchitectureConceptAlbumTemplate.ResolveSectionId(definition, slot),
            });
        }

        var buildingGroups = new List<ProjectBuildingGroup>
        {
            new() { Id = "annex", Name = "Туслах барилга", Order = 1 },
            new() { Id = "main", Name = "Үндсэн барилга", Order = 2 },
        };
        var assignments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [records["main-plan"].Key] = "main",
            [records["main-section"].Key] = "main",
            [records["main-elevation"].Key] = "main",
            [records["annex-plan"].Key] = "annex",
            [records["annex-section"].Key] = "annex",
            [records["annex-elevation"].Key] = "annex",
        };
        var sources = new List<ProjectDesignSource>
        {
            new() { Id = "revit-source", Kind = DesignSourceKind.Revit, NativeDocumentTitle = "MixedBuilding.rvt" },
            new() { Id = "autocad-source", Kind = DesignSourceKind.AutoCad, NativeDocumentTitle = "MixedBuilding.dwg" },
        };

        IReadOnlyList<ConceptAlbumSourcePage> sequence =
            BuildingArchitectureConceptAlbumSequencer.Create(
                definition,
                definition.Pages,
                library,
                sources,
                generatedPageCount: 0,
                buildingGroups,
                assignments);

        Assert.Equal(
            new[]
            {
                "annex-plan", "annex-section", "annex-elevation",
                "main-plan", "main-section", "main-elevation",
            },
            sequence.Select(item => item.Sheet!.Entry.SheetId));
        Assert.Equal(
            new[]
            {
                "Туслах барилга", "Туслах барилга", "Туслах барилга",
                "Үндсэн барилга", "Үндсэн барилга", "Үндсэн барилга",
            },
            sequence.Select(item => item.SourceGroupTitle));
        Assert.Equal(
            new[] { "autocad-source", "revit-source", "revit-source" },
            sequence.Take(3).Select(item => item.Sheet!.SourceId));
    }

    [Fact]
    public void ConceptAlbumTemplate_GeneratesFourA3PagesWithoutDocumentsOrSources()
    {
        var project = new AlbumProject
        {
            Name = "Туршилтын төсөл",
            Code = "ATD-001",
            ClientName = "З.Бат",
            Company = new CompanyProfile
            {
                Name = "Erk-S зураг төслийн компани",
                Email = "studio@erk-s.mn",
            },
            InitiationBasis = new ProjectInitiationBasis { ClientName = "З.Бат" },
            PlanningTask = new PlanningTaskInformation
            {
                AtdNumber = "АТД-001",
                IssuingAuthorityName = "Хот байгуулалтын газар",
                Status = "Issued",
            },
            Album = BuildingArchitectureConceptAlbumTemplate.CreateDefinition("Загвар зургийн альбум"),
        };
        var outputPath = Path.Combine(workDirectory, "concept-front-matter.pdf");

        var result = new AlbumBuilder(new PdfSharpAlbumWriter()).Build(project, new SheetLibrary(), outputPath);

        Assert.Equal(0, result.SheetCount);
        Assert.Equal(5, result.PageCount);
        Assert.Empty(result.Warnings);
        using var document = PdfReader.Open(outputPath, PdfDocumentOpenMode.Import);
        Assert.Equal(5, document.PageCount);
        foreach (var page in document.Pages.Cast<PdfPage>())
        {
            Assert.InRange(page.Width.Millimeter, 419.5, 420.5);
            Assert.InRange(page.Height.Millimeter, 296.5, 297.5);
        }
    }

    [Fact]
    public void ConceptAlbumTemplate_ExpandsMultiPageDocumentsWithoutMixingCategories()
    {
        string registrationPath = Path.Combine(workDirectory, "registration.pdf");
        string licensePath = Path.Combine(workDirectory, "license.pdf");
        string planningTaskPath = Path.Combine(workDirectory, "planning-task.pdf");
        WriteMultiPagePdf(registrationPath, 5, "Registration");
        WriteMultiPagePdf(licensePath, 2, "License");
        WriteMultiPagePdf(planningTaskPath, 3, "Planning task");

        var project = new AlbumProject
        {
            Name = "Баримттай төсөл",
            ProjectFolder = workDirectory,
            Company = new CompanyProfile
            {
                Name = "Erk-S зураг төслийн компани",
                RegistrationCertificateDocuments =
                [
                    CreateDocumentReference(
                        ProjectDocumentCategories.CompanyRegistrationCertificate,
                        registrationPath,
                        5),
                ],
                DesignLicenseDocuments =
                [
                    CreateDocumentReference(
                        ProjectDocumentCategories.CompanyDesignLicense,
                        licensePath,
                        2),
                ],
            },
            PlanningTask = new PlanningTaskInformation
            {
                Documents =
                [
                    CreateDocumentReference(
                        ProjectDocumentCategories.ApprovedPlanningTask,
                        planningTaskPath,
                        3),
                ],
            },
            Album = BuildingArchitectureConceptAlbumTemplate.CreateDefinition("Загвар зургийн альбум"),
        };

        IReadOnlyList<ConceptGeneratedPagePlan> plans =
            BuildingArchitectureConceptGeneratedPagePlanner.Create(project);

        Assert.Equal(8, plans.Count);
        Assert.Equal(new[] { "00", "01", "02", "03", "04", "05", "06", "07" }, plans.Select(plan => plan.Number));
        Assert.Equal(
            new[]
            {
                ConceptGeneratedDocumentKind.None,
                ConceptGeneratedDocumentKind.CompanyRegistrationCertificate,
                ConceptGeneratedDocumentKind.CompanyRegistrationCertificate,
                ConceptGeneratedDocumentKind.CompanyRegistrationCertificate,
                ConceptGeneratedDocumentKind.CompanyDesignLicense,
                ConceptGeneratedDocumentKind.ApprovedPlanningTask,
                ConceptGeneratedDocumentKind.ApprovedPlanningTask,
                ConceptGeneratedDocumentKind.None,
            },
            plans.Select(plan => plan.DocumentKind));
        Assert.Equal(new[] { 0, 2, 2, 1, 2, 2, 1, 0 }, plans.Select(plan => plan.DocumentPages.Count));
        Assert.All(
            plans.Where(plan => plan.DocumentKind is
                ConceptGeneratedDocumentKind.CompanyRegistrationCertificate or
                ConceptGeneratedDocumentKind.CompanyDesignLicense),
            plan => Assert.Equal(
                BuildingArchitectureConceptGeneratedPagePlanner.DesignOrganizationTitle,
                plan.Title));
        Assert.Equal(
            BuildingArchitectureConceptGeneratedPagePlanner.ApprovedPlanningTaskTitle,
            plans[^2].Title);
        Assert.Equal(AlbumGeneratedPageKind.SiteContext, plans[^1].Component.GeneratedPageKind);

        string outputPath = Path.Combine(workDirectory, "concept-document-pages.pdf");
        AlbumBuildResult result = new AlbumBuilder(new PdfSharpAlbumWriter())
            .Build(project, new SheetLibrary(), outputPath);

        Assert.Equal(0, result.SheetCount);
        Assert.Equal(8, result.PageCount);
        Assert.Empty(result.Warnings);
        using var document = PdfReader.Open(outputPath, PdfDocumentOpenMode.Import);
        Assert.Equal(8, document.PageCount);
        Assert.All(document.Pages.Cast<PdfPage>(), page =>
        {
            Assert.InRange(page.Width.Millimeter, 419.5, 420.5);
            Assert.InRange(page.Height.Millimeter, 296.5, 297.5);
        });

        PdfVectorDocumentProfile vectorProfile = PdfVectorQualityInspector.Inspect(outputPath);
        foreach ((ConceptGeneratedPagePlan plan, int index) in plans.Select((plan, index) => (plan, index)))
        {
            if (plan.DocumentPages.Count == 0)
                continue;

            int clippingPathCount = vectorProfile.Pages[index].Operators.Count(
                operation => operation is "W" or "W*");
            Assert.True(
                clippingPathCount >= plan.DocumentPages.Count,
                $"Generated document page {index + 1} must clip every source page to its tile.");
        }
    }

    [Fact]
    public void ProjectDocumentInspector_ReadsPdfPageCountAndAlbumStoreNormalizesLegacyNulls()
    {
        string sourcePath = Path.Combine(workDirectory, "approved-atd.pdf");
        WriteMultiPagePdf(sourcePath, 2, "ATD");

        ProjectDocumentAssetInspection inspection = ProjectDocumentAssetInspector.Inspect(sourcePath);

        Assert.Equal("application/pdf", inspection.ContentType);
        Assert.Equal(2, inspection.PageCount);
        Assert.Equal(64, inspection.Sha256.Length);

        var staleReference = new ProjectFileReference
        {
            PageCount = 1,
            ContentType = "",
            SizeBytes = 0,
            Sha256 = "",
        };
        Assert.True(ProjectDocumentAssetInspector.RefreshMetadata(staleReference, sourcePath));
        Assert.Equal("application/pdf", staleReference.ContentType);
        Assert.Equal(2, staleReference.PageCount);
        Assert.Equal(new FileInfo(sourcePath).Length, staleReference.SizeBytes);
        Assert.Equal(inspection.Sha256, staleReference.Sha256);
        Assert.False(ProjectDocumentAssetInspector.RefreshMetadata(staleReference, sourcePath));

        string projectPath = Path.Combine(workDirectory, "legacy-null-lists.erksalbum");
        File.WriteAllText(
            projectPath,
            """
            {
              "formatVersion": 2,
              "name": "Legacy",
              "initiationBasis": { "documents": null },
              "planningTask": { "requirements": null, "documents": null, "authorityMembers": null },
              "company": {
                "registrationCertificateDocuments": null,
                "designLicenseDocuments": null
              }
            }
            """);

        AlbumProject loaded = AlbumProjectStore.Load(projectPath);

        Assert.Empty(loaded.InitiationBasis.Documents);
        Assert.Empty(loaded.PlanningTask.Requirements);
        Assert.Empty(loaded.PlanningTask.Documents);
        Assert.Empty(loaded.PlanningTask.AuthorityMembers);
        Assert.Empty(loaded.Company.RegistrationCertificateDocuments);
        Assert.Empty(loaded.Company.DesignLicenseDocuments);
        loaded.Album = BuildingArchitectureConceptAlbumTemplate.CreateDefinition("Загвар зургийн альбум");
        Assert.Equal(5, BuildingArchitectureConceptGeneratedPagePlanner.Create(loaded).Count);
    }

    [Fact]
    public void ProjectStore_VersionOneFolderBecomesLegacyDesignSource()
    {
        var folder = Path.Combine(workDirectory, "legacy-inbox");
        var project = new AlbumProject
        {
            FormatVersion = 1,
            Name = "Legacy project",
            SourceFolders = [folder],
        };
        project.DesignSources.Clear();
        var path = Path.Combine(workDirectory, "legacy" + AlbumProject.FileExtension);
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(project, SheetPackageJson.Options));

        var loaded = AlbumProjectStore.Load(path);

        var source = Assert.Single(loaded.DesignSources);
        Assert.Equal(DesignSourceKind.Folder, source.Kind);
        Assert.Equal(folder, source.InboxFolder);
        Assert.True(source.UseLegacySheetKeys);
    }

    [Fact]
    public void Library_RegisteredSourceIdCreatesStableKey()
    {
        var manifestPath = WriteSamplePackage(workDirectory, sheetCount: 1);
        var library = new SheetLibrary();

        library.Absorb(SheetPackageReader.Load(manifestPath), "revit-architecture");

        var record = Assert.Single(library.Snapshot());
        Assert.Equal("revit-architecture", record.SourceId);
        Assert.Equal("revit-architecture|l1", record.Key);
    }

    [Fact]
    public void AlbumCompose_FormattedPageUsesStudioGeometry()
    {
        var manifestPath = WriteSamplePackage(workDirectory, sheetCount: 1);
        var library = new SheetLibrary();
        library.Absorb(SheetPackageReader.Load(manifestPath), "revit-architecture");
        var sheet = Assert.Single(library.Snapshot());

        var project = new AlbumProject { Name = "Formatted page" };
        project.Album.IncludeCover = false;
        project.Album.IncludeTableOfContents = false;
        project.Album.Pages.Add(new AlbumPageDefinition
        {
            SheetKey = sheet.Key,
            PageFormatId = PageFormatCatalog.WorkingDrawingA3LandscapeId,
            PlacementMode = PagePlacementMode.FitDrawingArea,
        });
        var outputPath = Path.Combine(workDirectory, "formatted-album.pdf");

        var result = new AlbumBuilder(new PdfSharpAlbumWriter()).Build(project, library, outputPath);

        Assert.Equal(1, result.SheetCount);
        Assert.Equal(1, result.PageCount);
        Assert.Empty(result.Warnings);
        using var composed = PdfReader.Open(outputPath, PdfDocumentOpenMode.Import);
        var page = Assert.Single(composed.Pages.Cast<PdfPage>());
        Assert.InRange(page.Width.Millimeter, 419.5, 420.5);
        Assert.InRange(page.Height.Millimeter, 296.5, 297.5);
    }

    [Fact]
    public void SchemaTwoManifestWithoutFormat_RemainsCompatible()
    {
        var pdfPath = Path.Combine(workDirectory, "legacy-v2.pdf");
        WriteMinimalPdf(pdfPath, "Legacy v2");
        var manifestPath = Path.Combine(workDirectory, "legacy-v2" + SheetPackageManifest.ManifestSuffix);
        var legacyManifest = new
        {
            schemaVersion = 2,
            packageId = Guid.NewGuid(),
            source = new
            {
                sourceId = "legacy-source",
                application = "Revit",
                documentPath = @"C:\sample\legacy.rvt",
            },
            exportedAtUtc = DateTimeOffset.UtcNow,
            sheets = new[]
            {
                new
                {
                    sheetId = "legacy-sheet",
                    number = "AR-01",
                    name = "Legacy sheet",
                    widthMm = 420,
                    heightMm = 297,
                    pdfFileName = Path.GetFileName(pdfPath),
                    sha256 = SheetPackageReader.ComputeSha256(pdfPath),
                    pageCount = 1,
                },
            },
        };
        File.WriteAllText(
            manifestPath,
            System.Text.Json.JsonSerializer.Serialize(legacyManifest, SheetPackageJson.Options));

        var result = SheetPackageReader.Load(manifestPath);

        Assert.True(result.IsLossless, string.Join("; ", result.Issues));
        var entry = Assert.Single(result.Manifest!.Sheets);
        Assert.Null(entry.Format);
        Assert.False(entry.IsCleanDrawingSpace);
        Assert.Equal("", entry.ScaleText);
    }

    [Fact]
    public void SchemaThreeInlineFormat_VerifiesGeometryHash()
    {
        var format = CreateConceptFormat();
        var pdfPath = Path.Combine(workDirectory, "clean-space.pdf");
        WriteMinimalPdf(pdfPath, "Clean drawing", format.DrawingArea.Width, format.DrawingArea.Height);
        var manifest = new SheetPackageManifest
        {
            SchemaVersion = 3,
            Source = new SheetPackageSource { Application = SheetSourceApplication.Revit },
            Sheets =
            [
                new SheetPackageEntry
                {
                    SheetId = "sheet-01",
                    Number = "AR-01",
                    Name = "Concept plan",
                    WidthMm = format.WidthMm,
                    HeightMm = format.HeightMm,
                    PageFormatId = format.Id,
                    Format = format,
                    IsCleanDrawingSpace = true,
                    ContentWidthMm = format.DrawingArea.Width,
                    ContentHeightMm = format.DrawingArea.Height,
                    PdfFileName = Path.GetFileName(pdfPath),
                },
            ],
        };
        var manifestPath = SheetPackageWriter.Write(manifest, workDirectory, "clean-space");

        var valid = SheetPackageReader.Load(manifestPath);
        Assert.True(valid.IsLossless, string.Join("; ", valid.Issues));

        manifest.Sheets[0].Format!.DrawingArea.Width += 1;
        File.WriteAllText(
            manifestPath,
            System.Text.Json.JsonSerializer.Serialize(manifest, SheetPackageJson.Options));
        var tampered = SheetPackageReader.Load(manifestPath);

        Assert.False(tampered.IsLossless);
        Assert.Contains(tampered.Issues, issue => issue.Contains("geometry hash", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SourceFormatSnapshot_RoundTripsWithAlbumPage()
    {
        var format = CreateConceptFormat();
        var entry = new SheetPackageEntry
        {
            PageFormatId = format.Id,
            Format = format,
            IsCleanDrawingSpace = true,
            ContentWidthMm = format.DrawingArea.Width,
            ContentHeightMm = format.DrawingArea.Height,
        };
        var page = new AlbumPageDefinition { SheetKey = "source|sheet" };

        Assert.True(PageFormatResolver.ApplySourceFormat(page, entry));
        Assert.Equal(PagePlacementMode.PreserveDrawingSpace, page.PlacementMode);
        Assert.True(page.FollowSourceFormat);
        Assert.Equal(format.GeometryHash, page.PageFormatSnapshot!.GeometryHash);

        var albumPath = Path.Combine(workDirectory, "snapshot.erksalbum");
        var album = new StudioAlbumDocument();
        album.Definition.Pages.Add(page);
        StudioAlbumDocumentStore.Save(album, albumPath);
        var loadedPage = Assert.Single(StudioAlbumDocumentStore.Load(albumPath).Definition.Pages);

        Assert.Equal(format.Id, loadedPage.PageFormatId);
        Assert.Equal(format.GeometryHash, loadedPage.PageFormatSnapshot!.GeometryHash);
        Assert.Equal(PagePlacementMode.PreserveDrawingSpace, loadedPage.PlacementMode);
    }

    [Fact]
    public void SourceFormat_AsIsUsesFullPageAndCanLaterSwitchToClean()
    {
        var format = CreateConceptFormat();
        var entry = new SheetPackageEntry
        {
            PageFormatId = format.Id,
            Format = format,
            IsCleanDrawingSpace = false,
            ContentWidthMm = format.WidthMm,
            ContentHeightMm = format.HeightMm,
        };
        var page = new AlbumPageDefinition
        {
            SheetKey = "source|sheet",
            PageFormatId = format.Id,
            PageFormatSnapshot = PageFormatResolver.FromSpec(format),
            PlacementMode = PagePlacementMode.PreserveDrawingSpace,
            FollowSourceFormat = true,
        };

        Assert.True(PageFormatResolver.ApplySourceFormat(page, entry));
        Assert.Equal(PageFormatCatalog.SourceAsIsId, page.PageFormatId);
        Assert.Null(page.PageFormatSnapshot);
        Assert.Equal(PagePlacementMode.FullPage, page.PlacementMode);
        Assert.True(page.FollowSourceFormat);

        entry.IsCleanDrawingSpace = true;
        entry.ContentWidthMm = format.DrawingArea.Width;
        entry.ContentHeightMm = format.DrawingArea.Height;

        Assert.True(PageFormatResolver.ApplySourceFormat(page, entry));
        Assert.Equal(format.Id, page.PageFormatId);
        Assert.Equal(format.GeometryHash, page.PageFormatSnapshot!.GeometryHash);
        Assert.Equal(PagePlacementMode.PreserveDrawingSpace, page.PlacementMode);
        Assert.True(page.FollowSourceFormat);
    }

    [Fact]
    public void PreserveDrawingSpace_RejectsImplicitResize()
    {
        var format = CreateConceptFormat();
        var pdfPath = Path.Combine(workDirectory, "wrong-size.pdf");
        WriteMinimalPdf(pdfPath, "Original size", format.DrawingArea.Width, format.DrawingArea.Height);
        var manifest = new SheetPackageManifest
        {
            Source = new SheetPackageSource { SourceId = "revit-source", Application = SheetSourceApplication.Revit },
            Sheets =
            [
                new SheetPackageEntry
                {
                    SheetId = "sheet-01",
                    Number = "AR-01",
                    Name = "Wrong size",
                    WidthMm = format.WidthMm,
                    HeightMm = format.HeightMm,
                    PageFormatId = format.Id,
                    Format = format,
                    IsCleanDrawingSpace = true,
                    ContentWidthMm = format.DrawingArea.Width,
                    ContentHeightMm = format.DrawingArea.Height,
                    PdfFileName = Path.GetFileName(pdfPath),
                },
            ],
        };
        var manifestPath = SheetPackageWriter.Write(manifest, workDirectory, "wrong-size");
        var library = new SheetLibrary();
        library.Absorb(SheetPackageReader.Load(manifestPath));
        var sheet = Assert.Single(library.Snapshot());

        // Simulate a source PDF and manifest being altered after a valid intake.
        // The album builder must independently enforce the 1:1 drawing space.
        WriteMinimalPdf(pdfPath, "Wrong size", format.DrawingArea.Width - 5, format.DrawingArea.Height);
        manifest.Sheets[0].Sha256 = SheetPackageReader.ComputeSha256(pdfPath);
        File.WriteAllText(
            manifestPath,
            System.Text.Json.JsonSerializer.Serialize(manifest, SheetPackageJson.Options));

        var project = new AlbumProject { Name = "No resize" };
        project.Album.IncludeCover = false;
        project.Album.IncludeTableOfContents = false;
        var page = new AlbumPageDefinition { SheetKey = sheet.Key };
        PageFormatResolver.ApplySourceFormat(page, sheet.Entry);
        project.Album.Pages.Add(page);
        var outputPath = Path.Combine(workDirectory, "wrong-size-album.pdf");
        var previousOutput = new byte[] { 1, 2, 3, 4 };
        File.WriteAllBytes(outputPath, previousOutput);

        var exception = Assert.Throws<AlbumBuildException>(() =>
            new AlbumBuilder(new PdfSharpAlbumWriter()).Build(project, library, outputPath));

        Assert.Contains("page size", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(previousOutput, File.ReadAllBytes(outputPath));
    }

    private static string WriteSamplePackage(string directory, int sheetCount)
    {
        Directory.CreateDirectory(directory);
        var manifest = new SheetPackageManifest
        {
            Source = new SheetPackageSource
            {
                SourceId = "sample-source",
                Application = SheetSourceApplication.AutoCad,
                DocumentPath = @"C:\sample\test.dwg",
                DocumentTitle = "test",
            },
        };

        for (var index = 1; index <= sheetCount; index++)
        {
            var fileName = $"sheet-{index:00}.pdf";
            WriteMinimalPdf(Path.Combine(directory, fileName), $"Sheet {index}", 210, 297);
            manifest.Sheets.Add(new SheetPackageEntry
            {
                SheetId = $"L{index}",
                Number = $"AR-{index:00}",
                Name = $"Test sheet {index}",
                SheetDescription = $"Description {index}",
                ScaleText = index == 1 ? "1:100" : "",
                WidthMm = 210,
                HeightMm = 297,
                PdfFileName = fileName,
            });
        }

        return SheetPackageWriter.Write(manifest, directory, "test");
    }

    private static void AddVectorPage(
        PdfDocument document,
        double widthMm,
        double heightMm,
        string label)
    {
        PdfPage page = document.AddPage();
        page.Width = XUnit.FromMillimeter(widthMm);
        page.Height = XUnit.FromMillimeter(heightMm);
        using XGraphics graphics = XGraphics.FromPdfPage(page);
        graphics.DrawRectangle(new XPen(XColors.Black, 0.5), 20, 20, page.Width.Point - 40, page.Height.Point - 40);
        graphics.DrawString(
            label,
            new XFont("Arial", 12),
            XBrushes.Black,
            new XRect(0, 0, page.Width.Point, page.Height.Point),
            XStringFormats.Center);
    }

    private static string WriteSourcePackage(
        string directory,
        string sourceId,
        IReadOnlyList<string> sheetIds,
        SheetPackageScope packageScope,
        DateTimeOffset exportedAtUtc,
        string projectId = "")
    {
        Directory.CreateDirectory(directory);
        var manifest = new SheetPackageManifest
        {
            Source = new SheetPackageSource
            {
                SourceId = sourceId,
                Application = SheetSourceApplication.Revit,
                DocumentPath = $@"C:\sample\{sourceId}.rvt",
                DocumentTitle = sourceId,
            },
            ProjectId = projectId,
            PackageScope = packageScope,
            ExportedAtUtc = exportedAtUtc,
        };
        foreach (var sheetId in sheetIds)
        {
            var fileName = $"{sheetId}.pdf";
            WriteMinimalPdf(Path.Combine(directory, fileName), sheetId, 210, 297);
            manifest.Sheets.Add(new SheetPackageEntry
            {
                SheetId = sheetId,
                Number = sheetId,
                Name = sheetId,
                WidthMm = 210,
                HeightMm = 297,
                PdfFileName = fileName,
            });
        }
        return SheetPackageWriter.Write(manifest, directory, "source-package");
    }

    private static string WriteConceptSourcePackage(
        string directory,
        string sourceId,
        string documentTitle,
        IReadOnlyList<(string SheetId, string Number, string Name, string ContentKind, string BuildingId, string BuildingName)> sheets,
        SheetSourceApplication application = SheetSourceApplication.Revit)
    {
        Directory.CreateDirectory(directory);
        var manifest = new SheetPackageManifest
        {
            Source = new SheetPackageSource
            {
                SourceId = sourceId,
                Application = application,
                DocumentPath = $@"C:\sample\{documentTitle}",
                DocumentTitle = documentTitle,
            },
            PackageScope = SheetPackageScope.FullSnapshot,
            ExportedAtUtc = DateTimeOffset.UtcNow,
        };
        var format = CreateConceptFormat();
        foreach (var sheet in sheets)
        {
            var fileName = $"{sheet.SheetId}.pdf";
            WriteMinimalPdf(Path.Combine(directory, fileName), sheet.Name, format.WidthMm, format.HeightMm);
            manifest.Sheets.Add(new SheetPackageEntry
            {
                SheetId = sheet.SheetId,
                Number = sheet.Number,
                Name = sheet.Name,
                ContentKind = sheet.ContentKind,
                BuildingId = sheet.BuildingId,
                BuildingName = sheet.BuildingName,
                WidthMm = format.WidthMm,
                HeightMm = format.HeightMm,
                PageFormatId = format.Id,
                Format = format,
                IsCleanDrawingSpace = false,
                ContentWidthMm = format.WidthMm,
                ContentHeightMm = format.HeightMm,
                PdfFileName = fileName,
            });
        }

        return SheetPackageWriter.Write(manifest, directory, "concept-source-package");
    }

    private static PageFormatSpec CreateConceptFormat()
    {
        var format = new PageFormatSpec
        {
            Id = "erks-concept-a3-landscape-left",
            Name = "Concept A3 landscape",
            Mode = "Concept",
            Code = "A3",
            Orientation = "LANDSCAPE",
            BindEdge = "LEFT",
            WidthMm = 420,
            HeightMm = 297,
            DrawingArea = new PageRectSpec { X = 15, Y = 14, Width = 400, Height = 250 },
            SheetTitleArea = new PageRectSpec { X = 15, Y = 5, Width = 400, Height = 9 },
            TitleBlockArea = new PageRectSpec { X = 231, Y = 264, Width = 184, Height = 28 },
            ModuleColumns = 1,
            ModuleRows = 1,
        };
        format.GeometryHash = PageFormatSpecGeometry.ComputeHash(format);
        return format;
    }

    private static void WriteMinimalPdf(
        string path,
        string text,
        double? widthMm = null,
        double? heightMm = null)
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        if (widthMm.HasValue && heightMm.HasValue)
        {
            page.Width = XUnit.FromMillimeter(widthMm.Value);
            page.Height = XUnit.FromMillimeter(heightMm.Value);
        }
        using var gfx = XGraphics.FromPdfPage(page);
        gfx.DrawString(text, new XFont("Arial", 20), XBrushes.Black,
            new XRect(0, 0, page.Width.Point, page.Height.Point), XStringFormats.Center);
        document.Save(path);
    }

    private static void WriteMultiPagePdf(string path, int pageCount, string label)
    {
        using var document = new PdfDocument();
        for (int index = 0; index < pageCount; index++)
        {
            PdfPage page = document.AddPage();
            using XGraphics gfx = XGraphics.FromPdfPage(page);
            gfx.DrawString(
                $"{label} {index + 1}",
                new XFont("Arial", 20),
                XBrushes.Black,
                new XRect(0, 0, page.Width.Point, page.Height.Point),
                XStringFormats.Center);
        }

        document.Save(path);
    }

    private static ProjectFileReference CreateDocumentReference(
        string category,
        string path,
        int pageCount) => new()
    {
        Category = category,
        Title = Path.GetFileNameWithoutExtension(path),
        RelativePath = path,
        OriginalFileName = Path.GetFileName(path),
        ContentType = "application/pdf",
        SizeBytes = new FileInfo(path).Length,
        PageCount = pageCount,
    };
}
