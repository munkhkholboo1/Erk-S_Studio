namespace ErkS.Platform.Core;

/// <summary>
/// The part of a card the drawing actually occupies.
///
/// 🔴 THE WRITER HELD THESE PRIVATELY AND THAT WAS FINE UNTIL SOMETHING ELSE HAD TO
/// AGREE WITH IT. Working out how many pixels a card needs means working out the frame
/// it lands in, and the frame is the cell less this reserve. A private copy in the
/// budget would be the second home - and the second home is always the one that stops
/// being updated.
/// </summary>
public static class BoardCardFrame
{
    /// <summary>The strip the caption is written in.</summary>
    public const double CaptionBandMm = 8;

    /// <summary>Clear space between the drawing and its caption.</summary>
    public const double CaptionGapMm = 2;

    /// <summary>What a caption costs the drawing, band and gap together.</summary>
    public static double CaptionReserveMm => CaptionBandMm + CaptionGapMm;

    /// <summary>
    /// The card area above its caption, in millimetres.
    ///
    /// ⚠ A CELL SHORTER THAN ITS OWN CAPTION RESERVE COMES BACK AT ZERO HEIGHT rather
    /// than being floored here. The writer has its own floor of one point for what it
    /// draws into; a second, differently-sized floor in this file would be two answers
    /// to one degenerate case. Zero reaches <see cref="VisualizationRasterBudget"/>,
    /// which answers «no work» - the source goes in untouched, which is the only answer
    /// that cannot be too soft.
    /// </summary>
    public static BoardRectMm ContentAbove(BoardRectMm cell, bool hasCaption) =>
        hasCaption
            ? new BoardRectMm(
                cell.LeftMm,
                cell.TopMm,
                cell.WidthMm,
                Math.Max(0d, cell.HeightMm - CaptionReserveMm))
            : cell;
}

/// <summary>
/// How many pixels a board card needs, under the owner's raster rule.
///
/// 🔴 THE THIRD WRITER, AND THE BOARD ALREADY HELD HALF THIS RULE. 300 DPI has been in
/// <see cref="BoardCardMeasurements"/> since the boards were built - but only as a
/// FLOOR: <see cref="BoardCardMeasurements.IsSharpEnough"/> answers «has this render
/// enough pixels», and it is asked of the person preparing artwork by hand. Nothing
/// answered «has it too many», so a 16k render placed on a card went onto the sheet
/// whole. Same density, opposite direction, and only one of the two directions existed.
///
/// 🔴 THE CROP IS WHAT MAKES THIS DIFFERENT FROM THE OTHER TWO, AND IT IS THE DIRECTION
/// THAT LOOKS LIKE NOTHING. A card shows a FRACTION of its source: the writer scales
/// the source until the cropped part fills the card and clips the rest away, so the
/// whole source is drawn 1/crop times larger than the card. Measuring the card and
/// stopping there would reduce a card showing a quarter of its source to a quarter of
/// the density it needs - and a soft card on a board printed a metre across is visible
/// to everyone standing in front of it.
///
/// The arithmetic that follows from that: fitting the visible fraction into the card is
/// the same as fitting the WHOLE source into a frame of card ÷ crop. So the crop does
/// not change the rule, it enlarges the frame - which is why this asks
/// <see cref="VisualizationRasterBudget"/> the ordinary question rather than growing a
/// second density calculation.
/// </summary>
public static class BoardRasterBudget
{
    /// <summary>
    /// What to prepare for one card.
    ///
    /// A full-bleed card COVERS its cell, so what hangs off the edges still has to
    /// exist at full density; every other layout fits inside it. That asymmetry is the
    /// reason the layout is passed through rather than assumed.
    /// </summary>
    public static VisualizationRasterPlan For(
        BoardRectMm cell,
        bool hasCaption,
        string? layout,
        double cropWidth,
        double cropHeight,
        int sourcePixelWidth,
        int sourcePixelHeight)
    {
        BoardRectMm content = BoardCardFrame.ContentAbove(cell, hasCaption);

        // ⚠ AN UNUSABLE CROP MEANS NO CARD AT ALL, NOT A CARD TO GUESS AT. The writer
        // refuses a crop it cannot place and warns instead of drawing, so there is
        // nothing on the sheet to be soft. Leaving the file untouched is the honest
        // answer and the only one that cannot make the board worse.
        if (!IsUsableFraction(cropWidth) || !IsUsableFraction(cropHeight))
        {
            return new VisualizationRasterPlan(
                Math.Max(1, sourcePixelWidth),
                Math.Max(1, sourcePixelHeight),
                Resample: false);
        }

        // A crop wider than the whole source shrinks the drawn source rather than
        // enlarging it, so holding the divisor at one asks for MORE pixels than such a
        // card needs. That is the safe side of a value nobody should be able to store.
        double across = Math.Min(1d, cropWidth);
        double down = Math.Min(1d, cropHeight);

        var frame = new PageRectMm
        {
            X = content.LeftMm,
            Y = content.TopMm,
            Width = content.WidthMm / across,
            Height = content.HeightMm / down,
        };

        bool covers = (layout ?? "").Trim()
            .Equals(ProjectPortfolioLayouts.FullBleed, StringComparison.OrdinalIgnoreCase);

        return VisualizationRasterBudget.For(
            sourcePixelWidth,
            sourcePixelHeight,
            frame,
            covers ? VisualizationImageFitMode.CenterCrop : VisualizationImageFitMode.Contain);
    }

    private static bool IsUsableFraction(double value) => double.IsFinite(value) && value > 0d;
}
