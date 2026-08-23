namespace ErkS.Platform.Core.Tests;

/// <summary>
/// A general plan reaches a board through two pieces of arithmetic: the arc a
/// bulge describes, and the scale a card turns out to be. Both are pinned here.
/// The arc matters because flattening a curve shows as facets on a sheet a
/// metre across; the scale matters because a scale bar drawn from anything else
/// would be a lie about the ground.
/// </summary>
public sealed class BoardPlanGeometryTests
{
    // tan(90 / 4) - the bulge of a quarter turn, the commonest kerb return.
    private const double QuarterTurnBulge = 0.41421356237309503;

    [Fact]
    public void AQuarterTurnBulgeGivesBackAQuarterTurn()
    {
        PlanArc arc = Require(BoardPlanArcs.Resolve(
            new PlanPoint(0, 0),
            new PlanPoint(1, 0),
            QuarterTurnBulge));

        Assert.Equal(90, arc.SweepAngleDegrees, precision: 6);
    }

    [Fact]
    public void TheArcPassesThroughBothItsEndpoints()
    {
        // The one property that matters: whatever the centre and the angles
        // come out as, the curve has to start and finish where the drawing put
        // it, or every outline on the board is broken.
        var start = new PlanPoint(12, -4);
        var end = new PlanPoint(30, 9);
        PlanArc arc = Require(BoardPlanArcs.Resolve(start, end, 0.6));

        Assert.Equal(arc.Radius, Distance(arc.Centre, start), precision: 9);
        Assert.Equal(arc.Radius, Distance(arc.Centre, end), precision: 9);

        double endAngle = (arc.StartAngleDegrees + arc.SweepAngleDegrees) * Math.PI / 180;
        Assert.Equal(arc.Centre.X + arc.Radius * Math.Cos(endAngle), end.X, precision: 9);
        Assert.Equal(arc.Centre.Y + arc.Radius * Math.Sin(endAngle), end.Y, precision: 9);
    }

    [Fact]
    public void TheSignOfTheBulgeDecidesWhichWayTheCurveTurns()
    {
        PlanArc left = Require(BoardPlanArcs.Resolve(
            new PlanPoint(0, 0), new PlanPoint(1, 0), QuarterTurnBulge));
        PlanArc right = Require(BoardPlanArcs.Resolve(
            new PlanPoint(0, 0), new PlanPoint(1, 0), -QuarterTurnBulge));

        Assert.Equal(-left.SweepAngleDegrees, right.SweepAngleDegrees, precision: 9);
        // Mirrored about the chord, so the centres sit on opposite sides.
        Assert.Equal(-left.Centre.Y, right.Centre.Y, precision: 9);
        Assert.Equal(left.Radius, right.Radius, precision: 9);
    }

    [Fact]
    public void TheRadiusAgreesWithTheOneCityGenReports()
    {
        // CityGen sends the radius beside the bulge. They are computed
        // independently, so agreement is a real cross-check of the formula.
        const double radius = 12.5;
        const double included = Math.PI / 2;
        double chord = 2 * radius * Math.Sin(included / 2);

        PlanArc arc = Require(BoardPlanArcs.Resolve(
            new PlanPoint(0, 0),
            new PlanPoint(chord, 0),
            QuarterTurnBulge));

        Assert.Equal(radius, arc.Radius, precision: 6);
    }

    [Fact]
    public void AHalfTurnIsHandledLikeAnyOther()
    {
        // Bulge 1 is a semicircle: the centre lands on the chord's midpoint,
        // which is where a formula that divides by the apothem would fail.
        PlanArc arc = Require(BoardPlanArcs.Resolve(
            new PlanPoint(0, 0), new PlanPoint(10, 0), 1));

        Assert.Equal(180, arc.SweepAngleDegrees, precision: 6);
        Assert.Equal(5, arc.Radius, precision: 9);
        Assert.Equal(5, arc.Centre.X, precision: 9);
        Assert.Equal(0, arc.Centre.Y, precision: 9);
    }

    [Fact]
    public void ABulgeOfMoreThanAHalfTurnKeepsItsMajorArc()
    {
        // tan(270 / 4): the centre falls on the far side of the chord, and a
        // formula that took the minor arc would cut the corner.
        double bulge = Math.Tan(270 * Math.PI / 180 / 4);
        PlanArc arc = Require(BoardPlanArcs.Resolve(
            new PlanPoint(0, 0), new PlanPoint(1, 0), bulge));

        Assert.Equal(270, arc.SweepAngleDegrees, precision: 6);
        Assert.Equal(arc.Radius, Distance(arc.Centre, new PlanPoint(1, 0)), precision: 9);
    }

