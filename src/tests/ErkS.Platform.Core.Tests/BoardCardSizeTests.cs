namespace ErkS.Platform.Core.Tests;

/// <summary>
/// A card sized by typing a figure rather than by dragging a corner.
///
/// The size is the number the user takes to AutoCAD or Revit to prepare
/// artwork by hand, so it has to be the number they asked for. Snapping it to
/// the nearest cell would have kept the grid whole and broken the arrangement
/// the size exists to serve. What a grid is actually for - alignment - survives
/// instead on the corner the card starts from.
/// </summary>
public sealed class BoardCardSizeTests
{
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
    public void ACardWithNoSizeOfItsOwnFollowsTheGrid()
    {
        var element = new BoardElement { Column = 2, ColumnSpan = 3, Row = 1, RowSpan = 2 };

        BoardRectMm resolved = Require(Resolve(element));
        BoardRectMm cell = Require(
            BoardGridGeometry.Resolve(Grid(), BoardWidth, BoardHeight, element.Span));

        Assert.Equal(cell, resolved);
    }

    [Fact]
    public void ATypedSizeIsTheSizeTheCardComesOutAt()
    {
        // 380 means 380. This is the whole point of the field.
        var element = new BoardElement
        {
            Column = 1,
            ColumnSpan = 3,
            Row = 1,
            RowSpan = 3,
            WidthMm = 380,
            HeightMm = 240,
        };

        BoardRectMm rect = Require(Resolve(element));

        Assert.Equal(380, rect.WidthMm, precision: 9);
        Assert.Equal(240, rect.HeightMm, precision: 9);
    }

    [Fact]
    public void TheGridStillPlacesTheCorner()
    {
        // Alignment is what a grid is for, and it survives here: only how far
        // the card reaches stops being the grid's business.
        var element = new BoardElement
        {
            Column = 4,
            ColumnSpan = 1,
            Row = 3,
            RowSpan = 1,
            WidthMm = 380,
            HeightMm = 240,
        };

        BoardRectMm rect = Require(Resolve(element));
        BoardRectMm cell = Require(
            BoardGridGeometry.Resolve(Grid(), BoardWidth, BoardHeight, element.Span));

        Assert.Equal(cell.LeftMm, rect.LeftMm, precision: 9);
        Assert.Equal(cell.TopMm, rect.TopMm, precision: 9);
    }

    [Fact]
    public void ACardAskedToBeWiderThanTheBoardGetsWhatThereIs()
    {
        // Unlike a span outside the grid this can be drawn, so it is - and the
        // inspector shows the size it came out at, which makes the limit
        // visible instead of silent.
        var element = new BoardElement
        {
            Column = 6,
            ColumnSpan = 1,
            Row = 0,
            RowSpan = 1,
            WidthMm = 5000,
            HeightMm = 200,
        };

        BoardRectMm rect = Require(Resolve(element));

        Assert.True(rect.RightMm <= BoardWidth + 1e-9, $"reached {rect.RightMm} on an {BoardWidth} board");
        Assert.Equal(200, rect.HeightMm, precision: 9);
    }

    [Fact]
    public void ACardWhoseCellIsOutsideTheGridIsStillRefused()
    {
        // A size of its own does not excuse a placement that cannot exist.
        var element = new BoardElement
        {
            Column = 20,
            ColumnSpan = 1,
            Row = 0,
            RowSpan = 1,
            WidthMm = 100,
            HeightMm = 100,
        };

        Assert.Null(Resolve(element));
    }

    [Theory]
    [InlineData(0, 200)]
    [InlineData(200, 0)]
    [InlineData(-50, 200)]
    [InlineData(double.NaN, 200)]
    public void HalfASizeIsNotOne(double width, double height)
    {
        // One dimension alone says nothing about a rectangle, so the card falls
        // back to the grid rather than to something invented.
        var element = new BoardElement
        {
            Column = 1,
            ColumnSpan = 2,
            Row = 1,
            RowSpan = 2,
            WidthMm = width,
            HeightMm = height,
        };
        element.Normalize();

        Assert.False(element.HasSizeOverride);
        BoardRectMm cell = Require(
            BoardGridGeometry.Resolve(Grid(), BoardWidth, BoardHeight, element.Span));
        Assert.Equal(cell, Require(Resolve(element)));
    }

    [Fact]
    public void ASeriesResolvesACardsOwnSizeToo()
    {
        var series = new ProjectBoardSeries
        {
            BoardWidthMm = BoardWidth,
            BoardHeightMm = BoardHeight,
            Grid = Grid(),
        };
        var element = new BoardElement
        {
            Column = 1,
            ColumnSpan = 1,
            Row = 1,
            RowSpan = 1,
            WidthMm = 300,
            HeightMm = 200,
        };

        BoardRectMm rect = Require(series.Resolve(element));

        Assert.Equal(300, rect.WidthMm, precision: 9);
    }

    [Fact]
    public void TheMeasurementReportsTheSizeTheCardActuallyIs()
    {
        // What the inspector shows and what the user copies for AutoCAD, so it
        // has to follow the resolved rectangle rather than the typed figure.
        var series = new ProjectBoardSeries
        {
            BoardWidthMm = BoardWidth,
            BoardHeightMm = BoardHeight,
            Grid = Grid(),
        };
        var element = new BoardElement
        {
            Column = 0,
            ColumnSpan = 1,
            Row = 0,
            RowSpan = 1,
            WidthMm = 380,
            HeightMm = 240,
        };

        BoardCardMeasurement measured = Assert.IsType<BoardCardMeasurement>(
            BoardCardMeasurements.Measure(series.Resolve(element), BoardCardMeasurements.PrintDpi));

        Assert.Equal(380, measured.WidthMm, precision: 9);
        Assert.Equal(4489, measured.WidthPixels);
    }

    [Fact]
    public void ACardKeepsItsOwnSizeThroughACopy()
    {
        var element = new BoardElement { WidthMm = 380, HeightMm = 240 };

        BoardElement copy = element.Clone();

        Assert.Equal(380, copy.WidthMm, precision: 9);
        Assert.True(copy.HasSizeOverride);
    }

    private static BoardRectMm? Resolve(BoardElement element) =>
        BoardCardGeometry.Resolve(Grid(), BoardWidth, BoardHeight, element);

    private static BoardRectMm Require(BoardRectMm? rect)
    {
        Assert.NotNull(rect);
        return rect!.Value;
    }
}
