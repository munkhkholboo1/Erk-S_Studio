using ErkS.Platform.Core;
using ErkS.Platform.Pdf;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ErkS.Platform.Core.Tests;

public sealed class VisualizationPageLayoutPlannerTests : IDisposable
{
    private readonly string workDirectory = Path.Combine(
        Path.GetTempPath(),
        "erks-visualization-layout-" + Guid.NewGuid().ToString("N"));

    public VisualizationPageLayoutPlannerTests()
    {
        Directory.CreateDirectory(workDirectory);
    }

    [Fact]
    public void Create_UsesConfiguredImagesPerPageAndContinuousStudioNumbers()
    {
        var source = CreateSource(3, 1600, 900);
        source.ImagesPerPage = 2;

        IReadOnlyList<VisualizationAlbumPagePlan> pages =
            VisualizationPageLayoutPlanner.Create(source, firstPageNumber: 12);

        Assert.Equal(2, pages.Count);
        Assert.Equal(new[] { "12", "13" }, pages.Select(page => page.Number));
        Assert.Equal(2, pages[0].Tiles.Count);
        Assert.Single(pages[1].Tiles);
        Assert.Equal(source.Images.Select(image => image.Id), pages.SelectMany(page => page.Tiles).Select(tile => tile.Image.Id));
    }

    [Fact]
    public void Create_ExcludesInactiveImagesAndReflowsRemainingImagesAcrossPages()
    {
        var source = CreateSource(5, 1600, 900);
        source.ImagesPerPage = 2;
        source.Images[1].IsIncludedInAlbum = false;

        IReadOnlyList<VisualizationAlbumPagePlan> pages =
            VisualizationPageLayoutPlanner.Create(source, firstPageNumber: 12);

        Assert.Equal(2, pages.Count);
        Assert.Equal(new[] { "12", "13" }, pages.Select(page => page.Number));
        Assert.All(pages, page => Assert.Equal(2, page.Tiles.Count));
        Assert.Equal(
            new[] { "image-1", "image-3", "image-4", "image-5" },
            pages.SelectMany(page => page.Tiles).Select(tile => tile.Image.Id));

        source.Images[1].IsIncludedInAlbum = true;
        IReadOnlyList<VisualizationAlbumPagePlan> restored =
            VisualizationPageLayoutPlanner.Create(source, firstPageNumber: 12);

        Assert.Equal(3, restored.Count);
        Assert.Equal(source.Images.Select(image => image.Id),
            restored.SelectMany(page => page.Tiles).Select(tile => tile.Image.Id));
    }

    [Fact]
    public void Create_ChoosesWideRowsForLandscapeImages()
    {
        var source = CreateSource(2, 1920, 1080);
        source.ImagesPerPage = 2;

        VisualizationAlbumPagePlan page = Assert.Single(
            VisualizationPageLayoutPlanner.Create(source, firstPageNumber: 9));

        Assert.All(page.Tiles, tile => Assert.True(tile.Frame.Width > tile.Frame.Height));
        Assert.True(page.Tiles[1].Frame.Y > page.Tiles[0].Frame.Y);
    }

    [Fact]
    public void Create_ChoosesColumnsForPortraitImages()
    {
        var source = CreateSource(2, 900, 1600);
        source.ImagesPerPage = 2;

        VisualizationAlbumPagePlan page = Assert.Single(
            VisualizationPageLayoutPlanner.Create(source, firstPageNumber: 9));

        Assert.All(page.Tiles, tile => Assert.True(tile.Frame.Height > tile.Frame.Width));
        Assert.True(page.Tiles[1].Frame.X > page.Tiles[0].Frame.X);
    }

    [Fact]
    public void Create_CropsOnlyWhenLossStaysBelowSafetyLimit()
    {
        var nearPageRatio = CreateSource(1, 1700, 1000);
        var panoramic = CreateSource(1, 4000, 1000);

        VisualizationImageTilePlan cropped = Assert.Single(
            Assert.Single(VisualizationPageLayoutPlanner.Create(nearPageRatio, 9)).Tiles);
        VisualizationImageTilePlan contained = Assert.Single(
            Assert.Single(VisualizationPageLayoutPlanner.Create(panoramic, 9)).Tiles);

        Assert.Equal(VisualizationImageFitMode.CenterCrop, cropped.FitMode);
        Assert.InRange(cropped.CropFraction, 0, VisualizationPageLayoutPlanner.MaximumCropFraction);
        Assert.Equal(VisualizationImageFitMode.Contain, contained.FitMode);
        Assert.True(contained.CropFraction > VisualizationPageLayoutPlanner.MaximumCropFraction);
    }

