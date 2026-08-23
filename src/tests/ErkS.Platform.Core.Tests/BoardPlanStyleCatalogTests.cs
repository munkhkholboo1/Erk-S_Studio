namespace ErkS.Platform.Core.Tests;

/// <summary>
/// The mechanism the user's own example rests on: a green area recognised as
/// grass and drawn with a grass pattern rather than as whatever colour the
/// drawing happened to use.
///
/// What each surface looks like is placeholder and will be replaced by the
/// template library. What is pinned here is the part that must not change: the
/// order the classification is consulted in, and that an unfamiliar value costs
/// refinement rather than the shape itself.
/// </summary>
public sealed class BoardPlanStyleCatalogTests
{
    [Fact]
    public void AGreenAreaIsDrawnAsGrass()
    {
        // The whole exercise in one assertion.
        PlanStyle style = BoardPlanStyleCatalog.Resolve(
            subtype: "green-area",
            material: "grass",
            flow: "GREEN_AREA",
            category: "Green");

        Assert.Equal(PlanFillPatterns.Grass, style.FillPattern);
        Assert.False(style.IsUnrecognised);
    }

    [Fact]
    public void TheMostSpecificClassificationWins()
    {
        // Subtype before material: when CityGen can tell a lawn from a wood,
        // the board should show the difference.
        PlanStyle lawn = BoardPlanStyleCatalog.Resolve("lawn", "grass", "LAWN", "Green");
        PlanStyle tree = BoardPlanStyleCatalog.Resolve("tree", "grass", "TREE", "Green");

        Assert.NotEqual(lawn.Key, tree.Key);
        Assert.Equal("tree", tree.Key);
    }

    [Fact]
    public void AnUnfamiliarSubtypeFallsToTheMaterial()
    {
        // CityGen's subtype slot is an open vocabulary: bioswale, green roof,
        // urban forest arrive as the ability to draw them arrives. Until Studio
        // has an entry, such a surface is still the green area it is.
        PlanStyle style = BoardPlanStyleCatalog.Resolve("bioswale", "grass", "GREEN_AREA", "Green");

        Assert.Equal("grass", style.Key);
        Assert.Equal(PlanFillPatterns.Grass, style.FillPattern);
    }

    [Fact]
    public void AnUnfamiliarMaterialFallsToTheFlow()
    {
        PlanStyle style = BoardPlanStyleCatalog.Resolve("", "rubberised-play-surface", "WALKWAY", "Pedestrian");

        Assert.Equal("WALKWAY", style.Key);
    }

    [Fact]
    public void AnUnfamiliarFlowFallsToTheCategory()
    {
        PlanStyle style = BoardPlanStyleCatalog.Resolve("", "", "SOMETHING_NEW", "Road");

        Assert.Equal("Road", style.Key);
    }

    [Fact]
    public void ASurfaceNothingRecognisesIsStillDrawnAndStillNamed()
    {
        // Never dropped. A shape that quietly disappeared because Studio did
        // not know its category is the worst failure available on a printed
        // board, so it draws neutrally and says so in the legend.
        PlanStyle style = BoardPlanStyleCatalog.Resolve("?", "?", "?", "?");

        Assert.True(style.IsUnrecognised);
        Assert.NotEqual(PlanFillPatterns.None, style.FillPattern);
        Assert.False(string.IsNullOrWhiteSpace(style.Label));
    }

    [Fact]
    public void AnEmptyClassificationIsNotAMatch()
    {
        // Blank fields are common - the subtype slot is mostly empty today -
        // and must fall through rather than matching an entry.
        PlanStyle style = BoardPlanStyleCatalog.Resolve("", "", "", "Green");

        Assert.Equal("Green", style.Key);
    }

    [Fact]
    public void TheResolutionOrderIsPartOfTheContract()
    {
        // Agreed with CityGen and written into their export contract, so it is
        // stated here rather than left implicit in the lookup code.
        Assert.Equal(["subtype", "material", "flow", "category"], BoardPlanStyleCatalog.ResolutionOrder);
    }

    [Fact]
    public void TheLegendIsWhatTheBoardActuallyShows()
    {
        CityGenBoardManifest manifest = Manifest(
            Object("lawn-1", "LAWN", "Green", "grass", "lawn"),
            Object("lawn-2", "LAWN", "Green", "grass", "lawn"),
            Object("road-1", "ROAD_ASPHALT_OUTLINE", "Road", "asphalt", ""));

        IReadOnlyList<PlanStyle> legend = BoardPlanStyleCatalog.Legend(manifest);

        // One line per surface on the board, not one per catalogue entry, and
        // the two lawns share theirs.
        Assert.Equal(2, legend.Count);
        Assert.Contains(legend, style => style.Key == "lawn");
        Assert.Contains(legend, style => style.Key == "asphalt");
    }

