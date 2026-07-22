using ErkS.Platform.Core;
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

        state.LinkCurrentProjectToCloud(cloud, "https://erk-s.mn");

        Assert.Empty(state.Project.Sources);
        Assert.Equal(2, state.Project.Cloud.SharedSources.Count);
        Assert.Equal([ownerA, ownerB], state.Project.Cloud.SharedSources
            .Select(source => source.OwnerEmail)
            .Order()
            .ToArray());
        Assert.Equal(2, state.Project.Cloud.SharedAlbumComponents.Count(component =>
            component.ComponentKind.Equals(
                StudioAlbumComponentIdentity.SourceComponentKind,
                StringComparison.OrdinalIgnoreCase)));
        ProjectWorkspace persisted = ProjectWorkspaceStore.Load(projectPath);
        Assert.Equal(2, persisted.Cloud.SharedSources.Count);
        Assert.Contains(persisted.Cloud.SharedAlbumComponents, component => component.Code == codeA);
        Assert.Contains(persisted.Cloud.SharedAlbumComponents, component => component.Code == codeB);
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
        };
}
