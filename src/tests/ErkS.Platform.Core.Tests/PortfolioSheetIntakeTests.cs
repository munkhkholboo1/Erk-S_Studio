using ErkS.Platform.Contracts;
using ErkS.Platform.Core;
using ErkS.Platform.Pdf;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using System.Text.Json.Nodes;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// A package entry marked <c>destination: Portfolio</c> is presentation
/// material: it must never enter the sheet library or the album, and it is
/// imported into the project portfolio as one stable, updatable item.
/// </summary>
public sealed class PortfolioSheetIntakeTests : IDisposable
{
    private const string SourceId = "portfolio-source";

    private readonly string workDirectory =
        Path.Combine(Path.GetTempPath(), "erks-platform-tests", Guid.NewGuid().ToString("N"));

    public PortfolioSheetIntakeTests()
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
    public void AlbumAndPortfolioEntries_RouteToLibraryAndPortfolio()
    {
        string manifestPath = WritePackage(
            "mixed",
            [Album("A1", "Байгуулалт"), Portfolio("P1", "Портфолио хуудас")],
            SheetPackageScope.Delta,
            DateTimeOffset.UtcNow);
        SheetPackageLoadResult result = SheetPackageReader.Load(manifestPath);
        Assert.True(result.IsLossless, string.Join("; ", result.Issues));
        var library = new SheetLibrary();
        (ProjectWorkspace project, string projectPath) = CreateProject();

        SheetLibraryChange change = library.Absorb(result);
        PortfolioSheetImportResult imported = PortfolioSheetImportService.Import(
            project,
            projectPath,
            result);

        Assert.Equal(1, change.UpdatedSheetCount);
        Assert.Equal(1, change.NewPortfolioEntryCount);
        Assert.True(change.HasChanges);
        SheetRecord record = Assert.Single(library.Snapshot());
        Assert.Equal("A1", record.Entry.SheetId);

        Assert.Equal(1, imported.CreatedItemCount);
        ProjectPortfolioItem item = Assert.Single(project.Portfolio.Items);
        Assert.Equal(ProjectPortfolioItemKinds.CadPage, item.Kind);
        Assert.Equal(ProjectPortfolioLayouts.FullBleed, item.Layout);
        Assert.Equal("Портфолио хуудас", item.Title);
        Assert.Equal(KeyOf(result, "P1"), item.SourceSheetKey);
        string storedPath = ProjectWorkspacePaths.ResolveInsideProject(
            projectPath,
            item.RelativePath);
        Assert.True(File.Exists(storedPath));
    }

    [Fact]
    public void LegacyPackageWithoutDestination_TreatsEveryEntryAsAlbum()
    {
        string manifestPath = WritePackage(
            "legacy",
            [Album("A1", "Нэг"), Album("A2", "Хоёр")],
            SheetPackageScope.Delta,
            DateTimeOffset.UtcNow);
        JsonObject manifestJson = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        foreach (JsonNode? sheet in manifestJson["sheets"]!.AsArray())
        {
            Assert.True(sheet!.AsObject().Remove("destination"));
        }
        File.WriteAllText(manifestPath, manifestJson.ToJsonString(SheetPackageJson.Options));
        SheetPackageLoadResult result = SheetPackageReader.Load(manifestPath);
        Assert.True(result.IsLossless, string.Join("; ", result.Issues));
        var library = new SheetLibrary();
        (ProjectWorkspace project, string projectPath) = CreateProject();

        SheetLibraryChange change = library.Absorb(result);
        PortfolioSheetImportResult imported = PortfolioSheetImportService.Import(
            project,
            projectPath,
            result);

        Assert.All(
            result.Manifest!.Sheets,
            entry => Assert.Equal(SheetDestinations.Album, entry.Destination));
        Assert.Equal(2, change.UpdatedSheetCount);
        Assert.Equal(0, change.NewPortfolioEntryCount);
        Assert.Equal(2, library.Snapshot().Count);
        Assert.Equal(0, imported.CreatedItemCount);
        Assert.Empty(project.Portfolio.Items);
    }

