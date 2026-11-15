using ErkS.Platform.Contracts;
using ErkS.Platform.Core.ProjectTypes.UrbanPlanning;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// ART-07/2026, 2026-11-15 (owner through CGA): «байршил бүр (Бэлх, Донгой, Чулуутын гүүр) альбумд тусдаа бүлэг
/// болно. Бүлэг бүр өөрийн жижиг нүүртэй». A sheet's package <c>category</c> is its group; every group is one
/// section of the urban-planning album, opened by a sub-cover; ungrouped sheets keep their place.
/// </summary>
public sealed class AMULTISITEPlanGroupsItsSheetsByLocationTests
{
    private static AlbumDefinition Album(params (string Key, string Category)[] sheets)
    {
        var album = new AlbumDefinition { TemplateId = UrbanPlanningAlbumTemplate.PartialPlanTemplateId };
        foreach ((string key, string category) in sheets)
            album.Pages.Add(new AlbumPageDefinition { SheetKey = key, SourceCategorySnapshot = category });
        return album;
    }

    [Fact]
    public void EVERYLocationIsOneRunInTheOrderItFirstArrived()
    {
        AlbumDefinition album = Album(
            ("common", ""),
            ("B-A1-1", "Бэлх"),
            ("D-A1-1", "Донгой"),
            ("B-A3-1", "Бэлх"),
            ("C-A1-1", "Чулуутын гүүр"),
            ("D-A3-1", "Донгой"));

        IReadOnlyList<AlbumSourceRun> runs = AlbumBuilder.BuildSourceOrderedRuns(album);

        Assert.Equal(["", "Бэлх", "Донгой", "Чулуутын гүүр"], runs.Select(run => run.Category));
        Assert.Equal(["B-A1-1", "B-A3-1"], runs[1].Pages.Select(page => page.SheetKey));
        Assert.Equal(["D-A1-1", "D-A3-1"], runs[2].Pages.Select(page => page.SheetKey));
        Assert.Equal("Бэлх", runs[1].Title);
    }

    [Fact]
    public void ANALBUMWithoutGroupsIsUnchanged()
    {
        IReadOnlyList<AlbumSourceRun> runs = AlbumBuilder.BuildSourceOrderedRuns(Album(("a", ""), ("b", "")));

        AlbumSourceRun run = Assert.Single(runs);
        Assert.Equal("", run.Category);
    }

    [Fact]
    public void THEPackagesCategoryIsRead()
    {
        var entry = System.Text.Json.JsonSerializer.Deserialize<SheetPackageEntry>(
            """{"sheetId":"s1","category":"Бэлх"}""",
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;

        Assert.Equal("Бэлх", entry.Category);
    }
}
