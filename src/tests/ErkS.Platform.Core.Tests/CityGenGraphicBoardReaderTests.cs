using System.Text.Json;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// The channel a general plan reaches a board through, carrying what a PDF
/// cannot: the classification. These pin the header promises the geometry is
/// worthless without, and the two ways a plan can go quietly wrong - an island
/// that loses its area, and an object that arrives unusable.
/// </summary>
public sealed class CityGenGraphicBoardReaderTests : IDisposable
{
    private readonly string workDirectory =
        Path.Combine(Path.GetTempPath(), "erks-platform-tests", Guid.NewGuid().ToString("N"));

    public CityGenGraphicBoardReaderTests() => Directory.CreateDirectory(workDirectory);

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
    public void APlanArrivesClassified()
    {
        CityGenBoardManifest manifest = Manifest(
            Area("lawn-1", "LAWN", "Green", "grass", "lawn"),
            Area("walk-1", "WALKWAY", "Pedestrian", "stone", "", parentId: "lawn-1"),
            Area("road-1", "ROAD_ASPHALT_OUTLINE", "Road", "asphalt", ""));

        CityGenBoardLoadResult result = CityGenGraphicBoardReader.Verify(manifest);

        Assert.True(result.IsLoaded);
        Assert.Empty(result.SkippedObjects);

        CityGenBoardSummary summary = CityGenBoardComposition.Summarize(result.Manifest!);
        Assert.Equal(3, summary.ObjectCount);
        Assert.Contains(summary.ByMaterial, tally => tally.Value == "grass" && tally.Count == 1);
        Assert.Contains(summary.ByCategory, tally => tally.Value == "Road" && tally.Count == 1);
    }

    [Fact]
    public void AnIslandBecomesAHoleInTheAreaItBelongsTo()
    {
        // A path across a lawn has to stop the grass, not be drawn over by it.
        CityGenBoardManifest manifest = Manifest(
            Area("lawn-1", "LAWN", "Green", "grass", "lawn"),
            Area("walk-1", "WALKING_AREA_ISLAND", "Pedestrian", "stone", "", parentId: "lawn-1"));

        IReadOnlyList<CityGenBoardShape> shapes = CityGenBoardComposition.Shapes(manifest);

        CityGenBoardShape shape = Assert.Single(shapes);
        Assert.Equal("lawn-1", shape.Outer.Id);
        Assert.True(shape.HasHoles);
        Assert.Equal("walk-1", Assert.Single(shape.Holes).Id);
    }

    [Fact]
    public void AnIslandWithoutItsAreaIsStillDrawnAndCounted()
    {
        // Dropping it would take a piece of the plan off the board with nothing
        // anywhere saying it had gone.
        CityGenBoardManifest manifest = Manifest(
            Area("walk-1", "GREEN_AREA_ISLAND", "Pedestrian", "stone", "", parentId: "missing"));

        IReadOnlyList<CityGenBoardShape> shapes = CityGenBoardComposition.Shapes(manifest);
        CityGenBoardSummary summary = CityGenBoardComposition.Summarize(manifest);

        Assert.Equal("walk-1", Assert.Single(shapes).Outer.Id);
        Assert.Equal(1, summary.OrphanedIslandCount);
    }

    [Fact]
    public void ShapesComeBackInDrawingOrder()
    {
        CityGenBoardManifest manifest = Manifest(
            Area("top", "WALKWAY", "Pedestrian", "stone", "", drawOrder: 30),
            Area("bottom", "LAWN", "Green", "grass", "lawn", drawOrder: 10),
            Area("middle", "ROAD_ASPHALT_OUTLINE", "Road", "asphalt", "", drawOrder: 20));

        IReadOnlyList<CityGenBoardShape> shapes = CityGenBoardComposition.Shapes(manifest);

        Assert.Equal(["bottom", "middle", "top"], shapes.Select(shape => shape.Outer.Id));
    }

    [Theory]
    [InlineData("erks.citygen.project-site", 1, "meter", "drawing")]
    [InlineData(CityGenGraphicBoardContract.Schema, 2, "meter", "drawing")]
    [InlineData(CityGenGraphicBoardContract.Schema, 1, "foot", "drawing")]
    [InlineData(CityGenGraphicBoardContract.Schema, 1, "meter", "wgs84")]
    public void AFileThatIsNotTheContractIsRefused(
        string schema,
        int schemaVersion,
        string units,
        string coordinateSpace)
    {
        // Metres read as feet would put a scale bar out by a factor of three
        // and nothing downstream could tell, so the header is judged first.
        CityGenBoardManifest manifest = Manifest(Area("lawn-1", "LAWN", "Green", "grass", "lawn"));
        manifest.Schema = schema;
        manifest.SchemaVersion = schemaVersion;
        manifest.Units = units;
        manifest.CoordinateSpace = coordinateSpace;

        CityGenBoardLoadResult result = CityGenGraphicBoardReader.Verify(manifest);

        Assert.False(result.IsLoaded);
        Assert.NotEmpty(result.Issues);
        Assert.Null(result.Manifest);
    }

