using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// The 2026 concept cover, from the DWG the user supplied and PFA measured.
///
/// Most of what is pinned here is a measurement. Three things are not, and each
/// is a deliberate departure from the drawing:
///
///   the left/right split      the drawing is 0.49 mm lopsided; that is a hand,
///                             not an intention
///   the logo column           likewise, 14.38 against 15.20
///   four rows and above       the drawing shows two and three only, so any
///                             fourth value would be invented either way
///
/// Marking which is which is the work. A reader who cannot tell a measurement
/// from a decision will eventually "fix" the decision back into the drawing's
/// own imprecision, and be sure they are restoring fidelity.
/// </summary>
public sealed class ConceptCoverSheetGridTests
{
    [Fact]
    public void ThePageAndFrameAreTheMeasuredOnes()
    {
        Assert.Equal(297.0, ConceptCoverSheetGrid.PageWidthMm, 2);
        Assert.Equal(210.0, ConceptCoverSheetGrid.PageHeightMm, 2);
        Assert.Equal(14.14, ConceptCoverSheetGrid.FrameLeftMm, 2);
        Assert.Equal(3.54, ConceptCoverSheetGrid.FrameBottomMm, 2);
        Assert.Equal(279.32, ConceptCoverSheetGrid.FrameWidthMm, 2);
        Assert.Equal(202.95, ConceptCoverSheetGrid.FrameHeightMm, 2);
    }

    [Fact]
    public void TheFrameStaysOnThePage()
    {
        Assert.True(
            ConceptCoverSheetGrid.FrameLeftMm + ConceptCoverSheetGrid.FrameWidthMm
                <= ConceptCoverSheetGrid.PageWidthMm,
            "the frame runs off the right edge");
        Assert.True(
            ConceptCoverSheetGrid.FrameBottomMm + ConceptCoverSheetGrid.FrameHeightMm
                <= ConceptCoverSheetGrid.PageHeightMm,
            "the frame runs off the top edge");
    }

    [Fact]
    public void TheTwoTablesAreEQUAL_WhichTheDrawingIsNot()
    {
        // The decision, stated as a test so it cannot be undone by accident.
        // 121.03 against 120.54 in the drawing; here both halves of 241.57.
        Assert.Equal(120.785, ConceptCoverSheetGrid.TableWidthMm, 3);
        Assert.Equal(153.555, ConceptCoverSheetGrid.TablesMiddleMm, 3);
        Assert.Equal(
            ConceptCoverSheetGrid.TablesRightMm - ConceptCoverSheetGrid.TablesLeftMm,
            ConceptCoverSheetGrid.TableWidthMm * 2,
            6);
    }

    [Fact]
    public void EveryColumnOfBothPairsAddsUpToItsTable()
    {
        // A column that does not add up overhangs the table's own edge, and on
        // a ruled form that reads as a broken drawing rather than a wrong number.
        double upper = ConceptCoverSheetGrid.UpperRoleColumnMm +
            ConceptCoverSheetGrid.NameColumnMm +
            ConceptCoverSheetGrid.SignatureColumnMm;
        double lower = ConceptCoverSheetGrid.LogoColumnMm +
            ConceptCoverSheetGrid.LowerRoleColumnMm +
            ConceptCoverSheetGrid.NameColumnMm +
            ConceptCoverSheetGrid.SignatureColumnMm;

        Assert.Equal(ConceptCoverSheetGrid.TableWidthMm, upper, 6);
        Assert.Equal(ConceptCoverSheetGrid.TableWidthMm, lower, 6);
    }

    [Fact]
    public void TheUpperTableIsFortyEightMillimetresWhateverTheRowCount()
    {
        // The drawing's two variants have two rows and three rows and are the
        // same height. Rows share a fixed body; they do not push the table down
        // into the pair below it.
        Assert.Equal(48.0, ConceptCoverSheetGrid.UpperTopMm - ConceptCoverSheetGrid.UpperBottomMm, 2);
        Assert.Equal(
            40.0,
            ConceptCoverSheetGrid.UpperTopMm - ConceptCoverSheetGrid.UpperHeaderHeightMm
                - ConceptCoverSheetGrid.UpperBottomMm,
            2);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(7)]
    public void TheRowsAlwaysFillTheBodyExactly(int rowCount)
    {
        IReadOnlyList<double> heights = ConceptCoverSheetGrid.UpperRowHeights(rowCount);

        Assert.Equal(rowCount, heights.Count);
        Assert.Equal(ConceptCoverSheetGrid.UpperBodyHeightMm, heights.Sum(), 6);
        Assert.All(heights, height => Assert.True(height > 0));
    }

    [Fact]
    public void TWOAndTHREERowsKeepTheDrawingsOwnUnEVENDivision()
    {
        // 40/3 is 13.33; the drawing says 16/12/12. Reproducing it is the point.
        // A later reader seeing an uneven split will want to "tidy" it, and this
        // test is the answer.
        Assert.Equal(new[] { 20.0, 20.0 }, ConceptCoverSheetGrid.UpperRowHeights(2));
        Assert.Equal(new[] { 16.0, 12.0, 12.0 }, ConceptCoverSheetGrid.UpperRowHeights(3));
        Assert.NotEqual(
            ConceptCoverSheetGrid.UpperRowHeights(3)[0],
            ConceptCoverSheetGrid.UpperRowHeights(3)[1]);
    }

    [Fact]
    public void FOURRowsAndAboveDivideEVENLY_WhichIsADecision()
    {
        // The drawing shows two and three. Anything past that had to be chosen,
        // and an even split is the one choice that cannot be mistaken for a
        // measurement somebody forgot to write down.
        Assert.All(
            ConceptCoverSheetGrid.UpperRowHeights(4),
            height => Assert.Equal(10.0, height, 6));
        Assert.All(
            ConceptCoverSheetGrid.UpperRowHeights(5),
            height => Assert.Equal(8.0, height, 6));
    }

    [Fact]
    public void TheRowBoundariesWalkDownFromTheHeaderToTheTablesFoot()
    {
        IReadOnlyList<double> boundaries = ConceptCoverSheetGrid.UpperRowBoundaries(3);

        Assert.Equal(97.0, boundaries[0], 2);
        Assert.Equal(81.0, boundaries[1], 2);
        Assert.Equal(69.0, boundaries[2], 2);
        Assert.Equal(ConceptCoverSheetGrid.UpperBottomMm, boundaries[^1], 2);
    }

    [Fact]
    public void TheLowerPairIsTwoRowsOfEightAndDoesNotVary()
    {
        Assert.Equal(16.0, ConceptCoverSheetGrid.LowerTopMm - ConceptCoverSheetGrid.LowerBottomMm, 2);
        Assert.Equal(8.0, ConceptCoverSheetGrid.LowerRowHeightMm, 2);
    }

    [Fact]
    public void TheTwoPairsDoNotOVERLAP()
    {
        Assert.True(
            ConceptCoverSheetGrid.LowerTopMm < ConceptCoverSheetGrid.UpperBottomMm,
            "the lower pair reaches into the upper one");
    }
}
