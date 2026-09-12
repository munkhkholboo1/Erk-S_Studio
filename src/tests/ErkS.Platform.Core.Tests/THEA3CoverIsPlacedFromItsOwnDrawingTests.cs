using System.Text;
using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// The A3 cover's text comes from the A3 drawing, not from A4 with a shift.
///
/// 🔴 THE OWNER SAW IT BEFORE ANY MEASUREMENT DID: «А4 дээрх байрлалаар А3 дээр
/// тавьчихсан». PFA then proved the derivation could not have worked at all - the
/// lines do not land on A4 × sqrt(2) either, X differing by as much as 27 mm. There
/// was no right answer available without measuring the sheet.
///
/// 🔴 AND ONE OF A4'S OWN FIVE VALUES WAS NEVER MEASURED. The reference draws the
/// title as TWO placeholder lines; the product holds one entry at A4's PAGE centre
/// (148.5, where its frame centre is 153.8) and a Y between the two baselines.
/// Collapsing the placeholder into one wrapped line is right - a real project name is
/// one string - but the position was chosen and the list was called «MeasuredOnA4».
/// A name that asserts its own provenance stops the next reader checking.
/// </summary>
public sealed class THEA3CoverIsPlacedFromItsOwnDrawingTests
{
    [Fact]
    public void THEA3SheetUsesItsOWNMeasuredBlock()
    {
        // Not «equal after shifting»: the same objects, so no arithmetic can creep
        // between the measurement and the sheet.
        Assert.Same(
            ConceptCoverTitleBlock.MeasuredOnA3,
            ConceptCoverTitleBlock.For(ConceptCoverLayout.A3));
    }

    [Fact]
    public void ASHEETWithNoMeasurementOfItsOwnStillGetsTheOldShift()
    {
        // ⚠ THE SHIFT IS KEPT, NOT TRUSTED. No sheet uses it today; deleting it would
        // leave a future sheet with nothing at all, and A4 with nothing to compare
        // against. What is asserted is that it is no longer in A3's way.
        var invented = new ConceptCoverLayout("A2", 594, 420, 25, 5, 564, 410);

        IReadOnlyList<ConceptCoverTitleLine> shifted =
            ConceptCoverTitleBlock.For(invented);

        Assert.NotSame(ConceptCoverTitleBlock.MeasuredOnA3, shifted);
        Assert.Equal(ConceptCoverTitleBlock.MeasuredOnA4.Count, shifted.Count);
    }

    [Theory]
    [InlineData(ConceptCoverTitleBlock.ApprovalLabel, 207.4834, 279.3112)]
    [InlineData(ConceptCoverTitleBlock.Approver, 245.9491, 269.792)]
    [InlineData(ConceptCoverTitleBlock.SiteAddress, 168.0716, 205.8343)]
    [InlineData(ConceptCoverTitleBlock.StageLine, 202.8031, 173.5367)]
    public void EVERYMeasuredLineCarriesTheDrawingsOwnNumbers(
        string key,
        double anchorXMm,
        double centreYMm)
    {
        // 🔴 THE Y IS A CONVERSION AND IT IS WRITTEN OUT HERE, because the two
        // representations of one line are easy to mix: the drawing gives a BASELINE,
        // and the box this product draws spans [c - h, c + h] with the glyph centred,
        // so c = baseline + cap/2. Getting it wrong moves body text 1.24 mm and the
        // 8 mm title 4 mm - «nearly right», which nobody reports and nothing catches.
        ConceptCoverTitleLine line = ConceptCoverTitleBlock.MeasuredOnA3
            .Single(item => item.Key == key);

        Assert.Equal(anchorXMm, line.CentreXMm, 4);
        Assert.Equal(centreYMm, line.CentreYMm, 4);
    }