    [Fact]
    public void UnknownDestination_RejectsAndQuarantinesPackage()
    {
        string manifestPath = WritePackage(
            "unknown-destination",
            [Album("A1", "Нэг"), Portfolio("P1", "Портфолио")],
            SheetPackageScope.Delta,
            DateTimeOffset.UtcNow);
        JsonObject manifestJson = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        manifestJson["sheets"]!.AsArray()[1]!.AsObject()["destination"] = "Elsewhere";
        File.WriteAllText(manifestPath, manifestJson.ToJsonString(SheetPackageJson.Options));
        var library = new SheetLibrary();
        (ProjectWorkspace project, string projectPath) = CreateProject();
        using var intake = new SheetIntakeService(library);

        intake.WatchFolder(Path.GetDirectoryName(manifestPath)!, scanExisting: true);

        RejectedSheetPackage rejected = Assert.Single(intake.RejectedPackages);
        Assert.Contains(
            rejected.Issues,
            issue => issue.Contains("destination", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(library.Snapshot());
        string auditPath = Path.Combine(
            Path.GetDirectoryName(manifestPath)!,
            ".erks-quarantine",
            "rejected-packages.jsonl");
        Assert.True(File.Exists(auditPath));

        SheetPackageLoadResult result = SheetPackageReader.Load(manifestPath);
        Assert.False(result.IsLossless);
        PortfolioSheetImportResult imported = PortfolioSheetImportService.Import(
            project,
            projectPath,
            result);
        Assert.Equal(0, imported.CreatedItemCount);
        Assert.Empty(project.Portfolio.Items);
    }

    [Fact]
    public void ReimportedPortfolioPage_UpdatesItemKeepingCuration()
    {
        var firstExportedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10);
        string firstManifestPath = WritePackage(
            "portfolio-v1",
            [Portfolio("P0", "Нүүр"), Portfolio("P1", "Хуучин нэр")],
            SheetPackageScope.Delta,
            firstExportedAtUtc);
        SheetPackageLoadResult firstResult = SheetPackageReader.Load(firstManifestPath);
        Assert.True(firstResult.IsLossless, string.Join("; ", firstResult.Issues));
        var library = new SheetLibrary();
        (ProjectWorkspace project, string projectPath) = CreateProject();
        library.Absorb(firstResult);
        PortfolioSheetImportService.Import(project, projectPath, firstResult);
        string sheetKey = KeyOf(firstResult, "P1");
        ProjectPortfolioItem item = project.Portfolio.Items.Single(candidate =>
            candidate.SourceSheetKey == sheetKey);
        string firstRelativePath = item.RelativePath;
        item.Caption = "Гараар бичсэн тайлбар";
        item.Layout = ProjectPortfolioLayouts.Contain;

        string secondManifestPath = WritePackage(
            "portfolio-v2",
            [Portfolio("P1", "Шинэ нэр", pdfText: "reworked graphic")],
            SheetPackageScope.Delta,
            firstExportedAtUtc.AddMinutes(5));
        SheetPackageLoadResult secondResult = SheetPackageReader.Load(secondManifestPath);
        Assert.True(secondResult.IsLossless, string.Join("; ", secondResult.Issues));
        SheetLibraryChange change = library.Absorb(secondResult);
        PortfolioSheetImportResult imported = PortfolioSheetImportService.Import(
            project,
            projectPath,
            secondResult);

        Assert.Equal(1, change.NewPortfolioEntryCount);
        Assert.Equal(0, imported.CreatedItemCount);
        Assert.Equal(1, imported.UpdatedItemCount);
        Assert.Equal(2, project.Portfolio.Items.Count);
        ProjectPortfolioItem updated = project.Portfolio.Items.Single(candidate =>
            candidate.SourceSheetKey == sheetKey);
        Assert.Equal(item.Id, updated.Id);
        Assert.Equal("Шинэ нэр", updated.Title);
        Assert.NotEqual(firstRelativePath, updated.RelativePath);
        Assert.Equal("Гараар бичсэн тайлбар", updated.Caption);
        Assert.Equal(ProjectPortfolioLayouts.Contain, updated.Layout);
        Assert.Equal(2, updated.Order);

        // A stale export re-scanned later must not roll the item back.
        PortfolioSheetImportResult stale = PortfolioSheetImportService.Import(
            project,
            projectPath,
            firstResult);
        Assert.Equal(1, stale.UpdatedItemCount);
        ProjectPortfolioItem afterStale = project.Portfolio.Items.Single(candidate =>
            candidate.SourceSheetKey == sheetKey);
        Assert.Equal("Шинэ нэр", afterStale.Title);
        Assert.Equal(updated.RelativePath, afterStale.RelativePath);
    }

    [Fact]
    public void FullSnapshotDroppingAlbumSheet_RemovesAlbumPageKeepsPortfolioItem()
    {
        var firstExportedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10);
        string firstManifestPath = WritePackage(
            "snapshot-v1",
            [Album("A1", "Нэг"), Album("A2", "Хоёр"), Portfolio("P1", "Портфолио")],
            SheetPackageScope.FullSnapshot,
            firstExportedAtUtc);
        SheetPackageLoadResult firstResult = SheetPackageReader.Load(firstManifestPath);
        Assert.True(firstResult.IsLossless, string.Join("; ", firstResult.Issues));
        var library = new SheetLibrary();
        (ProjectWorkspace project, string projectPath) = CreateProject();
        var album = BuildingArchitectureConceptAlbumTemplate.CreateDefinition("Concept");
        library.Absorb(firstResult);
        Assert.NotNull(ProjectPackageReconciliationService.Apply(
            project,
            album,
            library,
            firstResult));
        PortfolioSheetImportService.Import(project, projectPath, firstResult);
        string droppedKey = KeyOf(firstResult, "A2");
        string portfolioKey = KeyOf(firstResult, "P1");
        Assert.Contains(album.Pages, page => page.SheetKey == KeyOf(firstResult, "A1"));
        Assert.Contains(album.Pages, page => page.SheetKey == droppedKey);
        Assert.DoesNotContain(album.Pages, page => page.SheetKey == portfolioKey);
        Assert.Single(project.Portfolio.Items);

        string secondManifestPath = WritePackage(
            "snapshot-v2",
            [Album("A1", "Нэг"), Portfolio("P1", "Портфолио")],
            SheetPackageScope.FullSnapshot,
            firstExportedAtUtc.AddMinutes(5));
        SheetPackageLoadResult secondResult = SheetPackageReader.Load(secondManifestPath);
        Assert.True(secondResult.IsLossless, string.Join("; ", secondResult.Issues));
        SheetLibraryChange change = library.Absorb(secondResult);
        Assert.NotNull(ProjectPackageReconciliationService.Apply(
            project,
            album,
            library,
            secondResult));
        PortfolioSheetImportService.Import(project, projectPath, secondResult);

        Assert.Contains(droppedKey, change.RemovedSheetKeys);
        Assert.Equal(["A1"], library.Snapshot().Select(record => record.Entry.SheetId));
        Assert.DoesNotContain(album.Pages, page => page.SheetKey == droppedKey);
        Assert.DoesNotContain(album.Pages, page => page.SheetKey == portfolioKey);
        ProjectPortfolioItem item = Assert.Single(project.Portfolio.Items);
        Assert.Equal(portfolioKey, item.SourceSheetKey);
    }