    [Fact]
    public void WorkspaceRoundTrip_PreservesVisualizationSourceAndNormalizesSettings()
    {
        string projectPath = Path.Combine(workDirectory, ProjectWorkspace.DefaultFileName);
        ProjectWorkspace project = ProjectWorkspaceStore.Create("VIS-001", "Харагдах байдлын төсөл");
        project.Visualizations.ImagesPerPage = 99;
        project.Visualizations.Images.Add(new ProjectVisualizationImage
        {
            OriginalFileName = "exterior.png",
            RelativePath = "sources/visualizations/images/exterior.png",
            PixelWidth = 1800,
            PixelHeight = 1200,
            Sha256 = "abc",
            IsIncludedInAlbum = false,
        });

        ProjectWorkspaceStore.Save(project, projectPath);
        ProjectWorkspace loaded = ProjectWorkspaceStore.Load(projectPath);

        Assert.Equal(VisualizationPageLayoutPlanner.MaximumImagesPerPage, loaded.Visualizations.ImagesPerPage);
        Assert.True(loaded.Visualizations.IsConfiguredForProject(loaded.ProjectId));
        Assert.Equal(loaded.ProjectId, loaded.Visualizations.OwnerProjectId);
        ProjectVisualizationImage image = Assert.Single(loaded.Visualizations.Images);
        Assert.Equal(loaded.ProjectId, image.OwnerProjectId);
        Assert.Equal("exterior.png", image.OriginalFileName);
        Assert.False(image.IsIncludedInAlbum);
        Assert.Equal(0.5, image.FocalPointX);
        Assert.Equal(0.5, image.FocalPointY);
    }

