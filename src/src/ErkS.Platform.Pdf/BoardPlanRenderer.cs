using ErkS.Platform.Core;
using PdfSharp.Drawing;

namespace ErkS.Platform.Pdf;

public sealed record BoardPlanDrawResult(
    int ShapesDrawn,
    int HolesDrawn,
    int UnrecognisedShapes,
    double ScaleDenominator,
    IReadOnlyList<PlanStyle> Legend);

/// <summary>
/// Draws a general plan onto a card from its classification rather than from
/// its appearance.
///
/// This is the point of the whole vector channel. The drawing arrives saying
/// what each surface <em>is</em>, and what it looks like is decided here, so
/// changing the board's look changes the masterplan with it. Grass is drawn as
/// grass because the export said grass, not because the drawing happened to be
/// green.
/// </summary>
public static class BoardPlanRenderer
{
    private const double PointsPerMm = 72.0 / 25.4;

    /// <summary>Pattern spacing on the board, so density is a printed size.</summary>
    private const double PatternPitchMm = 3.0;

    /// <summary>Below this the pattern is skipped: marks would merge into a smudge.</summary>
    private const double SmallestPatternedAreaMm = 6.0;

    public static BoardPlanDrawResult Draw(
        XGraphics gfx,
        CityGenBoardManifest manifest,
        XRect areaPoints)
    {
        ArgumentNullException.ThrowIfNull(gfx);
        ArgumentNullException.ThrowIfNull(manifest);

        BoardPlanProjection? fitted = BoardPlanProjections.Fit(
            manifest,
            areaPoints.Left / PointsPerMm,
            areaPoints.Top / PointsPerMm,
            areaPoints.Width / PointsPerMm,
            areaPoints.Height / PointsPerMm);
        if (fitted is not { } projection)
            return new BoardPlanDrawResult(0, 0, 0, 0, []);

        int drawn = 0;
        int holes = 0;
        int unrecognised = 0;
        XGraphicsState state = gfx.Save();
        gfx.IntersectClip(areaPoints);
        foreach (CityGenBoardShape shape in CityGenBoardComposition.Shapes(manifest))
        {
            PlanStyle style = BoardPlanStyleCatalog.Resolve(shape.Outer);
            if (!DrawShape(gfx, shape, style, projection))
                continue;
            drawn++;
            holes += shape.Holes.Count;
            if (style.IsUnrecognised)
                unrecognised++;
        }
        gfx.Restore(state);

        return new BoardPlanDrawResult(
            drawn,
            holes,
            unrecognised,
            projection.ScaleDenominator,
            BoardPlanStyleCatalog.Legend(manifest));
    }

    private static bool DrawShape(
        XGraphics gfx,
        CityGenBoardShape shape,
        PlanStyle style,
        BoardPlanProjection projection)
    {
        (XGraphicsPath? path, XRect bounds) = BuildPath(shape, projection);
        if (path is null)
            return false;

        // Even-odd is what makes an island a hole: the outer ring and the rings
        // cut out of it are one path, and the fill rule decides the rest. No
        // polygon arithmetic, and a walkway across a lawn stops the grass
        // instead of being painted over by it.
        path.FillMode = XFillMode.Alternate;

        if (!style.FillPattern.Equals(PlanFillPatterns.None, StringComparison.Ordinal) &&
            TryBrush(style.FillColorHex) is { } fill)
        {
            gfx.DrawPath(fill, path);
        }

        DrawPattern(gfx, path, bounds, style);

        if (style.OutlineWidthMm > 0 && TryColor(style.OutlineColorHex) is { } outline)
            gfx.DrawPath(new XPen(outline, style.OutlineWidthMm * PointsPerMm), path);

        return true;
    }

    private static (XGraphicsPath? Path, XRect Bounds) BuildPath(
        CityGenBoardShape shape,
        BoardPlanProjection projection)
    {
        var path = new XGraphicsPath();
        var bounds = new PathBounds();
        if (!AddRing(path, bounds, shape.Outer, projection))
            return (null, default);
        foreach (CityGenBoardObject hole in shape.Holes)
            AddRing(path, bounds, hole, projection);
        return (path, bounds.ToRect());
    }

    private static bool AddRing(
        XGraphicsPath path,
        PathBounds bounds,
        CityGenBoardObject item,
        BoardPlanProjection projection)
    {
        List<CityGenBoardVertex> vertices = item.Vertices;
        if (vertices.Count < 2)
            return false;

        path.StartFigure();
        Dictionary<int, double> bulges = BulgesByStart(item);
        for (int index = 0; index < vertices.Count - 1; index++)
            AddRun(path, bounds, projection, vertices[index], vertices[index + 1], Bulge(bulges, index));
        if (item.IsClosed)
        {
            AddRun(
                path,
                bounds,
                projection,
                vertices[^1],
                vertices[0],
                Bulge(bulges, vertices.Count - 1));
            path.CloseFigure();
        }
        return true;
    }

    private static void AddRun(
        XGraphicsPath path,
        PathBounds bounds,
        BoardPlanProjection projection,
        CityGenBoardVertex from,
        CityGenBoardVertex to,
        double bulge)
    {
        PlanPoint start = projection.ToBoard(from.X, from.Y);
        PlanPoint end = projection.ToBoard(to.X, to.Y);
        bounds.Add(start.X * PointsPerMm, start.Y * PointsPerMm);
        bounds.Add(end.X * PointsPerMm, end.Y * PointsPerMm);

        // The board mirrors the drawing vertically, and mirroring reverses
        // which way a curve turns, so the bulge is negated to match.
        if (BoardPlanArcs.Resolve(start, end, -bulge) is { } arc && arc.Radius > 0)
        {
            path.AddArc(
                new XRect(
                    (arc.Centre.X - arc.Radius) * PointsPerMm,
                    (arc.Centre.Y - arc.Radius) * PointsPerMm,
                    arc.Radius * 2 * PointsPerMm,
                    arc.Radius * 2 * PointsPerMm),
                arc.StartAngleDegrees,
                arc.SweepAngleDegrees);
            return;
        }

        path.AddLine(
            start.X * PointsPerMm,
            start.Y * PointsPerMm,
            end.X * PointsPerMm,
            end.Y * PointsPerMm);
    }