    [Fact]
    public void ANorthAngleOutsideItsRangeIsRefused()
    {
        CityGenBoardManifest manifest = Manifest(Area("lawn-1", "LAWN", "Green", "grass", "lawn"));
        manifest.NorthAngleDegrees = 361;

        Assert.False(CityGenGraphicBoardReader.Verify(manifest).IsLoaded);
    }

    [Fact]
    public void AnAssumedNorthArrivesLabelledAsAnAssumption()
    {
        // The board prints this as an arrow. An assumption has to travel as one.
        CityGenBoardManifest assumed = Manifest(Area("lawn-1", "LAWN", "Green", "grass", "lawn"));
        assumed.NorthAngleSource = CityGenGraphicBoardContract.NorthAssumed;
        CityGenBoardManifest declared = Manifest(Area("lawn-1", "LAWN", "Green", "grass", "lawn"));
        declared.NorthAngleSource = CityGenGraphicBoardContract.NorthFromUtmGrid;

        Assert.True(CityGenBoardComposition.Summarize(assumed).NorthIsAssumed);
        Assert.False(CityGenBoardComposition.Summarize(declared).NorthIsAssumed);
    }

    [Fact]
    public void AnUnusableObjectIsReportedWithoutCostingThePlanTheRest()
    {
        CityGenBoardManifest manifest = Manifest(
            Area("lawn-1", "LAWN", "Green", "grass", "lawn"),
            new CityGenBoardObject { Id = "stub", Flow = "TREE", Vertices = [] },
            new CityGenBoardObject { Id = "", Flow = "LAWN", Vertices = [Vertex(0, 0), Vertex(1, 1)] });

        CityGenBoardLoadResult result = CityGenGraphicBoardReader.Verify(manifest);

        Assert.True(result.IsLoaded);
        Assert.Equal(2, result.SkippedObjects.Count);
        Assert.Single(CityGenBoardComposition.Shapes(result.Manifest!));
    }

    [Fact]
    public void ACountThatDisagreesWithTheBodyIsSaidOutLoud()
    {
        CityGenBoardManifest manifest = Manifest(Area("lawn-1", "LAWN", "Green", "grass", "lawn"));
        manifest.ObjectCount = 805;

        CityGenBoardLoadResult result = CityGenGraphicBoardReader.Verify(manifest);

        Assert.True(result.IsLoaded);
        Assert.Contains(result.SkippedObjects, issue => issue.Contains("805"));
    }

