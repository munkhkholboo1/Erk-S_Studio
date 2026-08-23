using ErkS.Platform.Pdf;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// A board is a composed sheet: many pieces of the project's material on one
/// large page, where the grid puts them. These pin what the writer owes the
/// rest of the plan - the board's own size rather than a sheet format, a page
/// per board, vector kept as vector, and a card that cannot be placed reported
/// instead of dropped.
/// </summary>
public sealed class BoardPdfWriterTests : IDisposable
{
    // A0 upright: not a size the sheet formats offer, which is the point. A
    // board is not a sheet and is not held to that matrix.
    private const double BoardWidthMm = 841;
    private const double BoardHeightMm = 1189;

    private readonly string workDirectory =
        Path.Combine(Path.GetTempPath(), "erks-platform-tests", Guid.NewGuid().ToString("N"));

    public BoardPdfWriterTests()
    {
        WindowsFontResolver.Register();
        Directory.CreateDirectory(workDirectory);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(workDirectory, recursive: true);
        }
        catch
        {
        }
    }

    [Fact]
    public void ABoardIsOnePageOfItsOwnSize()
    {
        string source = WriteSourcePdf("plan.pdf", 828, 582);
        string outputPath = Path.Combine(workDirectory, "board.pdf");

        BoardBuildResult result = Build(
            outputPath,
            Board("A1", "Ерөнхий төлөвлөгөө",
                Card(source, column: 0, columnSpan: 8, row: 0, rowSpan: 6),
                Card(source, column: 8, columnSpan: 4, row: 0, rowSpan: 3),
                Card(source, column: 8, columnSpan: 4, row: 3, rowSpan: 3)));

        Assert.Empty(result.Warnings);
        Assert.Equal(1, result.PageCount);

        using PdfDocument built = PdfReader.Open(result.OutputPath, PdfDocumentOpenMode.Import);
        PdfPage page = built.Pages[0];
        Assert.Equal(BoardWidthMm, page.Width.Millimeter, precision: 1);
        Assert.Equal(BoardHeightMm, page.Height.Millimeter, precision: 1);
    }

    [Fact]
    public void ASeriesGivesEveryBoardItsOwnPage()
    {
        string source = WriteSourcePdf("plan.pdf", 828, 582);
        string outputPath = Path.Combine(workDirectory, "series.pdf");

        BoardBuildResult result = Build(
            outputPath,
            Board("A1", "Хот төлөвлөлт", Card(source, 0, 12, 0, 6)),
            Board("A2", "Барилга", Card(source, 0, 12, 0, 6)),
            Board("A3", "Дэлгэрэнгүй", Card(source, 0, 12, 0, 6)));

        Assert.Empty(result.Warnings);
        Assert.Equal(3, result.PageCount);
    }

    [Fact]
    public void ACardKeepsItsSourceVector()
    {
        // A drawing placed on a board must stay drawn, not become a picture of
        // itself: a board is printed at a metre across and raster shows there.
        string source = WriteSourcePdf("plan.pdf", 828, 582);
        string outputPath = Path.Combine(workDirectory, "vector.pdf");

        BoardBuildResult result = Build(
            outputPath,
            Board("A1", "", Card(source, 0, 12, 0, 8)));

        Assert.Empty(result.Warnings);
        using PdfDocument built = PdfReader.Open(result.OutputPath, PdfDocumentOpenMode.Import);
        PdfPage page = built.Pages[0];
        PdfDictionary? resources = page.Elements.GetDictionary("/Resources");
        PdfDictionary? xObjects = resources?.Elements.GetDictionary("/XObject");
        Assert.NotNull(xObjects);
        // The source page is carried as a form, which is what keeps it vector.
        Assert.NotEmpty(xObjects!.Elements.Keys);
    }

    [Fact]
    public void ACardShowingOnlyPartOfItsPageStillBuilds()
    {
        // The plan rests on this: a sheet plotted at whatever size the source
        // program allows, with only its drawn area shown on the card. The
        // geometry is pinned separately; this is the writer end of it.
        string source = WriteSourcePdf("sheet.pdf", 828, 582);
        string outputPath = Path.Combine(workDirectory, "cropped.pdf");

        BoardBuildResult result = Build(
            outputPath,
            Board("A1", "", Card(source, 0, 6, 0, 4) with
            {
                CropX = 0.12,
                CropY = 0.14,
                CropWidth = 0.72,
                CropHeight = 0.69,
            }));

        Assert.Empty(result.Warnings);
        Assert.Equal(1, result.PageCount);
        Assert.True(new FileInfo(result.OutputPath).Length > 0);
    }

    [Fact]
    public void AnEmptyCardIsAPlaceholderRatherThanAWarning()
    {
        // The layout is made before the material arrives, so a card with
        // nothing in it yet is a state of the design, not a fault in it.
        string outputPath = Path.Combine(workDirectory, "placeholders.pdf");

        BoardBuildResult result = Build(
            outputPath,
            Board("A1", "Зохион байгуулалт",
                Card("", 0, 6, 0, 4),
                Card("", 6, 6, 0, 4)));

        Assert.Empty(result.Warnings);
        Assert.Equal(1, result.PageCount);
    }

    [Fact]
    public void ASubmissionLeavesNoMarkWhereACardIsStillEmpty()
    {
        string outputPath = Path.Combine(workDirectory, "submission.pdf");

        BoardBuildResult result = BoardPdfWriter.Build(new BoardBuildRequest(
            "Уралдаан",
            outputPath,
            BoardWidthMm,
            BoardHeightMm,
            new BoardGrid(),
            [Board("A1", "", Card("", 0, 6, 0, 4))],
            ShowPlaceholders: false));

        Assert.Empty(result.Warnings);
        Assert.Equal(1, result.PageCount);
    }

    [Fact]
    public void ACardThatCannotBePlacedIsReportedRatherThanDropped()
    {
        string source = WriteSourcePdf("plan.pdf", 828, 582);
        string outputPath = Path.Combine(workDirectory, "overflow.pdf");

        BoardBuildResult result = Build(
            outputPath,
            Board("A1", "Хэт өргөн", Card(source, column: 10, columnSpan: 6, row: 0, rowSpan: 2)));

        // Silence here would be the dangerous outcome: a card missing from a
        // printed board with nothing anywhere saying why.
        string warning = Assert.Single(result.Warnings);
        Assert.Contains("A1", warning);
        Assert.Equal(1, result.PageCount);
    }

    [Fact]
    public void AMissingFileLeavesTheBoardStandingAndSaysSo()
    {
        string outputPath = Path.Combine(workDirectory, "missing.pdf");

        BoardBuildResult result = Build(
            outputPath,
            Board("A2", "", Card(Path.Combine(workDirectory, "gone.pdf"), 0, 6, 0, 4)));

        Assert.Single(result.Warnings);
        Assert.Equal(1, result.PageCount);
    }

    [Fact]
    public void ASeriesWithNoBoardsStillProducesAReadableDocument()
    {
        string outputPath = Path.Combine(workDirectory, "empty.pdf");

        BoardBuildResult result = Build(outputPath);

        Assert.Single(result.Warnings);
        Assert.Equal(1, result.PageCount);
        Assert.True(File.Exists(result.OutputPath));
    }

    [Fact]
    public void OneCardFilledToTheBoardIsWhatThePortfolioAlwaysProduced()
    {
        // The old behaviour is a board with a single card across the whole
        // grid. Keeping that true is what lets boards arrive without anyone's
        // existing portfolio changing.
        string source = WriteSourcePdf("plan.pdf", 828, 582);
        var grid = new BoardGrid
        {
            MarginLeftMm = 0,
            MarginTopMm = 0,
            MarginRightMm = 0,
            MarginBottomMm = 0,
            Columns = 1,
            Rows = 1,
        };

        BoardRectMm rect = Assert.IsType<BoardRectMm>(
            BoardGridGeometry.Resolve(grid, BoardWidthMm, BoardHeightMm, new BoardGridSpan(0, 1, 0, 1)));

        Assert.Equal(0, rect.LeftMm, precision: 9);
        Assert.Equal(0, rect.TopMm, precision: 9);
        Assert.Equal(BoardWidthMm, rect.WidthMm, precision: 9);
        Assert.Equal(BoardHeightMm, rect.HeightMm, precision: 9);

        BoardBuildResult result = BoardPdfWriter.Build(new BoardBuildRequest(
            "Портфолио",
            Path.Combine(workDirectory, "single.pdf"),
            BoardWidthMm,
            BoardHeightMm,
            grid,
            [Board("", "", Card(source, 0, 1, 0, 1))]));

        Assert.Empty(result.Warnings);
        Assert.Equal(1, result.PageCount);
    }

    private BoardBuildResult Build(string outputPath, params BoardBuildBoard[] boards) =>
        BoardPdfWriter.Build(new BoardBuildRequest(
            "Уралдааны самбар",
            outputPath,
            BoardWidthMm,
            BoardHeightMm,
            new BoardGrid(),
            boards));

    private static BoardBuildBoard Board(string code, string title, params BoardBuildCard[] cards) =>
        new(code, title, cards);

    private static BoardBuildCard Card(
        string sourcePath,
        int column,
        int columnSpan,
        int row,
        int rowSpan) =>
        new(
            ProjectPortfolioLayouts.FitPage,
            Caption: "",
            SourcePath: sourcePath,
            SourcePageNumber: 1,
            Column: column,
            ColumnSpan: columnSpan,
            Row: row,
            RowSpan: rowSpan);

    private string WriteSourcePdf(string name, double widthMm, double heightMm)
    {
        string path = Path.Combine(workDirectory, name);
        using var document = new PdfDocument();
        PdfPage page = document.AddPage();
        page.Width = XUnit.FromMillimeter(widthMm);
        page.Height = XUnit.FromMillimeter(heightMm);
        using (XGraphics gfx = XGraphics.FromPdfPage(page))
        {
            gfx.DrawRectangle(new XSolidBrush(XColors.White), 0, 0, page.Width.Point, page.Height.Point);
            gfx.DrawRectangle(
                new XPen(XColors.Black, 1),
                20,
                20,
                page.Width.Point - 40,
                page.Height.Point - 40);
        }
        document.Save(path);
        return path;
    }
}
