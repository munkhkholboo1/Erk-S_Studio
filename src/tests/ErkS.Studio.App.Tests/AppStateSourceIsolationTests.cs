using ErkS.Platform.Core;
using ErkS.Platform.Core.ProjectTypes;
using ErkS.Platform.Pdf;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

public sealed class AppStateSourceIsolationTests : IDisposable
{
    private readonly string workDirectory = Path.Combine(
        Path.GetTempPath(),
        "erks-studio-source-isolation-tests",
        Guid.NewGuid().ToString("N"));

    public AppStateSourceIsolationTests()
    {
        Directory.CreateDirectory(workDirectory);
    }

    [Fact]
    public void OpenProject_SourceFreeWorkspacePrunesPersistedSourcePages()
    {
        var (projectPath, _) = WriteProject(
            sources: [],
            pageKeys: ["foreign-source|sheet-01"],
            lastPdfPath: "albums/stale.pdf");
        using var state = new AppState();

        state.OpenProject(projectPath);

        Assert.Empty(state.Project.Sources);
        Assert.Empty(state.Album.Pages);
        Assert.Empty(state.Project.PrimaryAlbum.LastPdfPath);
        StudioAlbumDocument persisted = StudioAlbumDocumentStore.Load(state.AlbumPath!);
        Assert.Empty(persisted.Definition.Pages);
    }

    [Fact]
    public void OpenProject_SourceFreeCloudMirrorPreservesCollaboratorPages()
    {
        var (projectPath, _) = WriteProject(
            sources: [],
            pageKeys: ["remote-source|sheet-01"],
            lastPdfPath: "albums/cloud/current.pdf",
            cloudMirror: true);
        using var state = new AppState();

        state.OpenProject(projectPath);

        Assert.Empty(state.Project.Sources);
        Assert.Equal("remote-source|sheet-01", Assert.Single(state.Album.Pages).SheetKey);
        Assert.Equal("albums/cloud/current.pdf", state.Project.PrimaryAlbum.LastPdfPath);
        StudioAlbumDocument persisted = StudioAlbumDocumentStore.Load(state.AlbumPath!);
        Assert.Equal("remote-source|sheet-01", Assert.Single(persisted.Definition.Pages).SheetKey);
    }

    [Fact]
    public void CreateAlbumBuildProject_UsesCanonicalSnapshotWhenLegacyLocalFieldsAreEmpty()
    {
        var (projectPath, _) = WriteProject(
            sources: [],
            pageKeys: [],
            lastPdfPath: "",
            cloudMirror: true);
        ProjectWorkspace project = ProjectWorkspaceStore.Load(projectPath);
        project.Identity.Name = "";
        project.Foundation.InitiationBasis.SiteAddress = "";
        project.Cloud.ServerSnapshot = new ProjectServerSnapshot
        {
            Name = "Canonical project name",
            DesignOrganizationName = "Canonical design company",
            Information = new ProjectServerInformation
            {
                Name = "Canonical project name",
                Location = "Canonical project address",
            },
            Foundation = new ProjectServerFoundation
            {
                IsAvailable = true,
                InitiationBasis = new ProjectServerInitiationBasis
                {
                    SiteAddress = "Canonical project address",
                },
            },
        };
        ProjectWorkspaceStore.Save(project, projectPath);
        using var state = new AppState();
        state.OpenProject(projectPath);

        AlbumProject buildProject = state.CreateAlbumBuildProject();

        Assert.Equal("Canonical project name", buildProject.Name);
        Assert.Equal("Canonical project address", buildProject.InitiationBasis.SiteAddress);
        Assert.Equal("Canonical design company", buildProject.DesignOrganizationName);
    }

