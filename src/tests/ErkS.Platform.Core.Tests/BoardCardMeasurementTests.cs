namespace ErkS.Platform.Core.Tests;

/// <summary>
/// What Studio tells the person composing about a card, and what they take to
/// AutoCAD or Revit by hand.
///
/// This replaced a channel that was going to send those programs a task. The
/// user judged that more machinery than the problem was worth, so the whole of
/// the arrangement is now this: say plainly how large the card is, what shape,
/// and how many pixels that needs.
/// </summary>
public sealed class BoardCardMeasurementTests
{
    [Fact]
    public void ACardStatesItsSizeItsShapeAndWhatItNeeds()
    {
        BoardCardMeasurement measured = Require(BoardCardMeasurements.Measure(
            new BoardRectMm(0, 0, 380, 240),
            BoardCardMeasurements.PrintDpi));

        Assert.Equal(380, measured.WidthMm, precision: 9);
        Assert.Equal(240, measured.HeightMm, precision: 9);
        Assert.Equal(380d / 240d, measured.AspectRatio, precision: 9);
        // 380 mm at 300 dpi. The figure nobody can eyeball, and the reason it
        // is shown at all.
        Assert.Equal(4489, measured.WidthPixels);
        Assert.Equal(2835, measured.HeightPixels);
    }

    [Fact]
    public void PixelsAreRoundedUp()
    {
        // A pixel short of the requirement is still short, and rounding down
        // would let a card claim it was ready when it was not.
        BoardCardMeasurement measured = Require(BoardCardMeasurements.Measure(
            new BoardRectMm(0, 0, 100.01, 100.01),
            300));

        Assert.Equal(1182, measured.WidthPixels);
    }

    [Fact]
    public void AskingForLessQualityAsksForFewerPixels()
    {
        // A whole number of inches across, so the halving is exact and the
        // rounding-up above cannot be mistaken for a fault here.
        BoardCardMeasurement print = Require(BoardCardMeasurements.Measure(
            new BoardRectMm(0, 0, 508, 254), 300));
        BoardCardMeasurement screen = Require(BoardCardMeasurements.Measure(
            new BoardRectMm(0, 0, 508, 254), 150));

        Assert.Equal(6000, print.WidthPixels);
        Assert.Equal(3000, screen.WidthPixels);
    }

    [Fact]
    public void ACardThatCannotBePlacedMeasuresNothing()
    {
        Assert.Null(BoardCardMeasurements.Measure(null, 300));
        Assert.Null(BoardCardMeasurements.Measure(new BoardRectMm(0, 0, 0, 100), 300));
        Assert.Null(BoardCardMeasurements.Measure(new BoardRectMm(0, 0, 100, 100), 0));
    }

    [Fact]
    public void ARenderTooSmallForItsCardIsNotSharpEnough()
    {
        // A board is printed at a metre across. A render that looks fine on a
        // screen is soft there, and the time to know is while it is being
        // placed - by the time it is printed the board is already wrong.
        BoardCardMeasurement measured = Require(BoardCardMeasurements.Measure(
            new BoardRectMm(0, 0, 400, 300), 300));

        Assert.False(BoardCardMeasurements.IsSharpEnough(measured, 1920, 1080));
        Assert.True(BoardCardMeasurements.IsSharpEnough(measured, 4800, 3600));
    }

    [Fact]
    public void ACardLeftOutsideAShrunkenGridIsPulledBackIn()
    {
        // A grid made smaller can leave a card reaching past its last column.
        // The writer would refuse it and the card would vanish from the printed
        // sheet; pulling it back keeps it visible and movable instead.
        var grid = new BoardGrid { Columns = 6, Rows = 6 };
        var element = new BoardElement { Column = 9, ColumnSpan = 4, Row = 8, RowSpan = 3 };

        bool moved = BoardGridFitting.HoldInside(grid, [element]);

        Assert.True(moved);
        Assert.True(element.Column >= 0 && element.Column + element.ColumnSpan <= grid.Columns);
        Assert.True(element.Row >= 0 && element.Row + element.RowSpan <= grid.Rows);
        Assert.NotNull(BoardGridGeometry.Resolve(grid, 841, 1189, element.Span));
    }

    [Fact]
    public void ACardAlreadyInsideTheGridIsLeftAlone()
    {
        var grid = new BoardGrid { Columns = 12, Rows = 12 };
        var element = new BoardElement { Column = 3, ColumnSpan = 4, Row = 2, RowSpan = 5 };

        Assert.False(BoardGridFitting.HoldInside(grid, [element]));
        Assert.Equal(new BoardGridSpan(3, 4, 2, 5), element.Span);
    }

    private static BoardCardMeasurement Require(BoardCardMeasurement? measured)
    {
        Assert.NotNull(measured);
        return measured!.Value;
    }
}