    [Theory]
    [InlineData(ConceptCoverTitleBlock.ApprovalLabel, 278.0737, 2.475)]
    [InlineData(ConceptCoverTitleBlock.Approver, 268.5545, 2.475)]
    [InlineData(ConceptCoverTitleBlock.SiteAddress, 204.5968, 2.475)]
    [InlineData(ConceptCoverTitleBlock.StageLine, 172.2992, 2.475)]
    public void THEBaselineToCentreConversionIsSTATEDNotAssumed(
        string key,
        double measuredBaselineMm,
        double capHeightMm)
    {
        // The other direction of the same conversion, so a value typed in above that
        // did not come from the measurement cannot pass: recovering the drawing's
        // baseline from what the product stores has to give the drawing's number back.
        ConceptCoverTitleLine line = ConceptCoverTitleBlock.MeasuredOnA3
            .Single(item => item.Key == key);

        Assert.Equal(measuredBaselineMm, line.CentreYMm - (capHeightMm / 2), 4);
        Assert.Equal(capHeightMm, line.CapHeightMm, 4);
    }

    [Fact]
    public void FIXEDLabelsAreAnchoredWhereTheDrawingSTARTSThem()
    {
        // 🔴 CENTRING A LABEL ON THE PLACEHOLDER'S START MOVES IT LEFT BY HALF ITS OWN
        // WIDTH - about 6.5 mm for «БАТЛАВ:» and 9 mm for «/ЗАГВАР ЗУРАГ/». The text
        // never changes, so there is nothing to centre for; the drawing's insertion
        // point is simply where it goes.
        foreach (string key in new[]
                 { ConceptCoverTitleBlock.ApprovalLabel, ConceptCoverTitleBlock.StageLine })
        {
            Assert.True(
                ConceptCoverTitleBlock.MeasuredOnA3.Single(l => l.Key == key).AnchorIsLeftEdge,
                key + " is a fixed label and must start where the drawing starts it");
        }
    }

    [Fact]
    public void REPLACEDTextIsCENTREDBecauseItsLengthIsUnknown()
    {
        // The other half of the same distinction: an approver's name, an address and a
        // building's name are all longer or shorter than the placeholder, so a fixed
        // left edge would run one of them off the sheet. A4 has always centred these;
        // that shape is kept.
        foreach (string key in new[]
                 {
                     // ⚠ THE TITLE IS NOT IN THIS LIST ANY MORE. It is replaced text too,
                     // so centring it would be consistent - but the axis to centre ON was
                     // a guess the measurement disproved, and no width was measured to
                     // recover a real one. Consistency with an unknown is not a reason.
                     ConceptCoverTitleBlock.Approver,
                     ConceptCoverTitleBlock.SiteAddress,
                 })
        {
            Assert.False(
                ConceptCoverTitleBlock.MeasuredOnA3.Single(l => l.Key == key).AnchorIsLeftEdge,
                key + " is replaced by the project and must be centred on its axis");
        }
    }

    [Fact]
    public void THETitleSTARTSWhereTheDrawingStartsItAndIsNOTCentred()
    {
        // 🔴 THIS TEST ASSERTED THE OPPOSITE FOR ONE COMMIT, AND THE MEASUREMENT
        // DISPROVED IT. The reference file lists every text within 0.2 mm of the
        // frame's centre line - exactly two, «ЗАХИАЛАГЧ.» and the footer - and the
        // title's placeholder lines are not among them. Borrowing the footer's axis
        // for its neighbour felt tidy and was a guess.
        //
        // ⚠ THE CENTRE IS NOT RECOVERABLE FROM WHAT WAS MEASURED: the placeholder
        // lines are left-justified TEXT, so their START is known and their WIDTH is
        // not. No width, no centre - so the title is anchored where the drawing puts
        // it, which invents nothing and leaves the decision to centre it, if anybody
        // wants that, resting on a measurement nobody has yet.
        ConceptCoverTitleLine title = ConceptCoverTitleBlock.MeasuredOnA3
            .Single(line => line.Key == ConceptCoverTitleBlock.ProjectTitle);

        Assert.Equal(144.4049, title.CentreXMm, 4);
        Assert.True(title.AnchorIsLeftEdge, "the title must start where the drawing starts it");
        Assert.NotEqual(ConceptCoverLayout.A3.TablesMiddleMm, title.CentreXMm);
    }

