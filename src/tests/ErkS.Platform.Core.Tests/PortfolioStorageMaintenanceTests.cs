using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

public sealed class PortfolioStorageMaintenanceTests : IDisposable
{
    private readonly string workDirectory =
        Path.Combine(Path.GetTempPath(), "erks-platform-tests", Guid.NewGuid().ToString("N"));

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
    public void FileNoPageRefersTo_IsRemoved()
    {
        (ProjectWorkspace project, string projectPath, string folder) = CreateProject();
        string kept = WriteFile(folder, "kept.pdf");
        string orphan = WriteFile(folder, "orphan.pdf");
        project.Portfolio.Items.Add(Page("foundation/documents/Portfolio/kept.pdf"));

        int removed = PortfolioStorageMaintenance.RemoveUnreferencedFiles(project, projectPath);

        Assert.Equal(1, removed);
        Assert.True(File.Exists(kept));
        Assert.False(File.Exists(orphan));
    }

    [Fact]
    public void FileAPageTheUserRemovedStillRefersTo_IsKept()
    {
        // A page taken out can be restored, so its drawing must survive.
        (ProjectWorkspace project, string projectPath, string folder) = CreateProject();
        string path = WriteFile(folder, "hidden.pdf");
        ProjectPortfolioItem page = Page("foundation/documents/Portfolio/hidden.pdf");
        page.RemovedAtUtc = DateTimeOffset.UtcNow;
        project.Portfolio.Items.Add(page);

        int removed = PortfolioStorageMaintenance.RemoveUnreferencedFiles(project, projectPath);

        Assert.Equal(0, removed);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void MissingFolder_IsNotAnError()
    {
        var project = new ProjectWorkspace();
        string projectPath = Path.Combine(workDirectory, "empty", "project.erksproj");
        Directory.CreateDirectory(Path.GetDirectoryName(projectPath)!);

        Assert.Equal(0, PortfolioStorageMaintenance.RemoveUnreferencedFiles(project, projectPath));
    }

    private (ProjectWorkspace Project, string ProjectPath, string Folder) CreateProject()
    {
        string projectFolder = Path.Combine(workDirectory, "project");
        string folder = Path.Combine(projectFolder, "foundation", "documents", "Portfolio");
        Directory.CreateDirectory(folder);
        return (new ProjectWorkspace(), Path.Combine(projectFolder, "project.erksproj"), folder);
    }

    private static string WriteFile(string folder, string name)
    {
        string path = Path.Combine(folder, name);
        File.WriteAllText(path, "pdf");
        return path;
    }

    private static ProjectPortfolioItem Page(string relativePath) => new()
    {
        Kind = ProjectPortfolioItemKinds.CadPage,
        RelativePath = relativePath,
    };
}
