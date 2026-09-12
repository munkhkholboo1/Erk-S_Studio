using System.Text;
using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// Which raster paths the album's density rule reaches, counted from source.
///
/// 🔴 THE RULE'S OWN FILE CARRIED A PROMISE TO MEASURE THIS. It said «the album also
/// rasterises transparent hatches and carries imported PDF pages, and whether those
/// land on 300 DPI is a separate measurement». A note saying «somebody should check»
/// is not a check, and the day it is written is the last day anybody remembers it.
///
/// 🔴 WHAT THE COUNT FOUND: an UNCAPPED TWIN of the path that was just capped. The
/// portfolio and the boards place raster with no ceiling, and the portfolio draws the
/// very files the visualisation pages now reduce - its intake copies
/// image.RelativePath off the owner's visualisation records. «I fixed the raster» was
/// true of one writer out of three.
///
/// ⚠ AND TWO OF THE FOUR PATHS ARE NOT STUDIO'S RASTER AT ALL, which is why this is a
/// measurement and not a defect list: transparent hatches are made by AutoCAD's plot,
/// and imported pages pass through as vector on purpose. A rule cannot be blamed for
/// not reaching bytes it would have to destroy vector content to touch.
/// </summary>
public sealed class THEOTHERRasterPathsAreCountedNotAssumedTests
{
    [Fact]
    public void THERuleIsConsultedByTheVisualisationPathANDNoOtherWriter()
    {
        // 🔴 DERIVED, NOT LISTED. Every file that names the rule is found by reading
        // the source tree, so a new consumer appears here as a red - and so does the
        // visualisation path quietly stopping to ask.
        IReadOnlyList<string> consumers = FilesMentioning("AlbumRasterRule");

        Assert.Contains("VisualizationRasterBudget.cs", consumers);
        Assert.Contains("AlbumRasterRule.cs", consumers);

        // 🔴 MENTIONING THE NAME IS NOT CONSULTING THE RULE, AND SABOTAGE PROVED IT:
        // replacing the budget's use of MillimetresPerPixel with a hand-written
        // «25.4d / 300d» left this test green, because the file still names the rule in
        // its own documentation. Behaviour is identical too - the same number, spelled
        // twice - so only a structural assertion can catch the second home appearing.
        string budget = ReadCoreSource("VisualizationRasterBudget.cs");
        Assert.Contains("AlbumRasterRule.MillimetresPerPixel", budget, StringComparison.Ordinal);
        Assert.Contains("AlbumRasterRule.PixelsAcross", budget, StringComparison.Ordinal);

        // The writers that place raster do NOT consult it. That is the finding, and
        // it is asserted so that fixing one of them shows up here.
        Assert.DoesNotContain("PortfolioPdfWriter.cs", consumers);
        Assert.DoesNotContain("BoardPdfWriter.cs", consumers);
    }

    [Fact]
    public void THEUNCAPPEDTwinsPlaceRasterStraightFromTheSourceFile()
    {
        // The mechanism, named: no size is computed, the file goes in as it is. If
        // either writer ever grows a ceiling, this goes red and the note above is due
        // an update - which is the point of pinning it.
        Assert.Contains(
            "XImage.FromFile(item.SourcePath)",
            ReadPdfSource("PortfolioPdfWriter.cs"),
            StringComparison.Ordinal);
        Assert.Contains(
            "XImage.FromFile(card.SourcePath)",
            ReadPdfSource("BoardPdfWriter.cs"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void IMPORTEDPagesStayVectorAndThatIsDELIBERATE()
    {
        // ⚠ NOT A GAP. The rule cannot reach raster sealed inside somebody else's PDF
        // page without re-rasterising the page, which would turn the vector drawing
        // around it into pixels too. The writers say so in their own words.
        string portfolio = ReadPdfSource("PortfolioPdfWriter.cs");

        Assert.Contains("XPdfForm.FromFile(item.SourcePath)", portfolio, StringComparison.Ordinal);
        Assert.Contains("stays vector", portfolio, StringComparison.Ordinal);
    }

    [Fact]
    public void STUDIOMakesNODPIDecisionInTheWriterLayerAtAll()
    {
        // 🔴 THE TRANSPARENT-HATCH ANSWER, AND IT IS AN ABSENCE. Searching the whole
        // PDF-writing project for a density decision finds none: the hatch raster is
        // AutoCAD's, made at plot time, and it arrives inside an imported page. The
        // lever is CGA's plot settings, not this rule.
        //
        // ⚠ Asserted as an absence, so it is stated the honest way: if Studio ever
        // starts rasterising here, this test goes red and the claim must be rewritten
        // rather than quietly outliving its truth.
        IReadOnlyList<string> deciders = FilesMentioning("DotsPerInch", "ErkS.Platform.Pdf");

        // 🔴 THE POSITIVE CONTROL FIRST, BECAUSE THE CLAIM IS A ZERO. A search that
        // was looking in the wrong place, or at nothing, would report «no density
        // decisions» just as confidently - and the conclusion drawn from it would be
        // that the hatch path is somebody else's problem. So the same search is asked
        // for something that MUST be there.
        Assert.NotEmpty(FilesMentioning("XImage", "ErkS.Platform.Pdf"));

        Assert.Empty(deciders);
    }

    [Fact]
    public void THEPORTFOLIOPageIsTheSizeTheNoteSaysItIs()
    {
        // The arithmetic behind «4961 px across», computed from the product's own
        // default rather than from the sentence in the comment - a number quoted in
        // prose and nowhere else is a number that drifts.
        var portfolio = new ProjectPortfolio();

        Assert.Equal(420d, portfolio.PageWidthMm);
        Assert.Equal(4961, AlbumRasterRule.PixelsAcross(portfolio.PageWidthMm));
    }

    private static IReadOnlyList<string> FilesMentioning(string needle, string? project = null)
    {
        DirectoryInfo root = FindSourceRoot();
        var found = new List<string>();
        IEnumerable<FileInfo> files = project is null
            ? root.GetFiles("*.cs", SearchOption.AllDirectories)
            : new DirectoryInfo(Path.Combine(root.FullName, project))
                .GetFiles("*.cs", SearchOption.AllDirectories);

        foreach (FileInfo file in files)
        {
            if (file.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                file.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            {
                continue;
            }

            if (File.ReadAllText(file.FullName, Encoding.UTF8).Contains(needle, StringComparison.Ordinal))
                found.Add(file.Name);
        }

        // The instrument first: a search that found nothing everywhere would make
        // every «does not contain» assertion pass for the wrong reason.
        if (project is null)
            Assert.True(found.Count > 0, "the source search found nothing at all");

        return found;
    }

    private static string ReadCoreSource(string fileName) =>
        File.ReadAllText(
            Path.Combine(FindSourceRoot().FullName, "ErkS.Platform.Core", fileName),
            Encoding.UTF8);

    private static string ReadPdfSource(string fileName) =>
        File.ReadAllText(
            Path.Combine(FindSourceRoot().FullName, "ErkS.Platform.Pdf", fileName),
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
