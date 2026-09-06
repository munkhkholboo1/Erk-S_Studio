namespace ErkS.Platform.Core;

/// <summary>
/// The 2026 concept-cover layout, measured off the DWG the user supplied.
///
/// A4 landscape, millimetres, origin at the page's BOTTOM-LEFT corner - the
/// same convention as the corner-table and working-cover contracts, so nothing
/// has to be converted twice.
///
/// FOUR TABLES IN TWO PAIRS, which is what makes this a different DRAWING and
/// not a different set of numbers for the old one:
///
///   ЗӨВШИЛЦСӨН | ХЯНАСАН     above, three columns each, variable rows
///   ГҮЙЦЭТГЭГЧ | ЗАХИАЛАГЧ   below, four columns each, the first a logo cell
///                            that spans both rows
///
/// The old cover has one table with a left and a right block. No arrangement of
/// its numbers produces this, which is why it gets its own drawing routine
/// rather than a third flag on the existing one - a flag is how the working and
/// concept covers drifted five to eight millimetres apart without anyone
/// seeing.
///
/// MEASURED VERSUS DECIDED. Everything here came off the drawing except three
/// things, and those are marked where they appear. Mixing the two silently is
/// how a later reader "corrects" a deliberate choice back into the drawing's own
/// imprecision.
/// </summary>
public static class ConceptCoverSheetGrid
{
    // ---- THE SHEET ------------------------------------------------------
    //
    // A4 remains the default sheet, and every value below that describes WHERE
    // something sits now belongs to ConceptCoverLayout - one instance per sheet
    // size. What stays here is everything a sheet size must NOT change.

    public static double PageWidthMm => ConceptCoverLayout.A4.PageWidthMm;
    public static double PageHeightMm => ConceptCoverLayout.A4.PageHeightMm;

    /// <summary>Outer border of the A4 sheet, measured off the drawing.</summary>
    public static double FrameLeftMm => ConceptCoverLayout.A4.FrameLeftMm;
    public static double FrameBottomMm => ConceptCoverLayout.A4.FrameBottomMm;
    public static double FrameWidthMm => ConceptCoverLayout.A4.FrameWidthMm;
    public static double FrameHeightMm => ConceptCoverLayout.A4.FrameHeightMm;

    /// <summary>
    /// The vertical division between the left and right tables: the frame's
    /// exact centre.
    ///
    /// DECIDED, not measured - and the measurement is what makes it safe. The
    /// drawing's divider already sits at 153.80, which IS the frame's centre,
    /// while its two halves come out at 121.03 and 120.54. A divider placed on
    /// centre with unequal halves is somebody aiming at symmetry and missing by
    /// half a millimetre, not somebody meaning one side to be wider.
    /// </summary>
    public static double TablesMiddleMm => ConceptCoverLayout.A4.TablesMiddleMm;

    /// <summary>
    /// Width of one of the two tables. On A4 this is 120.785 - half the measured
    /// total of 241.57 - and it is now DERIVED from the frame and the inset
    /// below rather than written down, so that A3 gets the same rule instead of
    /// a second measurement.
    /// </summary>
    public static double TableWidthMm => ConceptCoverLayout.A4.TableWidthMm;

    /// <summary>Both table pairs start and end here.</summary>
    public static double TablesLeftMm => ConceptCoverLayout.A4.TablesLeftMm;
    public static double TablesRightMm => ConceptCoverLayout.A4.TablesRightMm;

    // ---- WHAT A SHEET SIZE MUST NOT CHANGE -------------------------------
    //
    // 🔴 «Scale хийхгүй шүү». These are the values a bigger page is forbidden to
    // touch, and they are single constants rather than per-sheet fields ON
    // PURPOSE: a difference between A4 and A3 in any of them cannot be written
    // down here at all, which is a stronger guarantee than a test that they
    // happen to agree.

    /// <summary>
    /// How far the tables sit inside the frame, each side. Measured on A4 -
    /// 33.015 from the page less the frame's 14.14 - and applied to A3 as a
    /// rule, which is what makes the pair widen with the sheet.
    /// </summary>
    public const double TableInsetFromFrameMm = 18.875;

    /// <summary>
    /// How far the signature block sits above the frame's bottom edge. Measured
    /// on A4 (33.34 less 3.54) and the reason a taller sheet grows upwards:
    /// every extra millimetre lands above the tables, not under them.
    /// </summary>
    public const double SignatureBlockAboveFrameMm = 29.80;

    /// <summary>The clear strip between the two table pairs.</summary>
    public const double GapBetweenTablesMm = 7.66;

    /// <summary>Total height of the ЗӨВШИЛЦСӨН / ХЯНАСАН pair.</summary>
    public const double UpperTableHeightMm = 48.0;

