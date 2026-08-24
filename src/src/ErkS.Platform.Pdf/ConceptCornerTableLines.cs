namespace ErkS.Platform.Pdf;

using ErkS.Platform.Core;

/// <summary>
/// One ruled line of the corner title block, in millimetres.
/// </summary>
/// <param name="Heavy">
/// True for the table's outer border, false for an interior division. The
/// distinction is the whole point of this type: the two weights are what make
/// the block read as one table rather than several.
/// </param>
public readonly record struct ConceptCornerTableSegment(
    double X0,
    double Y0,
    double X1,
    double Y1,
    bool Heavy)
{
    public bool IsVertical => Math.Abs(X1 - X0) < Tolerance;

    private const double Tolerance = 1e-6;

    /// <summary>
    /// Whether this segment lies along <paramref name="other"/> and within its
    /// extent - the test for "the restamp put back a piece of the original
    /// line", as opposed to a line of its own.
    /// </summary>
    public bool LiesAlong(ConceptCornerTableSegment other)
    {
        if (IsVertical != other.IsVertical)
            return false;

        return IsVertical
            ? Near(X0, other.X0) && Within(Y0, other) && Within(Y1, other)
            : Near(Y0, other.Y0) && Within(X0, other) && Within(X1, other);
    }

    private static bool Near(double left, double right) =>
        Math.Abs(left - right) < Tolerance;

    private bool Within(double value, ConceptCornerTableSegment other) =>
        other.IsVertical
            ? value >= Math.Min(other.Y0, other.Y1) - Tolerance &&
              value <= Math.Max(other.Y0, other.Y1) + Tolerance
            : value >= Math.Min(other.X0, other.X1) - Tolerance &&
              value <= Math.Max(other.X0, other.X1) + Tolerance;
}

/// <summary>
/// Where the corner title block's lines go, and how heavy each one is.
///
/// The block is drawn twice by two different routines: once when a page is
/// composed, and again when the canonical metadata is restamped onto an album
/// that already exists. The restamp only clears the left part of the table -
/// the cells whose contents can change - and has to redraw what it erased.
///
/// It used to redraw that part as a rectangle in the heavy border pen, which
/// laid a 0.35 mm border down x3, in the middle of a table whose interior
/// divisions are 0.10 mm. On the page that reads as two tables pushed
/// together, which is what a user reported seeing. Both routines now take
/// their lines from here, so the two can be compared instead of trusted.
/// </summary>
public static class ConceptCornerTableLines
{
    /// <summary>
    /// The whole table, as drawn when a page is first composed.
    /// </summary>
    public static IReadOnlyList<ConceptCornerTableSegment> Full(
        BuildingArchitectureConceptCornerGrid grid)
    {
        var lines = new List<ConceptCornerTableSegment>(Border(grid.X0, grid.Y0, grid.X5, grid.Y4));
        foreach (double x in new[] { grid.X1, grid.X2, grid.X3, grid.X4 })
            lines.Add(new ConceptCornerTableSegment(x, grid.Y0, x, grid.Y4, false));
        foreach (double y in new[] { grid.Y1, grid.Y2, grid.Y3 })
            lines.Add(new ConceptCornerTableSegment(grid.X1, y, grid.X5, y, false));
        return lines;
    }

    /// <summary>
    /// The part a restamp erases and must put back: the metadata columns, from
    /// the left edge across to x3.
    ///
    /// x0, y0 and y4 are edges of the table itself and stay heavy. x3 is not -
    /// it is the same interior division the full table draws at 0.10 mm, and
    /// drawing it heavy is what split the block in two.
    /// </summary>
    public static IReadOnlyList<ConceptCornerTableSegment> Restamped(
        BuildingArchitectureConceptCornerGrid grid)
    {
        var lines = new List<ConceptCornerTableSegment>
        {
            new(grid.X0, grid.Y0, grid.X0, grid.Y4, true),
            new(grid.X0, grid.Y0, grid.X3, grid.Y0, true),
            new(grid.X0, grid.Y4, grid.X3, grid.Y4, true),
            new(grid.X3, grid.Y0, grid.X3, grid.Y4, false),
        };
        foreach (double x in new[] { grid.X1, grid.X2 })
            lines.Add(new ConceptCornerTableSegment(x, grid.Y0, x, grid.Y4, false));
        foreach (double y in new[] { grid.Y1, grid.Y2, grid.Y3 })
            lines.Add(new ConceptCornerTableSegment(grid.X1, y, grid.X3, y, false));
        return lines;
    }

    private static IEnumerable<ConceptCornerTableSegment> Border(
        double x0,
        double y0,
        double x1,
        double y1) =>
    [
        new(x0, y0, x0, y1, true),
        new(x1, y0, x1, y1, true),
        new(x0, y0, x1, y0, true),
        new(x0, y1, x1, y1, true),
    ];
}
