using ErkS.Platform.Core;
using ErkS.Platform.Pdf;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace ErkS.Platform.Core.Tests;

public sealed class ProjectAssetSourceReconcilerTests : IDisposable
{
    private readonly string workDirectory = Path.Combine(
        Path.GetTempPath(),
        "erks-asset-source-reconciliation-tests",
        Guid.NewGuid().ToString("N"));

    public ProjectAssetSourceReconcilerTests()
    {
        Directory.CreateDirectory(workDirectory);
    }

    [Fact]
    public void ChangedAtdSource_ReplacesOwnedCopyAndReplansDocumentPages()
    {
        string projectPath = ProjectPath();
        string sourcePath = Path.Combine(workDirectory, "approved-atd.pdf");
        WritePdf(sourcePath, pageCount: 2);
        ProjectWorkspace project = CreateProjectWithAtd(projectPath, sourcePath);
        ProjectFileReference document = Assert.Single(project.Foundation.PlanningTask.Documents);
        string originalOwnedPath = ResolveProjectPath(projectPath, document.RelativePath);
        string originalHash = document.Sha256;

        WritePdf(sourcePath, pageCount: 4);
        ProjectAssetSourceReconciliationResult result =
            ProjectAssetSourceReconciler.ReconcileProject(project, projectPath);

        Assert.Equal(1, result.UpdatedDocumentCount);
        Assert.True(document.IsAvailable);
        Assert.Equal(4, document.PageCount);
        Assert.Equal(2, document.Version);
        Assert.NotEqual(originalHash, document.Sha256);
        Assert.NotEqual(originalOwnedPath, ResolveProjectPath(projectPath, document.RelativePath));
        Assert.True(File.Exists(ResolveProjectPath(projectPath, document.RelativePath)));
        Assert.True(File.Exists(originalOwnedPath));

        AlbumProject albumProject = CreateAlbumProject(project);
        ConceptGeneratedPagePlan[] atdPages = BuildingArchitectureConceptGeneratedPagePlanner
            .Create(albumProject)
            .Where(plan => plan.DocumentKind == ConceptGeneratedDocumentKind.ApprovedPlanningTask)
            .ToArray();
        Assert.Equal(2, atdPages.Length);
        Assert.Equal(4, atdPages.Sum(page => page.DocumentPages.Count));
    }

    [Fact]
    public void MissingAndRestoredAtdSource_RemovesAndRestoresGeneratedContent()
    {
        string projectPath = ProjectPath();
        string sourcePath = Path.Combine(workDirectory, "approved-atd.pdf");
        WritePdf(sourcePath, pageCount: 2);
        ProjectWorkspace project = CreateProjectWithAtd(projectPath, sourcePath);
        ProjectFileReference document = Assert.Single(project.Foundation.PlanningTask.Documents);
        string ownedPath = ResolveProjectPath(projectPath, document.RelativePath);

        File.Delete(sourcePath);
        ProjectAssetSourceReconciliationResult missing =
            ProjectAssetSourceReconciler.ReconcileProject(project, projectPath);

        Assert.Equal(1, missing.MissingDocumentCount);
        Assert.False(document.IsAvailable);
        Assert.True(File.Exists(ownedPath));
        Assert.All(
            BuildingArchitectureConceptGeneratedPagePlanner.Create(CreateAlbumProject(project))
                .Where(plan => plan.DocumentKind == ConceptGeneratedDocumentKind.ApprovedPlanningTask),
            page => Assert.Empty(page.DocumentPages));

        WritePdf(sourcePath, pageCount: 2);
        ProjectAssetSourceReconciliationResult restored =
            ProjectAssetSourceReconciler.ReconcileProject(project, projectPath);

        Assert.Equal(1, restored.RestoredDocumentCount);
        Assert.True(document.IsAvailable);
        Assert.Equal(
            2,
            BuildingArchitectureConceptGeneratedPagePlanner.Create(CreateAlbumProject(project))
                .Where(plan => plan.DocumentKind == ConceptGeneratedDocumentKind.ApprovedPlanningTask)
                .Sum(page => page.DocumentPages.Count));
    }

    [Fact]
    public void UnchangedAtdSource_UsesMetadataDirtyDetectorWithoutReopeningFile()
    {
        string projectPath = ProjectPath();
        string sourcePath = Path.Combine(workDirectory, "unchanged-approved-atd.pdf");
        WritePdf(sourcePath, pageCount: 2);
        ProjectWorkspace project = CreateProjectWithAtd(projectPath, sourcePath);
        ProjectFileReference document = Assert.Single(project.Foundation.PlanningTask.Documents);

        using FileStream exclusiveSourceLock = File.Open(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.None);
        ProjectAssetSourceReconciliationResult result =
            ProjectAssetSourceReconciler.ReconcileProject(project, projectPath);

        Assert.False(result.Changed);
        Assert.Equal(0, result.ErrorCount);
        Assert.True(document.IsAvailable);
        Assert.Equal(1, document.Version);
    }

