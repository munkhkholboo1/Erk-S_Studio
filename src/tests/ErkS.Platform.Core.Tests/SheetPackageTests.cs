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
        project.Foundation.InitiationBasis.ClientName = "З.Бат";
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

        Assert.Equal("З.Бат", loaded.Foundation.InitiationBasis.ClientName);
        Assert.Equal("АТД-2026-01", loaded.Foundation.PlanningTask.AtdNumber);
        Assert.Equal("Хот байгуулалтын газар", loaded.Foundation.PlanningTask.IssuingAuthorityName);
        Assert.Equal("Erk-S зураг төслийн компани", loaded.DesignOrganizationName);
        Assert.Equal(["Major architect", "Architect"], loaded.Foundation.DesignCompany.Members.Single().Roles);
        Assert.Equal(ProjectWorkspace.ConceptAlbumRelativePath, loaded.PrimaryAlbum.DocumentPath);
        Assert.Equal("Загвар зургийн тайлан", loaded.Deliverables.Reports.Single().Title);
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
    public void ConceptAlbumTemplate_CreatesStudioFrontMatterAndSourceSlots()
    {
        var definition = BuildingArchitectureConceptAlbumTemplate.CreateDefinition("Загвар зургийн альбум");

        Assert.Equal(BuildingArchitectureConceptAlbumTemplate.TemplateId, definition.TemplateId);
        Assert.Equal(13, definition.Composition.Count);
        Assert.Equal(3, definition.Composition.Count(item => item.Kind == AlbumCompositionKind.Generated));
        Assert.Equal(10, definition.Composition.Count(item => item.Kind == AlbumCompositionKind.SourceSlot));
        Assert.Equal(
            new[] { "00", "01", "02" },
            definition.Composition.Take(3).Select(item => item.Number));
        Assert.Equal(
            new[]
            {
                "НҮҮР ХУУДАС",
                "ЗУРАГ ТӨСӨЛ БОЛОВСРУУЛСАН БАЙГУУЛЛАГА",
                "АРХИТЕКТУР ТӨЛӨВЛӨЛТИЙН ДААЛГАВАР",
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
        Assert.True(BuildingArchitectureConceptPageLayout.IsCanonical(format));
        Assert.Equal(420, format.WidthMm);
        Assert.Equal(297, format.HeightMm);
        Assert.Equal("LEFT", format.BindEdge);
        Assert.Equal((15d, 5d, 400d, 9d),
            (format.SheetTitleArea.X, format.SheetTitleArea.Y, format.SheetTitleArea.Width, format.SheetTitleArea.Height));
        Assert.Equal((15d, 14d, 400d, 250d),
            (format.DrawingArea.X, format.DrawingArea.Y, format.DrawingArea.Width, format.DrawingArea.Height));
        Assert.Equal((231d, 264d, 184d, 28d),
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
    [InlineData("Давхрын байгуулалт", "1-Р ДАВХРЫН БАЙГУУЛАЛТ", "floor-plans")]
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
        var sequence = BuildingArchitectureConceptAlbumSequencer.Create(
            definition,
            definition.Pages,
            library,
            sources);

        Assert.Equal(
            new[]
            {
                "office-a-plan", "office-a-section", "office-a-elevation",
                "office-b-plan", "office-b-section", "office-b-elevation",
                "storage-plan", "storage-section", "storage-elevation",
            },
            sequence.Select(item => item.Sheet!.Entry.SheetId));
        Assert.Equal(
            new[] { "09", "10", "11", "12", "13", "14", "15", "16", "17" },
            sequence.Select(item => item.Number));
        Assert.Equal("21", sequence[0].Sheet!.Entry.Number);
        Assert.Equal("Office.rvt · А барилга", sequence[0].SourceGroupTitle);
        Assert.Equal("Office.rvt · Б барилга", sequence[3].SourceGroupTitle);
        Assert.Equal("Storage.rvt", sequence[6].SourceGroupTitle);

        var project = new AlbumProject
        {
            Album = definition,
            DesignSources = sources,
        };
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
    public void ConceptAlbumTemplate_GeneratesThreeA3PagesWithoutSources()
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
        Assert.Equal(3, result.PageCount);
        Assert.Empty(result.Warnings);
        using var document = PdfReader.Open(outputPath, PdfDocumentOpenMode.Import);
        Assert.Equal(3, document.PageCount);
        foreach (var page in document.Pages.Cast<PdfPage>())
        {
            Assert.InRange(page.Width.Millimeter, 419.5, 420.5);
            Assert.InRange(page.Height.Millimeter, 296.5, 297.5);
        }
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
    }

    [Fact]
    public void SchemaThreeInlineFormat_VerifiesGeometryHash()
    {
        var format = CreateConceptFormat();
        var pdfPath = Path.Combine(workDirectory, "clean-space.pdf");
        WriteMinimalPdf(pdfPath, "Clean drawing", format.DrawingArea.Width, format.DrawingArea.Height);
        var manifest = new SheetPackageManifest
        {
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
        WriteMinimalPdf(pdfPath, "Wrong size", format.DrawingArea.Width - 5, format.DrawingArea.Height);
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
        var project = new AlbumProject { Name = "No resize" };
        project.Album.IncludeCover = false;
        project.Album.IncludeTableOfContents = false;
        var page = new AlbumPageDefinition { SheetKey = sheet.Key };
        PageFormatResolver.ApplySourceFormat(page, sheet.Entry);
        project.Album.Pages.Add(page);

        var result = new AlbumBuilder(new PdfSharpAlbumWriter()).Build(
            project,
            library,
            Path.Combine(workDirectory, "wrong-size-album.pdf"));

        Assert.Equal(0, result.SheetCount);
        Assert.Contains(result.Warnings, warning => warning.Contains("not resized", StringComparison.OrdinalIgnoreCase));
    }

    private static string WriteSamplePackage(string directory, int sheetCount)
    {
        Directory.CreateDirectory(directory);
        var manifest = new SheetPackageManifest
        {
            Source = new SheetPackageSource
            {
                Application = SheetSourceApplication.AutoCad,
                DocumentPath = @"C:\sample\test.dwg",
                DocumentTitle = "test",
            },
        };

        for (var index = 1; index <= sheetCount; index++)
        {
            var fileName = $"sheet-{index:00}.pdf";
            WriteMinimalPdf(Path.Combine(directory, fileName), $"Sheet {index}");
            manifest.Sheets.Add(new SheetPackageEntry
            {
                SheetId = $"L{index}",
                Number = $"AR-{index:00}",
                Name = $"Test sheet {index}",
                PdfFileName = fileName,
            });
        }

        return SheetPackageWriter.Write(manifest, directory, "test");
    }

    private static string WriteSourcePackage(
        string directory,
        string sourceId,
        IReadOnlyList<string> sheetIds,
        SheetPackageScope packageScope,
        DateTimeOffset exportedAtUtc)
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
            PackageScope = packageScope,
            ExportedAtUtc = exportedAtUtc,
        };
        foreach (var sheetId in sheetIds)
        {
            var fileName = $"{sheetId}.pdf";
            WriteMinimalPdf(Path.Combine(directory, fileName), sheetId);
            manifest.Sheets.Add(new SheetPackageEntry
            {
                SheetId = sheetId,
                Number = sheetId,
                Name = sheetId,
                PdfFileName = fileName,
            });
        }
        return SheetPackageWriter.Write(manifest, directory, "source-package");
    }

    private static string WriteConceptSourcePackage(
        string directory,
        string sourceId,
        string documentTitle,
        IReadOnlyList<(string SheetId, string Number, string Name, string ContentKind, string BuildingId, string BuildingName)> sheets)
    {
        Directory.CreateDirectory(directory);
        var manifest = new SheetPackageManifest
        {
            Source = new SheetPackageSource
            {
                SourceId = sourceId,
                Application = SheetSourceApplication.Revit,
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
            WriteMinimalPdf(Path.Combine(directory, fileName), sheet.Name);
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
}