    [Fact]
    public void ONLYTheFooterIsPlacedOnTheFramesCentreLine()
    {
        // The positive half: the one placement the drawing states exactly is the
        // footer's, measured at 0.0000 from the frame's centre. It keeps the axis;
        // nothing else borrows it.
        Assert.Equal(217.5, ConceptCoverLayout.A3.TablesMiddleMm, 4);

        foreach (ConceptCoverTitleLine line in ConceptCoverTitleBlock.MeasuredOnA3)
        {
            Assert.NotEqual(ConceptCoverLayout.A3.TablesMiddleMm, line.CentreXMm);
        }
    }

    [Fact]
    public void THETitleSitsAtTheMIDDLEOfThePlaceholdersInk()
    {
        // The two placeholder lines span [180.4316, 199.8663] - baseline of the lower
        // one to the top of the upper one's 8 mm caps - so their middle is 190.14895.
        //
        // ⚠ A4 DOES NOT FOLLOW THIS CONVENTION and is deliberately not made to: its own
        // placeholders span [125.01, 144.45], middle 134.73, and its single line sits
        // at 130.0 - 4.73 mm lower. Reproducing an unexplained offset is how one
        // sheet's accident becomes two sheets' rule.
        Assert.Equal(190.14895, ConceptCoverTitleBlock.A3TitleCentreYMm, 5);
        Assert.Equal(
            ConceptCoverTitleBlock.A3TitleCentreYMm,
            ConceptCoverTitleBlock.MeasuredOnA3
                .Single(line => line.Key == ConceptCoverTitleBlock.ProjectTitle).CentreYMm,
            5);
    }

