using ErkS.Platform.Contracts;
using ErkS.Platform.Core;
using ErkS.Platform.Pdf;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace ErkS.Platform.Core.Tests;

public sealed class LocalPdfSheetPackageImporterTests : IDisposable
{
    private readonly string workDirectory = Path.Combine(
        Path.GetTempPath(),
        "erks-local-pdf-tests-" + Guid.NewGuid().ToString("N"));

    public LocalPdfSheetPackageImporterTests()
    {
        Directory.CreateDirectory(workDirectory);
    }

    [Fact]
    public void MultiPagePdf_IsStoredOnceAndPublishedAsLogicalPages()
    {
        string sourcePath = Path.Combine(workDirectory, "existing-album.pdf");
        string inbox = Path.Combine(workDirectory, "inbox");
        WriteVectorPdf(sourcePath, 2);
        (ProjectWorkspace project, ProjectDesignSource source) =
            CreateProjectAndSource(sourcePath, inbox);

        LocalPdfSheetPackageImportResult result =
            new LocalPdfSheetPackageImporter().Import(project, source);
        SheetPackageLoadResult package = SheetPackageReader.Load(result.ManifestPath);

        Assert.True(result.Changed);
        Assert.Equal(2, result.PageCount);
        Assert.True(package.IsLossless, string.Join("; ", package.Issues));
        Assert.Equal(SheetPackageManifest.CurrentSchemaVersion, package.Manifest!.SchemaVersion);
        Assert.Equal(SheetPackageScope.FullSnapshot, package.Manifest.PackageScope);
        Assert.Equal(SheetSourceApplication.Pdf, package.Manifest.Source.Application);
        Assert.Equal(2, package.Manifest.Sheets.Count);
        Assert.Equal([1, 2], package.Manifest.Sheets.Select(entry => entry.PdfPageNumber));
        Assert.All(package.Manifest.Sheets, entry =>
        {
            Assert.Equal("source.pdf", entry.PdfFileName);
            Assert.Equal(1, entry.PageCount);
            Assert.False(entry.IsCleanDrawingSpace);
        });
        Assert.Single(Directory.EnumerateFiles(inbox, "*.pdf"));
        Assert.Equal(
            SheetPackageReader.ComputeSha256(sourcePath),
            SheetPackageReader.ComputeSha256(result.PdfPath));
    }

    [Fact]
    public void PdfBookmarks_AreUsedAsLogicalPageNames()
    {
        string sourcePath = Path.Combine(workDirectory, "bookmarked.pdf");
        string inbox = Path.Combine(workDirectory, "bookmarked-inbox");
        WriteBookmarkedPdf(sourcePath);
        (ProjectWorkspace project, ProjectDesignSource source) =
            CreateProjectAndSource(sourcePath, inbox);
        var importer = new LocalPdfSheetPackageImporter();

        LocalPdfSheetPackageImportResult first = importer.Import(project, source);
        SheetPackageManifest manifest =
            SheetPackageReader.Load(first.ManifestPath).Manifest!;
        LocalPdfSheetPackageImportResult unchanged = importer.Import(project, source);

        Assert.Equal(
            ["Давхрын байгуулалт", "Огтлол", "Нүүр тал"],
            manifest.Sheets.Select(entry => entry.Name));
        Assert.False(unchanged.Changed);
    }

    [Fact]
    public void NestedBookmark_UsesTheMostSpecificPageName()
    {
        string sourcePath = Path.Combine(workDirectory, "nested-bookmark.pdf");
        string inbox = Path.Combine(workDirectory, "nested-bookmark-inbox");
        using (var document = new PdfDocument())
        {
            PdfPage page = document.AddPage();
            PdfOutline group = document.Outlines.Add("Барилгын зураг", page);
            group.Outlines.Add("1-р давхрын байгуулалт", page);
            document.Save(sourcePath);
        }
        (ProjectWorkspace project, ProjectDesignSource source) =
            CreateProjectAndSource(sourcePath, inbox);

        LocalPdfSheetPackageImportResult result =
            new LocalPdfSheetPackageImporter().Import(project, source);
        SheetPackageEntry pageEntry = Assert.Single(
            SheetPackageReader.Load(result.ManifestPath).Manifest!.Sheets);

        Assert.Equal("1-р давхрын байгуулалт", pageEntry.Name);
    }

