using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// The 2026 concept cover, from the DWG the user supplied and PFA measured.
///
/// Most of what is pinned here is a measurement. Three things are not, and each
/// is a deliberate departure from the drawing:
///
///   the divider          centred on the frame, which the drawing already was;
///                        its two halves were 0.49 mm apart, which the aim was
///                        not
///   the logo column      14.38 against 15.20 in the drawing, 15.0 here
///   the row division     always 40/N, so the drawing's 16/12/12 is NOT
///                        reproduced - see the test that says why
///
/// Marking which is which is the work. A reader who cannot tell a measurement
/// from a decision will eventually "fix" the decision back into the drawing's
/// own imprecision, and be sure they are restoring fidelity. This file has
/// already held the opposite of its third departure, so the danger is real.
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
    public void TheDividerSitsOnTheFramesCENTRE_WhichTheDrawingAlreadyDid()
    {
        // The measurement is what makes the decision safe rather than merely
        // tidy: the drawing's divider is already at 153.80, which is exactly
        // the frame's centre, while its halves come out 121.03 and 120.54. A
        // divider on centre with unequal halves is an aim that missed by half a
        // millimetre, not an intention that one side be wider.
        Assert.Equal(153.80, ConceptCoverSheetGrid.TablesMiddleMm, 3);
        Assert.Equal(120.785, ConceptCoverSheetGrid.TableWidthMm, 3);
        Assert.Equal(33.015, ConceptCoverSheetGrid.TablesLeftMm, 3);
        Assert.Equal(274.585, ConceptCoverSheetGrid.TablesRightMm, 3);

        // The measured total width is kept; only the edges move, by 0.245 mm.
        Assert.Equal(
            241.57,
            ConceptCoverSheetGrid.TablesRightMm - ConceptCoverSheetGrid.TablesLeftMm,
            3);
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
    public void EveryRowCountDividesEVENLY_IncludingTheOneTheDrawingDisagreesWith()
    {
        // A DELIBERATE DEPARTURE, and this file used to hold the opposite.
        // The drawing's three-row variant is 16/12/12, and reproducing it means
        // a lookup table per row count - which makes row height a discontinuous
        // function of the count, so adding one party jumps the whole table.
        // The even split is continuous, and at two rows it matches the drawing
        // exactly.
        Assert.Equal(new[] { 20.0, 20.0 }, ConceptCoverSheetGrid.UpperRowHeights(2));
        Assert.All(
            ConceptCoverSheetGrid.UpperRowHeights(3),
            height => Assert.Equal(40.0 / 3, height, 6));
        Assert.All(
            ConceptCoverSheetGrid.UpperRowHeights(4),
            height => Assert.Equal(10.0, height, 6));
    }

    [Fact]
    public void ONERuleServesBothSides_WithNoSpecialCaseForХЯНАСАН()
    {
        // The drawing shows ХЯНАСАН with two rows in both of its variants. Two
        // examples are an observation, not a rule, and a special case would
        // freeze the accident: the day a project has three reviewers the table
        // would be wrong in a way traceable to a comment nobody wrote.
        for (int rows = 1; rows <= 8; rows++)
        {
            Assert.All(
                ConceptCoverSheetGrid.UpperRowHeights(rows),
                height => Assert.Equal(ConceptCoverSheetGrid.UpperBodyHeightMm / rows, height, 6));
        }
    }

    [Fact]
    public void PastTheComfortableLimitTheRowsStillDraw()
    {
        // Neither refused nor silently squeezed. Seven rows fit - at 5.7 mm
        // each - and the roster editor is where the reader is told they will be
        // tight, because that is where the count is chosen.
        Assert.Equal(6, ConceptCoverSheetGrid.ComfortableRowLimit);
        Assert.Equal(7, ConceptCoverSheetGrid.UpperRowHeights(7).Count);
        Assert.Equal(40.0, ConceptCoverSheetGrid.UpperRowHeights(7).Sum(), 6);
    }

    [Fact]
    public void TheRowBoundariesWalkDownFromTheHeaderToTheTablesFoot()
    {
        IReadOnlyList<double> boundaries = ConceptCoverSheetGrid.UpperRowBoundaries(2);

        Assert.Equal(97.0, boundaries[0], 2);
        Assert.Equal(77.0, boundaries[1], 2);
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