    [Fact]
    public void AStraightRunIsNotAnArc()
    {
        Assert.Null(BoardPlanArcs.Resolve(new PlanPoint(0, 0), new PlanPoint(1, 0), 0));
        Assert.Null(BoardPlanArcs.Resolve(new PlanPoint(0, 0), new PlanPoint(0, 0), 0.5));
    }

    [Fact]
    public void ThePlanFillsItsCardWithoutDistortion()
    {
        // A masterplan squeezed to fit is a lie about the site.
        BoardPlanProjection projection = Require(BoardPlanProjections.Fit(
            0, 0, 240, 180,
            cardLeftMm: 30, cardTopMm: 40, cardWidthMm: 200, cardHeightMm: 100));

        Assert.Equal(240d / 180d, projection.WidthMm / projection.HeightMm, precision: 9);
        Assert.True(projection.WidthMm <= 200 + 1e-9);
        Assert.True(projection.HeightMm <= 100 + 1e-9);
        // Limited by height here, so the height is used fully.
        Assert.Equal(100, projection.HeightMm, precision: 9);
    }

    [Fact]
    public void ThePlanIsCentredInItsCard()
    {
        BoardPlanProjection projection = Require(BoardPlanProjections.Fit(
            0, 0, 240, 180, 30, 40, 200, 100));

        Assert.Equal(30 + 200d / 2, projection.LeftMm + projection.WidthMm / 2, precision: 9);
        Assert.Equal(40 + 100d / 2, projection.TopMm + projection.HeightMm / 2, precision: 9);
    }

    [Fact]
    public void TheGroundGoesUpWhereTheBoardGoesDown()
    {
        // A drawing counts north upwards and a page counts downwards. Getting
        // this wrong prints the site upside down and it looks plausible.
        BoardPlanProjection projection = Require(BoardPlanProjections.Fit(
            0, 0, 100, 100, 0, 0, 100, 100));

        PlanPoint south = projection.ToBoard(50, 0);
        PlanPoint north = projection.ToBoard(50, 100);

        Assert.True(north.Y < south.Y, "north should print above south");
    }

    [Fact]
    public void TheCornersOfTheGroundLandOnTheCornersOfTheDrawnArea()
    {
        BoardPlanProjection projection = Require(BoardPlanProjections.Fit(
            10, 20, 110, 70, 0, 0, 200, 100));

        PlanPoint bottomLeft = projection.ToBoard(10, 20);
        PlanPoint topRight = projection.ToBoard(110, 70);

        Assert.Equal(projection.LeftMm, bottomLeft.X, precision: 9);
        Assert.Equal(projection.TopMm + projection.HeightMm, bottomLeft.Y, precision: 9);
        Assert.Equal(projection.LeftMm + projection.WidthMm, topRight.X, precision: 9);
        Assert.Equal(projection.TopMm, topRight.Y, precision: 9);
    }

    [Fact]
    public void TheScaleIsReportedRatherThanChosen()
    {
        // The card is sized by the layout, so the scale is whatever that turns
        // out to be. A scale bar has to be drawn from this number and no other.
        // 200 metres across 200 mm of board is 1 mm to the metre: 1:1000.
        BoardPlanProjection projection = Require(BoardPlanProjections.Fit(
            0, 0, 200, 100, 0, 0, 200, 100));

        Assert.Equal(1, projection.MillimetresPerMetre, precision: 9);
        Assert.Equal(1000, projection.ScaleDenominator, precision: 6);
    }

    [Fact]
    public void HalvingTheCardHalvesTheScale()
    {
        BoardPlanProjection whole = Require(BoardPlanProjections.Fit(0, 0, 200, 100, 0, 0, 200, 100));
        BoardPlanProjection half = Require(BoardPlanProjections.Fit(0, 0, 200, 100, 0, 0, 100, 50));

        Assert.Equal(whole.ScaleDenominator * 2, half.ScaleDenominator, precision: 6);
    }

    [Fact]
    public void GroundWithNoExtentIsRefused()
    {
        Assert.Null(BoardPlanProjections.Fit(0, 0, 0, 100, 0, 0, 200, 100));
        Assert.Null(BoardPlanProjections.Fit(0, 0, 200, 100, 0, 0, 0, 100));
    }

    private static double Distance(PlanPoint a, PlanPoint b) =>
        Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private static PlanArc Require(PlanArc? arc)
    {
        Assert.NotNull(arc);
        return arc!.Value;
    }

    private static BoardPlanProjection Require(BoardPlanProjection? projection)
    {
        Assert.NotNull(projection);
        return projection!.Value;
    }
}
