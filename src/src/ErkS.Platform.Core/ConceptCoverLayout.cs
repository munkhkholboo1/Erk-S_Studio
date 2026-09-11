namespace ErkS.Platform.Core;

/// <summary>
/// Where the 2026 concept cover's parts sit on a given sheet size.
///
/// 🔴 NOTHING HERE IS SCALED. «Scale хийхгүй шүү» - the user said it twice, and
/// it is the whole design of this type. Text heights, line weight, row heights,
/// table heights and the three hand-written columns are NOT part of this record
/// at all: they live once, in <see cref="ConceptCoverSheetGrid"/>, and both
/// sheets read the same values. A difference between A4 and A3 in any of them
/// cannot be expressed here, which is stronger than a test that checks they
/// happen to match.
///
/// WHAT A SHEET SIZE ACTUALLY CHANGES is two things, and the contract states
/// both as rules rather than as numbers:
///
///   * the tables sit the same distance inside the frame (18.875 mm), so they
///     WIDEN with the page instead of floating in the middle of it;
///   * the signature block sits the same distance above the frame's bottom
///     edge (29.80 mm), so the extra height of a taller page all lands in the
///     free area ABOVE the tables, where the title goes.
///
/// Everything else follows. That is why this is a layout RECORD and not a
/// second copy of the drawing: A3 was not measured off a second template, it is
/// the same rules over a different frame - and running them backwards over A4's
/// own frame reproduces A4's measured numbers exactly, which is the check that
/// makes the rules trustworthy rather than convenient.
/// </summary>
/// <param name="Name">For diagnostics and test failure messages only.</param>
public sealed record ConceptCoverLayout(
    string Name,
    double PageWidthMm,
    double PageHeightMm,
    double FrameLeftMm,
    double FrameBottomMm,
    double FrameWidthMm,
    double FrameHeightMm)
{
    /// <summary>
    /// A4 landscape, the sheet measured off the DWG the user supplied.
    ///
    /// Its frame is where the drawing put it - 14.14 from the left, 3.54 from
    /// the bottom - rather than a standard margin. Those numbers are that
    /// file's own, and they were deliberately NOT carried onto A3.
    /// </summary>
    public static ConceptCoverLayout A4 { get; } =
        new("A4", 297.0, 210.0, 14.14, 3.54, 279.32, 202.95);

    /// <summary>
    /// A3 landscape, MEASURED off the drawing the owner supplied.
    ///
    /// 🔴 THESE NUMBERS REPLACE A SET THAT WAS DERIVED RATHER THAN MEASURED, AND
    /// THE DERIVATION WAS WRONG. The earlier A3 margins - 15 at the binding edge,
    /// 400 wide - were reasoned out from the A4 sheet and from a wish to match the
    /// older cover bound beside it. The owner then supplied a real A3 drawing and
    /// it was measured (_shared/concept-cover-A3-2026-09-11.json, from
    /// concept-cover-A3-reference-2026-09-11.dwg): the frame is the standard
    /// 20/5/5/5, 395 x 287. An argument does not outrank a measurement.
    ///
    /// The measurement also settled what «scale хийхгүй» meant, and the answer was
    /// BOTH: the page and the frame really are the A4 sheet times sqrt(2), while
    /// the signature tables are not enlarged at all - their millimetres are
    /// identical on the two sheets. Half of the old guess was right, which is
    /// exactly why it survived as long as it did.
    /// </summary>
    public static ConceptCoverLayout A3 { get; } =
        new("A3", 420.0, 297.0, 20.0, 5.0, 395.0, 287.0);

    public double FrameRightMm => FrameLeftMm + FrameWidthMm;

    public double FrameTopMm => FrameBottomMm + FrameHeightMm;

    /// <summary>
    /// The vertical division between the left and right tables: the frame's
    /// exact centre, on either sheet.
    /// </summary>
    public double TablesMiddleMm => FrameLeftMm + FrameWidthMm / 2;

    /// <summary>
    /// Width of ONE of the two tables. The SAME on every sheet, and centred in
    /// the frame - see <see cref="ConceptCoverSheetGrid.TableWidthMm"/> for the
    /// measurement that replaced the widening rule this used to apply.
    /// </summary>
    public double TableWidthMm => ConceptCoverSheetGrid.TableWidthMm;

    public double TablesLeftMm => TablesMiddleMm - TableWidthMm;

    public double TablesRightMm => TablesMiddleMm + TableWidthMm;

    /// <summary>Bottom edge of the ГҮЙЦЭТГЭГЧ / ЗАХИАЛАГЧ pair.</summary>
    public double LowerBottomMm =>
        FrameBottomMm + ConceptCoverSheetGrid.SignatureBlockAboveFrameMm;

    public double LowerTopMm => LowerBottomMm + ConceptCoverSheetGrid.LowerTableHeightMm;

    /// <summary>Bottom edge of the ЗӨВШИЛЦСӨН / ХЯНАСАН pair.</summary>
    public double UpperBottomMm => LowerTopMm + ConceptCoverSheetGrid.GapBetweenTablesMm;

    public double UpperTopMm => UpperBottomMm + ConceptCoverSheetGrid.UpperTableHeightMm;

    /// <summary>
    /// The position column of the upper pair - the remainder once the two
    /// hand-written columns are taken out.
    ///
    /// THIS IS WHERE A WIDER PAGE GOES. «Нэр» and «Гарын үсэг» are sized for a
    /// person writing in them by hand, so they stay at 25 mm on any sheet;
    /// widening them would buy nothing and shrink the only column whose content
    /// is a sentence.
    /// </summary>
    public double UpperRoleColumnMm =>
        TableWidthMm - ConceptCoverSheetGrid.NameColumnMm - ConceptCoverSheetGrid.SignatureColumnMm;

    /// <summary>The same remainder in the lower pair, after the logo cell.</summary>
    public double LowerRoleColumnMm =>
        TableWidthMm -
        ConceptCoverSheetGrid.LogoColumnMm -
        ConceptCoverSheetGrid.NameColumnMm -
        ConceptCoverSheetGrid.SignatureColumnMm;

    /// <summary>
    /// The clear space above the tables, which is where the title, the location
    /// line and the stage line are drawn.
    ///
    /// On a taller sheet ALL of the extra height arrives here, because the
    /// signature block is anchored to the bottom. That is Master's decision M2
    /// and it is reversible: anchoring to the top instead would move the extra
    /// space under the tables and leave the title crowded against the frame.
    /// </summary>
    public double FreeAreaAboveMm => FrameTopMm - UpperTopMm;

    /// <summary>
    /// Row boundaries of the upper table's body, from its top edge downwards.
    /// The division itself is the sheet-independent rule in
    /// <see cref="ConceptCoverSheetGrid.UpperRowHeights"/>.
    /// </summary>
    public IReadOnlyList<double> UpperRowBoundaries(int rowCount)
    {
        var boundaries = new List<double> { UpperTopMm - ConceptCoverSheetGrid.UpperHeaderHeightMm };
        foreach (double height in ConceptCoverSheetGrid.UpperRowHeights(rowCount))
            boundaries.Add(boundaries[^1] - height);
        return boundaries;
    }
}
