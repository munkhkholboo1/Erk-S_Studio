namespace ErkS.Platform.Core;

/// <summary>
/// One line of the 2026 cover's title block, as measured on A4.
/// </summary>
/// <param name="CentreYMm">
/// The MIDDLE of the writing, not a baseline - see <see cref="ConceptCoverTextBox"/>.
/// </param>
public sealed record ConceptCoverTitleLine(
    string Key,
    double CentreXMm,
    double CentreYMm,
    double WidthMm,
    double CapHeightMm);

/// <summary>
/// The block of lines above the tables, and where it sits on a given sheet.
///
/// 🔴 WHAT COUNTS AS «THE BLOCK» IS WRITTEN DOWN HERE, and that is the point of
/// the type rather than a detail of it.
///
/// The block's offset from the centre of the free area was measured at -3.06 mm
/// and then used to place the title on A3. Measuring it again while leaving out
/// the one line that sits just above the tables gives +7.12 mm - a different
/// sign. So the number is not a property of the drawing; it is a property of the
/// drawing PLUS a definition, and carrying it around without the definition
/// makes it look like an observation when it is half an opinion.
///
/// THE DEFINITION: every line of text that is DRAWN above the tables' top edge.
/// The template's instruction notes and the ҮЛГЭРЧИЛСЭН ЗАГВАР watermark are not
/// drawn at all, so they are not in it; the footer is below the tables, so it is
/// not either. That is the whole rule, and the list below is its enumeration.
///
/// WHERE THE BLOCK GOES ON A BIGGER SHEET. The cover Studio has always drawn -
/// itself A3 - centres its own block in its own free area, 2.65% below centre in
/// a 130 mm band. It does NOT hang the block off the top of the tables. So this
/// one is centred too, less the offset measured on A4, which leaves A4 exactly
/// where it is and puts A3 where its predecessor puts its own.
/// </summary>
public static class ConceptCoverTitleBlock
{
    public const string ApprovalLabel = "approval-label";
    public const string Approver = "approver";
    public const string SiteAddress = "site-address";
    public const string ProjectTitle = "project-title";
    public const string StageLine = "stage-line";

    private const double BodyCapMm = 2.475;

    /// <summary>
    /// The lines, with the positions measured off the user's DWG. On A4 they are
    /// used unchanged; on any other sheet they are shifted, never resized.
    /// </summary>
    public static IReadOnlyList<ConceptCoverTitleLine> MeasuredOnA4 { get; } =
    [
        new(ApprovalLabel, 159.75, 188.49, 60.0, BodyCapMm),
        new(Approver, 184.65, 178.97, 90.0, BodyCapMm),
        new(SiteAddress, 141.47, 149.18, 200.0, BodyCapMm),
        new(ProjectTitle, 148.5, 130.0, 230.0, 8.0),
        new(StageLine, 141.51, 116.88, 90.0, BodyCapMm),
    ];

    /// <summary>The middle of the block's ink on A4 - 152.685 mm.</summary>
    public static double A4CentreMm { get; } =
        ConceptCoverTextBox.BlockCentreMm(
            MeasuredOnA4.Select(line => (line.CentreYMm, line.CapHeightMm)));

    /// <summary>
    /// How far below the middle of the free area the block sits, measured on A4:
    /// -3.06 mm.
    ///
    /// COMPUTED, not written down. If a line is added or a height changes, this
    /// follows - which is the only way it can stay true, since it is a
    /// measurement of the block rather than a property of the sheet.
    ///
    /// Applied as a CONSTANT millimetre offset on other sheets rather than as a
    /// percentage of the band. Master's reasoning, and it is a judgement rather
    /// than a finding: the evidence cannot separate the two, because on the old
    /// cover's band the two rules differ by 0.85 mm - less than the precision
    /// with which anybody places a line of text. Treating 3 mm as a proportion
    /// would mean deciding it MEANT something and then multiplying that meaning
    /// by every future page size.
    /// </summary>
    public static double OffsetFromFreeAreaCentreMm { get; } =
        A4CentreMm - FreeAreaCentreMm(ConceptCoverLayout.A4);

    /// <summary>The middle of the clear space between the tables and the frame.</summary>
    public static double FreeAreaCentreMm(ConceptCoverLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        return (layout.UpperTopMm + layout.FrameTopMm) / 2;
    }

    /// <summary>
    /// How far the whole block moves on this sheet. Exactly zero on A4, by
    /// construction rather than by luck: the offset above was measured against
    /// A4's own free area, so putting it back lands on the same millimetre.
    /// </summary>
    public static double VerticalShiftMm(ConceptCoverLayout layout) =>
        FreeAreaCentreMm(layout) + OffsetFromFreeAreaCentreMm - A4CentreMm;

    /// <summary>
    /// Horizontal shift: the block keeps its offset from the frame's centre, so
    /// it stays on the same axis as the tables it introduces.
    /// </summary>
    public static double HorizontalShiftMm(ConceptCoverLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        return layout.TablesMiddleMm - ConceptCoverLayout.A4.TablesMiddleMm;
    }

    /// <summary>
    /// The block as it should be drawn on <paramref name="layout"/>: same
    /// widths, same heights, moved.
    /// </summary>
    public static IReadOnlyList<ConceptCoverTitleLine> For(ConceptCoverLayout layout)
    {
        double dx = HorizontalShiftMm(layout);
        double dy = VerticalShiftMm(layout);
        return MeasuredOnA4
            .Select(line => line with
            {
                CentreXMm = line.CentreXMm + dx,
                CentreYMm = line.CentreYMm + dy,
            })
            .ToList();
    }
}
