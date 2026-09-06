using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// The number that positions a line of text on the 2026 cover, and the
/// arithmetic that rests on it.
///
/// It was named <c>baselineMm</c> and it is a CENTRE. Nothing had gone wrong,
/// which is the interesting part: the block's topmost and bottommost lines
/// happen to be the same height, so both readings of "where is the block" agree
/// to three decimals. The name carried a wrong assumption that was harmless
/// only by coincidence - and the coincidence is not written down anywhere, so
/// the next line added at a different size would have quietly broken a number
/// somebody had already used.
/// </summary>
public sealed class ConceptCoverTextBoxTests
{
    private const double Tolerance = 0.001;

    /// <summary>
    /// The title block as the sheet draws it: the value positioning each line
    /// and the height it is drawn at.
    /// </summary>
    private static readonly (double CentreYMm, double CapHeightMm)[] TitleBlockA4 =
    [
        (188.49, 2.475),   // БАТЛАВ:
        (178.97, 2.475),   // the approver
        (149.18, 2.475),   // the site address
        (130.00, 8.000),   // the project title
        (116.88, 2.475),   // /ЗАГВАР ЗУРАГ/
    ];

    [Fact]
    public void TheNumberIsTheMIDDLEOfTheWriting()
    {
        (double bottom, double top) = ConceptCoverTextBox.Extent(100.0, 4.0);

        Assert.Equal(98.0, bottom, Tolerance);
        Assert.Equal(102.0, top, Tolerance);
        Assert.Equal(100.0, (bottom + top) / 2, Tolerance);

        // The box the renderer gets is twice as tall so a long line can wrap -
        // and still centred on the same value, which is why the two agree.
        (double boxBottom, double boxTop) = ConceptCoverTextBox.DrawBox(100.0, 4.0);
        Assert.Equal((boxBottom + boxTop) / 2, (bottom + top) / 2, Tolerance);
    }

    [Fact]
    public void MASTERSMinus306CanBeRECOMPUTEDRatherThanRemembered()
    {
        // The offset the A3 title placement rests on, derived here instead of
        // carried in a message. If a line is ever added to the block at a
        // different size, this moves - and that is the point: the constant is a
        // measurement of the block, not a property of the sheet.
        double blockCentre = ConceptCoverTextBox.BlockCentreMm(TitleBlockA4);
        double freeAreaCentre =
            (ConceptCoverLayout.A4.UpperTopMm + ConceptCoverLayout.A4.FrameTopMm) / 2;

        Assert.Equal(152.685, blockCentre, Tolerance);
        Assert.Equal(155.745, freeAreaCentre, Tolerance);
        Assert.Equal(-3.060, blockCentre - freeAreaCentre, Tolerance);
    }

    [Fact]
    public void ITWouldHaveBeenWrongHadTheEndLinesDIFFEREDInSize()
    {
        // Why the rename mattered even though no number was wrong. Read as
        // baselines, the block's extent is 116.88..188.49 and its middle
        // 152.685 - the same answer, ONLY because the first and last lines are
        // both 2.475 tall. Make the top line bigger and the two readings part
        // company immediately.
        (double CentreYMm, double CapHeightMm)[] taller =
        [
            (188.49, 8.000),
            (116.88, 2.475),
        ];

        double byInk = ConceptCoverTextBox.BlockCentreMm(taller);
        double byPositioningNumbers = (188.49 + 116.88) / 2;

        Assert.NotEqual(byInk, byPositioningNumbers, precision: 2);
        Assert.Equal(1.381, byInk - byPositioningNumbers, Tolerance);
    }

    [Fact]
    public void ABlockOfONELineIsCentredOnThatLine()
    {
        Assert.Equal(50.0, ConceptCoverTextBox.BlockCentreMm([(50.0, 3.0)]), Tolerance);
    }

    [Fact]
    public void ANEmptyBlockIsRefusedRatherThanAnswered()
    {
        // There is no middle of nothing. Returning zero would put a caller's
        // text at the bottom of the sheet and look like a placement bug.
        Assert.Throws<ArgumentException>(() => ConceptCoverTextBox.BlockCentreMm([]));
    }
}
