using System.Text;
using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// All three writers ask the same ceiling, and turning it off reds all three.
///
/// 🔴 THE MEASUREMENT THE THREE-WRITER JOB ENDS ON. «The raster is fixed» was said once
/// and was true of one writer out of three; it was then said again and was true of two.
/// Counting consumers one at a time is what allowed that twice, so this asks the whole
/// question at once: three budgets, one rule, and no way for one of them to drift
/// without a red.
///
/// 🔴 HOW THE POSITIVE CONTROL IS BUILT, BECAUSE «ALL THREE CONSULT THE RULE» IS THE
/// KIND OF CLAIM THAT PASSES WHEN NOTHING IS CONNECTED. Asserting each budget against a
/// number DERIVED from the rule cannot catch a budget that hard-codes 300 dpi: the
/// derived expectation moves with the rule and the hard-coded copy agrees with it today.
/// So the load-bearing assertion is that the three answers are IDENTICAL for an
/// identical frame. A budget that stops asking - hard-codes the density, or is handed a
/// second one - diverges from the other two and reds here, whatever it hard-codes.
///
/// ⚠ AND THE THREE ARE ONLY IDENTICAL WHEN THEY ARE ASKED THE SAME QUESTION. Each has
/// its own frame arithmetic - page margins, caption bands, crop fractions - and those
/// are its own business, tested in its own file. What this file does is strip those away
/// by handing all three the same frame, so that what is left is the density alone.
/// </summary>
public sealed class THETHREEWritersShareONECeilingTests
{
    private const int DenseWidth = 15360;
    private const int DenseHeight = 8640;

    /// <summary>
    /// One frame, in three shapes. A full-bleed portfolio page IS its page, and a
    /// full-bleed board card with no caption and no crop IS its cell - so choosing a
    /// page and a cell of the same millimetres asks all three the same question.
    /// </summary>
    private const double FrameWidthMm = 420;

    private const double FrameHeightMm = 297;

    [Fact]
    public void THETHREEBudgetsGiveTheIDENTICALAnswerForAnIdenticalFrame()
    {
        VisualizationRasterPlan album = VisualizationRasterBudget.For(
            DenseWidth,
            DenseHeight,
            new PageRectMm { X = 0, Y = 0, Width = FrameWidthMm, Height = FrameHeightMm },
            VisualizationImageFitMode.CenterCrop);

        VisualizationRasterPlan portfolio = PortfolioRasterBudget.For(
            FrameWidthMm,
            FrameHeightMm,
            ProjectPortfolioLayouts.FullBleed,
            hasCaption: false,
            DenseWidth,
            DenseHeight);

        VisualizationRasterPlan board = BoardRasterBudget.For(
            new BoardRectMm(0, 0, FrameWidthMm, FrameHeightMm),
            hasCaption: false,
            ProjectPortfolioLayouts.FullBleed,
            cropWidth: 1,
            cropHeight: 1,
            DenseWidth,
            DenseHeight);

        // 🔴 THE WHOLE PLAN, NOT ONE DIMENSION. Width, height and the decision itself:
        // a budget that agreed on the pixels and disagreed on whether to do the work
        // would leave one writer re-encoding for ever or not at all.
        Assert.Equal(album, portfolio);
        Assert.Equal(album, board);

        // And the work is real, so the agreement is not three identical no-ops.
        Assert.True(album.Resample, "a 16k render over a 420 mm frame is over the rule");
        Assert.True(album.PixelWidth < DenseWidth);
    }

    [Fact]
    public void ANDTheAnswerTheyShareIsTheRULESDensity()
    {
        // What the shared number IS, stated once. Derived from the rule rather than
        // typed, because a typed pixel count is right for exactly one frame size - the
        // fault the density rule was written to replace.
        VisualizationRasterPlan board = BoardRasterBudget.For(
            new BoardRectMm(0, 0, FrameWidthMm, FrameHeightMm),
            false,
            ProjectPortfolioLayouts.FullBleed,
            1,
            1,
            DenseWidth,
            DenseHeight);

        // The covering axis touches the frame, so its density is the rule's exactly.
        // 16:9 over 420x297 is covered by its HEIGHT.
        Assert.Equal(
            1d / AlbumRasterRule.MillimetresPerPixel, board.PixelHeight / FrameHeightMm, 1);
        int fromTheRule = AlbumRasterRule.PixelsAcross(FrameHeightMm);
        Assert.InRange(board.PixelHeight, fromTheRule - 2, fromTheRule + 2);
    }