    /// <summary>Total height of the ГҮЙЦЭТГЭГЧ / ЗАХИАЛАГЧ pair.</summary>
    public const double LowerTableHeightMm = 16.0;

    /// <summary>A4 top edge of the upper pair, kept for readers of this class.</summary>
    public static double UpperTopMm => ConceptCoverLayout.A4.UpperTopMm;
    public static double UpperBottomMm => ConceptCoverLayout.A4.UpperBottomMm;

    /// <summary>Height of the label strip carrying «ЗӨВШИЛЦСӨН.» and «ХЯНАСАН.».</summary>
    public const double UpperHeaderHeightMm = 8.0;

    /// <summary>
    /// What the rows divide between them. FIXED: the drawing's two variants
    /// have two rows and three rows and the table is 48 mm tall in both, so
    /// adding a party makes the rows thinner rather than the table taller.
    /// </summary>
    public const double UpperBodyHeightMm = 40.0;

    /// <summary>Signature column width, both tables, both pairs.</summary>
    public const double SignatureColumnMm = 25.0;

    /// <summary>Name column width, both tables, both pairs.</summary>
    public const double NameColumnMm = 25.0;

    /// <summary>Position column of the upper pair on A4 - the remainder.</summary>
    public static double UpperRoleColumnMm => ConceptCoverLayout.A4.UpperRoleColumnMm;

    // ---- lower pair: ГҮЙЦЭТГЭГЧ | ЗАХИАЛАГЧ --------------------------------

    public static double LowerTopMm => ConceptCoverLayout.A4.LowerTopMm;
    public static double LowerBottomMm => ConceptCoverLayout.A4.LowerBottomMm;

    /// <summary>Two rows of eight millimetres. The lower pair does not vary.</summary>
    public const double LowerRowHeightMm = 8.0;

    /// <summary>
    /// The logo cell. It is ONE cell sixteen millimetres tall: the divider
    /// between the two rows stops at its edge and does not cross it.
    ///
    /// DECIDED width: the drawing has 14.38 on the left and 15.20 on the right,
    /// the same asymmetry as the tables above. 15.0 sits inside that range and
    /// is a whole number; if the user prefers the midpoint it becomes 14.79 and
    /// nothing else moves.
    /// </summary>
    public const double LogoColumnMm = 15.0;

    /// <summary>Position column of the lower pair on A4 - the remainder after the logo.</summary>
    public static double LowerRoleColumnMm => ConceptCoverLayout.A4.LowerRoleColumnMm;

    /// <summary>Every ruled line of both pairs. The drawing uses one weight.</summary>
    public const double LineWeightMm = 0.30;

    /// <summary>
    /// Where the horizontal divisions of the upper table's body fall, top-down,
    /// for a given number of rows.
    ///
    /// ALWAYS AN EVEN DIVISION, and this DEPARTS from the drawing on purpose.
    ///
    /// The drawing's three-row variant is 16/12/12, which is not 40/3. An
    /// earlier version of this file reproduced it, on the reasoning that a
    /// measurement beats a rule. The decision of 2026-09-06 reversed that, and
    /// the reason is worth keeping: reproducing 16/12/12 means a lookup table
    /// per row count, and a lookup table makes row height a DISCONTINUOUS
    /// function of the count - add a party and the whole table jumps. An even
    /// split is continuous, and at two rows it agrees with the drawing exactly.
    ///
    /// The same rule serves both sides. The drawing happens to show ХЯНАСАН
    /// with two rows in both of its variants; that is an observation about two
    /// examples, not a rule that it always has two, and giving it a special
    /// case would freeze an accident into the code.
    /// </summary>
    public static IReadOnlyList<double> UpperRowHeights(int rowCount)
    {
        int rows = Math.Max(1, rowCount);
        return Enumerable.Repeat(UpperBodyHeightMm / rows, rows).ToList();
    }

    /// <summary>
    /// Rows past this fit, and stop being readable: at seven the row is 5.7 mm
    /// and the text inside it smaller still.
    ///
    /// The table is NOT refused past it and the rows are NOT quietly squeezed -
    /// the roster editor says so where the rows are typed, and the sheet draws
    /// what it was given. Refusing to draw and drawing something unreadable
    /// without comment are both worse than drawing it and saying so.
    /// </summary>
    public const int ComfortableRowLimit = 6;

    /// <summary>
    /// Row boundaries of the upper table's body, from its top edge downwards.
    /// The first entry is the top of the first row, the last is the bottom of
    /// the table.
    /// </summary>
    public static IReadOnlyList<double> UpperRowBoundaries(int rowCount) =>
        ConceptCoverLayout.A4.UpperRowBoundaries(rowCount);
}
