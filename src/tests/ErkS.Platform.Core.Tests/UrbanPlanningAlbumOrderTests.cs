using ErkS.Platform.Core.ProjectTypes.UrbanPlanning;

namespace ErkS.Platform.Core.Tests;

public sealed class UrbanPlanningAlbumOrderTests
{
    [Fact]
    public void GeneralPlanAlbumKeepsTheOrderTheSheetsArrivedIn()
    {
        // The slot matcher's numbered branch never fires for an AutoCAD
        // package - its numbers are bare "00".."14" while the slots are
        // "ЕТ-03".. - so ordering by slot swept every unmatched sheet to the
        // end of the album.
        var album = new AlbumDefinition { TemplateId = UrbanPlanningAlbumTemplate.PartialPlanTemplateId };
        foreach (string sheetId in new[] { "ET-00", "ET-01", "ET-02", "ET-03" })
        {
            album.Pages.Add(new AlbumPageDefinition
            {
                SheetKey = sheetId,
                // Only one sheet matches a slot; the rest must not move behind it.
                TemplateSlotId = sheetId == "ET-02" ? "planning-proposal" : "",
            });
        }

        AlbumSourceRun run = Assert.Single(AlbumBuilder.BuildSourceOrderedRuns(album));

        Assert.Equal(
            ["ET-00", "ET-01", "ET-02", "ET-03"],
            run.Pages.Select(page => page.SheetKey).ToArray());
    }

    [Fact]
    public void GeneralPlanAlbumReadsSectionsOffThePageOrder()
    {
        var album = new AlbumDefinition { TemplateId = UrbanPlanningAlbumTemplate.PartialPlanTemplateId };
        var generalPlan = new AlbumSection { Title = "Ерөнхий төлөвлөгөө" };
        var infrastructure = new AlbumSection { Title = "Инженерийн дэд бүтэц" };
        album.Sections.Add(generalPlan);
        album.Sections.Add(infrastructure);
        foreach ((string sheetKey, Guid section) in new (string, Guid)[]
                 {
                     ("ET-00", generalPlan.Id),
                     ("ET-01", generalPlan.Id),
                     ("IDB-00", infrastructure.Id),
                     ("ET-02", generalPlan.Id),
                 })
        {
            album.Pages.Add(new AlbumPageDefinition { SheetKey = sheetKey, SectionId = section });
        }

        IReadOnlyList<AlbumSourceRun> runs = AlbumBuilder.BuildSourceOrderedRuns(album);

        // Three runs, because the sheets run ЕТ, ЕТ, ИДБ, ЕТ. The section is
        // read off the order rather than the album being regrouped by section.
        Assert.Equal(
            ["Ерөнхий төлөвлөгөө", "Инженерийн дэд бүтэц", "Ерөнхий төлөвлөгөө"],
            runs.Select(run => run.Title).ToArray());
        Assert.Equal(
            ["ET-00", "ET-01", "IDB-00", "ET-02"],
            runs.SelectMany(run => run.Pages).Select(page => page.SheetKey).ToArray());
    }

    [Fact]
    public void PageBelongingToNoSectionStaysWhereItIs()
    {
        var album = new AlbumDefinition { TemplateId = UrbanPlanningAlbumTemplate.PartialPlanTemplateId };
        var generalPlan = new AlbumSection { Title = "Ерөнхий төлөвлөгөө" };
        album.Sections.Add(generalPlan);
        album.Pages.Add(new AlbumPageDefinition { SheetKey = "ET-00", SectionId = generalPlan.Id });
        // Previously swept into a trailing "Бусад" bucket at the end.
        album.Pages.Add(new AlbumPageDefinition { SheetKey = "ET-01" });
        album.Pages.Add(new AlbumPageDefinition { SheetKey = "ET-02", SectionId = generalPlan.Id });

        IReadOnlyList<AlbumSourceRun> runs = AlbumBuilder.BuildSourceOrderedRuns(album);

        Assert.Equal(
            ["ET-00", "ET-01", "ET-02"],
            runs.SelectMany(run => run.Pages).Select(page => page.SheetKey).ToArray());
    }

    [Fact]
    public void GeneralPlanAlbumDropsTheLegacyTableOfContentsPage()
    {
        // The composition carries the drawing list as a page of its own; the
        // flag added a second, A4-portrait one in the middle of the album.
        AlbumDefinition definition = UrbanPlanningAlbumTemplate.CreateDefinition(
            PartialMasterPlanDrawingSequence.StageType);
        definition.IncludeTableOfContents = true;

        Assert.True(UrbanPlanningAlbumTemplate.EnsureGeneratedPages(definition));
        Assert.False(definition.IncludeTableOfContents);
    }
}
