using ErkS.Platform.Core;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// The store-level tests prove the relocation and relink helpers work; these
/// prove AppState actually calls them on open and saves the result — the
/// half the exporting plugins depend on, since they read the same file.
/// </summary>
public sealed class AppStateProjectPortabilityTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "erks-appstate-portability-tests",
        Guid.NewGuid().ToString("N"));

    public AppStateProjectPortabilityTests()
    {
        Directory.CreateDirectory(root);
    }

    [Fact]
    public void OpeningACopiedProjectRelocatesItsInboxAndRelinksItsDrawing_AndSavesBoth()
    {
        string originalFolder = Path.Combine(root, "machine-a", "ATD-009");
        string copiedFolder = Path.Combine(root, "machine-b", "ATD-009");
        Directory.CreateDirectory(originalFolder);
        Directory.CreateDirectory(Path.Combine(copiedFolder, "drawings"));

        // The drawing travelled with the copy; the recorded path did not.
        string movedDrawing = Path.Combine(copiedFolder, "drawings", "tower.dwg");
        File.WriteAllText(movedDrawing, "dwg");
        string originalInbox = Path.Combine(originalFolder, "sources", "AutoCAD", "deliveries");

        var project = ProjectWorkspaceStore.Create("ATD-009", "Portability through AppState");
        project.Sources.Add(new ProjectDesignSource
        {
            Name = "AutoCAD",
            Kind = DesignSourceKind.AutoCad,
            NativeDocumentPath = Path.Combine(originalFolder, "drawings", "tower.dwg"),
            InboxFolder = originalInbox,
        });
        string originalPath = Path.Combine(originalFolder, ProjectWorkspace.DefaultFileName);
        ProjectWorkspaceStore.Save(project, originalPath);
        string copiedPath = Path.Combine(copiedFolder, ProjectWorkspace.DefaultFileName);
        File.Copy(originalPath, copiedPath);

        using (var state = new AppState())
        {
            state.OpenProject(copiedPath);
        }

        // Read the file back cold: what AppState persisted is what AutoCAD will read.
        ProjectDesignSource reopened = Assert.Single(ProjectWorkspaceStore.Load(copiedPath).Sources);
        Assert.True(
            ProjectWorkspacePaths.IsInside(copiedFolder, reopened.InboxFolder),
            $"inbox still outside the copy: {reopened.InboxFolder}");
        Assert.Equal(originalInbox, reopened.Metadata["legacyExternalInbox"]);
        Assert.Equal(Path.GetFullPath(movedDrawing), reopened.NativeDocumentPath);
        Assert.Equal(
            Path.Combine(originalFolder, "drawings", "tower.dwg"),
            reopened.Metadata["previousNativeDocumentPath"]);

        // The original is untouched by its copy being opened elsewhere.
        ProjectDesignSource stillHome = Assert.Single(ProjectWorkspaceStore.Load(originalPath).Sources);
        Assert.Equal(originalInbox, stillHome.InboxFolder);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
