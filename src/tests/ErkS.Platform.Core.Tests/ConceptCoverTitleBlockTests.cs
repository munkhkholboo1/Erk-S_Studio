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
    public void THECENTRINGRuleAppliesToASheetWithNoMeasurementOfItsOwn()
    {
        // 🔴 THIS TEST USED TO MAKE THIS CLAIM ABOUT A3, AND A MEASUREMENT DISPROVED
        // IT (2026-09-12). The A3 block was being placed by centring A4's block in
        // A3's free area, less A4's own offset. PFA measured the real A3 drawing and
        // the lines are not there - and not on A4 * sqrt(2) either, X differing by up
        // to 27 mm. A3 now uses its own measurement
        // (ConceptCoverTitleBlock.MeasuredOnA3), so the rule this test describes no
        // longer decides anything the product draws.
        //
        // It is kept, pointed at a sheet that has no measurement, because the rule is
        // still the answer for such a sheet - and because deleting the test would
        // delete the record of what was believed and why it was wrong.
        var unmeasured = new ConceptCoverLayout("A2", 594, 420, 25, 5, 564, 410);

        double blockCentre = ConceptCoverTextBox.BlockCentreMm(
            ConceptCoverTitleBlock.For(unmeasured)
                .Select(line => (line.CentreYMm, line.CapHeightMm)));
        double freeCentre = ConceptCoverTitleBlock.FreeAreaCentreMm(unmeasured);

        Assert.Equal(ConceptCoverTitleBlock.OffsetFromFreeAreaCentreMm, blockCentre - freeCentre, Tolerance);
        Assert.Equal(-3.06, blockCentre - freeCentre, 0.01);
    }

    [Fact]
    public void A3IsNOLONGERDerivedFromA4AtAll()
    {
        // The positive statement of what replaced the rule above, so «the derivation
        // is gone» is asserted rather than left as the absence of a test.
        Assert.Same(
            ConceptCoverTitleBlock.MeasuredOnA3,
            ConceptCoverTitleBlock.For(ConceptCoverLayout.A3));
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
    public void THEBlockKeepsItsWIDTHSAndHEIGHTSOnAnUnmeasuredSheet()
    {
        // Moved, never resized - the whole instruction in one assertion. Aimed at a
        // sheet with no measurement of its own, since A3 no longer goes through this
        // path: its line heights and widths come from its own drawing, and its
        // SPACING is the drawing's, not A4's carried across.
        var unmeasured = new ConceptCoverLayout("A2", 594, 420, 25, 5, 564, 410);
        IReadOnlyList<ConceptCoverTitleLine> shifted = ConceptCoverTitleBlock.For(unmeasured);

        for (int index = 0; index < shifted.Count; index++)
        {
            ConceptCoverTitleLine measured = ConceptCoverTitleBlock.MeasuredOnA4[index];
            Assert.Equal(measured.Key, shifted[index].Key);
            Assert.Equal(measured.CapHeightMm, shifted[index].CapHeightMm, Tolerance);
            Assert.Equal(measured.WidthMm, shifted[index].WidthMm, Tolerance);
        }

        // The lines keep their spacing too: the block moves as one piece.
        for (int index = 1; index < shifted.Count; index++)
        {
            Assert.Equal(
                ConceptCoverTitleBlock.MeasuredOnA4[index - 1].CentreYMm
                    - ConceptCoverTitleBlock.MeasuredOnA4[index].CentreYMm,
                shifted[index - 1].CentreYMm - shifted[index].CentreYMm,
                Tolerance);
        }
    }

    [Fact]
    public void A3KEEPSTheHEIGHTSItsOwnDrawingMeasured()
    {
        // The replacement claim for A3: the cap heights are the drawing's - body text
        // at 2.475 and the title at 8 - and the box widths are carried over, because
        // the drawing measures where text STARTS and not how wide its box is.
        IReadOnlyList<ConceptCoverTitleLine> a3 = ConceptCoverTitleBlock.MeasuredOnA3;

        Assert.Equal(ConceptCoverTitleBlock.MeasuredOnA4.Count, a3.Count);
        foreach (ConceptCoverTitleLine line in a3)
        {
            ConceptCoverTitleLine a4 = ConceptCoverTitleBlock.MeasuredOnA4
                .Single(item => item.Key == line.Key);
            Assert.Equal(a4.CapHeightMm, line.CapHeightMm, Tolerance);
            Assert.Equal(a4.WidthMm, line.WidthMm, Tolerance);
        }
    }
}
