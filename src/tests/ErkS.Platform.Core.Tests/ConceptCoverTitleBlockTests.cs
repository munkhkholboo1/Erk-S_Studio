using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// Where the title block sits, and - the part that took three passes to get
/// right - WHAT the block is.
/// </summary>
public sealed class ConceptCoverTitleBlockTests
{
    private const double Tolerance = 0.001;

    [Fact]
    public void A4DoesNotMoveATHOUSANDTHOfAMillimetre()
    {
        // The A3 work must be invisible on A4. Not "close enough": the offset is
        // measured against A4's own free area, so putting it back has to land on
        // the same number exactly, and a shift of anything but zero would mean
        // the rule is not the one that was measured.
        Assert.Equal(0.0, ConceptCoverTitleBlock.VerticalShiftMm(ConceptCoverLayout.A4), Tolerance);
        Assert.Equal(0.0, ConceptCoverTitleBlock.HorizontalShiftMm(ConceptCoverLayout.A4), Tolerance);

        IReadOnlyList<ConceptCoverTitleLine> placed =
            ConceptCoverTitleBlock.For(ConceptCoverLayout.A4);
        for (int index = 0; index < placed.Count; index++)
        {
            Assert.Equal(ConceptCoverTitleBlock.MeasuredOnA4[index].CentreXMm, placed[index].CentreXMm, Tolerance);
            Assert.Equal(ConceptCoverTitleBlock.MeasuredOnA4[index].CentreYMm, placed[index].CentreYMm, Tolerance);
        }
    }

    [Fact]
    public void THEBlockIsCENTREDOnA3TheWayTheOLDCoverCentresItsOwn()
    {
        // The user asked for this to be judged against the cover Studio already
        // prints. That sheet is A3 too, and it puts its block at the middle of
        // its free area less about three millimetres - it does NOT hang it off
        // the top of its table. Measured: -3.45 mm in a 130.14 mm band.
        //
        // So this one is centred too, less the offset measured on A4.
        double blockCentre = ConceptCoverTextBox.BlockCentreMm(
            ConceptCoverTitleBlock.For(ConceptCoverLayout.A3)
                .Select(line => (line.CentreYMm, line.CapHeightMm)));
        double freeCentre = ConceptCoverTitleBlock.FreeAreaCentreMm(ConceptCoverLayout.A3);

        Assert.Equal(ConceptCoverTitleBlock.OffsetFromFreeAreaCentreMm, blockCentre - freeCentre, Tolerance);
        Assert.Equal(-3.06, blockCentre - freeCentre, 0.01);

        // And it really is a long way from where anchoring to the tables put it:
        // that version sat about 43 mm lower, a quarter of the band.
        Assert.True(
            ConceptCoverTitleBlock.VerticalShiftMm(ConceptCoverLayout.A3) > 40.0,
            "the A3 block should rise out of the tables, not sit on them");
    }

    [Fact]
    public void THEOffsetIsCOMPUTEDFromTheBlockRatherThanWrittenDown()
    {
        // -3.06 is a measurement OF THE BLOCK, so it has to follow the block.
        // Written as a literal it would go on being asserted after a line was
        // added at a different height - the exact way a number stops meaning
        // what its name says.
        Assert.Equal(152.685, ConceptCoverTitleBlock.A4CentreMm, Tolerance);
        Assert.Equal(155.745, ConceptCoverTitleBlock.FreeAreaCentreMm(ConceptCoverLayout.A4), Tolerance);
        Assert.Equal(-3.060, ConceptCoverTitleBlock.OffsetFromFreeAreaCentreMm, Tolerance);
    }

    [Fact]
    public void WHATCountsAsTheBlockIsWRITTENDownAndItMATTERS()
    {
        // 🔴 THE SENSITIVITY THAT MADE THIS A TYPE. Drop the line nearest the
        // tables and the offset does not merely shift - it changes SIGN, from
        // -3.06 to +2.12, a swing of 5.18 mm on a number whose own magnitude is
        // 3.06. So "the block sits 3 mm below centre" is not an observation
        // about the drawing; it is an observation about the drawing PLUS a
        // definition, and the definition has to travel with it.
        //
        // The figures here were themselves corrected by this test: the first
        // version of it asserted a swing of more than 9 mm, a number carried
        // over from the same measurement made on the OLD cover. Two sheets, two
        // sensitivities, one careless reuse.
        var withoutTheLowestLine = ConceptCoverTitleBlock.MeasuredOnA4
            .Where(line => line.Key != ConceptCoverTitleBlock.StageLine)
            .Select(line => (line.CentreYMm, line.CapHeightMm));

        double narrowed = ConceptCoverTextBox.BlockCentreMm(withoutTheLowestLine)
            - ConceptCoverTitleBlock.FreeAreaCentreMm(ConceptCoverLayout.A4);

        Assert.True(narrowed > 0, "dropping a line flips the sign - that is the point");
        Assert.Equal(2.119, narrowed, 0.01);
        Assert.True(
            Math.Abs(narrowed - ConceptCoverTitleBlock.OffsetFromFreeAreaCentreMm)
                > Math.Abs(ConceptCoverTitleBlock.OffsetFromFreeAreaCentreMm),
            "the two definitions differ by more than the offset itself");

        // The definition, enumerated: every line drawn above the tables, and
        // nothing else. The footer is below them and is not in the list.
        Assert.Equal(5, ConceptCoverTitleBlock.MeasuredOnA4.Count);
        Assert.All(
            ConceptCoverTitleBlock.MeasuredOnA4,
            line => Assert.True(line.CentreYMm > ConceptCoverLayout.A4.UpperTopMm));
    }

    [Fact]
    public void THEBlockKeepsItsWIDTHSAndHEIGHTSOnTheBiggerSheet()
    {
        // Moved, never resized - the whole instruction in one assertion.
        IReadOnlyList<ConceptCoverTitleLine> a3 = ConceptCoverTitleBlock.For(ConceptCoverLayout.A3);
        for (int index = 0; index < a3.Count; index++)
        {
            ConceptCoverTitleLine measured = ConceptCoverTitleBlock.MeasuredOnA4[index];
            Assert.Equal(measured.Key, a3[index].Key);
            Assert.Equal(measured.CapHeightMm, a3[index].CapHeightMm, Tolerance);
            Assert.Equal(measured.WidthMm, a3[index].WidthMm, Tolerance);
        }

        // The lines keep their spacing too: the block moves as one piece.
        for (int index = 1; index < a3.Count; index++)
        {
            Assert.Equal(
                ConceptCoverTitleBlock.MeasuredOnA4[index - 1].CentreYMm
                    - ConceptCoverTitleBlock.MeasuredOnA4[index].CentreYMm,
                a3[index - 1].CentreYMm - a3[index].CentreYMm,
                Tolerance);
        }
    }
}
