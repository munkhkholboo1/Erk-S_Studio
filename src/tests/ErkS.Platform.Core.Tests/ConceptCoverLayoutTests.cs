using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// The A3 concept cover, checked against Master's contract
/// (`_shared/concept-cover-A3-2026-09-06.json`) and against the instruction
/// behind it.
///
/// 🔴 «SCALE ХИЙХГҮЙ ШҮҮ.» The user said it twice, and it is the one thing that
/// makes this different from printing the A4 sheet larger. So the tests that
/// matter most here are not the positions - they are the values that must NOT
/// have moved, asked of both sheets at once.
/// </summary>
public sealed class ConceptCoverLayoutTests
{
    private const double Tolerance = 0.001;

    [Fact]
    public void NOTHINGThatCanBeSCALEDDiffersBetweenTheSheets()
    {
        // Text heights, line weight, row heights, table heights and the three
        // hand-written columns. On a scaled sheet EVERY one of these would be
        // 1.414 times bigger; here they are single constants that neither sheet
        // can override, and this test is what says so out loud.
        Assert.Equal(ConceptCoverLayout.A4.UpperTopMm - ConceptCoverLayout.A4.UpperBottomMm,
            ConceptCoverLayout.A3.UpperTopMm - ConceptCoverLayout.A3.UpperBottomMm, Tolerance);
        Assert.Equal(ConceptCoverLayout.A4.LowerTopMm - ConceptCoverLayout.A4.LowerBottomMm,
            ConceptCoverLayout.A3.LowerTopMm - ConceptCoverLayout.A3.LowerBottomMm, Tolerance);
        Assert.Equal(ConceptCoverLayout.A4.UpperBottomMm - ConceptCoverLayout.A4.LowerTopMm,
            ConceptCoverLayout.A3.UpperBottomMm - ConceptCoverLayout.A3.LowerTopMm, Tolerance);

        // Row division: the same forty millimetres, split the way the owner's
        // drawing splits them.
        //
        // 🔴 THIS LINE ASKED FOR 40/3 AND THE DRAWING SAYS 16/12/12. The even
        // split was chosen on 2026-09-06 to keep row height continuous as parties
        // are added; the owner has since deferred varying counts entirely, so
        // there is one count to draw and a measurement that disagreed by 2.67 mm.
        Assert.Equal(
            ConceptCoverSheetGrid.MeasuredUpperRowHeights,
            ConceptCoverSheetGrid.UpperRowHeights(3));
        Assert.Equal(
            40.0,
            ConceptCoverSheetGrid.UpperRowHeights(3).Sum(),
            Tolerance);

        // A3's own row boundaries step by the same amounts as A4's.
        IReadOnlyList<double> a4 = ConceptCoverLayout.A4.UpperRowBoundaries(3);
        IReadOnlyList<double> a3 = ConceptCoverLayout.A3.UpperRowBoundaries(3);
        Assert.Equal(a4.Count, a3.Count);
        for (int index = 1; index < a4.Count; index++)
            Assert.Equal(a4[index - 1] - a4[index], a3[index - 1] - a3[index], Tolerance);
    }

    [Fact]
    public void THEHandWrittenColumnsStayTheSameWidthOnABiggerSheet()
    {
        // «Нэр» and «Гарын үсэг» are sized for a person writing in them. A wider
        // page does not make handwriting wider, so all of the extra width goes
        // to the one column whose content is a sentence.
        Assert.Equal(25.0, ConceptCoverSheetGrid.NameColumnMm, Tolerance);
        Assert.Equal(25.0, ConceptCoverSheetGrid.SignatureColumnMm, Tolerance);
        Assert.Equal(15.0, ConceptCoverSheetGrid.LogoColumnMm, Tolerance);

        double widthGained = ConceptCoverLayout.A3.TableWidthMm - ConceptCoverLayout.A4.TableWidthMm;
        double roleGained =
            ConceptCoverLayout.A3.UpperRoleColumnMm - ConceptCoverLayout.A4.UpperRoleColumnMm;

        Assert.Equal(widthGained, roleGained, Tolerance);
    }

