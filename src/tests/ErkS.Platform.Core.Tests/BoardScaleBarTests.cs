namespace ErkS.Platform.Core.Tests;

/// <summary>
/// A scale bar has to end on a number a person can hold in their head. It is
/// read by eye and compared by eye, so a bar labelled 137 m is arithmetic
/// rather than a scale.
/// </summary>
public sealed class BoardScaleBarTests
{
    [Theory]
    [InlineData(500, 120)]
    [InlineData(1000, 120)]
    [InlineData(2000, 200)]
    [InlineData(200, 80)]
    [InlineData(5000, 300)]
    public void TheBarEndsOnANumberWorthPrinting(double scaleDenominator, double widthMm)
    {
        BoardScaleBarPlan plan = Require(BoardScaleBars.Choose(scaleDenominator, widthMm));

        double mantissa = plan.GroundMetres /
            Math.Pow(10, Math.Floor(Math.Log10(plan.GroundMetres)));
        Assert.True(
            Math.Abs(mantissa - 1) < 0.001 ||
            Math.Abs(mantissa - 2) < 0.001 ||
            Math.Abs(mantissa - 5) < 0.001,
            $"{plan.GroundMetres} m is not a length anyone would print");
    }

    [Fact]
    public void TheBarIsTheLengthThatGroundActuallyComesOutAt()
    {
        // At 1:1000, one millimetre of board is one metre of ground.
        BoardScaleBarPlan plan = Require(BoardScaleBars.Choose(1000, 120));

        Assert.Equal(plan.GroundMetres, plan.LengthMm, precision: 9);
    }

    [Fact]
    public void TheBarFitsTheSpaceItWasOffered()
    {
        for (double width = 20; width <= 300; width += 7)
        {
            if (BoardScaleBars.Choose(750, width) is not { } plan)
                continue;
            Assert.True(plan.LengthMm <= width + 1e-9, $"a {plan.LengthMm} mm bar in {width} mm");
            Assert.True(plan.LengthMm >= 15, "a bar too short to read is not a bar");
        }
    }

    [Fact]
    public void EachDivisionAlsoLandsOnANumberWorthPrinting()
    {
        // 200 m in four parts is 50 a division; 500 in four would be 125.
        BoardScaleBarPlan plan = Require(BoardScaleBars.Choose(1000, 300));

        double segment = plan.SegmentGroundMetres;
        double mantissa = segment / Math.Pow(10, Math.Floor(Math.Log10(segment)));
        Assert.True(
            Math.Abs(mantissa - 1) < 0.001 ||
            Math.Abs(mantissa - 2) < 0.001 ||
            Math.Abs(mantissa - 2.5) < 0.001 ||
            Math.Abs(mantissa - 5) < 0.001,
            $"a division of {segment} m is not worth printing");
    }

    [Fact]
    public void ACardTooSmallForAReadableBarGetsNone()
    {
        // A real answer rather than a failure: a card with no room should
        // carry no bar, not an unreadable one that still claims to measure.
        Assert.Null(BoardScaleBars.Choose(1000, 8));
    }

    [Fact]
    public void AScaleThatMeansNothingIsRefused()
    {
        Assert.Null(BoardScaleBars.Choose(0, 200));
        Assert.Null(BoardScaleBars.Choose(double.NaN, 200));
        Assert.Null(BoardScaleBars.Choose(1000, double.NaN));
    }

    [Fact]
    public void ASmallerScaleShowsMoreGroundInTheSameSpace()
    {
        BoardScaleBarPlan near = Require(BoardScaleBars.Choose(500, 200));
        BoardScaleBarPlan far = Require(BoardScaleBars.Choose(5000, 200));

        Assert.True(far.GroundMetres > near.GroundMetres);
    }

    [Fact]
    public void TheBarRemembersTheScaleItWasMadeFor()
    {
        // Printed beside it as 1:N, so the two can never disagree.
        BoardScaleBarPlan plan = Require(BoardScaleBars.Choose(1250, 200));

        Assert.Equal(1250, plan.ScaleDenominator, precision: 9);
    }

    private static BoardScaleBarPlan Require(BoardScaleBarPlan? plan)
    {
        Assert.NotNull(plan);
        return plan!.Value;
    }
}
