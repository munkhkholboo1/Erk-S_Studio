using System.Windows;
using System.Windows.Media;

namespace ErkS.Studio;

/// <summary>
/// Turns a mark's fractions of a page into a shape drawn at the size the page
/// happens to be on screen.
///
/// Every mark is stored as fractions and only ever becomes pixels here, which
/// is what lets the same cloud land on the same part of the same drawing at any
/// zoom, and on a sheet re-issued at another size.
/// </summary>
internal static class SheetMarkupGeometry
{
    /// <summary>How far a revision cloud's arcs bulge, as a fraction of the page.</summary>
    private const double CloudBumpFraction = 0.012;

    /// <summary>How long an arrow head is, as a fraction of the page.</summary>
    private const double ArrowHeadFraction = 0.022;

    public static Geometry? Build(
        string? shape,
        IReadOnlyList<Point> normalizedPoints,
        double width,
        double height)
    {
        if (width <= 0 || height <= 0 || normalizedPoints.Count == 0)
            return null;

        List<Point> points = normalizedPoints
            .Select(point => new Point(point.X * width, point.Y * height))
            .ToList();
        double scale = Math.Min(width, height);

        return StudioSheetCommentRules.NormalizeShape(shape) switch
        {
            StudioSheetCommentRules.ShapeRectangle => BuildRectangle(points),
            StudioSheetCommentRules.ShapeArrow => BuildArrow(points, scale),
            StudioSheetCommentRules.ShapeFreehand => BuildPolyline(points, closed: false),
            StudioSheetCommentRules.ShapeCloud => BuildCloud(points, scale),
            _ => BuildPin(points[0], scale),
        };
    }

    private static Geometry? BuildRectangle(IReadOnlyList<Point> points)
    {
        if (points.Count < 2)
            return null;

        return new RectangleGeometry(new Rect(points[0], points[^1]));
    }

    private static Geometry? BuildPin(Point point, double scale) =>
        new EllipseGeometry(point, scale * 0.012, scale * 0.012);

    /// <summary>A line with a head, drawn as one figure so it strokes evenly.</summary>
    private static Geometry? BuildArrow(IReadOnlyList<Point> points, double scale)
    {
        if (points.Count < 2)
            return null;

        Point from = points[0];
        Point to = points[^1];
        var geometry = new StreamGeometry();
        using (StreamGeometryContext context = geometry.Open())
        {
            context.BeginFigure(from, isFilled: false, isClosed: false);
            context.LineTo(to, isStroked: true, isSmoothJoin: false);

            Vector along = to - from;
            if (along.Length > 0.0001)
            {
                along.Normalize();
                var across = new Vector(-along.Y, along.X);
                double head = scale * ArrowHeadFraction;
                Point left = to - (along * head) + (across * head * 0.45);
                Point right = to - (along * head) - (across * head * 0.45);
                context.BeginFigure(left, isFilled: false, isClosed: false);
                context.LineTo(to, isStroked: true, isSmoothJoin: false);
                context.LineTo(right, isStroked: true, isSmoothJoin: false);
            }
        }
        geometry.Freeze();
        return geometry;
    }

    private static Geometry? BuildPolyline(IReadOnlyList<Point> points, bool closed)
    {
        if (points.Count < 2)
            return null;

        var geometry = new StreamGeometry();
        using (StreamGeometryContext context = geometry.Open())
        {
            context.BeginFigure(points[0], isFilled: false, isClosed: closed);
            for (int index = 1; index < points.Count; index++)
                context.LineTo(points[index], isStroked: true, isSmoothJoin: true);
        }
        geometry.Freeze();
        return geometry;
    }