    [Fact]
    public void PortfolioFormatMode_ResolvesToChromelessKind()
    {
        var spec = new PageFormatSpec
        {
            Id = "erks-portfolio-a1-landscape",
            Name = "Portfolio A1",
            Mode = "Portfolio",
            Code = "A1",
            Orientation = "LANDSCAPE",
            BindEdge = "NONE",
            WidthMm = 420,
            HeightMm = 297,
            DrawingArea = new PageRectSpec { X = 10, Y = 10, Width = 400, Height = 277 },
            SheetTitleArea = new PageRectSpec(),
            TitleBlockArea = new PageRectSpec(),
            ShowBorder = false,
            ShowGrid = false,
        };
        spec.GeometryHash = PageFormatSpecGeometry.ComputeHash(spec);

        PageFormatDefinition format = PageFormatResolver.FromSpec(spec);

        // A page that slips into an album must never be stamped with
        // working-drawing chrome by the unknown-mode fallback.
        Assert.Equal(PageFormatKind.Portfolio, format.Kind);
        Assert.Equal(
            PageFormatKind.WorkingDrawing,
            PageFormatResolver.FromSpec(new PageFormatSpec { Mode = "SomethingElse" }).Kind);
    }

    private (ProjectWorkspace Project, string ProjectPath) CreateProject()
    {
        string projectFolder = Path.Combine(
            workDirectory,
            "project-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(projectFolder);
        var project = new ProjectWorkspace
        {
            Sources =
            [
                new ProjectDesignSource
                {
                    Id = SourceId,
                    Name = "AutoCAD source",
                },
            ],
        };
        return (project, Path.Combine(projectFolder, "project.erksproj"));
    }

    private static string KeyOf(SheetPackageLoadResult result, string sheetId) =>
        SheetRecord.MakeKey(
            result.Manifest!.Source,
            result.Manifest.Sheets.Single(entry => entry.SheetId == sheetId));

    private sealed record PackageSheet(
        string SheetId,
        string Name,
        string Destination,
        string PdfText);

    private static PackageSheet Album(string sheetId, string name) =>
        new(sheetId, name, SheetDestinations.Album, name);

    private static PackageSheet Portfolio(string sheetId, string name, string? pdfText = null) =>
        new(sheetId, name, SheetDestinations.Portfolio, pdfText ?? name);

    private string WritePackage(
        string folderName,
        IReadOnlyList<PackageSheet> sheets,
        SheetPackageScope packageScope,
        DateTimeOffset exportedAtUtc)
    {
        string directory = Path.Combine(workDirectory, folderName);
        Directory.CreateDirectory(directory);
        var manifest = new SheetPackageManifest
        {
            Source = new SheetPackageSource
            {
                SourceId = SourceId,
                Application = SheetSourceApplication.AutoCad,
                DocumentPath = @"C:\sample\portfolio.dwg",
                DocumentTitle = "portfolio",
            },
            PackageScope = packageScope,
            ExportedAtUtc = exportedAtUtc,
        };
        foreach (PackageSheet sheet in sheets)
        {
            string fileName = $"{sheet.SheetId}.pdf";
            WriteMinimalPdf(Path.Combine(directory, fileName), sheet.PdfText, 420, 297);
            manifest.Sheets.Add(new SheetPackageEntry
            {
                SheetId = sheet.SheetId,
                Number = sheet.SheetId,
                Name = sheet.Name,
                Destination = sheet.Destination,
                WidthMm = 420,
                HeightMm = 297,
                PdfFileName = fileName,
            });
        }

        return SheetPackageWriter.Write(manifest, directory, "portfolio-package");
    }

    private static void WriteMinimalPdf(string path, string text, double widthMm, double heightMm)
    {
        using var document = new PdfDocument();
        PdfPage page = document.AddPage();
        page.Width = XUnit.FromMillimeter(widthMm);
        page.Height = XUnit.FromMillimeter(heightMm);
        using XGraphics graphics = XGraphics.FromPdfPage(page);
        graphics.DrawString(
            text,
            new XFont("Arial", 20),
            XBrushes.Black,
            new XRect(0, 0, page.Width.Point, page.Height.Point),
            XStringFormats.Center);
        document.Save(path);
    }
}
