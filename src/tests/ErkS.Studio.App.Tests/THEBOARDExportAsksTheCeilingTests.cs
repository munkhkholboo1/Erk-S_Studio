using System.Text;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// The board export consults the ceiling, and keeps its own cache.
///
/// 🔴 THE SEAM'S TEST, WRITTEN BEFORE THE RULE'S. Six times now a rule has been split
/// out, tested thoroughly, and the seam left unheld - twice the surviving mutation was
/// in the caller while every assertion was about the rule. The third writer gets the
/// order the pattern earned.
///
/// 🔴 AND THE THIRD SET IS WHAT MAKES THIS MORE THAN A REPEAT. Each sweep deletes what
/// its OWN reference set does not name. Two folders were enough for two questions; a
/// third question sharing a folder would delete the other two's live copies on every
/// build and re-encode them on every export, for ever.
///
/// ⚠ SOURCE-ANCHORED: building a board needs a project on disk, a grid and a PDF
/// writer. What is pinned is that the export routes its cards through the preparer,
/// that the three caches cannot collide, and that the crop reaches the rule. The
/// arithmetic is tested directly in Core.
/// </summary>
public sealed class THEBOARDExportAsksTheCeilingTests
{
    [Fact]
    public void THEEXPORTPreparesTheBoardsBeforeItBuilds()
    {
        // The boards handed to the writer must be the PREPARED ones. Passing the raw
        // list would leave the whole feature in place and unused - «code exists,
        // nobody calls it», this project's most repeated fault, and the one that made
        // «I fixed the raster» true of one writer out of three.
        string source = ReadAppSource("ShellView.Boards.cs");
        string compact = Compact(source);

        Assert.Contains("StudioBoardRasterPreparer.Prepare(", compact, StringComparison.Ordinal);

        // 🔴 THE ORDER, NOT ONLY THE CALL. Preparing after the request is constructed
        // would compute every reduction, write every file, and then build from the
        // originals - a slower export with identical output, which reads as «the rule
        // does not work» rather than as a wiring fault.
        int prepared = compact.IndexOf("StudioBoardRasterPreparer.Prepare(", StringComparison.Ordinal);
        int built = compact.IndexOf("BoardPdfWriter.Build(", StringComparison.Ordinal);
        Assert.True(prepared > 0 && built > prepared, "the boards are prepared before they are built");

        // And the request carries the prepared list, not the list that was just built.
        string request = Between(source, "BoardPdfWriter.Build(new BoardBuildRequest(", "));");
        Assert.Contains("prepared.Boards", Compact(request), StringComparison.Ordinal);
    }

    [Fact]
    public void THETHREECachesCannotCollideBecauseTheyAreDifferentFOLDERS()
    {
        // 🔴 THIS IS THE WHOLE DESIGN, NOT TIDINESS. Each sweep deletes what its own
        // reference set does not name; the album's set is built from the album's pages,
        // the portfolio's from its items, the board's from its cards. A shared folder
        // makes every pass an orphan-killer for the other two - the exact defect the
        // album's «do not delete a live cache entry» test was written against, now
        // reachable from two sides instead of one.
        string album = string.Join('/', StudioVisualizationRasterPreparer.PreparedSegments);
        string portfolio = string.Join('/', StudioPortfolioRasterPreparer.PreparedSegments);
        string board = string.Join('/', StudioBoardRasterPreparer.PreparedSegments);

        Assert.Equal(3, new HashSet<string>([album, portfolio, board], StringComparer.Ordinal).Count);

        // And no folder is inside another, which would make one sweep walk the other's
        // files without naming them.
        foreach ((string outer, string inner) in new[]
                 {
                     (album, portfolio), (album, board),
                     (portfolio, album), (portfolio, board),
                     (board, album), (board, portfolio),
                 })
        {
            Assert.False(
                inner.StartsWith(outer + "/", StringComparison.Ordinal),
                inner + " lives inside " + outer);
        }
    }

