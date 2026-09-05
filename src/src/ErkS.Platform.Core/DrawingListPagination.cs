namespace ErkS.Platform.Core;

/// <summary>
/// How many rows of the drawing list fit on one page, and therefore how many
/// pages the list needs.
///
/// It exists because the answer is needed in two places that run at different
/// times and must not disagree. The PLANNER reserves album pages before
/// anything is drawn - that reservation is what the numbering of every later
/// sheet is built on - and the WRITER decides where to break when it finally
/// draws. One rule, asked twice.
///
/// Before this, the writer had no answer at all: rows were emitted at a fixed
/// pitch with nothing checking the bottom of the page, so a list longer than
/// the sheet simply carried on past the edge. The rows were drawn; they were
/// not visible. A user with sixty sheets received a list of about thirty and
/// nothing anywhere said so.
///
/// Worth noting because it inverts the usual direction: the OLDER A4 list this
/// one replaced did page correctly. The capability was lost in the rewrite, so
/// restoring it is not a new feature.
/// </summary>
public static class DrawingListPagination
{
    /// <summary>Pitch of one row, matching the writer's ruled grid.</summary>
    public const double RowHeightMm = 7.0;

    /// <summary>Gap between the sheet title strip and the first row.</summary>
    public const double TopGapMm = 5.0;

    /// <summary>
    /// Gap kept above the corner title block. The list must not run into it -
    /// the block is drawn afterwards and would sit on top of the rows.
    /// </summary>
    public const double BottomGapMm = 4.0;

    /// <summary>
    /// Whether this album's generated pages use the working-drawing format -
    /// the only family whose drawing list paginates. Asked here so the planner
    /// and the writer cannot answer it differently.
    /// </summary>
    public static bool UsesWorkingDrawingFormat(AlbumDefinition? album) =>
        album is not null &&
        PageFormatCatalog.IsUsable(album.GeneratedPageFormat) &&
        album.GeneratedPageFormat!.Kind == PageFormatKind.WorkingDrawing;

    /// <summary>Where the header row starts, in millimetres from the page top.</summary>
    public static double HeaderTopMm(WorkingDrawingPageRegions regions)
    {
        ArgumentNullException.ThrowIfNull(regions);
        return regions.SheetTitleArea.Y + regions.SheetTitleArea.Height + TopGapMm;
    }

    /// <summary>
    /// Data rows that fit on one page, the repeated header already deducted.
    ///
    /// The header repeats on every page rather than appearing once: a
    /// continuation sheet whose columns are unlabelled is a sheet somebody has
    /// to hold next to the first one.
    /// </summary>
    public static int RowsPerPage(WorkingDrawingPageRegions regions)
    {
        ArgumentNullException.ThrowIfNull(regions);
        double availableMm = regions.TitleBlockArea.Y - BottomGapMm - HeaderTopMm(regions);
        int rows = (int)Math.Floor(availableMm / RowHeightMm) - 1;
        return Math.Max(1, rows);
    }

    /// <summary>
    /// Pages needed for <paramref name="rowCount"/> rows. Always at least one:
    /// an album with no sheets still prints the list page, empty.
    /// </summary>
    public static int PageCount(int rowCount, int rowsPerPage)
    {
        int perPage = Math.Max(1, rowsPerPage);
        return rowCount <= perPage ? 1 : (rowCount + perPage - 1) / perPage;
    }

    /// <summary>
    /// The rows belonging to one page. <paramref name="pageIndex"/> is
    /// zero-based.
    ///
    /// The LAST page takes everything that remains, even if that is more than
    /// fits. If the planner and the writer ever disagreed about the row count,
    /// the visible result would be a crowded final page - not rows that vanish.
    /// Losing them is the failure this class was written to end, and it must
    /// not come back through the arithmetic that ended it.
    /// </summary>
    public static (int Skip, int Take) Slice(int pageIndex, int rowsPerPage, int rowCount, int pageCount)
    {
        int perPage = Math.Max(1, rowsPerPage);
        int index = Math.Clamp(pageIndex, 0, Math.Max(0, pageCount - 1));
        int skip = index * perPage;
        if (skip >= rowCount)
            return (rowCount, 0);
        bool last = index == pageCount - 1;
        return (skip, last ? rowCount - skip : Math.Min(perPage, rowCount - skip));
    }
}