    [Fact]
    public void OpenProject_RecoversSiteContextSnapshotsAndQueuesCloudComponent()
    {
        var (projectPath, _) = WriteProject(
            sources: [],
            pageKeys: [],
            lastPdfPath: "albums/stale.pdf");
        string assetFolder = Path.Combine(
            ProjectWorkspacePaths.GetProjectFolder(projectPath),
            "assets",
            "site-context");
        Directory.CreateDirectory(assetFolder);
        byte[] onePixelPng = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
        File.WriteAllBytes(Path.Combine(assetFolder, "location-scheme.png"), onePixelPng);
        File.WriteAllBytes(Path.Combine(assetFolder, "surroundings-overview.png"), onePixelPng);
        using var state = new AppState();

        state.OpenProject(projectPath);

        Assert.True(state.Project.SiteContext.LocationScheme.HasSnapshot);
        Assert.True(state.Project.SiteContext.SurroundingsOverview.HasSnapshot);
        Assert.Empty(state.Project.PrimaryAlbum.LastPdfPath);
        Assert.Contains(
            ProjectCloudSyncMetadata.SiteContextComponentCode,
            ProjectCloudSyncMetadata.PendingAlbumComponents(state.Project));
        ProjectWorkspace persisted = ProjectWorkspaceStore.Load(projectPath);
        Assert.Equal(
            "assets/site-context/location-scheme.png",
            persisted.SiteContext.LocationScheme.SnapshotRelativePath);
        Assert.Contains(
            ProjectCloudSyncMetadata.SiteContextComponentCode,
            ProjectCloudSyncMetadata.PendingAlbumComponents(persisted));
    }

    [Fact]
    public void RemoveDesignSource_RemovesOnlyThatSourcesAlbumPages()
    {
        var sourceA = CreateSource("source-a", "Same name.rvt");
        var sourceB = CreateSource("source-b", "Same name.rvt");
        var (projectPath, _) = WriteProject(
            sources: [sourceA, sourceB],
            pageKeys: ["source-a|sheet-01", "source-b|sheet-01"],
            lastPdfPath: "albums/stale.pdf");
        using var state = new AppState();
        state.OpenProject(projectPath);

        int removed = state.RemoveDesignSource(state.Project.Sources.Single(item => item.Id == "source-a"));

        Assert.Equal(1, removed);
        Assert.Equal("source-b", Assert.Single(state.Project.Sources).Id);
        Assert.Equal("source-b|sheet-01", Assert.Single(state.Album.Pages).SheetKey);
        Assert.Empty(state.Project.PrimaryAlbum.LastPdfPath);
    }

    [Fact]
    public void RemoveDesignSource_LastLocalSourceInCloudMirror_PreservesCollaboratorPages()
    {
        var localSource = CreateSource("local-source", "Local building.rvt");
        var (projectPath, albumPath) = WriteProject(
            sources: [localSource],
            pageKeys:
            [
                "local-source|sheet-01",
                "remote-source|sheet-01",
            ],
            lastPdfPath: "albums/cloud/current.pdf",
            cloudMirror: true);
        StudioAlbumDocument seededAlbum = StudioAlbumDocumentStore.Load(albumPath);
        AlbumSection sourceSection = seededAlbum.Definition.Sections.First();
        sourceSection.SheetKeys.Add("local-source|sheet-01");
        sourceSection.SheetKeys.Add("remote-source|sheet-01");
        StudioAlbumDocumentStore.Save(seededAlbum, albumPath);
        using var state = new AppState();
        state.OpenProject(projectPath);

        int removed = state.RemoveDesignSource(
            Assert.Single(state.Project.Sources));

        Assert.Equal(1, removed);
        Assert.Empty(state.Project.Sources);
        Assert.Equal(
            "remote-source|sheet-01",
            Assert.Single(state.Album.Pages).SheetKey);
        Assert.Equal(
            "remote-source|sheet-01",
            Assert.Single(state.Album.Sections.First().SheetKeys));
        Assert.Empty(state.Project.PrimaryAlbum.LastPdfPath);

        StudioAlbumDocument persisted =
            StudioAlbumDocumentStore.Load(state.AlbumPath!);
        Assert.Equal(
            "remote-source|sheet-01",
            Assert.Single(persisted.Definition.Pages).SheetKey);
        Assert.Equal(
            "remote-source|sheet-01",
            Assert.Single(persisted.Definition.Sections.First().SheetKeys));
    }

