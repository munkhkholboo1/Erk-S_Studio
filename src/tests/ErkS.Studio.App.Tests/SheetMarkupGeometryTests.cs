using System.Windows;
using System.Windows.Media;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

public sealed class SheetMarkupGeometryTests
{
    private const double PageWidth = 1000;
    private const double PageHeight = 700;

    [Fact]
    public void Cloud_StaysWithinTheAreaItWasDrawnAround()
    {
        Point[] drawn =
        [
            new(0.10, 0.20),
            new(0.40, 0.20),
            new(0.40, 0.60),
            new(0.10, 0.60),
        ];

        Geometry? geometry = SheetMarkupGeometry.Build(
            StudioSheetCommentRules.ShapeCloud,
            drawn,
            PageWidth,
            PageHeight);

        Assert.NotNull(geometry);
        Rect bounds = geometry!.Bounds;
        // The bumps bulge outward, so a margin is expected - a runaway closing
        // arc is not. Before the loop was closed before resampling, the arc
        // that joined the last point back to the first swept far off the sheet.
        Assert.True(bounds.Left > -0.05 * PageWidth, $"left escaped: {bounds.Left}");
        Assert.True(bounds.Top > 0.15 * PageHeight, $"top escaped: {bounds.Top}");
        Assert.True(bounds.Right < 0.45 * PageWidth, $"right escaped: {bounds.Right}");
        Assert.True(bounds.Bottom < 0.65 * PageHeight, $"bottom escaped: {bounds.Bottom}");
    }

    [Fact]
    public void Rectangle_SpansTheTwoCornersItWasDraggedBetween()
    {
        Geometry? geometry = SheetMarkupGeometry.Build(
            StudioSheetCommentRules.ShapeRectangle,
            [new Point(0.2, 0.3), new Point(0.6, 0.5)],
            PageWidth,
            PageHeight);

        Assert.NotNull(geometry);
        Rect bounds = geometry!.Bounds;
        Assert.Equal(0.2 * PageWidth, bounds.Left, 1);
        Assert.Equal(0.3 * PageHeight, bounds.Top, 1);
        Assert.Equal(0.6 * PageWidth, bounds.Right, 1);
        Assert.Equal(0.5 * PageHeight, bounds.Bottom, 1);
    }

    [Fact]
    public void Build_RefusesAMarkWithNoPoints()
    {
        Assert.Null(SheetMarkupGeometry.Build(
            StudioSheetCommentRules.ShapeRectangle,
            [],
            PageWidth,
            PageHeight));
        Assert.Null(SheetMarkupGeometry.Build(
            StudioSheetCommentRules.ShapeRectangle,
            [new Point(0.5, 0.5)],
            PageWidth,
            PageHeight));
    }

    [Fact]
    public void LabelAnchor_SitsOnTheCornerOfAnEnclosingMarkAndOnThePointOfAPointingOne()
    {
        Point enclosing = SheetMarkupGeometry.ResolveLabelAnchor(
            StudioSheetCommentRules.ShapeCloud,
            [new Point(0.4, 0.5), new Point(0.2, 0.3), new Point(0.6, 0.7)]);
        Assert.Equal(0.2, enclosing.X, 5);
        Assert.Equal(0.3, enclosing.Y, 5);

        Point pointing = SheetMarkupGeometry.ResolveLabelAnchor(
            StudioSheetCommentRules.ShapeArrow,
            [new Point(0.8, 0.9), new Point(0.1, 0.1)]);
        Assert.Equal(0.8, pointing.X, 5);
    }

    [Fact]
    public void Composer_StandsBesideTheMarkRatherThanOverIt()
    {
        Point[] mark = [new(0.30, 0.30), new(0.56, 0.30), new(0.56, 0.54), new(0.30, 0.54)];

        Point place = SheetMarkupGeometry.PlaceComposer(mark, PageWidth, PageHeight, 330, 200);

        // Clear of the mark's right edge, so the reviewer can still see what
        // they are writing about.
        Assert.True(place.X >= 0.56 * PageWidth, $"box covers the mark: {place.X}");
        Assert.True(place.X + 330 <= PageWidth, $"box hangs off the page: {place.X}");
    }

    [Fact]
    public void Composer_MovesToTheOtherSideWhenTheMarkIsAtTheRightEdge()
    {
        Point[] mark = [new(0.80, 0.20), new(0.97, 0.20), new(0.97, 0.40), new(0.80, 0.40)];

        Point place = SheetMarkupGeometry.PlaceComposer(mark, PageWidth, PageHeight, 330, 200);

        Assert.True(place.X + 330 <= 0.80 * PageWidth, $"box covers the mark: {place.X}");
        Assert.True(place.X >= 0, $"box left the page: {place.X}");
    }

    [Fact]
    public void Composer_StaysOnThePageWhenTheMarkFillsIt()
    {
        Point[] mark = [new(0.02, 0.02), new(0.98, 0.02), new(0.98, 0.98), new(0.02, 0.98)];

        Point place = SheetMarkupGeometry.PlaceComposer(mark, PageWidth, PageHeight, 330, 200);

        Assert.InRange(place.X, 0, PageWidth - 330);
        Assert.InRange(place.Y, 0, PageHeight - 200);
    }

    [Fact]
    public void Arrow_ReachesThePointItWasAimedAt()
    {
        Geometry? geometry = SheetMarkupGeometry.Build(
            StudioSheetCommentRules.ShapeArrow,
            [new Point(0.2, 0.2), new Point(0.7, 0.6)],
            PageWidth,
            PageHeight);

        Assert.NotNull(geometry);
        Rect bounds = geometry!.Bounds;
        Assert.True(bounds.Right >= 0.7 * PageWidth - 1);
        Assert.True(bounds.Bottom >= 0.6 * PageHeight - 1);
    }
}
