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
    public const double PageWidthMm = 297.0;
    public const double PageHeightMm = 210.0;

    /// <summary>Outer border of the sheet.</summary>
    public const double FrameLeftMm = 14.14;
    public const double FrameBottomMm = 3.54;
    public const double FrameWidthMm = 279.32;
    public const double FrameHeightMm = 202.95;

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
    public const double TablesMiddleMm = FrameLeftMm + FrameWidthMm / 2;

    /// <summary>
    /// Width of one of the two tables: half the measured total of 241.57 mm.
    /// Centring that total moves each outer edge by 0.245 mm and changes
    /// nothing else.
    /// </summary>
    public const double TableWidthMm = 120.785;

    /// <summary>Both table pairs start and end here.</summary>
    public const double TablesLeftMm = TablesMiddleMm - TableWidthMm;
    public const double TablesRightMm = TablesMiddleMm + TableWidthMm;

    // ---- upper pair: ЗӨВШИЛЦСӨН | ХЯНАСАН ----------------------------------

    public const double UpperTopMm = 105.0;
    public const double UpperBottomMm = 57.0;

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

    /// <summary>Position column of the upper pair - the remainder.</summary>
    public const double UpperRoleColumnMm = TableWidthMm - NameColumnMm - SignatureColumnMm;

    // ---- lower pair: ГҮЙЦЭТГЭГЧ | ЗАХИАЛАГЧ --------------------------------

    public const double LowerTopMm = 49.34;
    public const double LowerBottomMm = 33.34;

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

    /// <summary>Position column of the lower pair - the remainder after the logo.</summary>
    public const double LowerRoleColumnMm =
        TableWidthMm - LogoColumnMm - NameColumnMm - SignatureColumnMm;

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
    public static IReadOnlyList<double> UpperRowBoundaries(int rowCount)
    {
        var boundaries = new List<double> { UpperTopMm - UpperHeaderHeightMm };
        foreach (double height in UpperRowHeights(rowCount))
            boundaries.Add(boundaries[^1] - height);
        return boundaries;
    }
}