    [Fact]
    public void RemoveDesignSource_WhenCityGenSourceRemoved_ClearsSiteContextAndDeletesSnapshots()
    {
        string projectFolder = Path.Combine(workDirectory, "citygen-remove-app");
        Directory.CreateDirectory(projectFolder);
        string drawingPath = Path.Combine(projectFolder, "site.dwg");
        string sidecarPath = Path.Combine(projectFolder, "site.erks-citygen-site.json");
        File.WriteAllText(drawingPath, "drawing content");
        File.WriteAllText(sidecarPath, """
            {
              "schema": "erks.citygen.project-site",
              "schemaVersion": 1,
              "sourceCrs": {
                "authority": "EPSG",
                "name": "UTM84-48N",
                "epsg": 32648
              },
              "coordinateMode": "direct-utm",
              "areaSquareMeters": 15000.0,
              "sourceDocument": {
                "name": "site.dwg"
              },
              "geometry": {
                "type": "Polygon",
                "coordinates": [
                  [
                    [106.90, 47.90],
                    [106.91, 47.90],
                    [106.91, 47.91],
                    [106.90, 47.91],
                    [106.90, 47.90]
                  ]
                ]
              },
              "updatedAtUtc": "2026-07-22T12:00:00Z"
            }
            """);

        var source = new ProjectDesignSource
        {
            Id = "citygen-src",
            Kind = DesignSourceKind.CityGen,
            Name = "CityGen source",
            NativeDocumentTitle = "site.dwg",
            NativeDocumentPath = drawingPath,
            InboxFolder = Path.Combine(projectFolder, "inbox"),
        };
        StudioLocalSourceBindingPolicy.Bind(source, "architect@erks.local", "device-1");

        var (projectPath, _) = WriteProject(
            sources: [source],
            pageKeys: ["citygen-src|sheet-01"],
            lastPdfPath: "albums/stale.pdf");

        string assetFolder = Path.Combine(
            ProjectWorkspacePaths.GetProjectFolder(projectPath),
            "assets",
            "site-context");
        Directory.CreateDirectory(assetFolder);
        byte[] onePixelPng = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
        string locationPng = Path.Combine(assetFolder, "location-scheme.png");
        string overviewPng = Path.Combine(assetFolder, "surroundings-overview.png");
        File.WriteAllBytes(locationPng, onePixelPng);
        File.WriteAllBytes(overviewPng, onePixelPng);

        using var state = new AppState();
        state.ConfigureSourceRuntimeContext("architect@erks.local", "device-1");
        state.OpenProject(projectPath);

        Assert.True(state.Project.SiteContext.Boundary.HasGeometry);
        Assert.Equal("citygen-src", state.Project.SiteContext.Boundary.SourceId);
        state.Project.SiteContext.LocationScheme.SnapshotRelativePath = "assets/site-context/location-scheme.png";
        state.Project.SiteContext.SurroundingsOverview.SnapshotRelativePath = "assets/site-context/surroundings-overview.png";
        Assert.True(state.Project.SiteContext.LocationScheme.HasSnapshot);
        Assert.True(File.Exists(locationPng));
        Assert.True(File.Exists(overviewPng));

        state.RemoveDesignSource(state.Project.Sources.Single(item => item.Id == "citygen-src"));

        Assert.Empty(state.Project.Sources);
        Assert.False(state.Project.SiteContext.Boundary.HasGeometry);
        Assert.Empty(state.Project.SiteContext.Boundary.SourceId);
        Assert.False(state.Project.SiteContext.LocationScheme.HasSnapshot);
        Assert.False(state.Project.SiteContext.SurroundingsOverview.HasSnapshot);
        Assert.False(File.Exists(locationPng));
        Assert.False(File.Exists(overviewPng));

        ProjectWorkspace reloaded = ProjectWorkspaceStore.Load(projectPath);
        Assert.False(reloaded.SiteContext.Boundary.HasGeometry);
        Assert.False(reloaded.SiteContext.LocationScheme.HasSnapshot);
        Assert.False(reloaded.SiteContext.SurroundingsOverview.HasSnapshot);
    }

    [Fact]
    public void AddDesignSource_SameFileNameInDifferentLocationsRemainsDistinct()
    {
        var (projectPath, _) = WriteProject(sources: [], pageKeys: [], lastPdfPath: "");
        using var state = new AppState();
        state.OpenProject(projectPath);
        ProjectDesignSource first = CreateSource("source-a", "Same name.rvt");
        ProjectDesignSource second = CreateSource("source-b", "Same name.rvt");
        second.InboxFolder = first.InboxFolder;

        state.AddDesignSource(first);
        state.AddDesignSource(second);

        Assert.Equal(2, state.Project.Sources.Count);
        Assert.Equal(2, state.Project.Sources.Select(source => source.Id).Distinct().Count());
        Assert.Equal(2, state.Project.Sources.Select(source => source.InboxFolder).Distinct().Count());
        Assert.All(state.Project.Sources, source => Assert.Equal("Same name.rvt", source.NativeDocumentTitle));
    }

