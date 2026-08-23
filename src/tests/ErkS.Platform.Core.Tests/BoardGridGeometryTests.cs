namespace ErkS.Platform.Core.Tests;

/// <summary>
/// The grid is what makes a board look composed rather than assembled, and it
/// only does that if its cells tile the sheet exactly. These pin that: no drift
/// across a row, the gutter between neighbours and nowhere else, and a span
/// that does not fit refused rather than quietly moved.
/// </summary>
public sealed class BoardGridGeometryTests
{
    private const double Tolerance = 1e-9;

    // A0 upright, the commonest competition board.
    private const double BoardWidth = 841;
    private const double BoardHeight = 1189;

    private static BoardGrid Grid() => new()
    {
        MarginLeftMm = 20,
        MarginTopMm = 20,
        MarginRightMm = 20,
        MarginBottomMm = 20,
        Columns = 12,
        Rows = 12,
        ColumnGutterMm = 6,
        RowGutterMm = 6,
    };

    [Fact]
    public void AFullSpanFillsExactlyTheAreaInsideTheMargins()
    {
        BoardGrid grid = Grid();

        BoardRectMm rect = Require(
            BoardGridGeometry.Content(grid, BoardWidth, BoardHeight));

        Assert.Equal(grid.MarginLeftMm, rect.LeftMm, precision: 9);
        Assert.Equal(grid.MarginTopMm, rect.TopMm, precision: 9);
        Assert.Equal(BoardWidth - grid.MarginRightMm, rect.RightMm, precision: 9);
        Assert.Equal(BoardHeight - grid.MarginBottomMm, rect.BottomMm, precision: 9);
    }

    [Fact]
    public void SpanningEveryColumnOneByOneLeavesNoDrift()
    {
        // Twelve single cells must end where one twelve-wide cell ends. If the
        // column width were rounded anywhere, the last card on a row would sit
        // short of the margin and the whole board would look off.
        BoardGrid grid = Grid();

        BoardRectMm last = Require(Resolve(grid, new BoardGridSpan(11, 1, 0, 1)));
        BoardRectMm whole = Require(Resolve(grid, new BoardGridSpan(0, 12, 0, 1)));

        Assert.Equal(whole.RightMm, last.RightMm, precision: 9);
    }

    [Fact]
    public void NeighbouringCellsAreSeparatedByExactlyTheGutter()
    {
        BoardGrid grid = Grid();

        BoardRectMm left = Require(Resolve(grid, new BoardGridSpan(3, 2, 0, 1)));
        BoardRectMm right = Require(Resolve(grid, new BoardGridSpan(5, 2, 0, 1)));

        Assert.Equal(grid.ColumnGutterMm, right.LeftMm - left.RightMm, precision: 9);
    }

    [Fact]
    public void ASpanAbsorbsTheGuttersItCrosses()
    {
        // Three cells joined into one card must reclaim the two gutters between
        // them, otherwise a wide card is narrower than the space it occupies.
        BoardGrid grid = Grid();

        BoardRectMm single = Require(Resolve(grid, new BoardGridSpan(0, 1, 0, 1)));
        BoardRectMm triple = Require(Resolve(grid, new BoardGridSpan(0, 3, 0, 1)));

        Assert.Equal(
            single.WidthMm * 3 + grid.ColumnGutterMm * 2,
            triple.WidthMm,
            precision: 9);
    }

    [Fact]
    public void RowsAndColumnsUseTheirOwnGutters()
    {
        BoardGrid grid = Grid();
        grid.RowGutterMm = 14;

        BoardRectMm top = Require(Resolve(grid, new BoardGridSpan(0, 1, 0, 1)));
        BoardRectMm below = Require(Resolve(grid, new BoardGridSpan(0, 1, 1, 1)));

        Assert.Equal(14, below.TopMm - top.BottomMm, precision: 9);
    }

    [Theory]
    [InlineData(12, 1, 0, 1)]  // starts past the last column
    [InlineData(10, 3, 0, 1)]  // reaches past it
    [InlineData(0, 1, 12, 1)]  // starts past the last row
    [InlineData(-1, 1, 0, 1)]  // before the first
    [InlineData(0, 0, 0, 1)]   // spans nothing
    public void ASpanOutsideTheGridIsRefusedRatherThanMoved(
        int column,
        int columnSpan,
        int row,
        int rowSpan)
    {
        // Clamping would put the card somewhere plausible and say nothing. On a
        // printed board an element silently moved is worse than one visibly
        // absent, so this returns nothing and lets the writer report it.
        Assert.Null(Resolve(Grid(), new BoardGridSpan(column, columnSpan, row, rowSpan)));
    }

    [Fact]
    public void ABoardTooSmallForItsOwnMarginsIsRefused()
    {
        BoardGrid grid = Grid();

        Assert.Null(BoardGridGeometry.Resolve(grid, 30, 30, new BoardGridSpan(0, 1, 0, 1)));
    }

    [Fact]
    public void EveryCellOfTheGridStaysInsideTheBoard()
    {
        BoardGrid grid = Grid();

        for (int column = 0; column < grid.Columns; column++)
        {
            for (int row = 0; row < grid.Rows; row++)
            {
                BoardRectMm rect = Require(
                    Resolve(grid, new BoardGridSpan(column, 1, row, 1)));

                Assert.True(rect.LeftMm >= grid.MarginLeftMm - Tolerance);
                Assert.True(rect.TopMm >= grid.MarginTopMm - Tolerance);
                Assert.True(rect.RightMm <= BoardWidth - grid.MarginRightMm + Tolerance);
                Assert.True(rect.BottomMm <= BoardHeight - grid.MarginBottomMm + Tolerance);
            }
        }
    }

    [Fact]
    public void ASeriesResolvesItsOwnCards()
    {
        var series = new ProjectBoardSeries { Grid = Grid() };
        var element = new BoardElement { Column = 6, ColumnSpan = 6, Row = 0, RowSpan = 4 };

        BoardRectMm rect = Require(series.Resolve(element));

        Assert.Equal(
            Require(Resolve(Grid(), element.Span)).LeftMm,
            rect.LeftMm,
            precision: 9);
    }

    private static BoardRectMm? Resolve(BoardGrid grid, BoardGridSpan span) =>
        BoardGridGeometry.Resolve(grid, BoardWidth, BoardHeight, span);

    private static BoardRectMm Require(BoardRectMm? rect)
    {
        Assert.NotNull(rect);
        return rect!.Value;
    }
}