    [Fact]
    public void NOPERSONNoADDRESSAndNOYEARFromTheDrawingIsInTheProductsCode()
    {
        // 🔴 THE MEASUREMENT CARRIES THE OWNER'S REAL SHEET, so it names a real person,
        // a real district and a real year. Those are DATA - the officials' directory,
        // the project's address, its own year. Baked into the code, one architect would
        // appear on every user's cover.
        //
        // ⚠ Asserted across the whole product tree rather than the two files just
        // edited: the point is that it never gets in anywhere.
        // ⚠ THE LIST IS NARROWER THAN THE FIRST DRAFT, AND THE NARROWING IS THE
        // FINDING. «ДҮҮРГИЙН» is in the product legitimately - it is the grammatical
        // ending an address composer needs - and «2026 ОН» appears only inside a
        // comment QUOTING the drawing. A word-lock that reddens on correct vocabulary
        // and on honest documentation is a lock somebody will delete.
        //
        // What must never appear is a PERSON, and that is checked everywhere including
        // comments: a name in a comment today is a name somebody copies into a string
        // tomorrow.
        string[] neverAnywhere = ["БАТЦОЛМОН"];

        // And the project's own values must not be literals in CODE. Comments may
        // quote the reference sheet; code may not carry it.
        string[] neverInCode = ["2026 ОН"];

        var offenders = new List<string>();
        foreach (FileInfo file in ProductFiles())
        {
            string source = File.ReadAllText(file.FullName, Encoding.UTF8);
            foreach (string content in neverAnywhere)
            {
                if (source.Contains(content, StringComparison.Ordinal))
                    offenders.Add($"{file.Name}: {content}");
            }

            string code = CodeOnly(source);
            foreach (string content in neverInCode)
            {
                if (code.Contains(content, StringComparison.Ordinal))
                    offenders.Add($"{file.Name} (code): {content}");
            }
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void THELabelsTheSheetOWNSAreStillAllowed()
    {
        // ⚠ THE POSITIVE CONTROL FOR THE TEST ABOVE. A search that matched nothing
        // would pass it while proving nothing, so the same search is asked for the form
        // labels that MUST be in the code - they are the sheet's own words, not the
        // project's.
        var found = new List<string>();
        foreach (FileInfo file in ProductFiles())
        {
            string source = File.ReadAllText(file.FullName, Encoding.UTF8);
            if (source.Contains("/ЗАГВАР ЗУРАГ/", StringComparison.Ordinal))
                found.Add(file.Name);
        }

        Assert.NotEmpty(found);
    }

    /// <summary>
    /// The source with its comment lines removed, so a lock on code cannot be broken
    /// by documentation that QUOTES the thing it forbids.
    /// </summary>
    private static string CodeOnly(string source)
    {
        var kept = new StringBuilder(source.Length);
        foreach (string line in source.Replace("\r\n", "\n").Split('\n'))
        {
            string trimmed = line.TrimStart();
            if (trimmed.StartsWith("//", StringComparison.Ordinal) ||
                trimmed.StartsWith("*", StringComparison.Ordinal))
            {
                continue;
            }

            kept.Append(line).Append('\n');
        }

        return kept.ToString();
    }

    [Fact]
    public void THECommentStripperItselfWorksBOTHWays()
    {
        // 🔴 THE LOCK ABOVE DEPENDS ON THIS, so it is tested rather than trusted: a
        // stripper that removed everything would make the code lock vacuous, and one
        // that removed nothing would red on the comment quoting the sheet.
        Assert.DoesNotContain("secret", CodeOnly("    // secret\n"), StringComparison.Ordinal);
        Assert.Contains("var x", CodeOnly("    var x = 1;\n"), StringComparison.Ordinal);
    }

    [Fact]
    public void THEWriterHONOURSTheAnchorKindAndCENTRESTheYearLine()
    {
        // 🔴 SABOTAGE FOUND THIS GAP. Every assertion above is about the RULE, and
        // two mutations in the WRITER survived all of them: ignoring the anchor kind,
        // and putting the year line back on A4's carried constant. A rule nobody
        // consults is the failure this project keeps finding, and it found it again
        // here - the fifth time today that splitting a rule out left the seam unheld.
        //
        // ⚠ SOURCE-ANCHORED, and here is the limit: it proves the writer READS the
        // anchor and centres on the frame, not that the ink lands there. The probe
        // beside it renders the sheet for measuring back, which is the only thing that
        // can prove the ink.
        string writer = ReadPdfSource("PdfSharpAlbumWriter.ConceptCover2026.cs");
        string compact = new string(writer.Where(c => !char.IsWhiteSpace(c)).ToArray());

        // The anchor reaches the drawing call, and the rectangle honours it.
        Assert.Contains("anchorIsLeftEdge:line.AnchorIsLeftEdge", compact, StringComparison.Ordinal);
        Assert.Contains("anchorIsLeftEdge?centreXMm:centreXMm-widthMm/2", compact, StringComparison.Ordinal);

        // The year line is centred on the frame rather than carrying A4's 148.5.
        Assert.Contains("centreXMm:layout.TablesMiddleMm", compact, StringComparison.Ordinal);
        Assert.DoesNotContain("HorizontalShiftMm(layout)+148.5", compact, StringComparison.Ordinal);
    }

    private static string ReadPdfSource(string fileName) =>
        File.ReadAllText(
            Path.Combine(FindProductRoot().FullName, "ErkS.Platform.Pdf", fileName),
            Encoding.UTF8);

    private static DirectoryInfo FindProductRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "src", "src");
            if (Directory.Exists(candidate))
                return new DirectoryInfo(candidate);
            directory = directory.Parent;
        }

        Assert.Fail("the product tree was not found; this test reads it");
        return new DirectoryInfo(".");
    }

    private static IEnumerable<FileInfo> ProductFiles()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "src", "src");
            if (Directory.Exists(candidate))
            {
                return new DirectoryInfo(candidate)
                    .GetFiles("*.cs", SearchOption.AllDirectories)
                    .Where(file =>
                        !file.FullName.Contains(
                            $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                            StringComparison.Ordinal) &&
                        !file.FullName.Contains(
                            $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                            StringComparison.Ordinal))
                    .ToList();
            }

            directory = directory.Parent;
        }

        Assert.Fail("the product tree was not found; this test reads it");
        return [];
    }
}
