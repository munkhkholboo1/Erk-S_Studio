using ErkS.Platform.Pdf;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// The corner title block, drawn twice.
///
/// A restamp clears the metadata columns of an existing album page and redraws
/// them. It used to redraw that region as a rectangle in the heavy border pen,
/// which put a 0.35 mm line down x3 - an interior division the table itself
/// draws at 0.10 mm. On paper the block read as two tables shoved together,
/// which is what a user reported: the vertical rule behind the name stood out.
///
/// The rule these pin is simply that a restamp must put back exactly what it
/// erased. Nothing heavier, nothing lighter, nothing extra.
/// </summary>
public sealed class ConceptCornerTableLineTests
{
    private static BuildingArchitectureConceptCornerGrid Grid() =>
        BuildingArchitectureConceptPageLayout.ResolveCornerGrid(
            BuildingArchitectureConceptPageLayout
                .Calculate(420, 297, "LEFT")
                .TitleBlockArea);

    [Fact]
    public void ARestampPutsBackOnlyLinesTheTableAlreadyHas()
    {
        // Every line the restamp draws has to lie along a line of the full
        // table and carry the same weight. A heavier x3 fails here, which is
        // the bug this test exists for.
        BuildingArchitectureConceptCornerGrid grid = Grid();
        IReadOnlyList<ConceptCornerTableSegment> full = ConceptCornerTableLines.Full(grid);

        foreach (ConceptCornerTableSegment line in ConceptCornerTableLines.Restamped(grid))
        {
            ConceptCornerTableSegment? match = full
                .Cast<ConceptCornerTableSegment?>()
                .FirstOrDefault(candidate => line.LiesAlong(candidate!.Value));

            Assert.True(
                match is not null,
                $"restamp draws a line the table does not have: {Describe(line)}");
            Assert.True(
                match!.Value.Heavy == line.Heavy,
                $"restamp draws {Weight(line.Heavy)} where the table has " +
                $"{Weight(match.Value.Heavy)}: {Describe(line)}");
        }
    }

    [Fact]
    public void TheDivisionBehindTheNameStaysFine()
    {
        // x3 by name, because this is the one the user could see.
        BuildingArchitectureConceptCornerGrid grid = Grid();

        ConceptCornerTableSegment division = Assert.Single(
            ConceptCornerTableLines.Restamped(grid),
            line => line.IsVertical &&
                Math.Abs(line.X0 - grid.X3) < 1e-6);

        Assert.False(division.Heavy);
    }

    [Fact]
    public void TheThreeOuterEdgesOfTheClearedRegionStayHeavy()
    {
        // The left edge and the two long edges are the table's own border and
        // must not thin out - erasing them and redrawing them fine would be
        // the same class of mistake in the other direction.
        BuildingArchitectureConceptCornerGrid grid = Grid();
        IReadOnlyList<ConceptCornerTableSegment> restamped =
            ConceptCornerTableLines.Restamped(grid);

        Assert.Contains(restamped, line =>
            line.Heavy && line.IsVertical && Math.Abs(line.X0 - grid.X0) < 1e-6);
        Assert.Contains(restamped, line =>
            line.Heavy && !line.IsVertical && Math.Abs(line.Y0 - grid.Y0) < 1e-6);
        Assert.Contains(restamped, line =>
            line.Heavy && !line.IsVertical && Math.Abs(line.Y0 - grid.Y4) < 1e-6);
    }

    [Fact]
    public void ARestampNeverReachesPastWhatItCleared()
    {
        // The right-hand cells - signature, scale, sheet number, year - are not
        // erased, so drawing over them would stamp a second set of rules on top
        // of the ones already there.
        BuildingArchitectureConceptCornerGrid grid = Grid();

        foreach (ConceptCornerTableSegment line in ConceptCornerTableLines.Restamped(grid))
        {
            Assert.True(
                line.X0 <= grid.X3 + 1e-6 && line.X1 <= grid.X3 + 1e-6,
                $"restamp reaches past x3: {Describe(line)}");
        }
    }

    [Fact]
    public void TheFullTableKeepsItsOuterBorderHeavyAndItsDivisionsFine()
    {
        BuildingArchitectureConceptCornerGrid grid = Grid();
        IReadOnlyList<ConceptCornerTableSegment> full = ConceptCornerTableLines.Full(grid);

        Assert.Equal(4, full.Count(line => line.Heavy));
        Assert.All(
            full.Where(line => !line.Heavy),
            line => Assert.True(
                line.X0 > grid.X0 - 1e-6 && line.X1 < grid.X5 + 1e-6,
                $"a division escapes the table: {Describe(line)}"));
    }

    private static string Weight(bool heavy) => heavy ? "the border pen" : "the fine pen";

    private static string Describe(ConceptCornerTableSegment line) =>
        $"({line.X0:0.###},{line.Y0:0.###}) -> ({line.X1:0.###},{line.Y1:0.###})";
}