    [Fact]
    public void THEA3SHEETSNumbersComeBackExactly()
    {
        // 🔴 THIS TEST USED TO PIN A DERIVED SET, AND EVERY ONE OF THEM MOVED.
        // The old numbers came from a frame of 15/400 and from a rule that made
        // the tables widen with the page; the owner's real A3 drawing disproved
        // both (_shared/concept-cover-A3-2026-09-11.json). The values below are
        // what the measured frame and the fixed table produce - the table
        // centred, 120.785 either side of the frame's middle.
        ConceptCoverLayout a3 = ConceptCoverLayout.A3;

        Assert.Equal(420.0, a3.PageWidthMm, Tolerance);
        Assert.Equal(297.0, a3.PageHeightMm, Tolerance);
        Assert.Equal(395.0, a3.FrameWidthMm, Tolerance);
        Assert.Equal(287.0, a3.FrameHeightMm, Tolerance);

        Assert.Equal(96.715, a3.TablesLeftMm, Tolerance);
        Assert.Equal(217.5, a3.TablesMiddleMm, Tolerance);
        Assert.Equal(338.285, a3.TablesRightMm, Tolerance);
        Assert.Equal(120.785, a3.TableWidthMm, Tolerance);

        // The signature block is still anchored to the bottom of the frame, so
        // these are unchanged by the wider sheet - which is the point of
        // anchoring it there.
        Assert.Equal(106.46, a3.UpperTopMm, Tolerance);
        Assert.Equal(58.46, a3.UpperBottomMm, Tolerance);
        Assert.Equal(50.8, a3.LowerTopMm, Tolerance);
        Assert.Equal(34.8, a3.LowerBottomMm, Tolerance);
    }

    [Fact]
    public void THESAMERULESRunBackwardsGiveA4ItsMEASUREDNumbers()
    {
        // 🔴 THE CHECK THAT MAKES THE RULES TRUSTWORTHY. A3 was not measured off
        // a second template - it is the inset and the anchor applied to a
        // different frame. If those two rules were merely convenient, running
        // them over A4's frame would miss A4's own measured numbers.
        //
        // They do not miss. 120.785 is half of the 241.57 measured off the DWG,
        // and 105.0 / 57.0 / 49.34 / 33.34 are the drawing's own edges.
        ConceptCoverLayout a4 = ConceptCoverLayout.A4;

        Assert.Equal(120.785, a4.TableWidthMm, Tolerance);
        Assert.Equal(153.80, a4.TablesMiddleMm, Tolerance);
        Assert.Equal(105.0, a4.UpperTopMm, Tolerance);
        Assert.Equal(57.0, a4.UpperBottomMm, Tolerance);
        Assert.Equal(49.34, a4.LowerTopMm, Tolerance);
        Assert.Equal(33.34, a4.LowerBottomMm, Tolerance);
        Assert.Equal(70.785, a4.UpperRoleColumnMm, Tolerance);
    }

    [Fact]
    public void CHANGINGOneSheetDoesNotMOVETheOther()
    {
        // Master's requirement, asked the only way it can be asked of immutable
        // statics: build a third sheet with a deliberately different frame and
        // check that neither shipped layout notices.
        double a3TablesLeftBefore = ConceptCoverLayout.A3.TablesLeftMm;
        double a4TablesLeftBefore = ConceptCoverLayout.A4.TablesLeftMm;

        var experiment = ConceptCoverLayout.A4 with { FrameWidthMm = 500.0, FrameLeftMm = 1.0 };

        Assert.NotEqual(experiment.TablesLeftMm, a4TablesLeftBefore, Tolerance);
        Assert.Equal(a4TablesLeftBefore, ConceptCoverLayout.A4.TablesLeftMm, Tolerance);
        Assert.Equal(a3TablesLeftBefore, ConceptCoverLayout.A3.TablesLeftMm, Tolerance);

        // And the values a sheet may not change are untouched even by the
        // experiment - they are not on the record at all.
        Assert.Equal(
            ConceptCoverLayout.A4.UpperTopMm - ConceptCoverLayout.A4.UpperBottomMm,
            experiment.UpperTopMm - experiment.UpperBottomMm,
            Tolerance);
    }

