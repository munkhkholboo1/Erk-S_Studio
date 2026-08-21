namespace ErkS.Platform.Core.Tests;

public sealed class ProjectLibraryDocumentTests
{
    [Fact]
    public void WorkspaceStore_RoundTripsResearchAndRecordDocuments()
    {
        string folder = Path.Combine(Path.GetTempPath(), "erks-libraries-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            ProjectWorkspace project = ProjectWorkspaceStore.Create("ERKS-U-01", "Хэсэгчилсэн ерөнхий төлөвлөгөө");
            project.ResearchDocuments.Add(new ProjectFileReference
            {
                Category = ProjectDocumentCategories.Research,
                Title = "Хөрсний судалгаа",
                RelativePath = "foundation/documents/Research/abc.pdf",
                ContentType = "application/pdf",
                PageCount = 12,
            });
            project.RecordDocuments.Add(new ProjectFileReference
            {
                Category = ProjectDocumentCategories.Record,
                Title = "Газрын шийдвэр",
                RelativePath = "foundation/documents/Record/def.pdf",
                ContentType = "application/pdf",
            });

            string path = Path.Combine(folder, "project.erksproject");
            ProjectWorkspaceStore.Save(project, path);
            ProjectWorkspace reloaded = ProjectWorkspaceStore.Load(path);

            ProjectFileReference research = Assert.Single(reloaded.ResearchDocuments);
            Assert.Equal("Хөрсний судалгаа", research.Title);
            Assert.Equal(12, research.PageCount);
            ProjectFileReference record = Assert.Single(reloaded.RecordDocuments);
            Assert.Equal("Газрын шийдвэр", record.Title);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void ProjectFileWithoutTheLibraries_LoadsWithEmptyOnes()
    {
        string folder = Path.Combine(Path.GetTempPath(), "erks-libraries-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            // A project saved before the libraries existed must still open.
            ProjectWorkspace project = ProjectWorkspaceStore.Create("ERKS-U-02", "Хөгжлийн ерөнхий төлөвлөгөө");
            string path = Path.Combine(folder, "project.erksproject");
            ProjectWorkspaceStore.Save(project, path);
            string json = File.ReadAllText(path)
                .Replace("\"researchDocuments\"", "\"legacyResearchDocuments\"", StringComparison.OrdinalIgnoreCase)
                .Replace("\"recordDocuments\"", "\"legacyRecordDocuments\"", StringComparison.OrdinalIgnoreCase);
            File.WriteAllText(path, json);

            ProjectWorkspace reloaded = ProjectWorkspaceStore.Load(path);

            Assert.Empty(reloaded.ResearchDocuments);
            Assert.Empty(reloaded.RecordDocuments);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