    [Fact]
    public void MissingVisualizationSource_IsExcludedWithoutDeletingOwnedCopy()
    {
        string projectPath = ProjectPath();
        string sourcePath = Path.Combine(workDirectory, "render.png");
        WriteTinyPng(sourcePath);
        ProjectWorkspace project = ProjectWorkspaceStore.Create("TEST-001", "Asset update test");
        project.Visualizations.ConfigureForProject(project.ProjectId);
        ProjectDocumentAssetInspection inspection = ProjectDocumentAssetInspector.Inspect(sourcePath);
        string storedPath = ProjectVisualizationFileStore.StoreInsideProject(projectPath, sourcePath);
        project.Visualizations.Images.Add(new ProjectVisualizationImage
        {
            OwnerProjectId = project.ProjectId,
            OriginalFileName = Path.GetFileName(sourcePath),
            LinkedSourcePath = Path.GetFullPath(sourcePath),
            LinkedSourceLastWriteTimeUtc = new DateTimeOffset(
                File.GetLastWriteTimeUtc(sourcePath),
                TimeSpan.Zero),
            RelativePath = storedPath,
            ContentType = inspection.ContentType,
            SizeBytes = inspection.SizeBytes,
            PixelWidth = inspection.PixelWidth,
            PixelHeight = inspection.PixelHeight,
            Sha256 = inspection.Sha256,
        });
        string ownedPath = ResolveProjectPath(projectPath, storedPath);

        File.Delete(sourcePath);
        ProjectAssetSourceReconciliationResult result =
            ProjectAssetSourceReconciler.ReconcileProject(project, projectPath);

        // Changed deliberately in 0.001.57. This used to drop the image from
        // the album, which meant a project copied to another machine lost the
        // renders it carried with it. Album membership and link health are
        // separate questions: the watched original is gone, the project's own
        // copy is not.
        Assert.Equal(0, result.MissingVisualizationCount);
        Assert.Equal(1, result.BrokenLinkCount);
        ProjectVisualizationImage stillThere = Assert.Single(project.Visualizations.Images);
        Assert.True(stillThere.IsAvailable);
        Assert.True(stillThere.LinkedSourceMissing);
        Assert.True(File.Exists(ownedPath));
        Assert.NotEmpty(VisualizationPageLayoutPlanner.Create(
            project.Visualizations,
            project.ProjectId,
            firstPageNumber: 10));

        // And the other direction: nothing left at all is still a missing
        // image, so a genuine removal is not quietly preserved.
        File.Delete(ownedPath);
        ProjectAssetSourceReconciliationResult afterOwnedCopyGone =
            ProjectAssetSourceReconciler.ReconcileProject(project, projectPath);

        Assert.Equal(1, afterOwnedCopyGone.MissingVisualizationCount);
        Assert.False(Assert.Single(project.Visualizations.Images).IsAvailable);
        Assert.Empty(VisualizationPageLayoutPlanner.Create(
            project.Visualizations,
            project.ProjectId,
            firstPageNumber: 10));
    }

    [Fact]
    public void ARestoredLinkClearsTheBrokenLinkMark()
    {
        string projectPath = ProjectPath();
        string sourcePath = Path.Combine(workDirectory, "restored.png");
        WriteTinyPng(sourcePath);
        ProjectWorkspace project = ProjectWorkspaceStore.Create("TEST-002", "Link restore test");
        project.Visualizations.ConfigureForProject(project.ProjectId);
        ProjectDocumentAssetInspection inspection = ProjectDocumentAssetInspector.Inspect(sourcePath);
        string storedPath = ProjectVisualizationFileStore.StoreInsideProject(projectPath, sourcePath);
        project.Visualizations.Images.Add(new ProjectVisualizationImage
        {
            OwnerProjectId = project.ProjectId,
            OriginalFileName = Path.GetFileName(sourcePath),
            LinkedSourcePath = Path.GetFullPath(sourcePath),
            RelativePath = storedPath,
            ContentType = inspection.ContentType,
            SizeBytes = inspection.SizeBytes,
            PixelWidth = inspection.PixelWidth,
            PixelHeight = inspection.PixelHeight,
            Sha256 = inspection.Sha256,
        });

        byte[] original = File.ReadAllBytes(sourcePath);
        File.Delete(sourcePath);
        ProjectAssetSourceReconciler.ReconcileProject(project, projectPath);
        Assert.True(Assert.Single(project.Visualizations.Images).LinkedSourceMissing);

        File.WriteAllBytes(sourcePath, original);
        ProjectAssetSourceReconciler.ReconcileProject(project, projectPath);

        ProjectVisualizationImage image = Assert.Single(project.Visualizations.Images);
        Assert.False(image.LinkedSourceMissing);
        Assert.True(image.IsAvailable);
    }