    [Fact]
    public void THECacheNameIsSHAREDSoTheThreePassesAgreeAboutWhatExists()
    {
        // Three writers place the same files at three different sizes. A second
        // spelling of the cache name would give the caches different opinions about
        // whether a copy already exists - which surfaces as re-encoding on every
        // export and reads as slowness rather than as a bug.
        string compact = Compact(ReadAppSource("StudioBoardRasterPreparer.cs"));

        Assert.Contains(
            "StudioVisualizationRasterPreparer.PreparedFileName(", compact, StringComparison.Ordinal);
        Assert.Contains(
            "StudioVisualizationRasterPreparer.WritePrepared(", compact, StringComparison.Ordinal);
    }

    [Fact]
    public void THECROPIsCarriedIntoTheRuleBecauseACroppedCardShowsLess()
    {
        // 🔴 THE BOARD'S OWN HAZARD, AND IT IS THE ONE DIRECTION THAT LOOKS LIKE
        // NOTHING. A card showing a quarter of its source across draws the WHOLE source
        // four times wider than the card, so it needs four times the pixels the card's
        // own size suggests. Ignoring the crop would reduce every cropped card to a
        // quarter of the density it needs - and a soft card on a board printed a metre
        // across is visible to everyone standing in front of it.
        string compact = Compact(ReadAppSource("StudioBoardRasterPreparer.cs"));

        Assert.Contains("card.CropWidth", compact, StringComparison.Ordinal);
        Assert.Contains("card.CropHeight", compact, StringComparison.Ordinal);
    }

    [Fact]
    public void APLANCardAndAPDFPageAreLEFTAloneAndCOUNTED()
    {
        // A plan card is drawn from its classification and a PDF page is placed as a
        // form; both are vector, and reaching the raster inside either means destroying
        // the vector around it. Counted so «nothing was prepared» can be told from
        // «nothing needed preparing».
        string source = ReadAppSource("StudioBoardRasterPreparer.cs");

        Assert.Contains("PlanPath", source, StringComparison.Ordinal);
        Assert.Contains("VectorCount", source, StringComparison.Ordinal);
    }

    [Fact]
    public void WHATThePassDidReachesTheOwnersSentence()
    {
        // The album's line names its reductions and so does the portfolio's; a board
        // export that silently halved its own file would leave the owner guessing.
        // ⚠ THE CONDITIONS, NOT THE MENTIONS - a first version of the portfolio's
        // equivalent asked only whether the names appeared, and «if (false)» passed it.
        string compact = Compact(Between(
            ReadAppSource("ShellView.Boards.cs"), "string ceiling = \"\";", "\n        }"));

        Assert.Contains("if(prepared.Did.PreparedCount>0)", compact, StringComparison.Ordinal);
        Assert.Contains("if(prepared.Did.FailedCount>0)", compact, StringComparison.Ordinal);
        Assert.Contains("if(prepared.Did.RemovedCount>0)", compact, StringComparison.Ordinal);
        Assert.Contains("AlbumRasterRule.DotsPerInch", compact, StringComparison.Ordinal);
    }

    private static string Compact(string text) =>
        new(text.Where(c => !char.IsWhiteSpace(c)).ToArray());

    private static string Between(string source, string anchor, string closer)
    {
        string normalised = source.Replace("\r\n", "\n");
        int at = normalised.IndexOf(anchor, StringComparison.Ordinal);
        Assert.True(at > 0, anchor + " was not found");
        int end = normalised.IndexOf(closer, at, StringComparison.Ordinal);
        Assert.True(end > at, "the end of the block was not found after " + anchor);
        return normalised[at..end];
    }

    private static string ReadAppSource(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(
                directory.FullName, "src", "src", "ErkS.Studio.App", fileName);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate, Encoding.UTF8);
            directory = directory.Parent;
        }

        Assert.Fail(fileName + " was not found; this test reads it from source");
        return "";
    }
}