    [Fact]
    public void TheFileIsReadFromDiskInTheShapeCityGenWritesIt()
    {
        // Written the way the exporter writes it - camelCase, an origin flag,
        // a bounding box - so a change in either shape shows up here.
        string path = Path.Combine(workDirectory, "plan.erks-citygen-board.json");
        File.WriteAllText(path, """
        {
          "schema": "erks.citygen.graphic-board",
          "schemaVersion": 1,
          "units": "meter",
          "coordinateSpace": "drawing",
          "northAngleDegrees": 0.0,
          "northAngleSource": "assumed",
          "origin": { "isDefined": false, "x": 0.0, "y": 0.0, "z": 0.0 },
          "bbox": [0.0, 0.0, 240.0, 180.0],
          "sourceDocument": "Erin_MP_ZZ_VL.dwg",
          "generatedAtUtc": "2026-08-24T03:00:00.0000000Z",
          "objectCount": 2,
          "objects": [
            {
              "id": "2AF:LAWN",
              "parentId": "",
              "flow": "LAWN",
              "category": "Green",
              "material": "grass",
              "subtype": "lawn",
              "layer": "Erk-S Landscape Lawn",
              "drawOrder": 20,
              "fallbackColorIndex": 84,
              "isClosed": true,
              "metric": 412.5,
              "sourceKey": "2AF",
              "vertices": [
                { "x": 0.0, "y": 0.0, "z": 0.0, "isPolylineVertex": true, "isArcSample": false, "isArcEndpoint": false },
                { "x": 40.0, "y": 0.0, "z": 0.0, "isPolylineVertex": true, "isArcSample": false, "isArcEndpoint": true },
                { "x": 40.0, "y": 30.0, "z": 0.0, "isPolylineVertex": true, "isArcSample": false, "isArcEndpoint": false }
              ],
              "segments": [
                { "startVertexIndex": 0, "endVertexIndex": 1, "bulge": 0.0, "isArc": false, "radius": null, "includedAngle": null },
                { "startVertexIndex": 1, "endVertexIndex": 2, "bulge": 0.4142, "isArc": true, "radius": 12.5, "includedAngle": 1.5708 }
              ]
            },
            {
              "id": "2AF:WALKING_AREA_ISLAND:1",
              "parentId": "2AF:LAWN",
              "flow": "WALKING_AREA_ISLAND",
              "category": "Pedestrian",
              "material": "stone",
              "subtype": "",
              "layer": "Erk-S WalkingArea",
              "drawOrder": 40,
              "fallbackColorIndex": 8,
              "isClosed": true,
              "metric": 22.0,
              "sourceKey": "2AF",
              "vertices": [
                { "x": 10.0, "y": 10.0, "z": 0.0, "isPolylineVertex": true, "isArcSample": false, "isArcEndpoint": false },
                { "x": 20.0, "y": 10.0, "z": 0.0, "isPolylineVertex": true, "isArcSample": false, "isArcEndpoint": false }
              ],
              "segments": []
            }
          ]
        }
        """);

        CityGenBoardLoadResult result = CityGenGraphicBoardReader.Load(path);

        Assert.True(result.IsLoaded, string.Join("; ", result.Issues));
        Assert.Empty(result.SkippedObjects);

        CityGenBoardManifest manifest = result.Manifest!;
        Assert.Equal("Erin_MP_ZZ_VL.dwg", manifest.SourceDocument);
        Assert.False(manifest.Origin.IsDefined);
        Assert.True(manifest.NorthIsAssumed);

        // The arc kept its bulge: a curve flattened here would show as facets
        // on a board printed at a metre across.
        CityGenBoardSegment arc = manifest.Objects[0].Segments[1];
        Assert.True(arc.IsArc);
        Assert.Equal(0.4142, arc.Bulge, precision: 4);
        Assert.Equal(12.5, arc.Radius);

        CityGenBoardShape shape = Assert.Single(CityGenBoardComposition.Shapes(manifest));
        Assert.Equal("2AF:LAWN", shape.Outer.Id);
        Assert.Equal("2AF:WALKING_AREA_ISLAND:1", Assert.Single(shape.Holes).Id);

        CityGenBoardSummary summary = CityGenBoardComposition.Summarize(manifest);
        Assert.Equal(240, summary.WidthMetres);
        Assert.Equal(180, summary.HeightMetres);
    }

    [Fact]
    public void TheSidecarSitsBesideItsDrawing()
    {
        string path = CityGenGraphicBoardContract.ResolveSidecarPath(
            Path.Combine(workDirectory, "Erin_MP_ZZ_VL.dwg"));

        Assert.EndsWith(".erks-citygen-board.json", path);
        Assert.Equal(
            path,
            CityGenGraphicBoardContract.ResolveSidecarPath(path));
        Assert.Equal("", CityGenGraphicBoardContract.ResolveSidecarPath("plan.pdf"));
    }

    [Fact]
    public void AFileThatIsNotJsonIsRefusedRatherThanThrown()
    {
        string path = Path.Combine(workDirectory, "broken.erks-citygen-board.json");
        File.WriteAllText(path, "{ this is not json");

        CityGenBoardLoadResult result = CityGenGraphicBoardReader.Load(path);

        Assert.False(result.IsLoaded);
        Assert.NotEmpty(result.Issues);
    }

    private static CityGenBoardManifest Manifest(params CityGenBoardObject[] objects) => new()
    {
        Schema = CityGenGraphicBoardContract.Schema,
        SchemaVersion = CityGenGraphicBoardContract.CurrentSchemaVersion,
        Units = CityGenGraphicBoardContract.ExpectedUnits,
        CoordinateSpace = CityGenGraphicBoardContract.ExpectedCoordinateSpace,
        NorthAngleDegrees = 0,
        NorthAngleSource = CityGenGraphicBoardContract.NorthAssumed,
        Origin = new CityGenBoardOrigin { IsDefined = false },
        Bbox = [0, 0, 100, 80],
        SourceDocument = "plan.dwg",
        ObjectCount = objects.Length,
        Objects = [.. objects],
    };

    private static CityGenBoardObject Area(
        string id,
        string flow,
        string category,
        string material,
        string subtype,
        string parentId = "",
        int drawOrder = 10) => new()
    {
        Id = id,
        ParentId = parentId,
        Flow = flow,
        Category = category,
        Material = material,
        Subtype = subtype,
        Layer = "Erk-S " + flow,
        DrawOrder = drawOrder,
        IsClosed = true,
        Vertices = [Vertex(0, 0), Vertex(10, 0), Vertex(10, 10)],
        Segments = [],
    };

    private static CityGenBoardVertex Vertex(double x, double y) =>
        new() { X = x, Y = y, IsPolylineVertex = true };
}
