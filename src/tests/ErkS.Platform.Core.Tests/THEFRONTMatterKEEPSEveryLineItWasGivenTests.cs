using ErkS.Platform.Core;
using ErkS.Platform.Pdf;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// PFR, Batnasan, 2026-10-20: a list item's continuation line printed as a new
/// paragraph, and a 13th indicator row disappeared from a frame that took 12.
/// </summary>
public sealed class THEFRONTMatterKEEPSEveryLineItWasGivenTests
{
    [Fact]
    public void AListItemRunsOnOverItsContinuationLines()
    {
        string markdown = string.Join("\n",
            "## ЕРӨНХИЙ ЗҮЙЛ",
            "- Барилгын даацын хана",
            "  цутгамал төмөрбетон.",
            "- Хоёр дахь зүйл",
            "",
            "Энгийн догол мөр.");

        string note = WorkingDrawingFrontMatterDraft.Parse(markdown).ExplanatoryNote;

        Assert.Contains("– Барилгын даацын хана цутгамал төмөрбетон.", note, StringComparison.Ordinal);
        Assert.Contains("– Хоёр дахь зүйл", note, StringComparison.Ordinal);
        Assert.DoesNotContain("\nцутгамал", note, StringComparison.Ordinal);
        Assert.Contains("Энгийн догол мөр.", note, StringComparison.Ordinal);
        Assert.DoesNotContain("Хоёр дахь зүйл Энгийн", note, StringComparison.Ordinal);
    }

    [Fact]
    public void ThirteenIndicatorsFitTheFrameRevitReserves()
    {
        // The Batnasan 01: technical-indicators frame 96 x 46 mm.
        List<WorkingDrawingIndicatorRow> rows = Enumerable.Range(1, 13)
            .Select(index => new WorkingDrawingIndicatorRow
            {
                Number = index.ToString(),
                Name = "Үзүүлэлт " + index,
                Unit = "м²",
                Value = "1",
            })
            .ToList();

        WorkingDrawingFrontMatterTable table = WorkingDrawingFrontMatterLayout.Indicators(96, 46, rows);

        Assert.Equal(0, table.Overflow);
        Assert.Contains(table.Cells, cell => cell.Text == "Үзүүлэлт 13");
    }

    [Fact]
    public void TheBridgeReturnsWhatTheBuildSaid()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src", "ErkS.Studio.App")))
            directory = directory.Parent;
        string bridge = File.ReadAllText(Path.Combine(directory!.FullName, "src", "ErkS.Studio.App", "ShellView.Bridge.cs"));
        int import = bridge.IndexOf("private StudioBridgeResponse BridgeImportFrontMatter(", StringComparison.Ordinal);

        Assert.True(bridge.IndexOf("warnings = lastAlbumBuildWarnings,", import, StringComparison.Ordinal) > import);
    }
}