    [Fact]
    public void Import_IsDirtyAwareAndReplacesDeletedLogicalPages()
    {
        string sourcePath = Path.Combine(workDirectory, "refreshable.pdf");
        string inbox = Path.Combine(workDirectory, "refresh-inbox");
        WriteVectorPdf(sourcePath, 2);
        (ProjectWorkspace project, ProjectDesignSource source) =
            CreateProjectAndSource(sourcePath, inbox);
        var importer = new LocalPdfSheetPackageImporter();

        LocalPdfSheetPackageImportResult first = importer.Import(project, source);
        LocalPdfSheetPackageImportResult unchanged = importer.Import(project, source);
        WriteVectorPdf(sourcePath, 1);
        LocalPdfSheetPackageImportResult refreshed = importer.Import(project, source);
        SheetPackageLoadResult package = SheetPackageReader.Load(refreshed.ManifestPath);

        Assert.True(first.Changed);
        Assert.False(unchanged.Changed);
        Assert.True(refreshed.Changed);
        Assert.Equal(1, refreshed.PageCount);
        SheetPackageEntry page = Assert.Single(package.Manifest!.Sheets);
        Assert.Equal("pdf-page-0001", page.SheetId);
        Assert.Equal(1, page.PdfPageNumber);
        Assert.Single(Directory.EnumerateFiles(inbox, "*.pdf"));
    }

    [Fact]
    public void SourceClassificationChange_RebuildsLogicalPageMetadata()
    {
        string sourcePath = Path.Combine(workDirectory, "classified.pdf");
        string inbox = Path.Combine(workDirectory, "classified-inbox");
        WriteVectorPdf(sourcePath, 1);
        (ProjectWorkspace project, ProjectDesignSource source) =
            CreateProjectAndSource(sourcePath, inbox);
        var building = new ProjectBuildingGroup
        {
            Id = "building-a",
            Name = "A байр",
            Order = 1,
        };
        project.BuildingGroups.Add(building);
        var importer = new LocalPdfSheetPackageImporter();

        ProjectDesignSourceClassification.SetExplicitPurpose(
            source,
            ProjectDesignSourcePurpose.GeneralPlan);
        importer.Import(project, source);
        SheetPackageEntry generalPlan = Assert.Single(
            SheetPackageReader.Load(
                Path.Combine(inbox, "pdf-source" + SheetPackageManifest.ManifestSuffix))
                .Manifest!.Sheets);
        Assert.Equal("GeneralPlan", generalPlan.Discipline);
        Assert.Equal("master-plan", generalPlan.ContentKind);

        ProjectDesignSourceClassification.SetExplicitPurpose(
            source,
            ProjectDesignSourcePurpose.Building,
            building.Id);
        LocalPdfSheetPackageImportResult changed = importer.Import(project, source);
        SheetPackageEntry buildingPage = Assert.Single(
            SheetPackageReader.Load(changed.ManifestPath).Manifest!.Sheets);

        Assert.True(changed.Changed);
        Assert.Equal("Architecture", buildingPage.Discipline);
        Assert.Equal(building.Id, buildingPage.BuildingId);
        Assert.Equal(building.Name, buildingPage.BuildingName);
        Assert.Empty(buildingPage.ContentKind);
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

    private static (ProjectWorkspace Project, ProjectDesignSource Source)
        CreateProjectAndSource(string sourcePath, string inbox)
    {
        var source = new ProjectDesignSource
        {
            Id = "local-pdf-source",
            Kind = DesignSourceKind.Pdf,
            Name = "Existing PDF",
            NativeDocumentTitle = "Existing drawings.pdf",
            NativeDocumentPath = sourcePath,
            InboxFolder = inbox,
        };
        var project = new ProjectWorkspace
        {
            ProjectId = "project-pdf-test",
            Identity = new ProjectIdentity
            {
                Code = "PDF-001",
                Name = "PDF test",
                StageCode = ProjectWorkspace.ConceptDesignStage,
            },
            Sources = [source],
        };
        return (project, source);
    }

    private static void WriteVectorPdf(string path, int pageCount)
    {
        using var document = new PdfDocument();
        for (int index = 0; index < pageCount; index++)
        {
            PdfPage page = document.AddPage();
            page.Width = XUnit.FromMillimeter(index == 0 ? 420 : 297);
            page.Height = XUnit.FromMillimeter(index == 0 ? 297 : 420);
            using XGraphics graphics = XGraphics.FromPdfPage(page);
            graphics.DrawLine(
                new XPen(XColors.Black, 0.5),
                20,
                20 + index,
                page.Width.Point - 20,
                page.Height.Point - 20);
        }
        document.Save(path);
    }

    private static void WriteBookmarkedPdf(string path)
    {
        using var document = new PdfDocument();
        string[] names = ["Давхрын байгуулалт", "Огтлол", "Нүүр тал"];
        foreach (string name in names)
        {
            PdfPage page = document.AddPage();
            page.Width = XUnit.FromMillimeter(420);
            page.Height = XUnit.FromMillimeter(297);
            document.Outlines.Add(name, page);
        }
        document.Save(path);
    }
}