    [Fact]
    public void WorkspaceLoad_DefaultsLegacyVisualizationImageToIncluded()
    {
        string projectPath = Path.Combine(workDirectory, "legacy-visualization.erksproject");
        ProjectWorkspace project = ProjectWorkspaceStore.Create("VIS-LEGACY", "Legacy visualization");
        project.Visualizations.Images.Add(new ProjectVisualizationImage
        {
            OriginalFileName = "legacy.jpg",
            RelativePath = "sources/visualizations/images/legacy.jpg",
            PixelWidth = 1600,
            PixelHeight = 900,
            Sha256 = "legacy",
        });
        ProjectWorkspaceStore.Save(project, projectPath);

        JsonObject root = JsonNode.Parse(File.ReadAllText(projectPath))!.AsObject();
        JsonObject image = root["visualizations"]!["images"]![0]!.AsObject();
        Assert.True(image.Remove("isIncludedInAlbum"));
        File.WriteAllText(
            projectPath,
            root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        ProjectWorkspace loaded = ProjectWorkspaceStore.Load(projectPath);

        Assert.True(Assert.Single(loaded.Visualizations.Images).IsIncludedInAlbum);
    }

    [Fact]
    public void ProjectSnapshot_RejectsVisualizationSourceOwnedByAnotherProject()
    {
        var source = new ProjectVisualizationSource();
        source.ConfigureForProject("project-a");
        source.Images.Add(new ProjectVisualizationImage
        {
            OwnerProjectId = "project-a",
            OriginalFileName = "project-a.jpg",
            RelativePath = "sources/visualizations/images/project-a.jpg",
            PixelWidth = 1600,
            PixelHeight = 900,
        });

        ProjectVisualizationSource projectA = source.CreateProjectSnapshot("project-a");
        ProjectVisualizationSource projectB = source.CreateProjectSnapshot("project-b");

        Assert.True(projectA.IsConfiguredForProject("project-a"));
        Assert.Single(projectA.Images);
        Assert.False(projectB.IsConfiguredForProject("project-b"));
        Assert.Empty(projectB.Images);
        Assert.Empty(VisualizationPageLayoutPlanner.Create(projectB, 12));
    }

    [Fact]
    public void ProjectScopedPlanner_RejectsForeignAndOwnerlessVisualizationImages()
    {
        var foreign = new ProjectVisualizationSource();
        foreign.ConfigureForProject("project-a");
        foreign.Images.Add(new ProjectVisualizationImage
        {
            OwnerProjectId = "project-a",
            OriginalFileName = "project-a.jpg",
            RelativePath = "sources/visualizations/images/project-a.jpg",
            PixelWidth = 1600,
            PixelHeight = 900,
        });
        var ownerless = CreateSource(1, 1600, 900);

        Assert.Empty(VisualizationPageLayoutPlanner.Create(foreign, "project-b", 12));
        Assert.Empty(VisualizationPageLayoutPlanner.Create(ownerless, "project-b", 12));
        Assert.Single(VisualizationPageLayoutPlanner.Create(foreign, "project-a", 12));
    }

    [Fact]
    public void NewWorkspace_DoesNotExposeVisualizationSourceUntilProjectConfiguresIt()
    {
        ProjectWorkspace project = ProjectWorkspaceStore.Create("VIS-EMPTY", "Хоосон төсөл");

        Assert.False(project.Visualizations.IsConfiguredForProject(project.ProjectId));

        project.Visualizations.ConfigureForProject(project.ProjectId);

        Assert.True(project.Visualizations.IsConfiguredForProject(project.ProjectId));
        Assert.Equal(project.ProjectId, project.Visualizations.OwnerProjectId);
    }

    [Fact]
    public void ImageInspectionAndStorage_PreserveVerifiedProjectOwnedAsset()
    {
        string projectPath = Path.Combine(workDirectory, ProjectWorkspace.DefaultFileName);
        ProjectWorkspaceStore.Save(ProjectWorkspaceStore.Create("VIS-002", "Зургийн сан"), projectPath);
        string sourcePath = Path.Combine(workDirectory, "render.png");
        File.WriteAllBytes(
            sourcePath,
            Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="));

        ProjectDocumentAssetInspection inspection = ProjectDocumentAssetInspector.Inspect(sourcePath);
        string relativePath = ProjectVisualizationFileStore.StoreInsideProject(projectPath, sourcePath);
        string storedPath = ProjectWorkspacePaths.ResolveInsideProject(projectPath, relativePath);

        Assert.Equal("image/png", inspection.ContentType);
        Assert.Equal(1, inspection.PixelWidth);
        Assert.Equal(1, inspection.PixelHeight);
        Assert.StartsWith("sources/visualizations/images/", relativePath, StringComparison.Ordinal);
        Assert.True(File.Exists(storedPath));
        Assert.Equal(inspection.Sha256, ProjectVisualizationFileStore.ComputeSha256(storedPath));
    }

    [Fact]
    public void ConceptAlbumBuild_AppendsHighQualityVisualizationPages()
    {
        string imagePath = Path.Combine(workDirectory, "album-render.png");
        File.WriteAllBytes(
            imagePath,
            Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="));
        var project = new AlbumProject
        {
            Name = "Харагдах байдлын альбум",
            ProjectFolder = workDirectory,
            Album = BuildingArchitectureConceptAlbumTemplate.CreateDefinition("Загвар зургийн альбум"),
            Visualizations = new ProjectVisualizationSource
            {
                ImagesPerPage = 2,
                Images = Enumerable.Range(1, 3).Select(index => new ProjectVisualizationImage
                {
                    Id = $"render-{index}",
                    OriginalFileName = $"render-{index}.png",
                    RelativePath = Path.GetFileName(imagePath),
                    ContentType = "image/png",
                    PixelWidth = 1600,
                    PixelHeight = 900,
                    Sha256 = $"hash-{index}",
                }).ToList(),
            },
        };
        string outputPath = Path.Combine(workDirectory, "visualization-album.pdf");

        AlbumBuildResult result = new AlbumBuilder(new PdfSharpAlbumWriter())
            .Build(project, new SheetLibrary(), outputPath);

        Assert.Equal(6, result.PageCount);
        using PdfDocument document = PdfReader.Open(outputPath, PdfDocumentOpenMode.Import);
        Assert.Equal(6, document.PageCount);
        PdfVectorDocumentProfile profile = PdfVectorQualityInspector.Inspect(outputPath);
        Assert.True(profile.Pages[^2].ImageXObjectCount > 0);
        Assert.True(profile.Pages[^1].ImageXObjectCount > 0);
    }

    [Fact]
    public void ConceptAlbumBuild_DoesNotAppendVisualizationPagesOwnedByAnotherProject()
    {
        string imagePath = Path.Combine(workDirectory, "foreign-render.png");
        File.WriteAllBytes(
            imagePath,
            Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="));

        var cleanProject = new AlbumProject
        {
            ProjectId = "project-b",
            Name = "Project B",
            ProjectFolder = workDirectory,
            Album = BuildingArchitectureConceptAlbumTemplate.CreateDefinition("Project B album"),
        };
        var foreignSource = new ProjectVisualizationSource();
        foreignSource.ConfigureForProject("project-a");
        foreignSource.Images.Add(new ProjectVisualizationImage
        {
            OwnerProjectId = "project-a",
            OriginalFileName = Path.GetFileName(imagePath),
            RelativePath = Path.GetFileName(imagePath),
            ContentType = "image/png",
            PixelWidth = 1600,
            PixelHeight = 900,
            Sha256 = "foreign-hash",
        });
        var contaminatedProject = new AlbumProject
        {
            ProjectId = "project-b",
            Name = "Project B",
            ProjectFolder = workDirectory,
            Album = BuildingArchitectureConceptAlbumTemplate.CreateDefinition("Project B album"),
            Visualizations = foreignSource,
        };

        AlbumBuildResult clean = new AlbumBuilder(new PdfSharpAlbumWriter()).Build(
            cleanProject,
            new SheetLibrary(),
            Path.Combine(workDirectory, "project-b-clean.pdf"));
        AlbumBuildResult contaminated = new AlbumBuilder(new PdfSharpAlbumWriter()).Build(
            contaminatedProject,
            new SheetLibrary(),
            Path.Combine(workDirectory, "project-b-foreign-source.pdf"));

        Assert.Equal(clean.PageCount, contaminated.PageCount);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(workDirectory, recursive: true);
        }
        catch
        {
            // Test cleanup must not hide the assertion that failed.
        }
    }

    private static ProjectVisualizationSource CreateSource(int count, int width, int height) => new()
    {
        Images = Enumerable.Range(1, count)
            .Select(index => new ProjectVisualizationImage
            {
                Id = $"image-{index}",
                OriginalFileName = $"image-{index}.jpg",
                RelativePath = $"sources/visualizations/images/image-{index}.jpg",
                PixelWidth = width,
                PixelHeight = height,
                Sha256 = $"hash-{index}",
            })
            .ToList(),
    };
}
