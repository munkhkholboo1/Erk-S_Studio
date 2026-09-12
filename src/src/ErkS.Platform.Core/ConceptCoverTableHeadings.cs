namespace ErkS.Platform.Core;

/// <summary>
/// Where a role label sits relative to the table it names.
///
/// 🔴 RELATIVE TO THE TABLE, NOT TO THE PAGE, AND THAT IS A DECISION WITH A REASON.
/// The drawing's absolute anchors cannot be used: STU centres the signature pair on the
/// frame's centre line while the reference has it 2.6434 mm to the left, and PFA recorded
/// that the reference does NOT fix the position - «Байрлалыг эх зураг ТОГТООХГҮЙ — STU
/// зангуугаа ЗОРИУД сонгоно». Absolute anchors would therefore put each label 2.4 mm
/// left of the very table edge it is meant to line up with. Offsets from the table's own
/// corner carry the measurement and survive the decision.
/// </summary>
/// <param name="LeftOffsetMm">From the table's own left edge to the start of the writing.</param>
/// <param name="TopAboveTableMm">
/// From the table's top edge up to the TOP of the writing - which is what a top-left
/// anchored MTEXT records.
/// </param>
/// <param name="CapHeightMm">The height the label is drawn at.</param>
public readonly record struct ConceptCoverRoleLabelPlacement(
    double LeftOffsetMm,
    double TopAboveTableMm,
    double CapHeightMm)
{
    /// <summary>
    /// The MIDDLE of the writing, measured up from the table's top edge.
    ///
    /// ⚠ A NAMED CONVERSION RATHER THAN FOUR HAND-SUMS. The title block's centre was
    /// worked out by hand and came out 0.00005 mm wrong; the repair was to give the
    /// arithmetic one home. The measurement records a top and the writer positions a
    /// middle, so the step between them exists exactly once.
    /// </summary>
    public double CentreAboveTableMm => TopAboveTableMm - (CapHeightMm / 2);
}

/// <summary>
/// What the signature tables' header rows actually contain, and where the four role
/// labels sit.
///
/// 🔴 THE HEADER ROW WAS HOLDING THE WRONG TEXT, AND THE WRONG TEXT IS WHY IT LOOKED
/// FULL. The album drew ЗӨВШИЛЦСӨН across the whole 8 mm row, bold and centred. The
/// reference drawing puts three column headings there - «Албан тушаал | Нэр | Гарын
/// үсэг», one per column, with the column rules running the table's full 48 mm - and
/// puts the role label ABOVE the table, outside it. PFA's classification states it
/// plainly: the four labels are in sheetTexts and not tableTexts «because they really do
/// sit outside the table».
///
/// 🔴 SO ONE DEFECT HID TWO MORE. The label in the wrong place made the row look
/// occupied, so nobody asked what belonged in it; and because a merged row needs no
/// dividers, the dividers stopping short looked deliberate too.
///
/// ⚠ WHAT IS MEASURED AND WHAT IS STU'S: the band, the column each heading belongs to,
/// the four label anchors and every height are measured. A heading's placement WITHIN
/// its column is NOT - the headings are MTEXT, and AutoCAD's textbox returns nil on
/// MTEXT, so PFA could not measure their extents and «centred» cannot be tested against
/// the drawing. The reference's own left and right tables disagree by 12.8 mm on the
/// same heading, which is hand placement rather than a rule. They are therefore centred
/// in their columns, as every other cell on this sheet is - a choice, stated as one.
/// Vertically that lands within 0.36 mm of the measured text centres.
///
/// Source: _shared/concept-cover-A3-full-2026-09-12.json (75 objects) and
/// _shared/concept-cover-A3-text-extents-2026-09-12.json, both from
/// concept-cover-A3-reference-2026-09-11.dwg.
/// </summary>
public static class ConceptCoverTableHeadings
{
    /// <summary>The position column's heading, on three of the four tables.</summary>
    public const string Position = "Албан тушаал";

    /// <summary>
    /// The client's table says this instead, because a client can be a private person.
    /// Measured, not reasoned: it is the one heading the reference spells differently,
    /// and it is the same distinction <see cref="ProjectClientTypes"/> already carries.
    /// </summary>
    public const string PositionOrCitizen = "Албан тушаал / Иргэн";

    public const string PersonName = "Нэр";

    public const string Signature = "Гарын үсэг";

    /// <summary>
    /// The height every heading is drawn at.
    ///
    /// ⚠ DERIVED FROM THE RAW TEXT, NOT READ OFF THE MEASURED heightMm. Three of the six
    /// are stored at 3.0 mm with an inline «\H0.86667x» run - 3.0 × 0.86667 = 2.6 - and
    /// the other three at 2.6 with no scale. Trusting the stored height alone would draw
    /// half of them 15% too large, with the measurement file looking like the authority
    /// for it.
    /// </summary>
    public const double CapHeightMm = 2.6;

    /// <summary>ЗӨВШИЛЦСӨН, above the upper-left table.</summary>
    public static ConceptCoverRoleLabelPlacement Concurring { get; } = new(0.0982, 3.8745, 2.6);

    /// <summary>
    /// ХЯНАСАН, above the upper-right table.
    ///
    /// ⚠ 2.5 mm, A TENTH SMALLER THAN ITS THREE SIBLINGS, AND KEPT THAT WAY. Rounding
    /// the four heights to one number would be tidier and would be a value nobody
    /// measured.
    /// </summary>
    public static ConceptCoverRoleLabelPlacement Reviewing { get; } = new(0.0766, 3.8574, 2.5);

    /// <summary>ГҮЙЦЭТГЭГЧ, above the lower-left table.</summary>
    public static ConceptCoverRoleLabelPlacement Performing { get; } = new(1.3991, 5.5298, 2.6);

    /// <summary>ЗАХИАЛАГЧ, above the lower-right table.</summary>
    public static ConceptCoverRoleLabelPlacement Commissioning { get; } = new(2.3109, 5.5298, 2.6);
}
