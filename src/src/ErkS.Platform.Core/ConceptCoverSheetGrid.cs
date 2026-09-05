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

    /// <summary>Both table pairs start and end here.</summary>
    public const double TablesLeftMm = 32.77;
    public const double TablesRightMm = 274.34;

    /// <summary>
    /// The vertical division between the left and right tables.
    ///
    /// DECIDED, not measured. The drawing puts the left table at 121.03 mm and
    /// the right at 120.54 - and its logo columns at 14.38 and 15.20 - which is
    /// the hand of whoever drew it rather than an intention. Reproducing a
    /// half-millimetre wobble would make every future reader wonder which side
    /// was meant to be wider.
    /// </summary>
    public const double TablesMiddleMm = (TablesLeftMm + TablesRightMm) / 2;

    /// <summary>Width of one of the two tables after that decision.</summary>
    public const double TableWidthMm = (TablesRightMm - TablesLeftMm) / 2;

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
    /// the same asymmetry as the tables above.
    /// </summary>
    public const double LogoColumnMm = 14.79;

    /// <summary>Position column of the lower pair - the remainder after the logo.</summary>
    public const double LowerRoleColumnMm =
        TableWidthMm - LogoColumnMm - NameColumnMm - SignatureColumnMm;

    /// <summary>Every ruled line of both pairs. The drawing uses one weight.</summary>
    public const double LineWeightMm = 0.30;

    /// <summary>
    /// Where the horizontal divisions of the upper table's body fall, top-down,
    /// for a given number of rows.
    ///
    /// TWO and THREE are the drawing's own: 20/20, and 16/12/12. The second is
    /// NOT an even split - 40/3 would be 13.33 - and reproducing it is the
    /// point, because that is what the drawing shows.
    ///
    /// FOUR AND ABOVE ARE DECIDED, because the drawing does not show them. An
    /// even division is the only rule that cannot be mistaken for a measurement
    /// somebody forgot to record.
    /// </summary>
    public static IReadOnlyList<double> UpperRowHeights(int rowCount)
    {
        int rows = Math.Max(1, rowCount);
        return rows switch
        {
            1 => [UpperBodyHeightMm],
            2 => [20.0, 20.0],
            3 => [16.0, 12.0, 12.0],
            _ => Enumerable.Repeat(UpperBodyHeightMm / rows, rows).ToList(),
        };
    }

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
