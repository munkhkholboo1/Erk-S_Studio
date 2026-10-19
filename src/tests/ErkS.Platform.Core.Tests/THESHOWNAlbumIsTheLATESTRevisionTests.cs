using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// The album composed from the shared album is recorded in the .erksalbum as
/// its latest revision, with the packages it was drawn from (Мастер / Батнасан,
/// 2026-10-20: R23 named an old ЕТ package while the album on screen used the
/// new ЕТ and Revit packages).
/// </summary>
public sealed class THESHOWNAlbumIsTheLATESTRevisionTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "erks-shown-album-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch
        {
        }
    }

    private static ProjectDesignSource Source(string id, string manifest) => new()
    {
        Id = id,
        Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["cloud.manifestId"] = manifest,
            ["cloud.contentHash"] = "hash-" + manifest,
        },
    };

    private (ProjectWorkspace Project, StudioAlbumDocument Album, string Pdf) Setup()
    {
        Directory.CreateDirectory(Path.Combine(root, "albums"));
        string pdf = Path.Combine(root, "albums", "shown.pdf");
        File.WriteAllBytes(pdf, [1, 2, 3]);
        var project = new ProjectWorkspace();
        project.Sources.Add(Source("et", "bc04bc21"));
        project.Sources.Add(Source("revit", "21a80c95"));
        project.Cloud.SyncStatus = ProjectSyncStatuses.Synced;
        return (project, new StudioAlbumDocument(), pdf);
    }

    [Fact]
    public void TheShownAlbumBecomesTheLatestRevisionWithItsPackages()
    {
        (ProjectWorkspace project, StudioAlbumDocument album, string pdf) = Setup();

        DeliverableRevisionRecord revision = ProjectCloudSyncMetadata.RecordShownAlbum(
            project, album, Path.Combine(root, "project.erksproject"), pdf, 59, "A3", "studio");

        Assert.Same(revision, album.Revisions.Last());
        Assert.Equal(["21a80c95", "bc04bc21"], revision.SourcePackageIds);
        Assert.Equal("albums/shown.pdf", revision.PdfPath.Replace(System.IO.Path.DirectorySeparatorChar, '/'));
        Assert.Equal(59, revision.PageCount);
        Assert.Equal(ProjectSyncStatuses.Synced, project.Cloud.SyncStatus);
    }

    [Fact]
    public void TheSameAlbumShownTwiceIsOneRevision()
    {
        (ProjectWorkspace project, StudioAlbumDocument album, string pdf) = Setup();

        ProjectCloudSyncMetadata.RecordShownAlbum(project, album, Path.Combine(root, "project.erksproject"), pdf, 59, "A3", "studio");
        ProjectCloudSyncMetadata.RecordShownAlbum(project, album, Path.Combine(root, "project.erksproject"), pdf, 59, "A3", "studio");

        Assert.Single(album.Revisions);
    }

    [Fact]
    public void BothCloudCompositionsRecordTheShownAlbum()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src", "ErkS.Studio.App")))
            directory = directory.Parent;
        string components = File.ReadAllText(Path.Combine(directory!.FullName, "src", "ErkS.Studio.App", "ShellView.AlbumComponents.cs"));

        int count = 0;
        int at = 0;
        while ((at = components.IndexOf("state.RecordShownAlbum(", at + 1, StringComparison.Ordinal)) >= 0)
            count++;
        Assert.Equal(2, count);
    }
}