    [Fact]
    public void EACHBudgetReachesTheRuleThroughTheONEApplierNotItsOwnArithmetic()
    {
        // 🔴 STRUCTURAL, BECAUSE BEHAVIOUR CANNOT TELL THE DIFFERENCE. A budget that
        // spelled «25.4 / 300» by hand would answer identically today and drift the day
        // the rule moves - and sabotage has already proved that on this very rule, in
        // VisualizationRasterBudget, where replacing the name with the arithmetic left
        // every behavioural test green.
        foreach (string file in new[] { "PortfolioRasterBudget.cs", "BoardRasterBudget.cs" })
        {
            string source = ReadCoreSource(file);
            Assert.Contains("VisualizationRasterBudget.For(", source, StringComparison.Ordinal);

            // No second density in the CODE. 300 and 25.4 belong to the rule alone.
            // ⚠ CODE ONLY, AND THE FIRST VERSION READ THE WHOLE FILE AND WENT RED ON
            // CORRECT PROSE: both files quote the owner's «яг зөв растерийн
            // дүрэм 300dpi», which is the reason the rule exists and belongs in the
            // comment. A banned word fails both ways - it reds on the right answer as
            // readily as on the wrong one - so the ban is narrowed to what executes.
            string code = CodeOnly(source);
            Assert.DoesNotContain("25.4", code, StringComparison.Ordinal);
            Assert.DoesNotContain("300", code, StringComparison.Ordinal);
        }

        // And the one applier reaches the one owner by name.
        string applier = ReadCoreSource("VisualizationRasterBudget.cs");
        Assert.Contains("AlbumRasterRule.MillimetresPerPixel", applier, StringComparison.Ordinal);

        // 🔴 THE LAYOUT NAMES ARE THE SAME KIND OF FACT AND THEY WERE COPIED ONCE
        // ALREADY - into the very file whose comment warns against a second home. No
        // behavioural test can catch that: a copied literal answers identically until the
        // day the catalogue is renamed, and then the budget computes a FITTED frame for a
        // page the writer COVERS. Fewer pixels than the page shows is the one failure
        // direction that looks like nothing at all, so this is pinned structurally.
        foreach (string file in new[] { "PortfolioRasterBudget.cs", "BoardRasterBudget.cs" })
        {
            Assert.Contains(
                "ProjectPortfolioLayouts.FullBleed",
                CodeOnly(ReadCoreSource(file)),
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "\"FullBleed\"", CodeOnly(ReadCoreSource(file)), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ALLTHREEPreparersAreWIREDAndTheirCachesCannotCollide()
    {
        // 🔴 THE OTHER HALF OF «CODE EXISTS, NOBODY CALLS IT», COUNTED IN ONE PLACE.
        // Each of the three App preparers must name its own budget - a budget nobody
        // calls is a rule that is not applied, and that is exactly how the portfolio and
        // the boards went uncapped while the rule sat in Core looking finished.
        //
        // ⚠ Read from the App project's source because the preparers are internal to a
        // WPF assembly this project does not reference. What it can hold is the wiring;
        // the sentences and the folders are held from inside that project.
        foreach ((string preparer, string budget) in new[]
                 {
                     ("StudioVisualizationRasterPreparer.cs", "VisualizationRasterBudget.For("),
                     ("StudioPortfolioRasterPreparer.cs", "PortfolioRasterBudget.For("),
                     ("StudioBoardRasterPreparer.cs", "BoardRasterBudget.For("),
                 })
        {
            Assert.Contains(budget, ReadAppSource(preparer), StringComparison.Ordinal);
        }

        // Three reference sets, three folders. Each sweep deletes what its own set does
        // not name, so a shared folder makes every pass an orphan-killer for the other
        // two. Held here as well as inside the App project because this is the file that
        // asks the three-writer question.
        var folders = new List<string>();
        foreach (string preparer in new[]
                 {
                     "StudioVisualizationRasterPreparer.cs",
                     "StudioPortfolioRasterPreparer.cs",
                     "StudioBoardRasterPreparer.cs",
                 })
        {
            folders.Add(PreparedSegmentsOf(ReadAppSource(preparer)));
        }

        Assert.Equal(3, new HashSet<string>(folders, StringComparer.Ordinal).Count);
    }

    [Fact]
    public void THEWRITERReadsTheCaptionReserveRatherThanKeepingItsOwn()
    {
        // 🔴 THE OTHER HALF OF THE CAPTION SEAM, AND IT WAS CLAIMED BEFORE IT EXISTED.
        // A comment in THEBOARDObeysTheSameCeilingTests said this was «pinned from source in
        // a sibling»; there was no sibling. Writing the claim down is not the same as
        // holding it, and a comment asserting a test exists is worse than no comment -
        // the next reader stops looking.
        //
        // What has to be true: the band the WRITER reserves and the band the BUDGET
        // subtracts are one number. If they drift, every captioned card is prepared for a
        // frame the writer does not draw into - slightly soft, on every board, silently.
        string writer = CodeOnly(ReadPdfSource("BoardPdfWriter.cs"));

        Assert.Contains("BoardCardFrame.CaptionBandMm", writer, StringComparison.Ordinal);
        Assert.Contains("BoardCardFrame.CaptionGapMm", writer, StringComparison.Ordinal);

        // And no literal beside it. The values are 8 and 2, which are too ordinary to
        // notice coming back as a private pair.
        Assert.DoesNotContain("CaptionBandMm = 8", writer, StringComparison.Ordinal);
        Assert.DoesNotContain("CaptionGapMm = 2", writer, StringComparison.Ordinal);
    }

    /// <summary>
    /// The file with its comments taken out, so a ban applies to what executes.
    ///
    /// ⚠ LINE COMMENTS AND DOC COMMENTS ONLY - these files use no block comments, and a
    /// half-written block-comment stripper would silently pass code it failed to see.
    /// Asserted rather than assumed, so the day one appears this says so.
    /// </summary>
    private static string CodeOnly(string source)
    {
        Assert.DoesNotContain("/*", source, StringComparison.Ordinal);

        // ⚠ NO ESCAPE SEQUENCES: the newline is named by its code point because
        // every tool between here and the file has its own opinion about a backslash,
        // and two of them rewrote this line into a real line break already today.
        var kept = new List<string>();
        foreach (string line in source.Split((char)10))
        {
            string bare = line.TrimEnd((char)13);
            if (bare.TrimStart().StartsWith("//", StringComparison.Ordinal))
                continue;
            int at = bare.IndexOf("//", StringComparison.Ordinal);
            kept.Add(at >= 0 ? bare[..at] : bare);
        }

        return string.Join((char)10, kept);
    }

    /// <summary>
    /// The PreparedSegments initialiser, as written. Compared as text rather than
    /// evaluated, because these are internal to a WPF assembly.
    /// </summary>
    private static string PreparedSegmentsOf(string source)
    {
        const string anchor = "PreparedSegments =";
        int at = source.IndexOf(anchor, StringComparison.Ordinal);
        Assert.True(at > 0, "PreparedSegments was not found");
        int end = source.IndexOf(';', at);
        Assert.True(end > at, "the initialiser did not end");
        string written = source[(at + anchor.Length)..end];
        return new string(written.Where(c => !char.IsWhiteSpace(c)).ToArray());
    }

    private static string ReadCoreSource(string fileName) =>
        File.ReadAllText(
            Path.Combine(FindSourceRoot().FullName, "ErkS.Platform.Core", fileName),
            Encoding.UTF8);

    private static string ReadPdfSource(string fileName) =>
        File.ReadAllText(
            Path.Combine(FindSourceRoot().FullName, "ErkS.Platform.Pdf", fileName),
            Encoding.UTF8);

    private static string ReadAppSource(string fileName) =>
        File.ReadAllText(
            Path.Combine(FindSourceRoot().FullName, "ErkS.Studio.App", fileName),
            Encoding.UTF8);

    private static DirectoryInfo FindSourceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "src", "src");
            if (Directory.Exists(candidate))
                return new DirectoryInfo(candidate);
            directory = directory.Parent;
        }

        Assert.Fail("the source tree was not found; this test reads it");
        return new DirectoryInfo(".");
    }
}
