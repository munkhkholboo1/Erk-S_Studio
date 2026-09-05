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
/// table looks correct either way, and its columns sit somewhere else. It also
/// explains what looked at first like a contradiction between Studio's Revit
/// measurement and PFR's contract - they are measurements of two different
/// covers, and neither was wrong.
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
    double RowBottom)
{
    /// <summary>
    /// The concept album's cover, unchanged. Measured from the Revit sketch A3
    /// family and pinned by ConceptPageFormat_MatchesRevitSketchA3Geometry.
    /// The user produces these albums today and did not ask for them to move.
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
        RowBottom: 93.86);

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
        RowBottom: 93.86);

    /// <summary>Picks the grid by the skin being drawn, so the choice is made once.</summary>
    public static CoverApprovalTableGrid For(bool workingDrawing) =>
        workingDrawing ? WorkingDrawing : Concept;

    public double Width => TableRight - TableLeft;

    public double ReviewWidth => ReviewRight - TableLeft;

    public double CompanyWidth => TableRight - CompanyLogoRight;
}
