using ErkS.Platform.Core;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// A page of the album must be able to say what it is called.
///
/// One component covers a whole run of pages and carries one label for the run.
/// Naming each of its pages after it tells a reader that six pages are all
/// "Орон сууц-1", which is true of the run and useless for finding one page in
/// it. The name of the page travels with the page.
/// </summary>
public sealed class AlbumPageTitleTests
{
    [Fact]
    public void PreviewManifest_GivesEveryPageOfAComponentItsOwnName()
    {
        var result = new AlbumBuildResult
        {
            OutputPath = @"C:\albums\album.pdf",
            SheetCount = 3,
            PageCount = 3,
        };
        result.Components.Add(new AlbumBuildComponent
        {
            Code = "AR",
            Label = "Орон сууц-1",
            Order = 1,
            Pages =
            [
                Page(1, "ДАВХРЫН БАЙГУУЛАЛТ"),
                Page(2, "ЗҮСЭЛТ 1-1"),
                Page(3, "ФАСАД А-Б"),
            ],
            PageNumbers = [1, 2, 3],
        });

        var cache = new StudioAlbumPreviewManifestCache();
        cache.Record(result);

        IReadOnlyList<ProjectCloudAlbumComponentPageReference> pages =
            cache.Resolve(@"C:\albums\album.pdf", sharedManifest: null)[0].Pages;

        Assert.Equal("ДАВХРЫН БАЙГУУЛАЛТ", pages[0].Title);
        Assert.Equal("ЗҮСЭЛТ 1-1", pages[1].Title);
        Assert.Equal("ФАСАД А-Б", pages[2].Title);
    }

    [Fact]
    public void PreviewManifest_LeavesThePageUnnamedRatherThanBorrowingTheComponentLabel()
    {
        // A page rendered before pages carried their own name has none. The
        // caller decides what to show for it; the manifest must not invent one,
        // or an empty name becomes indistinguishable from a real one.
        var result = new AlbumBuildResult
        {
            OutputPath = @"C:\albums\album.pdf",
            SheetCount = 1,
            PageCount = 1,
        };
        result.Components.Add(new AlbumBuildComponent
        {
            Code = "COVER",
            Label = "Нүүр хуудас",
            Order = 1,
            Pages = [Page(1, "")],
            PageNumbers = [1],
        });

        var cache = new StudioAlbumPreviewManifestCache();
        cache.Record(result);

        Assert.Equal(
            "",
            cache.Resolve(@"C:\albums\album.pdf", sharedManifest: null)[0].Pages[0].Title);
    }

    private static AlbumBuildComponentPage Page(int number, string title) => new()
    {
        PageNumber = number,
        PageKey = $"key-{number}",
        Title = title,
        NativeSheetId = $"sheet-{number}",
        NativePageNumber = 1,
    };
}