    [Fact]
    public void THETablesAreCENTREDOnTheFrameOnBothSheets()
    {
        foreach (ConceptCoverLayout layout in new[] { ConceptCoverLayout.A4, ConceptCoverLayout.A3 })
        {
            Assert.Equal(
                layout.TablesMiddleMm - layout.TablesLeftMm,
                layout.TablesRightMm - layout.TablesMiddleMm,
                Tolerance);
            Assert.Equal(
                layout.TablesLeftMm - layout.FrameLeftMm,
                layout.FrameRightMm - layout.TablesRightMm,
                Tolerance);
            // 🔴 THE INSET IS GONE, BECAUSE IT WAS THE WRONG RULE. It said the
            // tables widen with the page; the owner's real A3 drawing has them at
            // the SAME 241.57 as A4, on a frame 116 mm wider. What is constant is
            // the table, and the gap is merely what is left over.
            Assert.Equal(ConceptCoverSheetGrid.TableWidthMm, layout.TableWidthMm, Tolerance);
        }
    }

    [Fact]
    public void ATallerSheetPutsTheExtraHeightABOVETheTables()
    {
        // Master's M2, and the reason it matters: anchoring the other way would
        // leave the title crowded against the top of the frame with a band of
        // empty paper under the signatures.
        double heightGained =
            ConceptCoverLayout.A3.FrameHeightMm - ConceptCoverLayout.A4.FrameHeightMm;
        double freeAreaGained =
            ConceptCoverLayout.A3.FreeAreaAboveMm - ConceptCoverLayout.A4.FreeAreaAboveMm;

        Assert.Equal(heightGained, freeAreaGained, Tolerance);
        Assert.Equal(
            ConceptCoverSheetGrid.SignatureBlockAboveFrameMm,
            ConceptCoverLayout.A3.LowerBottomMm - ConceptCoverLayout.A3.FrameBottomMm,
            Tolerance);
        Assert.Equal(
            ConceptCoverLayout.A4.LowerBottomMm - ConceptCoverLayout.A4.FrameBottomMm,
            ConceptCoverLayout.A3.LowerBottomMm - ConceptCoverLayout.A3.FrameBottomMm,
            Tolerance);
    }

    [Fact]
    public void THEA3FrameIsTHEONEMeasuredOffTheOwnersDrawing()
    {
        // 🔴 THIS TEST USED TO PIN 15 mm, ON AN ARGUMENT. The reasoning was
        // that the older A3 cover also uses 15 and the two sit in one album, so a
        // five-millimetre jump would show on turning the page. Then the owner
        // supplied a real A3 drawing and it was measured: the frame is the
        // standard 20/5/5/5, 395 x 287
        // (_shared/concept-cover-A3-2026-09-11.json, 2026-09-11).
        //
        // The argument was not refuted, it was OUTRANKED - and the album it
        // worried about no longer contains the other cover at all, because the
        // owner removed it.
        Assert.Equal(20.0, ConceptCoverLayout.A3.FrameLeftMm, Tolerance);
        Assert.Equal(5.0, ConceptCoverLayout.A3.FrameBottomMm, Tolerance);
        Assert.Equal(395.0, ConceptCoverLayout.A3.FrameWidthMm, Tolerance);
        Assert.Equal(287.0, ConceptCoverLayout.A3.FrameHeightMm, Tolerance);
        Assert.Equal(
            5.0,
            ConceptCoverLayout.A3.PageWidthMm - ConceptCoverLayout.A3.FrameRightMm,
            Tolerance);
        Assert.Equal(
            5.0,
            ConceptCoverLayout.A3.PageHeightMm - ConceptCoverLayout.A3.FrameTopMm,
            Tolerance);
    }

    [Fact]
    public void THETableIsTHESAMESizeOnBothSheets()
    {
        // The measurement that overturned the widening rule, stated as itself.
        Assert.Equal(
            ConceptCoverLayout.A4.TableWidthMm,
            ConceptCoverLayout.A3.TableWidthMm,
            Tolerance);
        Assert.Equal(241.57, ConceptCoverLayout.A3.TableWidthMm * 2, 0.01);
    }
}