    [Fact]
    public void ChangedCompanyCertificate_RefreshesCompanyLibraryReference()
    {
        string sourcePath = Path.Combine(workDirectory, "company-certificate.pdf");
        WritePdf(sourcePath, pageCount: 1);
        var store = new CompanyLibraryStore(
            Path.Combine(workDirectory, "company", "companies.json"),
            Path.Combine(workDirectory, "company", "assets"));
        ProjectDocumentAssetInspection inspection = ProjectDocumentAssetInspector.Inspect(sourcePath);
        var profile = new CompanyProfile
        {
            OrganizationId = "organization-1",
            RegistrationCertificateDocuments =
            [
                new ProjectFileReference
                {
                    Category = ProjectDocumentCategories.CompanyRegistrationCertificate,
                    Title = "Company certificate",
                    RelativePath = store.StoreDocument(
                        "organization-1",
                        ProjectDocumentCategories.CompanyRegistrationCertificate,
                        sourcePath),
                    OriginalFileName = Path.GetFileName(sourcePath),
                    LinkedSourcePath = Path.GetFullPath(sourcePath),
                    LinkedSourceLastWriteTimeUtc = new DateTimeOffset(
                        File.GetLastWriteTimeUtc(sourcePath),
                        TimeSpan.Zero),
                    ContentType = inspection.ContentType,
                    SizeBytes = inspection.SizeBytes,
                    PageCount = inspection.PageCount,
                    Sha256 = inspection.Sha256,
                },
            ],
        };
        string originalStoredPath = Assert.Single(profile.RegistrationCertificateDocuments).RelativePath;

        WritePdf(sourcePath, pageCount: 2);
        ProjectAssetSourceReconciliationResult result =
            ProjectAssetSourceReconciler.ReconcileCompanyProfile(profile, store);

        ProjectFileReference refreshed = Assert.Single(profile.RegistrationCertificateDocuments);
        Assert.Equal(1, result.UpdatedDocumentCount);
        Assert.Equal(2, refreshed.PageCount);
        Assert.Equal(2, refreshed.Version);
        Assert.NotEqual(originalStoredPath, refreshed.RelativePath);
        Assert.True(File.Exists(originalStoredPath));
        Assert.True(File.Exists(refreshed.RelativePath));
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

    private string ProjectPath()
    {
        string projectFolder = Path.Combine(workDirectory, "project");
        Directory.CreateDirectory(projectFolder);
        return Path.Combine(projectFolder, ProjectWorkspace.DefaultFileName);
    }

    private static ProjectWorkspace CreateProjectWithAtd(string projectPath, string sourcePath)
    {
        ProjectWorkspace project = ProjectWorkspaceStore.Create("TEST-001", "Asset update test");
        ProjectDocumentAssetInspection inspection = ProjectDocumentAssetInspector.Inspect(sourcePath);
        string storedPath = ProjectDocumentFileStore.StoreInsideProject(
            projectPath,
            ProjectDocumentCategories.ApprovedPlanningTask,
            sourcePath);
        project.Foundation.PlanningTask.Documents.Add(new ProjectFileReference
        {
            Category = ProjectDocumentCategories.ApprovedPlanningTask,
            Title = "Approved ATD",
            RelativePath = storedPath,
            OriginalFileName = Path.GetFileName(sourcePath),
            LinkedSourcePath = Path.GetFullPath(sourcePath),
            LinkedSourceLastWriteTimeUtc = new DateTimeOffset(
                File.GetLastWriteTimeUtc(sourcePath),
                TimeSpan.Zero),
            ContentType = inspection.ContentType,
            SizeBytes = inspection.SizeBytes,
            PageCount = inspection.PageCount,
            Sha256 = inspection.Sha256,
        });
        return project;
    }

    private static AlbumProject CreateAlbumProject(ProjectWorkspace project) => new()
    {
        ProjectId = project.ProjectId,
        PlanningTask = project.Foundation.PlanningTask,
        Company = project.Foundation.DesignCompany.OrganizationSnapshot,
        Album = BuildingArchitectureConceptAlbumTemplate.CreateDefinition("Concept album"),
    };

    private static string ResolveProjectPath(string projectPath, string relativePath) =>
        ProjectWorkspacePaths.ResolveInsideProject(projectPath, relativePath);

    private static void WritePdf(string path, int pageCount)
    {
        if (File.Exists(path))
            File.Delete(path);
        using var document = new PdfDocument();
        for (int index = 0; index < pageCount; index++)
        {
            PdfPage page = document.AddPage();
            page.Width = XUnit.FromMillimeter(210);
            page.Height = XUnit.FromMillimeter(297);
            using XGraphics graphics = XGraphics.FromPdfPage(page);
            graphics.DrawRectangle(
                new XPen(XColors.Black, 0.5),
                20,
                20,
                100 + index,
                40);
            graphics.DrawLine(
                new XPen(XColors.Black, 0.25),
                20,
                20 + index,
                120 + index,
                60);
        }
        document.Save(path);
    }

    private static void WriteTinyPng(string path) => File.WriteAllBytes(
        path,
        Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="));
}
