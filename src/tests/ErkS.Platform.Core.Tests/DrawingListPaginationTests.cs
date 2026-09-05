using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// The drawing list used to run off the bottom of its page. Rows were emitted
/// at a fixed pitch with nothing checking where the page ended, so an album of
/// sixty sheets printed a list of about thirty - and the rows that did not fit
/// were still drawn, just not anywhere visible. Nothing reported a loss.
///
/// Restoring paging is not a new feature: the older A4 list it replaced paged
/// correctly, so this is a capability the rewrite dropped.
///
/// The reason it stayed broken is worth stating: fixing it in the writer alone
/// would have traded a silent loss for a silent MISNUMBERING, because the
/// number of pages the list occupies is decided by the planner before anything
/// is drawn and every later sheet's number is built on that count. One rule,
/// asked in both places.
/// </summary>
public sealed class DrawingListPaginationTests
{
    private static WorkingDrawingPageRegions Regions() =>
        WorkingDrawingPageLayout.Resolve(PageFormatCatalog.DefaultWorkingDrawing);

    [Fact]
    public void APageHoldsAWorkableNumberOfRows()
    {
        // Not a specific number - the layout may change - but it must be a
        // plausible page of rows. A zero or a negative would make every album
        // one page per row.
        int rows = DrawingListPagination.RowsPerPage(Regions());

        Assert.InRange(rows, 10, 60);
    }

    [Fact]
    public void TheRowsStopAboveTheCornerTitleBlock()
    {
        // The block is drawn after the rows and would sit on top of them.
        WorkingDrawingPageRegions regions = Regions();
        double lastRowBottom = DrawingListPagination.HeaderTopMm(regions) +
            (DrawingListPagination.RowsPerPage(regions) + 1) * DrawingListPagination.RowHeightMm;

        Assert.True(
            lastRowBottom <= regions.TitleBlockArea.Y,
            $"rows reach {lastRowBottom:F1} mm, title block starts at {regions.TitleBlockArea.Y:F1} mm");
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(20, 1)]
    [InlineData(21, 2)]
    [InlineData(40, 2)]
    [InlineData(41, 3)]
    public void PageCountRoundsUp_AndAnEmptyAlbumStillPrintsOnePage(int rowCount, int expected)
    {
        // An album with no sheets still gets its list page, empty. Printing no
        // page at all would shift every number after it.
        Assert.Equal(expected, DrawingListPagination.PageCount(rowCount, rowsPerPage: 20));
    }

    [Fact]
    public void EveryRowLandsOnExactlyOnePage()
    {
        // The property that matters: no row is dropped and none is drawn twice.
        const int rowsPerPage = 12;
        for (int rowCount = 0; rowCount <= 100; rowCount++)
        {
            int pages = DrawingListPagination.PageCount(rowCount, rowsPerPage);
            var seen = new List<int>();
            for (int page = 0; page < pages; page++)
            {
                (int skip, int take) = DrawingListPagination.Slice(page, rowsPerPage, rowCount, pages);
                for (int i = 0; i < take; i++)
                    seen.Add(skip + i);
            }

            Assert.Equal(rowCount, seen.Count);
            Assert.Equal(Enumerable.Range(0, rowCount), seen);
        }
    }

    [Fact]
    public void ExtraRowsCROWDTheLastPageRatherThanDisappearing()
    {
        // If the planner and the writer ever disagreed about how many rows there
        // are - the planner counts the album's sheets before they are sequenced -
        // the last page takes the remainder. A crowded page is visible; rows that
        // silently stop being drawn are what this whole class exists to end.
        (int skip, int take) = DrawingListPagination.Slice(
            pageIndex: 1,
            rowsPerPage: 10,
            rowCount: 27,
            pageCount: 2);

        Assert.Equal(10, skip);
        Assert.Equal(17, take);
    }

    [Fact]
    public void APageIndexBeyondTheEndFallsBackToTheLASTPage()
    {
        // Written expecting an empty result, and the code was right instead: an
        // index past the end can only mean the planner and the writer disagree,
        // and in that case showing the remaining rows beats showing none. The
        // rule throughout is that rows are never what gives way.
        (int skip, int take) = DrawingListPagination.Slice(
            pageIndex: 9,
            rowsPerPage: 10,
            rowCount: 5,
            pageCount: 1);

        Assert.Equal(0, skip);
        Assert.Equal(5, take);
    }