    /// <summary>
    /// A revision cloud: the drawn path replaced by arcs bulging outward along
    /// it. This is the mark a construction drawing is corrected with, so it is
    /// drawn as one rather than approximated with a wobbly line.
    /// </summary>
    private static Geometry? BuildCloud(IReadOnlyList<Point> points, double scale)
    {
        // The loop is closed before it is resampled, so the run from the last
        // drawn point back to the first is broken into bumps like every other
        // run. Left open, that one closing chord was many times the arc radius
        // and the arc ballooned across the sheet.
        var loop = points.ToList();
        if (loop.Count > 2 && (loop[^1] - loop[0]).Length > 0.0001)
            loop.Add(loop[0]);

        double radius = scale * CloudBumpFraction;
        List<Point> path = Resample(loop, radius * 2);
        if (path.Count < 3)
            return null;

        var geometry = new StreamGeometry();
        using (StreamGeometryContext context = geometry.Open())
        {
            context.BeginFigure(path[0], isFilled: false, isClosed: true);
            for (int index = 1; index <= path.Count; index++)
            {
                Point from = path[index - 1];
                Point next = path[index % path.Count];
                // An arc cannot span a chord longer than its own diameter. The
                // resampler leaves one short run at the end of the loop, and a
                // rounding error there must not turn into a sweep.
                double chord = (next - from).Length;
                double arc = Math.Max(radius, chord / 2);
                context.ArcTo(
                    next,
                    new Size(arc, arc),
                    rotationAngle: 0,
                    isLargeArc: false,
                    SweepDirection.Clockwise,
                    isStroked: true,
                    isSmoothJoin: false);
            }
        }
        geometry.Freeze();
        return geometry;
    }

    /// <summary>
    /// Walks the drawn path and drops a point every <paramref name="step"/>, so
    /// a cloud's arcs are the same size however fast the mark was drawn.
    /// </summary>
    private static List<Point> Resample(IReadOnlyList<Point> points, double step)
    {
        if (step <= 0 || points.Count < 2)
            return points.ToList();

        var result = new List<Point> { points[0] };
        double carried = 0;
        for (int index = 1; index < points.Count; index++)
        {
            Point from = points[index - 1];
            Point to = points[index];
            Vector segment = to - from;
            double length = segment.Length;
            if (length <= 0.0001)
                continue;

            Vector direction = segment / length;
            double travelled = -carried;
            while (travelled + step <= length)
            {
                travelled += step;
                result.Add(from + (direction * travelled));
            }
            carried = length - travelled;
        }
        return result;
    }

    /// <summary>
    /// Where the comment box belongs for a mark just drawn: beside it, never
    /// over it, and never off the page.
    ///
    /// A reviewer has to see the thing they are complaining about while they
    /// write the complaint, so the box goes to the right of the mark. A mark
    /// drawn near the right edge leaves no room there, and the box goes to its
    /// left instead; a mark that fills the page leaves room on neither side,
    /// and the box is pinned inside the page rather than allowed off it.
    /// </summary>
    public static Point PlaceComposer(
        IReadOnlyList<Point> normalizedPoints,
        double pageWidth,
        double pageHeight,
        double cardWidth,
        double cardHeight)
    {
        if (normalizedPoints.Count == 0)
            return new Point(8, 8);

        double leftEdge = normalizedPoints.Min(point => point.X) * pageWidth;
        double rightEdge = normalizedPoints.Max(point => point.X) * pageWidth;
        double topEdge = normalizedPoints.Min(point => point.Y) * pageHeight;

        double rightLimit = Math.Max(8d, pageWidth - cardWidth - 8);
        double left = rightEdge + ComposerGap;
        if (left > rightLimit)
            left = leftEdge - cardWidth - ComposerGap;

        double bottomLimit = Math.Max(8d, pageHeight - cardHeight - 8);
        return new Point(
            Math.Clamp(left, 8d, rightLimit),
            Math.Clamp(topEdge - 10, 8d, bottomLimit));
    }

    /// <summary>How far the comment box stands off the mark it belongs to.</summary>
    private const double ComposerGap = 22;

    /// <summary>
    /// Where a mark's label belongs: the first point for a pointing mark, the
    /// top-left of the area for one that encloses something.
    /// </summary>
    public static Point ResolveLabelAnchor(
        string? shape,
        IReadOnlyList<Point> normalizedPoints)
    {
        if (normalizedPoints.Count == 0)
            return new Point(0.5, 0.5);

        return StudioSheetCommentRules.NormalizeShape(shape) switch
        {
            StudioSheetCommentRules.ShapeRectangle or
            StudioSheetCommentRules.ShapeCloud or
            StudioSheetCommentRules.ShapeFreehand => new Point(
                normalizedPoints.Min(point => point.X),
                normalizedPoints.Min(point => point.Y)),
            _ => normalizedPoints[0],
        };
    }
}
