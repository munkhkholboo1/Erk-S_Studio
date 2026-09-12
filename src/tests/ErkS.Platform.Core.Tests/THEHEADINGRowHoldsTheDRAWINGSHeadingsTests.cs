using System.Text;
using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// The 8 mm header row holds the drawing's COLUMN HEADINGS, and the role label sits
/// outside the table.
///
/// 🔴 WHAT THE MEASUREMENT SAID, AFTER BEING ASKED THE RIGHT QUESTION. The role label
/// was being drawn across the header row, bold and centred. The reference drawing puts
/// something else there entirely: «Албан тушаал | Нэр | Гарын үсэг», one heading per
/// column - and puts ЗӨВШИЛЦСӨН / ХЯНАСАН / ГҮЙЦЭТГЭГЧ / ЗАХИАЛАГЧ ABOVE the table,
/// outside it. PFA's own classification says so in one line: the four labels are in
/// sheetTexts rather than tableTexts «because they really do sit outside the table».
///
/// 🔴 SO THE ALBUM WAS WRONG THREE WAYS AT ONCE, and only one of them had been noticed:
///   the label was in the header row instead of above the table (~60 mm across, which
///     is half a table width, and ~4 mm up);
///   the three column headings were not drawn AT ALL;
///   the column dividers stopped below the header row, so it was one merged cell - the
///     drawing runs them the full 48 mm, from the table's top edge down.
/// The label being in the wrong place is what made the row look occupied.
///
/// ⚠ WHAT IS MEASURED AND WHAT IS CHOSEN. The band, the column each heading belongs to,
/// and every label anchor are measured. The heading's placement WITHIN its column is
/// not recoverable: they are MTEXT, and PFA states outright that MTEXT extents could not
/// be measured because AutoCAD's textbox returns nil on them - so «centred» cannot be
/// tested, and the drawing's own two tables disagree by 12.8 mm on the same heading.
/// Centring them is STU's choice, the house style of every other cell on this sheet, and
/// it lands within 0.36 mm of the measured text centres vertically.
/// </summary>
public sealed class THEHEADINGRowHoldsTheDRAWINGSHeadingsTests
{
    [Fact]
    public void THEWRITERDrawsTheHEADINGSInTheHeaderRowAndNotTheLabel()
    {
        // 🔴 THE SEAM FIRST. Six times a rule was split out and the seam left unheld, so
        // the writer's own behaviour is asserted before the numbers are. What must be
        // true: the header cells carry the headings, and the label is NOT drawn into
        // them - a version that drew both would look almost right and print two texts
        // on top of each other.
        string upper = MethodBody("DrawConceptCover2026UpperTable");
        string compact = Compact(upper);

        Assert.Contains("ConceptCoverTableHeadings.Position", compact, StringComparison.Ordinal);
        Assert.Contains("ConceptCoverTableHeadings.PersonName", compact, StringComparison.Ordinal);
        Assert.Contains("ConceptCoverTableHeadings.Signature", compact, StringComparison.Ordinal);

        // The label no longer goes into a cell of this table at all.
        Assert.DoesNotContain("Cell(gfx,layout,label,", compact, StringComparison.Ordinal);

        // 🔴 AND NOT BOLD, WHICH THE MEASUREMENT SETTLES: every label and heading on the
        // sheet is b0 in its own MTEXT format run. The old header cell asked for bold.
        Assert.DoesNotContain("label,leftMm,headerBottom,rightMm,top,bold:true", compact, StringComparison.Ordinal);
    }

    [Fact]
    public void THELABELIsDrawnABOVETheTableAndLEFTAligned()
    {
        // ⚠ THE ARITHMETIC HAS ONE HOME, SO THIS ASSERTS IN TWO PLACES. A first version
        // demanded the offsets inside each table method, which would have forced the
        // placement arithmetic to be written twice - the test shaping the code badly
        // rather than holding it. The delegation is asserted at each table, the
        // arithmetic once at the helper.
        foreach (string method in new[]
                 {
                     "DrawConceptCover2026UpperTable",
                     "DrawConceptCover2026LowerTable",
                 })
        {
            Assert.Contains(
                "DrawConceptCover2026RoleLabel(gfx,layout,label,placement,leftMm,rightMm,top)",
                Compact(MethodBody(method)),
                StringComparison.Ordinal);
        }

        // Above: the Y is the table's TOP plus an offset, not a cell inside it.
        // Left-aligned: the drawing anchors these top-left and that anchor IS the left
        // edge of the writing, so centring would move it half a table width.
        string helper = Compact(MethodBody("DrawConceptCover2026RoleLabel"));

        Assert.Contains("anchorIsLeftEdge:true", helper, StringComparison.Ordinal);
        Assert.Contains("centreYMm:topMm+placement.CentreAboveTableMm", helper, StringComparison.Ordinal);
        Assert.Contains("centreXMm:leftMm+placement.LeftOffsetMm", helper, StringComparison.Ordinal);

        // And each pair hands its OWN measured placement over - one placement used for
        // all four would put three labels in the wrong place and look almost right.
        string upperPair = Compact(MethodBody("DrawConceptCover2026UpperPair"));
        string lowerPair = Compact(MethodBody("DrawConceptCover2026LowerPair"));

        Assert.Contains("ConceptCoverTableHeadings.Concurring", upperPair, StringComparison.Ordinal);
        Assert.Contains("ConceptCoverTableHeadings.Reviewing", upperPair, StringComparison.Ordinal);
        Assert.Contains("ConceptCoverTableHeadings.Performing", lowerPair, StringComparison.Ordinal);
        Assert.Contains("ConceptCoverTableHeadings.Commissioning", lowerPair, StringComparison.Ordinal);
    }

