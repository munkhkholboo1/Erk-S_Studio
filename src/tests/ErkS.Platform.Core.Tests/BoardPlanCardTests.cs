using System.Text.Json;
using ErkS.Platform.Pdf;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// The whole chain, end to end: CityGen classifies a general plan, Studio reads
/// the classification, and a card on a board draws grass as grass. What each
/// surface looks like is placeholder; that the mechanism carries meaning from
/// the drawing to the sheet is not.
/// </summary>
public sealed class BoardPlanCardTests : IDisposable
{
    private const double BoardWidthMm = 841;
    private const double BoardHeightMm = 1189;

    private readonly string workDirectory =
        Path.Combine(Path.GetTempPath(), "erks-platform-tests", Guid.NewGuid().ToString("N"));

    public BoardPlanCardTests()
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
    public void AGeneralPlanIsDrawnOnACardFromItsClassification()
    {
        string planPath = WritePlan(
            Lawn("lawn-1"),
            Island("walk-1", "lawn-1"),
            Road("road-1"));

        BoardBuildResult result = Build(planPath, out string outputPath);

        // The assumed north is reported, and nothing else is.
        Assert.All(result.Warnings, warning => Assert.Contains("хойд зүг", warning));
        Assert.Equal(1, result.PageCount);

        using PdfDocument built = PdfReader.Open(outputPath, PdfDocumentOpenMode.Import);
        PdfPage page = built.Pages[0];
        Assert.Equal(BoardWidthMm, page.Width.Millimeter, precision: 1);
    }

    [Fact]
    public void APlanThatIsNotTheContractLeavesTheBoardStandingAndSaysWhy()
    {
        string planPath = Path.Combine(workDirectory, "wrong.erks-citygen-board.json");
        File.WriteAllText(planPath, """{ "schema": "something.else", "schemaVersion": 1 }""");

        BoardBuildResult result = Build(planPath, out _);

        Assert.Contains(result.Warnings, warning => warning.Contains("уншиж чадсангүй"));
        Assert.Equal(1, result.PageCount);
    }

    [Fact]
    public void AnUnrecognisedSurfaceIsDrawnAndReported()
    {
        // Drawn neutrally rather than dropped, and named rather than left for
        // the user to notice as a grey patch on a printed board.
        string planPath = WritePlan(
            Lawn("lawn-1"),
            new CityGenBoardObject
            {
                Id = "odd-1",
                Flow = "SOMETHING_NEW",
                Category = "Unclassified",
                IsClosed = true,
                Vertices = Ring(60, 10, 20),
            });

        BoardBuildResult result = Build(planPath, out _);

        Assert.Contains(result.Warnings, warning => warning.Contains("танигдсангүй"));
    }

    [Fact]
    public void TheRenderReportsWhatItDrewAndAtWhatScale()
    {
        CityGenBoardManifest manifest = Manifest(
            Lawn("lawn-1"),
            Island("walk-1", "lawn-1"),
            Road("road-1"));

        BoardPlanDrawResult drawn = Render(manifest, widthMm: 200, heightMm: 100);

        // Two shapes: the island belongs to the lawn rather than standing alone.
        Assert.Equal(2, drawn.ShapesDrawn);
        Assert.Equal(1, drawn.HolesDrawn);
        Assert.Equal(0, drawn.UnrecognisedShapes);

        // 200 metres of ground across 200 mm of card is 1:1000, and the scale
        // bar beside it has to be drawn from this and nothing else.
        Assert.Equal(1000, drawn.ScaleDenominator, precision: 3);
    }

    [Fact]
    public void TheLegendComesBackWithTheDrawing()
    {
        CityGenBoardManifest manifest = Manifest(Lawn("lawn-1"), Road("road-1"));

        BoardPlanDrawResult drawn = Render(manifest, 200, 100);

        Assert.Equal(2, drawn.Legend.Count);
        Assert.Contains(drawn.Legend, style => style.FillPattern == PlanFillPatterns.Grass);
    }

    [Fact]
    public void APlanWithNoExtentDrawsNothingRatherThanGuessing()
    {
        CityGenBoardManifest manifest = Manifest(Lawn("lawn-1"));
        manifest.Bbox = [];

        BoardPlanDrawResult drawn = Render(manifest, 200, 100);

        Assert.Equal(0, drawn.ShapesDrawn);
        Assert.Equal(0, drawn.ScaleDenominator);
    }

