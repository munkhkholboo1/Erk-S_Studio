using ErkS.Platform.Pdf;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// Owner, 2026-10-29: «студиод Erk-S Sheet Splitter г нэмэх цаг болсон» - an album is kept whole and split
/// into A3, and how it splits can be seen (and hidden). The rule is the Erk-S PDF Sheet Splitter's
/// (D:\Installs\SplitPDF core/detector.py): joined A3 modules share a 12 mm band, an «H» format ends in an
/// A4 tail, each tile is a whole A3 (or A4) with a guide 6 mm into each shared band.
/// </summary>
public sealed class ANALBUMSheetSPLITSIntoA3TilesLikeTheSPLITTERTests
{
    [Theory]
    [InlineData(420d, 297d, "A1", 1)]
    [InlineData(297d, 420d, "A1", 1)]
    [InlineData(210d, 297d, "", 1)]
    [InlineData(828d, 297d, "B1", 2)]
    [InlineData(582d, 420d, "A2", 2)]
    [InlineData(618d, 297d, "AH1", 2)]
    [InlineData(828d, 582d, "B2", 4)]
    [InlineData(1644d, 1152d, "D4", 16)]
    public void AFormatIsKnownByItsSizeAndSplitsIntoItsModules(double width, double height, string code, int tiles)
    {
        SheetSplitPlan plan = SheetSplitPlanner.Plan(width, height);

        Assert.Equal(code, plan.Code);
        Assert.Equal(tiles, plan.Tiles.Count);
    }

    [Fact]
    public void AB1SheetIsTwoWholeA3sOverlappingTwelveMillimetres()
    {
        SheetSplitPlan plan = SheetSplitPlanner.Plan(828, 297);

        SheetSplitTile left = plan.Tiles[0], right = plan.Tiles[1];
        Assert.Equal(0, left.Clip.X, 3);
        Assert.Equal(420, left.Clip.Width, 3);
        Assert.Equal(408, right.Clip.X, 3); // 420 - 12
        Assert.Equal(420, right.Clip.Width, 3);
        Assert.Equal((420d, 297d), (left.TargetWidthMm, left.TargetHeightMm));
        // the cut line is the middle of the shared band
        Assert.Equal([414d], left.VerticalGuidesMm);
        Assert.Equal([6d], right.VerticalGuidesMm);
        Assert.Empty(left.HorizontalGuidesMm);
    }

    [Fact]
    public void AnHFormatEndsInAnA4Tail()
    {
        SheetSplitPlan plan = SheetSplitPlanner.Plan(618, 297);

        Assert.Equal((420d, 297d), (plan.Tiles[0].TargetWidthMm, plan.Tiles[0].TargetHeightMm));
        Assert.Equal((210d, 297d), (plan.Tiles[1].TargetWidthMm, plan.Tiles[1].TargetHeightMm));
        Assert.Equal(408, plan.Tiles[1].Clip.X, 3);
    }

    [Fact]
    public void AStandingA2IsTwoStandingA3sSideBySide()
    {
        // A2 = one A3 column, two rows, laid landscape: 582 × 420.
        SheetSplitPlan plan = SheetSplitPlanner.Plan(582, 420);

        Assert.All(plan.Tiles, tile => Assert.Equal((297d, 420d), (tile.TargetWidthMm, tile.TargetHeightMm)));
        Assert.Equal(285, plan.Tiles[1].Clip.X, 3);
    }

    [Fact]
    public void TilesAreNumberedRowByRowFromTheTopLeft()
    {
        SheetSplitPlan plan = SheetSplitPlanner.Plan(828, 582);

        Assert.Equal([1, 2, 3, 4], plan.Tiles.Select(tile => tile.Label));
        Assert.Equal(0, plan.Tiles[1].Clip.Y, 3);
        Assert.Equal(285, plan.Tiles[2].Clip.Y, 3);
        Assert.Equal([291d], plan.Tiles[0].HorizontalGuidesMm);
    }

    [Fact]
    public void AnOversizedSheetOffTheGridStillSplitsIntoPiecesThatFitA3()
    {
        SheetSplitPlan plan = SheetSplitPlanner.Plan(1000, 300);

        Assert.Equal("", plan.Code);
        Assert.True(plan.Tiles.Count > 1);
        Assert.All(plan.Tiles, tile => Assert.True(
            Math.Max(tile.TargetWidthMm, tile.TargetHeightMm) <= 421.2 &&
            Math.Min(tile.TargetWidthMm, tile.TargetHeightMm) <= 298.2));
    }

    [Fact]
    public void TheSplitPdfHasOneA3PageForEveryTileAndTheWholeOneIsUntouched()
    {
        string folder = Path.Combine(Path.GetTempPath(), "erks-split-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string whole = Path.Combine(folder, "album.pdf");
            using (var document = new PdfDocument())
            {
                foreach ((double w, double h) in new[] { (420d, 297d), (828d, 582d), (618d, 297d) })
                {
                    PdfPage page = document.AddPage();
                    page.Width = XUnit.FromMillimeter(w);
                    page.Height = XUnit.FromMillimeter(h);
                    using XGraphics gfx = XGraphics.FromPdfPage(page);
                    gfx.DrawLine(XPens.Black, 0, 0, page.Width.Point, page.Height.Point);
                }
                document.Save(whole);
            }
            long wholeLength = new FileInfo(whole).Length;

            string split = Path.Combine(folder, "album_A3.pdf");
            SheetSplitResult result = AlbumSheetSplitWriter.Split(whole, split, drawJoinGuides: true);

            Assert.Equal(3, result.SourcePages);
            Assert.Equal(1 + 4 + 2, result.OutputPages);
            using PdfDocument read = PdfReader.Open(split, PdfDocumentOpenMode.Import);
            Assert.Equal(7, read.PageCount);
            Assert.Equal(420, read.Pages[1].Width.Millimeter, 0);
            Assert.Equal(297, read.Pages[1].Height.Millimeter, 0);
            Assert.Equal(210, read.Pages[6].Width.Millimeter, 0);
            Assert.Equal(wholeLength, new FileInfo(whole).Length);

            string preview = Path.Combine(folder, "album_preview.pdf");
            AlbumSheetSplitWriter.WriteSplitPreview(whole, preview);
            using PdfDocument shown = PdfReader.Open(preview, PdfDocumentOpenMode.Import);
            Assert.Equal(3, shown.PageCount); // the pages stay; only the lines are drawn over them
            Assert.Equal(828, shown.Pages[1].Width.Millimeter, 0);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
