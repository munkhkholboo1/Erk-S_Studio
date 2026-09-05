namespace ErkS.Platform.Core;

/// <summary>
/// Where the lines of the cover's approval table go, in millimetres from the
/// page's BOTTOM-LEFT corner - the convention both this table and the corner
/// table were measured in.
///
/// TWO SKINS, AND THE DISTINCTION IS THE POINT. One drawing routine has always
/// produced both the concept album's cover and the working-drawing cover, with
/// a flag choosing the trim. That was harmless while both used one set of
/// numbers; it stopped being harmless on 2026-09-06, when the working-drawing
/// cover became Studio's job to reproduce exactly and the concept cover did
/// not.
///
/// The two sets AGREE on the table's outer bounds and disagree by 5 to 8 mm on
/// four interior columns. That is why nothing could see the difference: the
/// table looks correct either way, and its columns sit somewhere else.
///
/// WHY BOTH SETS ARE KEPT. Only one of them is a measurement: PFR read the
/// eight vertical rules off a cover Revit actually exported and every one
/// agrees with the working-drawing set to within 0.02 mm. The concept set has
/// no such provenance - it entered the file in an unrelated commit four days
/// AFTER that cover existed, and no exported concept cover exists to check it
/// against. The reading that fits the evidence is that it was a failed attempt
/// at the same drawing.
///
/// It is kept because the user was asked and answered on 2026-09-06 that the
/// concept cover's different split is deliberate. That is a decision, not a
/// measurement, and it is the only thing holding this set up - which is worth
/// knowing before anyone "corrects" it.
/// </summary>
/// <param name="TableLeft">The table's left edge.</param>
/// <param name="ReviewRoleRight">End of the left block's position column.</param>
/// <param name="ReviewNameRight">End of the left block's name column; a signature column follows.</param>
/// <param name="ReviewRight">End of the left block, and the start of the right one.</param>
/// <param name="CompanyLogoRight">End of the right block's logo cell.</param>
/// <param name="CompanyRoleRight">End of the right block's position column.</param>
/// <param name="CompanyNameRight">End of the right block's name column; a signature column follows.</param>
/// <param name="TableRight">The table's right edge.</param>
/// <param name="TableTop">The table's top edge.</param>
/// <param name="ColumnHeaderTop">Top of the column-header strip.</param>
/// <param name="ColumnHeaderBottom">Bottom of the column-header strip.</param>
/// <param name="RowBottom">The bottom edge the last signature row rests on.</param>
/// <param name="CityCenterY">Centre of the footer's city line.</param>
/// <param name="YearCenterY">Centre of the footer's year line.</param>
/// <param name="ReviewRowsTop">Top edge of the first review row.</param>
/// <param name="ReviewRowsSpan">Height the review rows divide between them.</param>
/// <param name="TitleTextHeight">Cap height of the project name.</param>
/// <param name="AddressTextHeight">Cap height of the site address line.</param>
/// <param name="FooterTextHeight">Cap height of the city and year lines.</param>
/// <param name="ShrinksReviewTextToFit">
/// Whether long review text is shrunk before the row is allowed to grow. Revit
/// shrinks to a floor and then lets the text overflow; Studio's concept cover
/// grows the row instead. The working cover does BOTH - see
/// <see cref="CoverReviewTextFitting"/> for why.
/// </param>
/// <remarks>
/// The footer lines are not part of the table, and they travel with it anyway:
/// they are measured from the same drawing and must follow the same skin. A
/// second selector for "the rest of the cover" is how two halves of one page
/// end up disagreeing about which cover they are drawing.
/// </remarks>
public sealed record CoverApprovalTableGrid(
    double TableLeft,
    double ReviewRoleRight,
    double ReviewNameRight,
    double ReviewRight,
    double CompanyLogoRight,
    double CompanyRoleRight,
    double CompanyNameRight,
    double TableRight,
    double TableTop,
    double ColumnHeaderTop,
    double ColumnHeaderBottom,
    double RowBottom,
    double CityCenterY,
    double YearCenterY,
    double ReviewRowsTop,
    double ReviewRowsSpan,
    bool ShrinksReviewTextToFit,
    double TitleTextHeight,
    double AddressTextHeight,
    double FooterTextHeight)
{
    /// <summary>
    /// The concept album's cover, unchanged, pinned by
    /// ConceptPageFormat_MatchesRevitSketchA3Geometry - a test whose NAME
    /// claims a Revit measurement that nothing in the history records. The user
    /// produces these albums today and confirmed the difference is deliberate.
    /// </summary>
    public static CoverApprovalTableGrid Concept { get; } = new(
        TableLeft: BuildingArchitectureConceptPageLayout.CoverTableLeftMm,
        ReviewRoleRight: BuildingArchitectureConceptPageLayout.CoverReviewRoleRightMm,
        ReviewNameRight: BuildingArchitectureConceptPageLayout.CoverReviewNameRightMm,
        ReviewRight: BuildingArchitectureConceptPageLayout.CoverProcessedLeftMm,
        CompanyLogoRight: BuildingArchitectureConceptPageLayout.CoverProcessedLogoRightMm,
        CompanyRoleRight: BuildingArchitectureConceptPageLayout.CoverProcessedRoleRightMm,
        CompanyNameRight: BuildingArchitectureConceptPageLayout.CoverProcessedNameRightMm,
        TableRight: BuildingArchitectureConceptPageLayout.CoverTableRightMm,
        TableTop: BuildingArchitectureConceptPageLayout.CoverTableTopMm,
        ColumnHeaderTop: 161.86,
        ColumnHeaderBottom: BuildingArchitectureConceptPageLayout.CoverColumnHeaderBottomMm,
        RowBottom: 93.86,
        CityCenterY: 26.125,
        YearCenterY: 15.625,
        ReviewRowsTop: BuildingArchitectureConceptPageLayout.CoverColumnHeaderBottomMm,
        ReviewRowsSpan: BuildingArchitectureConceptPageLayout.CoverReviewRowsBaseHeightMm,
        // The concept cover keeps full-size text and lets the row grow. Nobody
        // asked for it to change, and it has never overflowed.
        ShrinksReviewTextToFit: false,
        TitleTextHeight: BuildingArchitectureConceptPageLayout.CoverProjectNameTextHeightMm,
        AddressTextHeight: BuildingArchitectureConceptPageLayout.CoverBodyTextHeightMm,
        FooterTextHeight: BuildingArchitectureConceptPageLayout.CoverBodyTextHeightMm);

    /// <summary>
    /// The working-drawing cover, from
    /// _shared/cover-sheet-contract-2026-09-06.json. Revit drew this one until
    /// PFR deleted their cover code, so these numbers are not ours to choose -
    /// reproducing them is the whole requirement.
    ///
    /// Note TableTop: the contract's table reaches 169.86, eight millimetres
    /// above where the concept table stops. The concept skin calls 161.86 its
    /// top and draws headers above it anyway, which is the same band arrived at
    /// by a different name.
    /// </summary>
    public static CoverApprovalTableGrid WorkingDrawing { get; } = new(
        TableLeft: 68.275,
        ReviewRoleRight: 138.275,
        ReviewNameRight: 166.275,
        ReviewRight: 196.275,
        CompanyLogoRight: 226.275,
        CompanyRoleRight: 292.975,
        CompanyNameRight: 321.725,
        TableRight: 351.725,
        TableTop: 169.86,
        ColumnHeaderTop: 161.86,
        ColumnHeaderBottom: 153.86,
        RowBottom: 93.86,
        // work.Y0 + 55 and + 36, with work.Y0 = 10 on an A3 sheet bound on the
        // left. PFR confirmed these are box CENTRES, not bottoms - a 12 mm box
        // makes that a 6 mm difference, and the contract's own wording did not
        // say which until asked.
        CityCenterY: 65.0,
        YearCenterY: 46.0,
        // The left block's rows start one millimetre lower than the right
        // block's header and divide 59.00 mm evenly - measured off the exported
        // cover, where four rows fell exactly on 152.86 / 138.11 / 123.36 /
        // 108.61 / 93.86.
        ReviewRowsTop: 152.86,
        ReviewRowsSpan: 59.0,
        ShrinksReviewTextToFit: true,
        // Read off the exported cover and converted from the em sizes the PDF
        // carries: 8.47 / 2.82 / 5.29 mm em, which at Arial's cap ratio are
        // 5.8 / 2.0 / 3.8 - the contract's compact title, tiny and label. A3
        // landscape is compact by the contract's own rule (work 390 x 277 mm),
        // and the measurement agrees, which is what makes this a reading rather
        // than an assumption.
        TitleTextHeight: 5.8,
        AddressTextHeight: 2.0,
        FooterTextHeight: 3.8);

    /// <summary>Picks the grid by the skin being drawn, so the choice is made once.</summary>
    public static CoverApprovalTableGrid For(bool workingDrawing) =>
        workingDrawing ? WorkingDrawing : Concept;

    public double Width => TableRight - TableLeft;

    public double ReviewWidth => ReviewRight - TableLeft;

    public double CompanyWidth => TableRight - CompanyLogoRight;
}
