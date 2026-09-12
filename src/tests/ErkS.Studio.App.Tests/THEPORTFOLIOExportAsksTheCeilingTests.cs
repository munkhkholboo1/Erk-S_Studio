using System.Text;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// The portfolio export actually consults the ceiling, and keeps its own cache.
///
/// 🔴 WRITTEN BEFORE THE RULE'S OWN TESTS WERE FINISHED, ON PURPOSE. Five times today
/// a rule was split out, tested thoroughly, and the SEAM left unheld - twice the
/// mutation that survived was in the writer while every assertion was about the rule.
/// The pattern repeating five times is not carelessness, it is a property of the shape,
/// so from here the seam gets its test first.
///
/// ⚠ SOURCE-ANCHORED: exporting needs a project on disk, a portfolio and a PDF writer.
/// What is pinned is that the export routes its items through the preparer and that the
/// two caches cannot collide. The arithmetic is tested directly in Core.
/// </summary>
public sealed class THEPORTFOLIOExportAsksTheCeilingTests
{
    [Fact]
    public void THEEXPORTPreparesTheItemsBeforeItBuilds()
    {
        // The items handed to the writer must be the PREPARED ones. Passing the raw
        // list would leave the whole feature in place and unused - «code exists,
        // nobody calls it», which is this project's most repeated fault.
        string body = MethodBody(ReadAppSource("ShellView.Portfolio.cs"), "var request = new PortfolioBuildRequest(");
        string compact = new string(
            ReadAppSource("ShellView.Portfolio.cs").Where(c => !char.IsWhiteSpace(c)).ToArray());

        Assert.Contains("StudioPortfolioRasterPreparer.Prepare(", compact, StringComparison.Ordinal);

        // And the request carries the prepared list, not the original expression.
        Assert.Contains("items,", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Select(ResolveBuildItem).ToList(),\n                Portfolio.UsesSourcePageSize)", body, StringComparison.Ordinal);
    }

    [Fact]
    public void THETwoCachesCannotCollideBecauseTheyAreDifferentFOLDERS()
    {
        // 🔴 THIS IS NOT TIDINESS, IT IS THE WHOLE DESIGN. Each sweep deletes what its
        // own reference set does not name, and the album's set is built from the album's
        // pages. Portfolio copies in the album's folder would be swept as orphans on the
        // next reconciling build and re-encoded on the next export, for ever - the exact
        // defect the album's «do not delete a live cache entry» test was written
        // against, arriving from the other side.
        Assert.NotEqual(
            string.Join('/', StudioVisualizationRasterPreparer.PreparedSegments),
            string.Join('/', StudioPortfolioRasterPreparer.PreparedSegments));

        // Neither is inside the other, and neither is the payload store the album's
        // sweep walks.
        Assert.False(
            string.Join('/', StudioPortfolioRasterPreparer.PreparedSegments)
                .StartsWith(string.Join('/', StudioVisualizationRasterPreparer.PreparedSegments), StringComparison.Ordinal));
        Assert.DoesNotContain("images", StudioPortfolioRasterPreparer.PreparedSegments);
    }

    [Fact]
    public void THECacheNameIsSHAREDSoTheTwoPassesAgreeAboutWhatExists()
    {
        // Two writers place the same files at different sizes. A second spelling of the
        // cache name would give the two caches different opinions about whether a copy
        // already exists - which surfaces as re-encoding on every export and reads as
        // slowness rather than as a bug.
        string compact = new string(
            ReadAppSource("StudioPortfolioRasterPreparer.cs").Where(c => !char.IsWhiteSpace(c)).ToArray());

        Assert.Contains(
            "StudioVisualizationRasterPreparer.PreparedFileName(",
            compact,
            StringComparison.Ordinal);
        Assert.Contains(
            "StudioVisualizationRasterPreparer.WritePrepared(",
            compact,
            StringComparison.Ordinal);
    }

    [Fact]
    public void APDFPageIsLEFTAloneAndCOUNTED()
    {
        // A PDF page is placed as a form and stays vector; reaching the raster inside
        // one means re-rasterising the page and turning the vector around it into
        // pixels. Counted separately so «nothing was prepared» can be told from
        // «nothing needed preparing».
        string source = ReadAppSource("StudioPortfolioRasterPreparer.cs");

        Assert.Contains("vector++;", source, StringComparison.Ordinal);
        Assert.Contains("VectorCount", source, StringComparison.Ordinal);
    }

    [Fact]
    public void WHATThePassDidReachesTheOwnersSentence()
    {
        // The album's line names its reductions; this one has to as well, or the owner
        // watching their portfolio get smaller has no way to know why.
        string body = MethodBody(ReadAppSource("ShellView.Portfolio.cs"), "string ceiling = \"\";");

        // ⚠ THE CONDITIONS, NOT THE MENTIONS. A first version asked only whether the
        // names appeared, and a mutation that replaced the condition with «if (false)»
        // left them all in place and passed - the same «mentions is not uses» weakness
        // found in the raster-consumer test an hour earlier. Compared without
        // whitespace so a reformat is not a red.
        string compact = new string(body.Where(c => !char.IsWhiteSpace(c)).ToArray());

        Assert.Contains("if(prepared.PreparedCount>0)", compact, StringComparison.Ordinal);
        Assert.Contains("if(prepared.FailedCount>0)", compact, StringComparison.Ordinal);
        Assert.Contains("if(prepared.RemovedCount>0)", compact, StringComparison.Ordinal);
        Assert.Contains("AlbumRasterRule.DotsPerInch", compact, StringComparison.Ordinal);
    }

    private static string MethodBody(string source, string anchor)
    {
        string normalised = source.Replace("\r\n", "\n");
        int at = normalised.IndexOf(anchor, StringComparison.Ordinal);
        Assert.True(at > 0, anchor + " was not found");
        int end = normalised.IndexOf("\n        }", at, StringComparison.Ordinal);
        Assert.True(end > at, "the end of the block was not found");
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
