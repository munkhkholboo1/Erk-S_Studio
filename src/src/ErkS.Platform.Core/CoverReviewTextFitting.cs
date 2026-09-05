namespace ErkS.Platform.Core;

/// <summary>
/// How large the text in the working-drawing cover's review rows is drawn.
///
/// Revit shrank it to fit and stopped at a floor, letting anything still too
/// long overflow its cell. Studio did the opposite: it kept the text at full
/// size and grew the row, letting the table stretch downwards. Neither is the
/// other's bug - they are two answers to "what gives way when a name is long",
/// and Revit's answer produces a defect on a signed document while Studio's
/// produces a table one or two millimetres taller than the drawing it copies.
///
/// THE DECISION, 2026-09-06: shrink first, exactly as Revit does, and grow the
/// row only if the text still does not fit. On every project measured that
/// makes no difference at all - the shrink is enough - and where it does differ,
/// it differs by not overflowing.
///
/// This matters more than it sounds: the one exported cover on disk had its
/// review rows at 1.78 mm, which IS the floor. The reference drawing was
/// already at the point of overflowing, so copying the rule unchanged would
/// have started producing overflowing covers immediately.
/// </summary>
public static class CoverReviewTextFitting
{
    /// <summary>The size a cell uses when the text fits, in cap-height millimetres.</summary>
    public const double BaseTextHeightMm = 2.5;

    /// <summary>Below this it stops shrinking. Revit's floor, kept.</summary>
    public const double MinimumTextHeightMm = 1.80;

    /// <summary>
    /// Total horizontal inset taken off the cell before fitting - 1.2 mm each
    /// side.
    ///
    /// Revit's own role cell actually insets 2.8 mm (2.0 left, 0.8 right) while
    /// still fitting against 2.4, so its sizing there is 0.4 mm optimistic. The
    /// number is copied rather than corrected: the aim is to arrive at the size
    /// Revit chose, and the optimism only ever makes text slightly too wide -
    /// which is precisely what growing the row now absorbs.
    /// </summary>
    public const double CellInsetMm = 2.4;

    /// <summary>
    /// Average glyph width as a fraction of cap height. Not a measurement of
    /// any particular font - it is the constant Revit fits with, and the point
    /// is to arrive at the SAME size it did.
    /// </summary>
    public const double CharacterWidthFactor = 0.52;

    /// <summary>
    /// A difference this small is not worth a second text size on the page.
    /// </summary>
    public const double NegligibleDifferenceMm = 0.05;

    /// <summary>
    /// The size for one COLUMN, given the longest line in it.
    ///
    /// The column, not the row: every cell in a column is drawn at one size, so
    /// the longest entry decides for all of them.
    ///
    /// "Line" means what the WRITER of the text separated with a line break, not
    /// what word-wrapping produces. That distinction is what keeps this from
    /// being circular - a size that depended on wrapping would depend on itself,
    /// since wrapping depends on the size. Text with no line breaks of its own
    /// is therefore measured whole, which is what drove the one exported cover's
    /// 88-character position down to the floor.
    /// </summary>
    public static double FitColumn(int longestTextLength, double cellWidthMm)
    {
        if (longestTextLength <= 0)
            return BaseTextHeightMm;

        double usableWidthMm = Math.Max(1.0, cellWidthMm - CellInsetMm);
        double estimatedWidthMm = longestTextLength * BaseTextHeightMm * CharacterWidthFactor;
        if (estimatedWidthMm <= usableWidthMm)
            return BaseTextHeightMm;

        double fitted = BaseTextHeightMm * usableWidthMm / estimatedWidthMm;
        fitted = Math.Clamp(fitted, MinimumTextHeightMm, BaseTextHeightMm);
        return BaseTextHeightMm - fitted < NegligibleDifferenceMm
            ? BaseTextHeightMm
            : Math.Round(fitted, 2);
    }

    /// <summary>The same question asked of a whole column of strings.</summary>
    public static double FitColumn(IEnumerable<string?> texts, double cellWidthMm) =>
        FitColumn(LongestLineLength(texts), cellWidthMm);

    /// <summary>
    /// The longest line across a column, splitting only on the line breaks the
    /// text itself carries.
    /// </summary>
    public static int LongestLineLength(IEnumerable<string?> texts)
    {
        int longest = 0;
        foreach (string? text in texts ?? [])
        {
            foreach (string line in (text ?? "").Split(
                         ['\r', '\n'],
                         StringSplitOptions.RemoveEmptyEntries))
            {
                longest = Math.Max(longest, line.Trim().Length);
            }
        }

        return longest;
    }
}
