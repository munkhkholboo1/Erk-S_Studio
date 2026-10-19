namespace ErkS.Studio.App.Tests;

/// <summary>
/// The organizer shows a working-drawing album from the same outline the
/// album prints from (three parts, the printed numbers), not from the concept
/// tree with the sequencer's numbering (Батнасан, 2026-10-20).
/// </summary>
public sealed class THEORGANIZERShowsTheWORKINGDrawingsAsTheyPRINTTests
{
    [Fact]
    public void TheOrganizerReadsTheWorkingDrawingOutlineBeforeTheConceptTree()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src", "src", "ErkS.Studio.App")))
            directory = directory.Parent;
        string view = File.ReadAllText(Path.Combine(directory!.FullName, "src", "src", "ErkS.Studio.App", "ShellView.Workspaces.cs"));

        int builder = view.IndexOf("private List<AlbumPageWorkspaceItem> BuildAlbumWorkspaceItems()", StringComparison.Ordinal);
        int outline = view.IndexOf("WorkingDrawingAlbumOutline.Create(buildProject, state.Library)", builder, StringComparison.Ordinal);
        int append = view.IndexOf("AppendWorkingDrawingOutline(root, albumNodeKey, workingDrawingOutline);", builder, StringComparison.Ordinal);
        int sequence = view.IndexOf("BuildingArchitectureConceptAlbumSequencer.Create(", builder, StringComparison.Ordinal);

        Assert.True(builder > 0 && outline > builder && append > outline && sequence > append);
    }
}