    [Fact]
    public void TheLegendAdmitsWhatItDidNotRecognise()
    {
        CityGenBoardManifest manifest = Manifest(
            Object("lawn-1", "LAWN", "Green", "grass", "lawn"),
            Object("odd-1", "UNKNOWN_FLOW", "Unclassified", "", ""));

        IReadOnlyList<PlanStyle> legend = BoardPlanStyleCatalog.Legend(manifest);

        Assert.Equal(2, legend.Count);
        Assert.Contains(legend, style => style.IsUnrecognised);
    }

    [Fact]
    public void AnIslandDoesNotEarnItsOwnLegendLine()
    {
        // It is a hole in another surface, not a surface of its own.
        CityGenBoardManifest manifest = Manifest(
            Object("lawn-1", "LAWN", "Green", "grass", "lawn"),
            Object("walk-1", "WALKING_AREA_ISLAND", "Pedestrian", "stone", "", parentId: "lawn-1"));

        IReadOnlyList<PlanStyle> legend = BoardPlanStyleCatalog.Legend(manifest);

        Assert.Equal("lawn", Assert.Single(legend).Key);
    }

    [Theory]
    [InlineData("PlannedBuilding")]
    [InlineData("PlannedRoad")]
    [InlineData("PlannedWalkway")]
    [InlineData("PlannedGreenArea")]
    public void CityGensOwnCategoryNamesResolve(string category)
    {
        // Measured against a real masterplan export: these are the names that
        // actually arrive. Missing them left five hundred and thirty-five
        // shapes - every building among them - drawn as unrecognised.
        PlanStyle style = BoardPlanStyleCatalog.Resolve("", "unknown", "SOURCE", category);

        Assert.False(style.IsUnrecognised, "category " + category + " should resolve");
    }

    [Theory]
    [InlineData("ROAD_LANE_DIVIDER")]
    [InlineData("ROAD_LANE_LIMIT")]
    [InlineData("ROAD_CURB")]
    public void AMarkingIsStrokedRatherThanFilled(string flow)
    {
        // A lane divider is a painted line, not a piece of ground. Filling its
        // outline lays a band of road colour across the carriageway - and on a
        // real road drawing two thirds of the objects are markings, so this is
        // not a detail.
        PlanStyle style = BoardPlanStyleCatalog.Resolve("", "unknown", flow, "PlannedRoad");

        Assert.Equal(PlanFillPatterns.None, style.FillPattern);
        Assert.True(style.OutlineWidthMm > 0, "a marking still has to be drawn");
    }

    [Fact]
    public void TheWordUnknownIsNotAClassification()
    {
        // CityGen states "unknown" rather than leaving the field empty, and
        // most of a real plan carries it: a building has no surface material.
        // It has to fall through exactly as an empty field does.
        PlanStyle style = BoardPlanStyleCatalog.Resolve("unknown", "unknown", "SOURCE", "PlannedBuilding");

        Assert.Equal("PlannedBuilding", style.Key);
    }

    private static CityGenBoardManifest Manifest(params CityGenBoardObject[] objects) => new()
    {
        Schema = CityGenGraphicBoardContract.Schema,
        SchemaVersion = CityGenGraphicBoardContract.CurrentSchemaVersion,
        Units = CityGenGraphicBoardContract.ExpectedUnits,
        CoordinateSpace = CityGenGraphicBoardContract.ExpectedCoordinateSpace,
        NorthAngleSource = CityGenGraphicBoardContract.NorthAssumed,
        Bbox = [0, 0, 100, 80],
        ObjectCount = objects.Length,
        Objects = [.. objects],
    };

    private static CityGenBoardObject Object(
        string id,
        string flow,
        string category,
        string material,
        string subtype,
        string parentId = "") => new()
    {
        Id = id,
        ParentId = parentId,
        Flow = flow,
        Category = category,
        Material = material,
        Subtype = subtype,
        IsClosed = true,
        Vertices =
        [
            new CityGenBoardVertex { X = 0, Y = 0 },
            new CityGenBoardVertex { X = 10, Y = 0 },
            new CityGenBoardVertex { X = 10, Y = 10 },
        ],
    };
}
