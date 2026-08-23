namespace ErkS.Platform.Core.Tests;

/// <summary>
/// Revit's visuals joining the pool the boards draw from.
///
/// It is the sheet package's intake again rather than a second one beside it:
/// keyed by the source and the view's own identity, copied into the project,
/// refreshed in place. A render the user has captioned and placed on three
/// boards keeps all of that when the model changes.
/// </summary>
public sealed class VisualPackageImportTests : IDisposable
{
    private readonly string workDirectory =
        Path.Combine(Path.GetTempPath(), "erks-platform-tests", Guid.NewGuid().ToString("N"));
    private readonly string packageFolder;
    private readonly string projectPath;

    public VisualPackageImportTests()
    {
        packageFolder = Path.Combine(workDirectory, "package");
        Directory.CreateDirectory(packageFolder);
        projectPath = Path.Combine(workDirectory, "project.erksproj");
        File.WriteAllText(projectPath, "{}");
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
    public void AVisualBecomesMaterialTheBoardsCanDrawFrom()
    {
        var project = new ProjectWorkspace();
        VisualAsset render = Raster("view-1", "Шөнийн рендер");

        VisualPackageImportResult result = Import(project, Manifest(render));

        Assert.Equal(1, result.CreatedItemCount);
        ProjectPortfolioItem item = Assert.Single(project.Portfolio.Items);
        Assert.Equal(ProjectPortfolioItemKinds.Visual, item.Kind);
        Assert.Equal("Шөнийн рендер", item.Title);
        Assert.Equal(4489, item.SourceWidthPixels);
        Assert.True(File.Exists(
            ProjectWorkspacePaths.ResolveInsideProject(projectPath, item.RelativePath)));
    }

    [Fact]
    public void AVectorViewRemembersWhichPartOfItsPageIsTheDrawing()
    {
        // The whole reason Revit can keep its fixed paper sizes: it exports onto
        // a standard sheet and says where the drawing landed, so the surrounding
        // paper is not part of what anyone asked for.
        var project = new ProjectWorkspace();

        Import(project, Manifest(Vector("view-1", "Аксонометр")));

        ProjectPortfolioItem item = Assert.Single(project.Portfolio.Items);
        Assert.Equal(60d / 420d, item.SourceCropX, precision: 9);
        Assert.Equal(300d / 420d, item.SourceCropWidth, precision: 9);
    }

    [Fact]
    public void ARasterIsAllDrawing()
    {
        var project = new ProjectWorkspace();

        Import(project, Manifest(Raster("view-1", "Рендер")));

        ProjectPortfolioItem item = Assert.Single(project.Portfolio.Items);
        Assert.Equal(0, item.SourceCropX, precision: 9);
        Assert.Equal(1, item.SourceCropWidth, precision: 9);
    }

    [Fact]
    public void ReExportingAViewRefreshesItInsteadOfAppendingADuplicate()
    {
        var project = new ProjectWorkspace();
        Import(project, Manifest(Raster("view-1", "Рендер")));

        VisualAsset again = Raster("view-1", "Рендер", content: "the model changed");
        VisualPackageImportResult result = Import(
            project,
            Manifest(again, exportedAt: new DateTimeOffset(2026, 8, 25, 0, 0, 0, TimeSpan.Zero)));

        Assert.Equal(1, result.UpdatedItemCount);
        Assert.Equal(0, result.CreatedItemCount);
        Assert.Single(project.Portfolio.Items);
    }

    [Fact]
    public void TheUsersOwnWordingSurvivesAReExport()
    {
        var project = new ProjectWorkspace();
        Import(project, Manifest(Raster("view-1", "Рендер")));
        project.Portfolio.Items[0].Title = "ХОЙД ТАЛААС";
        project.Portfolio.Items[0].Caption = "Оройн гэрэлтүүлэгтэй";

        Import(
            project,
            Manifest(
                Raster("view-1", "Рендер", content: "changed"),
                exportedAt: new DateTimeOffset(2026, 8, 25, 0, 0, 0, TimeSpan.Zero)));

        Assert.Equal("ХОЙД ТАЛААС", project.Portfolio.Items[0].Title);
        Assert.Equal("Оройн гэрэлтүүлэгтэй", project.Portfolio.Items[0].Caption);
    }

    [Fact]
    public void AnOlderExportScannedLaterDoesNotRollTheItemBack()
    {
        var project = new ProjectWorkspace();
        Import(
            project,
            Manifest(
                Raster("view-1", "Шинэ"),
                exportedAt: new DateTimeOffset(2026, 8, 25, 0, 0, 0, TimeSpan.Zero)));

        VisualPackageImportResult result = Import(
            project,
            Manifest(
                Raster("view-1", "Хуучин", content: "older"),
                exportedAt: new DateTimeOffset(2026, 8, 20, 0, 0, 0, TimeSpan.Zero)));

        Assert.Equal(0, result.UpdatedItemCount);
        Assert.Equal("Шинэ", project.Portfolio.Items[0].Title);
    }

    [Fact]
    public void ASeriesKeepsItsGroupingAndItsOrder()
    {
        // A board draws a group as one strip, so equal size and equal spacing
        // are structural rather than manual.
        var project = new ProjectWorkspace();
        VisualAsset first = Vector("view-1", "Нэг");
        VisualAsset second = Vector("view-2", "Хоёр");
        first.SeriesId = second.SeriesId = "strip";
        first.SeriesOrder = 1;
        second.SeriesOrder = 2;

        Import(project, Manifest(first, second));

        Assert.All(project.Portfolio.Items, item => Assert.Equal("strip", item.SourceSeriesId));
        Assert.Equal([1, 2], project.Portfolio.Items.Select(item => item.SourceSeriesOrder));
    }

    [Fact]
    public void AnUnusableAssetIsReportedAndTheRestStillArrive()
    {
        var project = new ProjectWorkspace();
        VisualAsset good = Raster("view-1", "Сайн");
        VisualAsset bad = Raster("view-2", "Муу");
        bad.HeightPx = 0;

        VisualPackageImportResult result = Import(project, Manifest(good, bad));

        Assert.Equal(1, result.CreatedItemCount);
        Assert.Equal(1, result.SkippedAssetCount);
        Assert.NotEmpty(result.Issues);
    }

    [Fact]
    public void ARefusedPackageBringsNothingAtAll()
    {
        var project = new ProjectWorkspace();
        VisualPackageManifest manifest = Manifest(Raster("view-1", "Рендер"));
        manifest.SchemaVersion = 99;

        VisualPackageImportResult result = Import(project, manifest);

        Assert.False(result.BroughtAnything);
        Assert.Empty(project.Portfolio.Items);
    }

    [Fact]
    public void TwoSourcesMayDeliverViewsOfTheSameName()
    {
        // The key carries the source as well, so two models with a view called
        // the same thing do not collide.
        var project = new ProjectWorkspace();

        Import(project, Manifest(Raster("view-1", "Аксонометр")), sourceId: "revit-a");
        Import(project, Manifest(Raster("view-1", "Аксонометр")), sourceId: "revit-b");

        Assert.Equal(2, project.Portfolio.Items.Count);
    }

    [Fact]
    public void ACardShowsTheDrawingRatherThanThePaperAroundIt()
    {
        // The asset knows which part of the file is the drawing and the card
        // does not, so a card that has not been cropped by hand defers to it.
        var project = new ProjectWorkspace();
        Import(project, Manifest(Vector("view-1", "Аксонометр")));
        ProjectPortfolioItem asset = project.Portfolio.Items[0];
        var card = new BoardElement { AssetItemId = asset.Id };

        (double x, double _, double width, double _) = BoardCardContent.ResolveCrop(card, asset);

        Assert.Equal(asset.SourceCropX, x, precision: 9);
        Assert.Equal(asset.SourceCropWidth, width, precision: 9);
    }

    [Fact]
    public void ACardCroppedByHandKeepsItsOwnAnswer()
    {
        // What the user touched is theirs.
        var project = new ProjectWorkspace();
        Import(project, Manifest(Vector("view-1", "Аксонометр")));
        ProjectPortfolioItem asset = project.Portfolio.Items[0];
        var card = new BoardElement
        {
            AssetItemId = asset.Id,
            CropX = 0.25,
            CropY = 0.25,
            CropWidth = 0.5,
            CropHeight = 0.5,
        };

        (double x, double _, double width, double _) = BoardCardContent.ResolveCrop(card, asset);

        Assert.Equal(0.25, x, precision: 9);
        Assert.Equal(0.5, width, precision: 9);
    }

    private VisualPackageImportResult Import(
        ProjectWorkspace project,
        VisualPackageManifest manifest,
        string? sourceId = null) =>
        VisualPackageImportService.Import(
            project,
            projectPath,
            VisualPackageReader.Verify(manifest, packageFolder),
            packageFolder,
            sourceId);

    private VisualPackageManifest Manifest(
        VisualAsset first,
        VisualAsset? second = null,
        DateTimeOffset? exportedAt = null)
    {
        var assets = second is null ? new List<VisualAsset> { first } : [first, second];
        return new VisualPackageManifest
        {
            SchemaVersion = VisualPackageContract.CurrentSchemaVersion,
            PackageId = Guid.NewGuid().ToString("N"),
            ProjectId = "P-001",
            ExportedAtUtc = exportedAt ?? new DateTimeOffset(2026, 8, 24, 0, 0, 0, TimeSpan.Zero),
            Source = new VisualPackageSource
            {
                SourceId = "revit-1",
                Application = VisualPackageContract.ApplicationRevit,
            },
            Assets = assets,
        };
    }

    private VisualAsset Raster(string assetId, string viewName, string content = "raster")
    {
        var asset = new VisualAsset
        {
            AssetId = assetId,
            ViewName = viewName,
            ViewType = "ThreeD",
            Kind = VisualAssetKinds.Render,
            MediaType = VisualMediaTypes.Png,
            FileName = $"{assetId}.png",
            WidthPx = 4489,
            HeightPx = 2835,
            Dpi = 300,
        };
        WriteFile(asset, content);
        return asset;
    }

    private VisualAsset Vector(string assetId, string viewName)
    {
        var asset = new VisualAsset
        {
            AssetId = assetId,
            ViewName = viewName,
            ViewType = "ThreeD",
            Kind = VisualAssetKinds.LineView,
            MediaType = VisualMediaTypes.Pdf,
            FileName = $"{assetId}.pdf",
            Page = new VisualAssetPage
            {
                PaperWidthMm = 420,
                PaperHeightMm = 297,
                ViewXMm = 60,
                ViewYMm = 48.5,
                ViewWidthMm = 300,
                ViewHeightMm = 200,
            },
        };
        WriteFile(asset, "%PDF-1.7 " + assetId);
        return asset;
    }

    private void WriteFile(VisualAsset asset, string content)
    {
        string path = Path.Combine(packageFolder, asset.FileName);
        File.WriteAllText(path, content);
        asset.Sha256 = ProjectDocumentFileStore.ComputeSha256(path);
    }
}