    [Fact]
    public void ThePLANNERActuallyReservesTheExtraPages()
    {
        // Sabotaging the planner back to a single draft left every rule in this
        // file green: the arithmetic was right and nobody asked it. The count
        // reserved here is what the sequencer numbers the rest of the album
        // from, so a list that grows without reserving misnumbers everything
        // after it - silently.
        AlbumProject project = WorkingDrawingProjectWith(sheetCount: 400);

        IReadOnlyList<ConceptGeneratedPagePlan> plan =
            BuildingArchitectureConceptGeneratedPagePlanner.Create(project);
        List<ConceptGeneratedPagePlan> listPages = plan
            .Where(entry => entry.Component.GeneratedPageKind == AlbumGeneratedPageKind.None)
            .ToList();

        Assert.True(
            listPages.Count > 1,
            $"400 sheets reserved {listPages.Count} list page(s); the planner is not counting them");
        Assert.All(listPages, entry => Assert.Equal(listPages.Count, entry.BatchCount));
        Assert.Equal(
            Enumerable.Range(1, listPages.Count),
            listPages.Select(entry => entry.BatchNumber));
    }

    [Fact]
    public void AShortAlbumStillReservesExactlyOneListPage()
    {
        AlbumProject project = WorkingDrawingProjectWith(sheetCount: 3);

        Assert.Single(BuildingArchitectureConceptGeneratedPagePlanner
            .Create(project)
            .Where(entry => entry.Component.GeneratedPageKind == AlbumGeneratedPageKind.None));
    }

    [Fact]
    public void TheWRITERDrawsOnlyItsOwnSliceOfTheRows()
    {
        // The other half of the same defect: the planner can reserve three pages
        // and the writer still draw the whole list onto each of them. The two
        // only meet inside a call that emits into a PDF, so this reads the
        // source rather than the output.
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        string? writer = null;
        while (directory is not null && writer is null)
        {
            string candidate = Path.Combine(
                directory.FullName, "src", "src", "ErkS.Platform.Pdf", "PdfSharpAlbumWriter.cs");
            if (File.Exists(candidate))
                writer = File.ReadAllText(candidate);
            directory = directory.Parent;
        }

        Assert.NotNull(writer);
        int start = writer!.IndexOf(
            "private static void DrawWorkingDrawingTableOfContents(",
            StringComparison.Ordinal);
        Assert.True(start >= 0, "the drawing-list writer was renamed; check this test with it");
        string body = writer[start..(start + 1600)];

        Assert.Contains("DrawingListPagination.Slice", body, StringComparison.Ordinal);
        Assert.Contains("plan.BatchNumber", body, StringComparison.Ordinal);
        Assert.Contains("Skip(skip).Take(take)", body, StringComparison.Ordinal);
    }

    private static AlbumProject WorkingDrawingProjectWith(int sheetCount)
    {
        var project = new AlbumProject();
        project.Album.GeneratedPageFormat = PageFormatCatalog.DefaultWorkingDrawing;
        project.Album.Composition.Add(new AlbumCompositionItem
        {
            Kind = AlbumCompositionKind.Generated,
            GeneratedPageKind = AlbumGeneratedPageKind.None,
            Title = "ЗУРГИЙН ЖАГСААЛТ, ТАЙЛБАР БИЧИГ",
            Number = "01",
        });
        for (int index = 0; index < sheetCount; index++)
            project.Album.Pages.Add(new AlbumPageDefinition());
        return project;
    }

    [Fact]
    public void OnlyTheWorkingDrawingFamilyPaginates()
    {
        // The concept album's list is the older A4 one, which pages inside a
        // single component. Reserving extra album pages for it would shift
        // numbers in an album whose list never needed them.
        Assert.False(DrawingListPagination.UsesWorkingDrawingFormat(null));
        Assert.False(DrawingListPagination.UsesWorkingDrawingFormat(new AlbumDefinition()));
    }
}