    /// <summary>
    /// Marks laid over the shape and clipped to it. Drawn rather than tiled so
    /// the density is a size on the printed board, which is what a pattern is
    /// for: a lawn at 1:2000 and one at 1:200 should read the same.
    /// </summary>
    private static void DrawPattern(
        XGraphics gfx,
        XGraphicsPath path,
        XRect bounds,
        PlanStyle style)
    {
        if (style.FillPattern.Equals(PlanFillPatterns.Solid, StringComparison.Ordinal) ||
            style.FillPattern.Equals(PlanFillPatterns.None, StringComparison.Ordinal))
        {
            return;
        }
        if (TryColor(style.PatternColorHex) is not { } colour)
            return;

        if (bounds.Width < SmallestPatternedAreaMm * PointsPerMm ||
            bounds.Height < SmallestPatternedAreaMm * PointsPerMm)
        {
            return;
        }

        double pitch = PatternPitchMm * PointsPerMm;
        var pen = new XPen(colour, 0.35 * PointsPerMm);
        XGraphicsState state = gfx.Save();
        gfx.IntersectClip(path);
        // Walked over the shape's own bounds only, so the cost follows the area
        // actually covered rather than the size of the card.
        for (double y = bounds.Top; y < bounds.Bottom; y += pitch)
        {
            for (double x = bounds.Left; x < bounds.Right; x += pitch)
                DrawMark(gfx, pen, style.FillPattern, x, y, pitch);
        }
        gfx.Restore(state);
    }

    private static void DrawMark(
        XGraphics gfx,
        XPen pen,
        string pattern,
        double x,
        double y,
        double pitch)
    {
        double unit = pitch * 0.32;
        switch (pattern)
        {
            case PlanFillPatterns.Grass:
                // A tuft: two blades leaning apart from one root.
                gfx.DrawLine(pen, x, y + unit, x - unit * 0.6, y - unit);
                gfx.DrawLine(pen, x, y + unit, x + unit * 0.6, y - unit);
                break;
            case PlanFillPatterns.Water:
                gfx.DrawLine(pen, x - unit, y, x + unit, y);
                break;
            case PlanFillPatterns.Gravel:
                gfx.DrawLine(pen, x, y, x + unit * 0.28, y);
                break;
            case PlanFillPatterns.Paving:
                gfx.DrawLine(pen, x - unit, y - unit, x - unit, y + unit);
                gfx.DrawLine(pen, x - unit, y - unit, x + unit, y - unit);
                break;
            case PlanFillPatterns.Hatch:
                gfx.DrawLine(pen, x - unit, y + unit, x + unit, y - unit);
                break;
        }
    }

    /// <summary>
    /// The extent of a path, gathered as it is built. Computed here rather than
    /// asked of the path so the renderer stays clear of PdfSharp's internals.
    /// </summary>
    private sealed class PathBounds
    {
        private double left = double.MaxValue;
        private double top = double.MaxValue;
        private double right = double.MinValue;
        private double bottom = double.MinValue;

        public void Add(double x, double y)
        {
            if (!double.IsFinite(x) || !double.IsFinite(y))
                return;
            left = Math.Min(left, x);
            top = Math.Min(top, y);
            right = Math.Max(right, x);
            bottom = Math.Max(bottom, y);
        }

        public XRect ToRect() => right >= left && bottom >= top
            ? new XRect(left, top, right - left, bottom - top)
            : default;
    }

    /// <summary>
    /// The bulge of each run, by the vertex it starts at. A segment list may be
    /// absent or partial, and a missing entry simply means a straight run.
    /// </summary>
    private static Dictionary<int, double> BulgesByStart(CityGenBoardObject item)
    {
        var bulges = new Dictionary<int, double>();
        foreach (CityGenBoardSegment segment in item.Segments)
        {
            if (double.IsFinite(segment.Bulge) && segment.Bulge != 0)
                bulges[segment.StartVertexIndex] = segment.Bulge;
        }
        return bulges;
    }

    private static double Bulge(Dictionary<int, double> bulges, int startIndex) =>
        bulges.TryGetValue(startIndex, out double bulge) ? bulge : 0;

    private static XSolidBrush? TryBrush(string hex) =>
        TryColor(hex) is { } colour ? new XSolidBrush(colour) : null;

    private static XColor? TryColor(string hex)
    {
        string value = (hex ?? "").Trim().TrimStart('#');
        if (value.Length == 8)
        {
            // Alpha first, so a fully transparent entry means "draw nothing".
            return Convert.ToInt32(value[..2], 16) == 0
                ? null
                : XColor.FromArgb(
                    Convert.ToInt32(value[..2], 16),
                    Convert.ToInt32(value.Substring(2, 2), 16),
                    Convert.ToInt32(value.Substring(4, 2), 16),
                    Convert.ToInt32(value.Substring(6, 2), 16));
        }
        if (value.Length != 6)
            return null;
        return XColor.FromArgb(
            Convert.ToInt32(value[..2], 16),
            Convert.ToInt32(value.Substring(2, 2), 16),
            Convert.ToInt32(value.Substring(4, 2), 16));
    }
}
