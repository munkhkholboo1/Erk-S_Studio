using ErkS.Platform.Core;

namespace ErkS.Studio;

/// <summary>The front matter rendered for the layout editor, and what the build said.</summary>
internal sealed record FrontMatterPreview(System.Windows.Media.Imaging.BitmapSource? Image, IReadOnlyList<string> Warnings);

/// <summary>Which grip of a selected block is being dragged.</summary>
internal enum FrontMatterGrip
{
    Move,
    North,
    South,
    East,
    West,
    NorthEast,
    NorthWest,
    SouthEast,
    SouthWest,
    ColumnGap,
}

/// <summary>A drag's result: the block as it now stands and the guides it snapped to.</summary>
internal sealed record FrontMatterDragResult(FrontMatterBlockLayout Block, double? GuideX, double? GuideY);

/// <summary>
/// The geometry of the front-matter layout editor, apart from the window: a
/// block is moved or resized by a grip, and its edges snap to the guides - the
/// corner table, the drawing frame, the other blocks and the page's centre.
/// </summary>
/// <remarks>
/// Owner, 2026-10-20: «бичих биш өөрийг нь идэвхжүүлээд хөөрхөн цэгэн болон
/// тохирсон гоё guide уудаар удирддаг байна» - the blocks are arranged on the
/// page with dot grips and guides, not typed in as numbers.
/// </remarks>
internal static class FrontMatterLayoutEditing
{
    public const double SnapToleranceMm = 2.5;

    /// <summary>The lines a block's edges are drawn to.</summary>
    public static (List<double> Xs, List<double> Ys) Guides(
        double pageWidthMm,
        double pageHeightMm,
        PageRectMm corner,
        PageRectMm innerFrame,
        IEnumerable<FrontMatterBlockLayout> others)
    {
        var xs = new List<double>
        {
            innerFrame.X,
            innerFrame.X + innerFrame.Width,
            corner.X,
            corner.X + corner.Width,
            pageWidthMm / 2,
        };
        var ys = new List<double>
        {
            innerFrame.Y,
            innerFrame.Y + innerFrame.Height,
            corner.Y,
            corner.Y + corner.Height,
            pageHeightMm / 2,
        };
        foreach (FrontMatterBlockLayout other in others ?? [])
        {
            xs.Add(other.X);
            xs.Add(other.X + other.Width);
            ys.Add(other.Y);
            ys.Add(other.Y + other.Height);
        }
        return (xs, ys);
    }

    /// <summary>Applies a drag of (dx, dy) millimetres on a grip, then snaps.</summary>
    public static FrontMatterDragResult Drag(
        FrontMatterBlockLayout start,
        FrontMatterGrip grip,
        double dx,
        double dy,
        IReadOnlyList<double> guideXs,
        IReadOnlyList<double> guideYs)
    {
        FrontMatterBlockLayout block = start.Clone();
        double left = start.X;
        double top = start.Y;
        double right = start.X + start.Width;
        double bottom = start.Y + start.Height;
        double? guideX = null;
        double? guideY = null;

        if (grip == FrontMatterGrip.ColumnGap)
        {
            block.ColumnGapMm = Math.Clamp(start.ColumnGapMm + dx * 2, 0, 50);
            if (block.ColumnWidthMm < FrontMatterBlockLayout.MinSizeMm)
                block.ColumnGapMm = start.ColumnGapMm;
            return new(block, null, null);
        }

        if (grip == FrontMatterGrip.Move)
        {
            double newLeft = left + dx;
            double newTop = top + dy;
            (double snapLeft, double? gx) = SnapEdges(newLeft, start.Width, guideXs);
            (double snapTop, double? gy) = SnapEdges(newTop, start.Height, guideYs);
            block.X = snapLeft;
            block.Y = snapTop;
            return new(block, gx, gy);
        }

        bool west = grip is FrontMatterGrip.West or FrontMatterGrip.NorthWest or FrontMatterGrip.SouthWest;
        bool east = grip is FrontMatterGrip.East or FrontMatterGrip.NorthEast or FrontMatterGrip.SouthEast;
        bool north = grip is FrontMatterGrip.North or FrontMatterGrip.NorthEast or FrontMatterGrip.NorthWest;
        bool south = grip is FrontMatterGrip.South or FrontMatterGrip.SouthEast or FrontMatterGrip.SouthWest;
        if (west)
            (left, guideX) = Snap(left + dx, guideXs);
        if (east)
            (right, guideX) = Snap(right + dx, guideXs);
        if (north)
            (top, guideY) = Snap(top + dy, guideYs);
        if (south)
            (bottom, guideY) = Snap(bottom + dy, guideYs);

        double min = FrontMatterBlockLayout.MinSizeMm;
        if (right - left < min)
        {
            if (west)
                left = right - min;
            else
                right = left + min;
            guideX = null;
        }
        if (bottom - top < min)
        {
            if (north)
                top = bottom - min;
            else
                bottom = top + min;
            guideY = null;
        }

        block.X = left;
        block.Y = top;
        block.Width = right - left;
        block.Height = bottom - top;
        return new(block, guideX, guideY);
    }

    /// <summary>One more or one fewer column, never narrower than the minimum.</summary>
    public static FrontMatterBlockLayout WithColumns(FrontMatterBlockLayout start, int columns)
    {
        FrontMatterBlockLayout block = start.Clone();
        block.Columns = Math.Clamp(columns, 1, FrontMatterBlockLayout.MaxColumns);
        return block.ColumnWidthMm < FrontMatterBlockLayout.MinSizeMm ? start.Clone() : block;
    }

    private static (double Value, double? Guide) Snap(double value, IReadOnlyList<double> guides)
    {
        double? best = null;
        foreach (double guide in guides)
        {
            if (Math.Abs(guide - value) <= SnapToleranceMm &&
                (best is null || Math.Abs(guide - value) < Math.Abs(best.Value - value)))
            {
                best = guide;
            }
        }
        return best is double snapped ? (snapped, snapped) : (value, null);
    }

    /// <summary>Moves a span so that its nearer edge, or its centre, sits on a guide.</summary>
    private static (double Start, double? Guide) SnapEdges(double start, double length, IReadOnlyList<double> guides)
    {
        (double, double?) best = (start, null);
        double bestDistance = double.MaxValue;
        foreach (double guide in guides)
        {
            foreach (double offset in new[] { 0, length, length / 2 })
            {
                double distance = Math.Abs(guide - (start + offset));
                if (distance <= SnapToleranceMm && distance < bestDistance)
                {
                    bestDistance = distance;
                    best = (guide - offset, guide);
                }
            }
        }
        return best;
    }
}