    [Fact]
    public void LinkCloudProject_PersistsEveryContributorsMetadataOnlySourceSlot()
    {
        var (projectPath, _) = WriteProject(sources: [], pageKeys: [], lastPdfPath: "");
        using var state = new AppState();
        state.OpenProject(projectPath);
        const string ownerA = "architect-a@erks.local";
        const string ownerB = "architect-b@erks.local";
        const string sourceKey = "shared-building";
        string codeA = StudioAlbumComponentIdentity.SourceCode(ownerA, sourceKey);
        string codeB = StudioAlbumComponentIdentity.SourceCode(ownerB, sourceKey);
        var cloud = new StudioCloudProjectDetail
        {
            Project = new StudioCloudProjectSummary
            {
                ProjectId = "cloud-project-1",
                ProjectCode = "CLOUD-001",
                Name = "Shared source project",
                CurrentStage = "ConceptDesign",
                CurrentUserRoles = ["Architect"],
                CurrentUserScopes = ["concept.write"],
                ConcurrencyToken = "token-1",
            },
            DesignPackages =
            [
                new StudioCloudDesignPackage
                {
                    SourcePackages =
                    [
                        CloudSource("source-a", sourceKey, ownerA),
                        CloudSource("source-b", sourceKey, ownerB),
                    ],
                },
            ],
            Albums =
            [
                new StudioCloudAlbum
                {
                    AlbumId = "album-1",
                    CurrentRevisionId = "revision-1",
                    Revisions =
                    [
                        new StudioCloudAlbumRevision
                        {
                            RevisionId = "revision-1",
                            PageCount = 3,
                            SectionManifest =
                            [
                                new StudioCloudAlbumSection
                                {
                                    Code = "generated:cover:Cover",
                                    Label = "Нүүр хуудас",
                                    Order = 0,
                                    PageNumbers = [1],
                                    ComponentKind = StudioAlbumComponentIdentity.GeneratedComponentKind,
                                },
                                CloudSourceSection(codeA, ownerA, sourceKey, 100, 2),
                                CloudSourceSection(codeB, ownerB, sourceKey, 110, 3),
                            ],
                        },
                    ],
                },
            ],
        };

        state.LinkCurrentProjectToCloud(
            cloud,
            "https://erk-s.mn",
            " Architect-A@ERKS.Local ");

        Assert.Empty(state.Project.Sources);
        Assert.Equal(
            "architect-a@erks.local",
            state.Project.Cloud.PermissionSnapshotAccountEmail);
        Assert.True(state.Project.Cloud.HasScope(
            "concept.write",
            "architect-a@erks.local"));
        Assert.False(state.Project.Cloud.HasScope(
            "concept.write",
            ownerB));
        Assert.Equal(2, state.Project.Cloud.SharedSources.Count);
        Assert.Equal([ownerA, ownerB], state.Project.Cloud.SharedSources
            .Select(source => source.OwnerEmail)
            .Order()
            .ToArray());
        Assert.Equal(2, state.Project.Cloud.SharedAlbumComponents.Count(component =>
            component.ComponentKind.Equals(
                StudioAlbumComponentIdentity.SourceComponentKind,
                StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(
            ["album-page:" + ownerA, "album-page:" + ownerB],
            state.Project.Cloud.SharedAlbumComponents
                .Where(component => component.Pages.Count > 0)
                .Select(component => Assert.Single(component.Pages).PageKey)
                .Order()
                .ToArray());
        ProjectWorkspace persisted = ProjectWorkspaceStore.Load(projectPath);
        Assert.Equal(
            "architect-a@erks.local",
            persisted.Cloud.PermissionSnapshotAccountEmail);
        Assert.Equal(2, persisted.Cloud.SharedSources.Count);
        Assert.Contains(persisted.Cloud.SharedAlbumComponents, component => component.Code == codeA);
        Assert.Contains(persisted.Cloud.SharedAlbumComponents, component => component.Code == codeB);
        Assert.Equal(
            ["album-page:" + ownerA, "album-page:" + ownerB],
            persisted.Cloud.SharedAlbumComponents
                .Where(component => component.Pages.Count > 0)
                .Select(component => Assert.Single(component.Pages).PageKey)
                .Order()
                .ToArray());
    }

    [Fact]
    public void LinkCloudProject_ReplacesPermissionSnapshotForTheAuthenticatedAccount()
    {
        var (projectPath, _) = WriteProject(sources: [], pageKeys: [], lastPdfPath: "");
        using var state = new AppState();
        state.OpenProject(projectPath);
        var cloud = new StudioCloudProjectDetail
        {
            Project = new StudioCloudProjectSummary
            {
                ProjectId = "cloud-project-1",
                ProjectCode = "CLOUD-001",
                Name = "Account-bound permission project",
                CurrentStage = "ConceptDesign",
                CurrentUserRoles = ["Architect"],
                CurrentUserScopes = ["concept.write"],
                ConcurrencyToken = "token-1",
            },
        };

        state.LinkCurrentProjectToCloud(
            cloud,
            "https://erk-s.mn",
            "account-a@example.com");

        cloud.Project.CurrentUserRoles = ["ProjectAdmin"];
        cloud.Project.CurrentUserScopes = ["team.manage"];
        cloud.Project.ConcurrencyToken = "token-2";
        state.LinkCurrentProjectToCloud(
            cloud,
            "https://erk-s.mn",
            "ACCOUNT-B@example.com",
            preserveCreation: true,
            preserveSyncState: true);

        Assert.Equal(
            "account-b@example.com",
            state.Project.Cloud.PermissionSnapshotAccountEmail);
        Assert.False(state.Project.Cloud.HasScope(
            "concept.write",
            "account-a@example.com"));
        Assert.True(state.Project.Cloud.HasScope(
            "team.manage",
            "account-b@example.com"));
        Assert.Equal(["ProjectAdmin"], state.Project.Cloud.CurrentUserRoles);
        Assert.Equal(["team.manage"], state.Project.Cloud.CurrentUserScopes);

        ProjectWorkspace persisted = ProjectWorkspaceStore.Load(projectPath);
        Assert.Equal(
            "account-b@example.com",
            persisted.Cloud.PermissionSnapshotAccountEmail);
        Assert.Equal(["team.manage"], persisted.Cloud.CurrentUserScopes);
    }

    [Fact]
    public void LinkCloudProject_PreserveCreationKeepsTheLocallySelectedStage()
    {
        var (projectPath, albumPath) = WriteProject(
            sources: [],
            pageKeys: [],
            lastPdfPath: "");
        ProjectWorkspace project = ProjectWorkspaceStore.Load(projectPath);
        project.Identity.ProjectType = BuildingDesignProjectType.TypeId;
        project.Identity.StageCode = "working-drawings";
        project.Identity.StageName = "Ажлын зураг";
        ProjectWorkspaceStore.Save(project, projectPath);
        StudioAlbumDocumentStore.Save(new StudioAlbumDocument
        {
            ProjectId = project.ProjectId,
            AlbumId = project.PrimaryAlbum.Id,
            PackageType = BuildingDesignProjectType.TypeId,
            StageCode = "working-drawings",
            Definition = BuildingWorkingDrawingAlbumTemplate.CreateDefinition(
                "Барилга архитектурын загвар зургийн альбум"),
        }, albumPath);
        using var state = new AppState();
        state.OpenProject(projectPath);
        var staleCloud = new StudioCloudProjectDetail
        {
            Project = new StudioCloudProjectSummary
            {
                ProjectId = "cloud-project-1",
                ProjectCode = "TEST-001",
                Name = "Source isolation test",
                ProjectDomain = BuildingDesignProjectType.TypeId,
                StageType = "model-design",
                CurrentStage = "model-design",
                ConcurrencyToken = "token-1",
            },
            ProjectInformation = new StudioCloudProjectInformation
            {
                ProjectId = "cloud-project-1",
                ProjectDomain = BuildingDesignProjectType.TypeId,
                StageType = "model-design",
            },
        };

        state.LinkCurrentProjectToCloud(
            staleCloud,
            "https://erk-s.mn",
            "architect@example.com",
            preserveCreation: true,
            preserveSyncState: true);

        Assert.Equal(BuildingDesignProjectType.TypeId, state.Project.Identity.ProjectType);
        Assert.Equal("working-drawings", state.Project.Identity.StageCode);
        Assert.Equal("Ажлын зураг", state.Project.Identity.StageName);
        Assert.Equal("working-drawings", state.AlbumDocument.StageCode);
        Assert.Equal(BuildingWorkingDrawingAlbumTemplate.TemplateId, state.Album.TemplateId);
        Assert.Equal(BuildingWorkingDrawingAlbumTemplate.DefaultTitle, state.Album.Title);
    }

    [Fact]
    public void LinkCloudProject_MirrorVersionOnlyChangeDoesNotReorderLocalAlbumPages()
    {
        const string sourceId = "general-plan";
        var source = new ProjectDesignSource
        {
            Id = sourceId,
            Kind = DesignSourceKind.CityGen,
            Name = "General plan",
            NativeDocumentTitle = "GeneralPlan.dwg",
            InboxFolder = Path.Combine(
                workDirectory,
                "sources",
                sourceId,
                "deliveries"),
        };
        var (projectPath, albumPath) = WriteProject(
            sources: [source],
            pageKeys: [sourceId + "|master", sourceId + "|traffic"],
            lastPdfPath: "",
            cloudMirror: true);
        ProjectWorkspace project = ProjectWorkspaceStore.Load(projectPath);
        project.Cloud.SharedBuildingCompositionVersion = 1;
        ProjectWorkspaceStore.Save(project, projectPath);
        StudioAlbumDocument album = StudioAlbumDocumentStore.Load(albumPath);
        album.Definition.Pages[0].TemplateSlotId = "master-plan";
        album.Definition.Pages[1].TemplateSlotId = "traffic-scheme";
        StudioAlbumDocumentStore.Save(album, albumPath);
        using var state = new AppState();
        state.OpenProject(projectPath);
        var cloud = new StudioCloudProjectDetail
        {
            Project = new StudioCloudProjectSummary
            {
                ProjectId = "cloud-project-1",
                ProjectCode = "TEST-001",
                Name = "Source isolation test",
                CurrentStage = "ConceptDesign",
                ConcurrencyToken = "token-2",
            },
            BuildingComposition = new StudioCloudBuildingComposition
            {
                Version = 2,
                Groups = [],
                SheetAssignments = [],
            },
        };

        state.LinkCurrentProjectToCloud(
            cloud,
            "https://erk-s.mn",
            "architect@example.com",
            preserveCreation: true,
            preserveSyncState: true);

        Assert.Equal(
            [sourceId + "|master", sourceId + "|traffic"],
            state.Album.Pages.Select(page => page.SheetKey));
        StudioAlbumDocument persisted =
            StudioAlbumDocumentStore.Load(state.AlbumPath!);
        Assert.Equal(
            [sourceId + "|master", sourceId + "|traffic"],
            persisted.Definition.Pages.Select(page => page.SheetKey));
    }

    [Fact]
    public void ReconcileProjectAssetSources_MissingAtdInvalidatesBuiltAlbum()
    {
        string sourcePath = Path.Combine(workDirectory, "approved-atd.png");
        File.WriteAllBytes(
            sourcePath,
            Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="));
        ProjectWorkspace project = ProjectWorkspaceStore.Create("TEST-002", "Asset source test");
        string projectFolder = Path.Combine(workDirectory, "asset-project");
        string projectPath = Path.Combine(projectFolder, ProjectWorkspace.DefaultFileName);
        ProjectDocumentAssetInspection inspection = ProjectDocumentAssetInspector.Inspect(sourcePath);
        project.Foundation.PlanningTask.Documents.Add(new ProjectFileReference
        {
            Category = ProjectDocumentCategories.ApprovedPlanningTask,
            RelativePath = ProjectDocumentFileStore.StoreInsideProject(
                projectPath,
                ProjectDocumentCategories.ApprovedPlanningTask,
                sourcePath),
            OriginalFileName = Path.GetFileName(sourcePath),
            LinkedSourcePath = Path.GetFullPath(sourcePath),
            ContentType = inspection.ContentType,
            SizeBytes = inspection.SizeBytes,
            PageCount = inspection.PageCount,
            Sha256 = inspection.Sha256,
        });
        project.PrimaryAlbum.LastPdfPath = "albums/stale.pdf";
        string albumPath = ProjectWorkspacePaths.ResolveInsideProject(
            projectPath,
            project.PrimaryAlbum.DocumentPath);
        ProjectWorkspaceStore.Save(project, projectPath);
        StudioAlbumDocumentStore.Save(new StudioAlbumDocument
        {
            ProjectId = project.ProjectId,
            AlbumId = project.PrimaryAlbum.Id,
            Definition = BuildingArchitectureConceptAlbumTemplate.CreateDefinition(project.PrimaryAlbum.Title),
        }, albumPath);
        using var state = new AppState();
        state.OpenProject(projectPath);

        File.Delete(sourcePath);
        ProjectAssetSourceReconciliationResult result = state.ReconcileProjectAssetSources();

        Assert.Equal(1, result.MissingDocumentCount);
        Assert.False(Assert.Single(state.Project.Foundation.PlanningTask.Documents).IsAvailable);
        Assert.Empty(state.Project.PrimaryAlbum.LastPdfPath);
        ProjectWorkspace persisted = ProjectWorkspaceStore.Load(projectPath);
        Assert.False(Assert.Single(persisted.Foundation.PlanningTask.Documents).IsAvailable);
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

    private (string ProjectPath, string AlbumPath) WriteProject(
        IReadOnlyList<ProjectDesignSource> sources,
        IReadOnlyList<string> pageKeys,
        string lastPdfPath,
        bool cloudMirror = false)
    {
        ProjectWorkspace project = ProjectWorkspaceStore.Create("TEST-001", "Source isolation test");
        project.Sources = sources.ToList();
        project.PrimaryAlbum.LastPdfPath = lastPdfPath;
        if (cloudMirror)
        {
            project.Cloud.Origin = ProjectOrigins.Cloud;
            project.Cloud.ServerProjectId = "cloud-project-1";
            project.Cloud.ServerUrl = "https://erk-s.mn";
        }
        string projectPath = Path.Combine(workDirectory, ProjectWorkspace.DefaultFileName);
        string albumPath = ProjectWorkspacePaths.ResolveInsideProject(
            projectPath,
            project.PrimaryAlbum.DocumentPath);
        var album = new StudioAlbumDocument
        {
            ProjectId = project.ProjectId,
            AlbumId = project.PrimaryAlbum.Id,
            Definition = BuildingArchitectureConceptAlbumTemplate.CreateDefinition(project.PrimaryAlbum.Title),
        };
        foreach (string pageKey in pageKeys)
        {
            album.Definition.Pages.Add(new AlbumPageDefinition { SheetKey = pageKey });
        }

        ProjectWorkspaceStore.Save(project, projectPath);
        StudioAlbumDocumentStore.Save(album, albumPath);
        return (projectPath, albumPath);
    }

    private ProjectDesignSource CreateSource(string id, string fileName)
    {
        string sourceFolder = Path.Combine(workDirectory, "sources", id, "deliveries");
        return new ProjectDesignSource
        {
            Id = id,
            Kind = DesignSourceKind.Revit,
            Name = fileName,
            NativeDocumentTitle = fileName,
            NativeDocumentPath = Path.Combine(workDirectory, "native", id, fileName),
            InboxFolder = sourceFolder,
        };
    }

    private static StudioCloudSourcePackage CloudSource(
        string sourceId,
        string sourceKey,
        string ownerEmail) => new()
        {
            SourceId = sourceId,
            SourceKey = sourceKey,
            SourceApplication = "Revit",
            SourceDocumentReference = "Shared building.rvt",
            ManifestId = "manifest-" + sourceId,
            ContentHash = "hash-" + sourceId,
            Status = "Registered",
            RegisteredBy = ownerEmail,
            RegisteredAtUtc = DateTimeOffset.UtcNow,
        };

    private static StudioCloudAlbumSection CloudSourceSection(
        string code,
        string ownerEmail,
        string sourceKey,
        int order,
        int page) => new()
        {
            Code = code,
            Label = "Shared building",
            Order = order,
            PageNumbers = [page],
            Status = "Available",
            OwnerEmail = ownerEmail,
            SourceKey = sourceKey,
            ComponentKind = StudioAlbumComponentIdentity.SourceComponentKind,
            Pages =
            [
                new StudioCloudAlbumComponentPage
                {
                    PageNumber = page,
                    PageKey = "album-page:" + ownerEmail,
                    SortKey = "A-" + page,
                    SequenceKey = "floor-plans",
                },
            ],
        };
}