    [Fact]
    public void TheSameCardAtHalfTheSizeReportsHalfTheScale()
    {
        CityGenBoardManifest manifest = Manifest(Lawn("lawn-1"), Road("road-1"));

        BoardPlanDrawResult whole = Render(manifest, 200, 100);
        BoardPlanDrawResult half = Render(manifest, 100, 50);

        Assert.Equal(whole.ScaleDenominator * 2, half.ScaleDenominator, precision: 3);
    }

    private BoardPlanDrawResult Render(CityGenBoardManifest manifest, double widthMm, double heightMm)
    {
        const double pointsPerMm = 72.0 / 25.4;
        using var document = new PdfDocument();
        PdfPage page = document.AddPage();
        page.Width = PdfSharp.Drawing.XUnit.FromMillimeter(BoardWidthMm);
        page.Height = PdfSharp.Drawing.XUnit.FromMillimeter(BoardHeightMm);
        using PdfSharp.Drawing.XGraphics gfx = PdfSharp.Drawing.XGraphics.FromPdfPage(page);
        return BoardPlanRenderer.Draw(
            gfx,
            manifest,
            new PdfSharp.Drawing.XRect(0, 0, widthMm * pointsPerMm, heightMm * pointsPerMm));
    }

    private BoardBuildResult Build(string planPath, out string outputPath)
    {
        outputPath = Path.Combine(workDirectory, $"board-{Guid.NewGuid():N}.pdf");
        return BoardPdfWriter.Build(new BoardBuildRequest(
            "Уралдааны самбар",
            outputPath,
            BoardWidthMm,
            BoardHeightMm,
            new BoardGrid(),
            [
                new BoardBuildBoard("A1", "Ерөнхий төлөвлөгөө",
                [
                    new BoardBuildCard(
                        ProjectPortfolioLayouts.FitPage,
                        Caption: "",
                        SourcePath: "",
                        SourcePageNumber: 1,
                        Column: 0,
                        ColumnSpan: 8,
                        Row: 0,
                        RowSpan: 6,
                        PlanPath: planPath),
                ]),
            ]));
    }

    private string WritePlan(params CityGenBoardObject[] objects)
    {
        string path = Path.Combine(workDirectory, $"plan-{Guid.NewGuid():N}.erks-citygen-board.json");
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(
                Manifest(objects),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        return path;
    }

    private static CityGenBoardManifest Manifest(params CityGenBoardObject[] objects) => new()
    {
        Schema = CityGenGraphicBoardContract.Schema,
        SchemaVersion = CityGenGraphicBoardContract.CurrentSchemaVersion,
        Units = CityGenGraphicBoardContract.ExpectedUnits,
        CoordinateSpace = CityGenGraphicBoardContract.ExpectedCoordinateSpace,
        NorthAngleDegrees = 0,
        NorthAngleSource = CityGenGraphicBoardContract.NorthAssumed,
        Origin = new CityGenBoardOrigin { IsDefined = true, X = 0, Y = 0 },
        Bbox = [0, 0, 200, 100],
        SourceDocument = "plan.dwg",
        ObjectCount = objects.Length,
        Objects = [.. objects],
    };

    private static CityGenBoardObject Lawn(string id) => new()
    {
        Id = id,
        Flow = "LAWN",
        Category = "Green",
        Material = "grass",
        Subtype = "lawn",
        Layer = "Erk-S Landscape Lawn",
        DrawOrder = 10,
        IsClosed = true,
        Vertices = Ring(10, 10, 80),
    };

    private static CityGenBoardObject Island(string id, string parentId) => new()
    {
        Id = id,
        ParentId = parentId,
        Flow = "WALKING_AREA_ISLAND",
        Category = "Pedestrian",
        Material = "stone",
        DrawOrder = 30,
        IsClosed = true,
        Vertices = Ring(30, 30, 20),
    };

    private static CityGenBoardObject Road(string id) => new()
    {
        Id = id,
        Flow = "ROAD_ASPHALT_OUTLINE",
        Category = "Road",
        Material = "asphalt",
        DrawOrder = 20,
        IsClosed = true,
        Vertices = Ring(110, 10, 70),
    };

    private static List<CityGenBoardVertex> Ring(double x, double y, double size) =>
    [
        new() { X = x, Y = y, IsPolylineVertex = true },
        new() { X = x + size, Y = y, IsPolylineVertex = true },
        new() { X = x + size, Y = y + size, IsPolylineVertex = true },
        new() { X = x, Y = y + size, IsPolylineVertex = true },
    ];
}
