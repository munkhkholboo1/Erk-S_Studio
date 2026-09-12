namespace ErkS.Platform.Core;

/// <summary>
/// One line of the 2026 cover's title block, as measured on A4.
/// </summary>
/// <param name="CentreYMm">
/// The MIDDLE of the writing, not a baseline - see <see cref="ConceptCoverTextBox"/>.
/// </param>
/// <param name="AnchorIsLeftEdge">
/// True when <see cref="ConceptCoverTitleLine.CentreXMm"/> is the LEFT edge of the
/// writing rather than its middle.
///
/// 🔴 FIXED LABELS AND REPLACED TEXT WANT DIFFERENT ANCHORS, AND THE DRAWING
/// SAYS WHICH. Every line in the reference is a left insertion point. For text the
/// project replaces - an approver, an address, the building's name - a fixed left
/// edge would let a long name run off the sheet, so the measured point is used as
/// the axis to centre on; that is what A4 has always done. For a label that never
/// changes («БАТЛАВ:», «/ЗАГВАР ЗУРАГ/») centring on the placeholder's START moves
/// it left by half its own width - about 6.5 mm and 9 mm. Those two are anchored
/// where the drawing puts them.
///
/// ⚠ Defaults to false so <see cref="ConceptCoverTitleBlock.MeasuredOnA4"/> keeps
/// the shape it shipped with, including that displacement.
/// </param>
public sealed record ConceptCoverTitleLine(
    string Key,
    double CentreXMm,
    double CentreYMm,
    double WidthMm,
    double CapHeightMm,
    bool AnchorIsLeftEdge = false);

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

    /// <summary>
    /// The lines as MEASURED ON THE A3 DRAWING the owner supplied - the sheet the
    /// album actually prints (_shared/concept-cover-A3-full-2026-09-12.json, 75
    /// objects, from concept-cover-A3-reference-2026-09-11.dwg).
    ///
    /// 🔴 WHY THIS LIST EXISTS AT ALL: the A3 sheet was being placed by taking
    /// A4's numbers and shifting them. The owner saw it and said so - «А4 дээрх
    /// байрлалаар А3 дээр тавьчихсан» - and PFA then proved the derivation could
    /// never have worked: the lines do not land on A4 × sqrt(2) either, X differing
    /// by up to 27 mm. There was no right answer available without a measurement.
    ///
    /// 🔴 AND ONE OF A4'S FIVE VALUES WAS NEVER MEASURED, WHICH IS WHY THE NAME
    /// «MeasuredOnA4» STOPPED BEING TRUSTED. The reference draws the title as TWO
    /// placeholder lines - «ТӨЛӨВЛӨЖ БУЙ БАРИЛГЫН» at (83.11, 136.45) and «НЭР,
    /// ТӨРӨЛ» at (119.62, 125.01) - and the product holds ONE entry at (148.5,
    /// 130.0). 148.5 is A4's PAGE centre, not its frame centre (153.8); 130.0 sits
    /// between the two baselines. Collapsing a two-line placeholder into one wrapped
    /// line is the RIGHT decision - a real project name is one string and finds its
    /// own second line - but the position was chosen rather than measured, and
    /// nothing said so. Recorded here, 2026-09-12, so the next reader does not take
    /// the word «Measured» for a provenance.
    ///
    /// 🔴 THE TITLE IS THEREFORE PLACED BY RULE, NOT BY A COPIED NUMBER: the
    /// centre of the two placeholder lines' ink, on the FRAME's axis. The frame is
    /// the axis the sheet states exactly for the footer, and using the page's would
    /// be inheriting A4's unexplained 5.3 mm.
    /// </summary>
    /// <summary>
    /// The drawing gives a BASELINE; this product positions the MIDDLE of the writing.
    ///
    /// 🔴 ONE LINE, ONE CONVERSION, WRITTEN DOWN ONCE. The box drawn for a line
    /// spans [c - h, c + h] with the glyph centred in it, so the middle of a glyph
    /// standing on baseline b with cap height h is b + h/2. Adding the halves by hand
    /// in a list of five is how one of them ends up 0.00005 mm out - which is exactly
    /// what happened while writing this, and a rounding slip is the friendly version.
    /// The unfriendly version is forgetting the conversion: body text lands 1.24 mm
    /// high and the 8 mm title 4 mm, both of which read as «nearly right».
    /// </summary>
    public static double CentreFromBaselineMm(double baselineMm, double capHeightMm) =>
        baselineMm + (capHeightMm / 2);

    /// <summary>
    /// The title's axis: the frame's horizontal centre, 217.5 mm on A3.
    ///
    /// Computed from the sheet rather than written down, so a frame change carries it.
    /// </summary>
    public static double A3TitleCentreXMm { get; } = ConceptCoverLayout.A3.TablesMiddleMm;

    /// <summary>
    /// The title's height: the middle of the ink of the two placeholder lines -
    /// [180.4316, 199.8663], so 190.14895 mm.
    ///
    /// 🔴 THE CONVENTION WAS TAKEN FROM A4 EMPIRICALLY, not invented here: A4's
    /// own placeholders span [125.01, 144.45], whose middle is 134.73, and the
    /// product placed its single line at 130.0 - 4.73 mm lower. So A4 does not follow
    /// this convention either; it is recorded rather than reproduced, because
    /// reproducing an unexplained offset is how one sheet's accident becomes two.
    /// </summary>
    public static double A3TitleCentreYMm { get; } =
        (180.4316 + (191.8663 + 8.0)) / 2;

    /// <summary>
    /// The lines as MEASURED ON THE A3 DRAWING, with the baselines converted once.
    ///
    /// ⚠ DECLARED AFTER THE VALUES IT USES, AND A TEST HOLDS IT THERE. Static
    /// initialisers run in declaration order, so when this list sat above
    /// <see cref="A3TitleCentreXMm"/> the title was built with both of them still
    /// zero - the title would have been drawn off the sheet, silently, and nothing
    /// about the code looked wrong. A test asserting the numbers caught it.
    /// </summary>
    public static IReadOnlyList<ConceptCoverTitleLine> MeasuredOnA3 { get; } =
    [
        // Fixed labels: anchored where the drawing starts them.
        new(
            ApprovalLabel,
            207.4834,
            CentreFromBaselineMm(278.0737, BodyCapMm),
            60.0,
            BodyCapMm,
            AnchorIsLeftEdge: true),
        new(
            StageLine,
            202.8031,
            CentreFromBaselineMm(172.2992, BodyCapMm),
            90.0,
            BodyCapMm,
            AnchorIsLeftEdge: true),

        // Replaced by the project: the measured point is the axis to centre on.
        new(Approver, 245.9491, CentreFromBaselineMm(268.5545, BodyCapMm), 90.0, BodyCapMm),
        new(SiteAddress, 168.0716, CentreFromBaselineMm(204.5968, BodyCapMm), 200.0, BodyCapMm),
        new(ProjectTitle, A3TitleCentreXMm, A3TitleCentreYMm, 230.0, 8.0),
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
        ArgumentNullException.ThrowIfNull(layout);

        // 🔴 THE SHEET THAT IS ACTUALLY DRAWN USES ITS OWN MEASUREMENT. Shifting
        // A4's numbers is what put the text in the wrong place, and PFA's count shows
        // no shift could have fixed it. The shift below stays for any sheet that has
        // no measurement of its own - there are none today - because deleting it would
        // leave such a sheet with nothing at all.
        if (layout.Name.Equals(ConceptCoverLayout.A3.Name, StringComparison.Ordinal))
            return MeasuredOnA3;

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
