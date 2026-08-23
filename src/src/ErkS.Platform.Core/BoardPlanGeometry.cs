namespace ErkS.Platform.Core;

public readonly record struct PlanPoint(double X, double Y);

/// <summary>
/// An arc recovered from a bulge, ready to be drawn as a curve rather than as
/// a run of straight lines. Angles are degrees measured the way the coordinate
/// space they were resolved in measures them, and the sweep is signed.
/// </summary>
public readonly record struct PlanArc(
    PlanPoint Centre,
    double Radius,
    double StartAngleDegrees,
    double SweepAngleDegrees);

/// <summary>
/// Turns AutoCAD's bulge back into the arc it describes.
///
/// A bulge is the tangent of a quarter of the included angle, signed so that a
/// positive value turns the way the coordinate space counts angles. Everything
/// else - the radius, the centre, where the sweep starts - follows from it and
/// the two endpoints, which is why an exporter only has to send the one number.
///
/// The work is done in whatever space the points are given in. A board mirrors
/// the drawing vertically, and mirroring reverses which way a curve turns, so a
/// caller working in board coordinates passes the negated bulge and gets angles
/// it can use directly.
/// </summary>
public static class BoardPlanArcs
{
    /// <summary>A bulge smaller than this is a straight line for drawing purposes.</summary>
    public const double StraightBulge = 1e-9;

    public static PlanArc? Resolve(PlanPoint start, PlanPoint end, double bulge)
    {
        if (!double.IsFinite(bulge) || Math.Abs(bulge) <= StraightBulge)
            return null;
        if (!double.IsFinite(start.X) || !double.IsFinite(start.Y) ||
            !double.IsFinite(end.X) || !double.IsFinite(end.Y))
        {
            return null;
        }

        double chordX = end.X - start.X;
        double chordY = end.Y - start.Y;
        double chord = Math.Sqrt(chordX * chordX + chordY * chordY);
        if (chord <= 0)
            return null;

        double included = 4 * Math.Atan(bulge);
        double halfSine = Math.Sin(included / 2);
        if (Math.Abs(halfSine) < 1e-12)
            return null;

        // Signed on purpose: the sign carries which side of the chord the
        // centre falls on, so a clockwise arc needs no separate case.
        double signedRadius = chord / (2 * halfSine);
        double apothem = signedRadius * Math.Cos(included / 2);
        double perpendicularX = -chordY / chord;
        double perpendicularY = chordX / chord;
        var centre = new PlanPoint(
            (start.X + end.X) / 2 + perpendicularX * apothem,
            (start.Y + end.Y) / 2 + perpendicularY * apothem);

        return new PlanArc(
            centre,
            Math.Abs(signedRadius),
            Degrees(Math.Atan2(start.Y - centre.Y, start.X - centre.X)),
            included * 180 / Math.PI);
    }

    private static double Degrees(double radians) => radians * 180 / Math.PI;
}

/// <summary>
/// Where the general plan lands on a card: metres of ground onto millimetres
/// of board, upright, undistorted, and reporting the scale it came out at.
///
/// The scale is an output rather than an input because a card is sized by the
/// layout, not by the drawing. Whatever size the card ends up, the plan fills
/// it and then says what scale that turned out to be - which is the only
/// honest way to draw a scale bar beside it.
/// </summary>
public readonly record struct BoardPlanProjection(
    double MillimetresPerMetre,
    double OriginXMetres,
    double OriginYMetres,
    double LeftMm,
    double TopMm,
    double WidthMm,
    double HeightMm)
{
    /// <summary>The N of 1:N. Zero when the projection could not be made.</summary>
    public double ScaleDenominator =>
        MillimetresPerMetre > 0 ? 1000 / MillimetresPerMetre : 0;

    /// <summary>
    /// Ground to board. The vertical axis flips: a drawing counts upwards and
    /// a page counts downwards.
    /// </summary>
    public PlanPoint ToBoard(double xMetres, double yMetres) => new(
        LeftMm + (xMetres - OriginXMetres) * MillimetresPerMetre,
        TopMm + HeightMm - (yMetres - OriginYMetres) * MillimetresPerMetre);
}

public static class BoardPlanProjections
{
    /// <summary>
    /// Fits the ground area wholly inside the card, centred, keeping its
    /// proportions. Nothing of the plan is cropped: a masterplan missing its
    /// eastern edge is a different masterplan.
    /// </summary>
    public static BoardPlanProjection? Fit(
        double minXMetres,
        double minYMetres,
        double maxXMetres,
        double maxYMetres,
        double cardLeftMm,
        double cardTopMm,
        double cardWidthMm,
        double cardHeightMm)
    {
        double groundWidth = maxXMetres - minXMetres;
        double groundHeight = maxYMetres - minYMetres;
        if (!double.IsFinite(groundWidth) || !double.IsFinite(groundHeight) ||
            groundWidth <= 0 || groundHeight <= 0 ||
            cardWidthMm <= 0 || cardHeightMm <= 0)
        {
            return null;
        }

        double scale = Math.Min(cardWidthMm / groundWidth, cardHeightMm / groundHeight);
        if (!double.IsFinite(scale) || scale <= 0)
            return null;

        double drawnWidth = groundWidth * scale;
        double drawnHeight = groundHeight * scale;
        return new BoardPlanProjection(
            scale,
            minXMetres,
            minYMetres,
            cardLeftMm + (cardWidthMm - drawnWidth) / 2,
            cardTopMm + (cardHeightMm - drawnHeight) / 2,
            drawnWidth,
            drawnHeight);
    }

    /// <summary>The projection for a whole board export onto one card.</summary>
    public static BoardPlanProjection? Fit(
        CityGenBoardManifest manifest,
        double cardLeftMm,
        double cardTopMm,
        double cardWidthMm,
        double cardHeightMm)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        double[] box = manifest.Bbox ?? [];
        return box.Length == 4 && box.All(double.IsFinite)
            ? Fit(box[0], box[1], box[2], box[3], cardLeftMm, cardTopMm, cardWidthMm, cardHeightMm)
            : null;
    }
}