    [Fact]
    public void THECOLUMNDividersRunTheFullHeightOfTheTable()
    {
        // 🔴 MEASURED, AND IT IS WHY THE HEADER ROW IS THREE CELLS. The left table's
        // role|name and name|signature rules are 48 mm long - the table's whole height,
        // starting at its top edge - so they cross the header row. The writer stopped
        // them at the header's bottom, which made the row one merged cell and made a
        // single centred label look like the right thing to put in it.
        string compact = Compact(MethodBody("DrawConceptCover2026UpperTable"));

        Assert.DoesNotContain("roleRight,bottom,roleRight,headerBottom", compact, StringComparison.Ordinal);
        Assert.DoesNotContain("nameRight,bottom,nameRight,headerBottom", compact, StringComparison.Ordinal);
        Assert.Contains("roleRight,bottom,roleRight,top", compact, StringComparison.Ordinal);
        Assert.Contains("nameRight,bottom,nameRight,top", compact, StringComparison.Ordinal);

        // The header row's own horizontal rule stays - it is what makes it a row.
        Assert.Contains("leftMm,headerBottom,rightMm,headerBottom", compact, StringComparison.Ordinal);
    }

    [Fact]
    public void THEFOURLabelPlacementsAreTheMEASUREDOffsetsFromTheirOwnTable()
    {
        // Offsets from the table's own corner, NOT the drawing's absolute page
        // positions. 🔴 THE TABLE IS NOT WHERE THE DRAWING PUTS IT, BY DECISION: PFA
        // measured the pair centred 2.6434 mm left of the frame centre and recorded that
        // the reference does not fix the position - «STU зангуугаа ЗОРИУД сонгоно» - and
        // STU centres on the frame. Absolute anchors would therefore sit 2.4 mm left of
        // the very table edge they are supposed to line up with, which on a signed sheet
        // reads as a mistake rather than as a decision.
        Assert.Equal(0.0982, ConceptCoverTableHeadings.Concurring.LeftOffsetMm, 4);
        Assert.Equal(3.8745, ConceptCoverTableHeadings.Concurring.TopAboveTableMm, 4);
        Assert.Equal(2.6, ConceptCoverTableHeadings.Concurring.CapHeightMm, 4);

        Assert.Equal(0.0766, ConceptCoverTableHeadings.Reviewing.LeftOffsetMm, 4);
        Assert.Equal(3.8574, ConceptCoverTableHeadings.Reviewing.TopAboveTableMm, 4);

        // ⚠ 2.5, NOT 2.6, AND KEPT AS MEASURED. ХЯНАСАН is drawn a tenth smaller than
        // its three siblings. Rounding the four to one number would be tidier and would
        // be a value nobody measured.
        Assert.Equal(2.5, ConceptCoverTableHeadings.Reviewing.CapHeightMm, 4);

        Assert.Equal(1.3991, ConceptCoverTableHeadings.Performing.LeftOffsetMm, 4);
        Assert.Equal(2.3109, ConceptCoverTableHeadings.Commissioning.LeftOffsetMm, 4);

        // The lower pair's two labels share a Y exactly, and sit further above their
        // table than the upper pair's do.
        Assert.Equal(
            ConceptCoverTableHeadings.Performing.TopAboveTableMm,
            ConceptCoverTableHeadings.Commissioning.TopAboveTableMm,
            4);
        Assert.True(
            ConceptCoverTableHeadings.Performing.TopAboveTableMm >
            ConceptCoverTableHeadings.Concurring.TopAboveTableMm,
            "the lower pair's labels stand further off their table, as measured");
    }

