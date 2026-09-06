namespace ErkS.Platform.Core;

/// <summary>
/// Where a line of text on the 2026 concept cover actually sits.
///
/// 🔴 THE NUMBER IS A CENTRE, NOT A BASELINE, and it was called
/// <c>baselineMm</c> for as long as nobody had to do arithmetic with it.
///
/// The box handed to the text renderer spans <c>[c - h, c + h]</c> and the glyph
/// is centred inside it, so the value positions the MIDDLE of the writing. The
/// name said otherwise. Nothing had gone wrong yet, and the reason is pure
/// luck: the topmost and bottommost lines of the title block happen to be the
/// same height, so a centre-based and a baseline-based reading of the block's
/// extent give the same answer to three decimals. Master's -3.06 mm offset was
/// computed through that coincidence and is correct - and would have been wrong
/// by half the height difference the moment those two lines differed.
///
/// So the fix is not the name. The name is now honest, and the meaning lives
/// here where it can be asked a question.
/// </summary>
public static class ConceptCoverTextBox
{
    /// <summary>
    /// The vertical extent of a line of text: its ink, in millimetres from the
    /// bottom of the sheet.
    /// </summary>
    /// <param name="centreYMm">Where the middle of the writing sits.</param>
    /// <param name="capHeightMm">
    /// The height the sheet asks for - the contract's millimetres are cap
    /// heights, matching the ratio the cover writer already uses.
    /// </param>
    public static (double BottomMm, double TopMm) Extent(double centreYMm, double capHeightMm) =>
        (centreYMm - capHeightMm / 2, centreYMm + capHeightMm / 2);

    /// <summary>
    /// The box the renderer is given: twice the height, so a line that wraps has
    /// somewhere to wrap into, still centred on <paramref name="centreYMm"/>.
    /// </summary>
    public static (double BottomMm, double TopMm) DrawBox(double centreYMm, double capHeightMm) =>
        (centreYMm - capHeightMm, centreYMm + capHeightMm);

    /// <summary>
    /// The middle of a block of lines, by ink rather than by the numbers the
    /// lines were positioned with.
    ///
    /// This is the calculation Master's title-block offset rests on. Having it
    /// here means the offset can be recomputed rather than remembered - and
    /// that it stays right if a line is ever added at a different size.
    /// </summary>
    public static double BlockCentreMm(IEnumerable<(double CentreYMm, double CapHeightMm)> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        double bottom = double.MaxValue;
        double top = double.MinValue;
        foreach ((double centre, double height) in lines)
        {
            (double lineBottom, double lineTop) = Extent(centre, height);
            bottom = Math.Min(bottom, lineBottom);
            top = Math.Max(top, lineTop);
        }

        if (bottom > top)
            throw new ArgumentException("A block needs at least one line.", nameof(lines));

        return (bottom + top) / 2;
    }
}