    [Fact]
    public void THECENTREIsDerivedFromTheMeasuredTOPNotHandSummed()
    {
        // 🔴 THE SAME TRAP THE TITLE BLOCK FELL INTO. Its centre was hand-summed and
        // came out 0.00005 mm wrong; the fix was to name the conversion and let it do
        // the arithmetic. The measurement gives the TOP of the writing and the writer
        // needs its MIDDLE, so the conversion exists once.
        foreach (ConceptCoverRoleLabelPlacement placement in new[]
                 {
                     ConceptCoverTableHeadings.Concurring,
                     ConceptCoverTableHeadings.Reviewing,
                     ConceptCoverTableHeadings.Performing,
                     ConceptCoverTableHeadings.Commissioning,
                 })
        {
            Assert.Equal(
                placement.TopAboveTableMm - (placement.CapHeightMm / 2),
                placement.CentreAboveTableMm,
                6);

            // And the label clears the table it names: its lowest ink is above the top
            // edge. A negative here would print the label through the table's own rule.
            Assert.True(
                placement.TopAboveTableMm - placement.CapHeightMm > 0,
                "the label sits clear of the table, not across its edge");
        }
    }

    [Fact]
    public void THECLIENTSHeadingNamesTheCitizenCaseBecauseTheDrawingDoes()
    {
        // ⚠ MEASURED, NOT INVENTED: the ЗАХИАЛАГЧ table's heading reads «Албан тушаал /
        // Иргэн» while the other three read «Албан тушаал». A client can be a private
        // person, and the sheet says so - which is the same distinction
        // ProjectClientTypes already carries.
        Assert.Equal("Албан тушаал", ConceptCoverTableHeadings.Position);
        Assert.Equal("Албан тушаал / Иргэн", ConceptCoverTableHeadings.PositionOrCitizen);
        Assert.Equal("Нэр", ConceptCoverTableHeadings.PersonName);
        Assert.Equal("Гарын үсэг", ConceptCoverTableHeadings.Signature);

        // And the writer uses the citizen form on the client's side only.
        string compact = Compact(MethodBody("DrawConceptCover2026LowerPair"));
        Assert.Contains("ConceptCoverTableHeadings.PositionOrCitizen", compact, StringComparison.Ordinal);
        Assert.Contains("ConceptCoverTableHeadings.Position,", compact, StringComparison.Ordinal);
    }

    [Fact]
    public void EVERYHeadingIsTheSameHeightOnceTheINLINEScaleIsApplied()
    {
        // ⚠ DERIVED FROM THE RAW TEXT, NOT READ OFF heightMm. Three of the six headings
        // are stored at 3.0 mm with an inline «\H0.86667x» run, which is 2.6 mm on the
        // page; the other three are stored at 2.6 with no scale. Trusting heightMm alone
        // would have drawn half of them 15% too large, and the file would have looked
        // like the authority for it.
        Assert.Equal(2.6, ConceptCoverTableHeadings.CapHeightMm, 4);
    }

    private static string Compact(string text) =>
        new(text.Where(c => !char.IsWhiteSpace(c)).ToArray());

    /// <summary>
    /// One method of the cover writer, as written.
    ///
    /// ⚠ SOURCE-ANCHORED, AND THE LIMIT IS NAMED: these are private statics in the PDF
    /// project, so what can be held here is the delegation and the condition, not the
    /// pixels. The end-to-end check is ConceptCover2026RenderProbe, which writes the
    /// real sheet out to be measured back.
    /// </summary>
    private static string MethodBody(string name)
    {
        string source = ReadPdfSource("PdfSharpAlbumWriter.ConceptCover2026.cs")
            .Replace("\r\n", "\n");
        int at = source.IndexOf("private static void " + name + "(", StringComparison.Ordinal);
        Assert.True(at > 0, name + " was not found in the writer");
        // ⚠ TWO METHOD SHAPES. A block body ends at a closing brace in column five;
        // an expression body ends before the next member. Scanning for the brace alone
        // would silently return the REST OF THE FILE for an expression-bodied method,
        // and every Contains would then pass for the wrong reason - a source-anchored
        // test reading the wrong span is green, not red.
        int brace = source.IndexOf("\n    }", at, StringComparison.Ordinal);
        int nextMember = source.IndexOf("\n    private static ", at + 1, StringComparison.Ordinal);
        int end = brace < 0
            ? nextMember
            : (nextMember < 0 ? brace : Math.Min(brace, nextMember));
        Assert.True(end > at, "the end of " + name + " was not found");
        return source[at..end];
    }

    private static string ReadPdfSource(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(
                directory.FullName, "src", "src", "ErkS.Platform.Pdf", fileName);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate, Encoding.UTF8);
            directory = directory.Parent;
        }

        Assert.Fail(fileName + " was not found; this test reads it from source");
        return "";
    }
}
