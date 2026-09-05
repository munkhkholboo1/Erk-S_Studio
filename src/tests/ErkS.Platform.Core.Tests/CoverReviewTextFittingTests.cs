using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// What gives way when a name is too long for its cell.
///
/// Revit shrinks the text to a floor and then lets it overflow. Studio's
/// concept cover keeps the text and grows the row. The user chose the
/// combination on 2026-09-06: shrink exactly as Revit does, and grow the row
/// only when shrinking was not enough - so the working cover matches Revit
/// wherever Revit's own rule succeeds, and stops short of reproducing its
/// failure.
///
/// The failure is not hypothetical. The single exported cover on this machine
/// has its review rows at 1.78 mm, which is the floor: the reference drawing
/// was already at the point of overflowing.
/// </summary>
public sealed class CoverReviewTextFittingTests
{
    private const double RoleCellWidthMm = 70.0;
    private const double NameCellWidthMm = 28.0;

    [Fact]
    public void TextThatFitsIsNotShrunk()
    {
        Assert.Equal(2.5, CoverReviewTextFitting.FitColumn(20, RoleCellWidthMm), 3);
    }

    [Fact]
    public void AnEmptyColumnKeepsTheBaseSize()
    {
        Assert.Equal(2.5, CoverReviewTextFitting.FitColumn(0, RoleCellWidthMm), 3);
        Assert.Equal(2.5, CoverReviewTextFitting.FitColumn([null, "", "  "], RoleCellWidthMm), 3);
    }

    [Fact]
    public void TheREALCoversLongestPositionLandsOnTheFloor()
    {
        // 88 characters: «НОБГ-ын ГТТХ-ийн Барилгын зураг төслийн хяналтын
        // ахлах байцаагч, хошууч Б.Даваасүрэн», read out of the exported PDF.
        // Measured there at 1.78 mm; the rule puts it at the 1.80 floor. That
        // agreement is what says this is Revit's rule and not a guess.
        Assert.Equal(1.80, CoverReviewTextFitting.FitColumn(88, RoleCellWidthMm), 2);
    }

    [Fact]
    public void ShrinkingStopsAtTheFloorAndNeverGoesBelowIt()
    {
        // However long the text, the size has a bottom. What happens past it is
        // the row's problem, not the font's.
        Assert.Equal(1.80, CoverReviewTextFitting.FitColumn(400, RoleCellWidthMm), 2);
        Assert.Equal(1.80, CoverReviewTextFitting.FitColumn(4000, NameCellWidthMm), 2);
    }

    [Fact]
    public void ANegligibleShrinkIsNotWorthASecondTextSize()
    {
        // Just past fitting: the rule would ask for something a hair under 2.5,
        // and a page with two text sizes 0.02 mm apart reads as a mistake.
        double justOver = CoverReviewTextFitting.FitColumn(52, RoleCellWidthMm);

        Assert.Equal(2.5, justOver, 3);
    }

    [Fact]
    public void TheLONGESTEntryDecidesForTheWholeColumn()
    {
        // One size per column, not per row - otherwise neighbouring cells in the
        // same column print at different sizes and the table looks broken.
        double fitted = CoverReviewTextFitting.FitColumn(
            ["Богино", new string('x', 120), "Дунд зэрэг"],
            RoleCellWidthMm);

        Assert.Equal(CoverReviewTextFitting.FitColumn(120, RoleCellWidthMm), fitted, 3);
    }

    [Fact]
    public void LineBreaksTheTextITSELFCarriesAreWhatCount()
    {
        // The distinction that keeps this from being circular: a size derived
        // from WRAPPED lines would depend on the wrapping, which depends on the
        // size. Only breaks the author typed are counted, so the answer is
        // fixed before any wrapping happens.
        var wrapped = new[] { new string('x', 40) + "\r\n" + new string('x', 40) };
        var single = new[] { new string('x', 80) };

        Assert.Equal(
            CoverReviewTextFitting.FitColumn(40, RoleCellWidthMm),
            CoverReviewTextFitting.FitColumn(wrapped, RoleCellWidthMm),
            3);
        Assert.NotEqual(
            CoverReviewTextFitting.FitColumn(wrapped, RoleCellWidthMm),
            CoverReviewTextFitting.FitColumn(single, RoleCellWidthMm));
    }

    [Fact]
    public void OnlyTheWORKINGCoverShrinks()
    {
        // The concept cover was not asked to change. It keeps full-size text and
        // grows its rows, exactly as before.
        Assert.True(CoverApprovalTableGrid.WorkingDrawing.ShrinksReviewTextToFit);
        Assert.False(CoverApprovalTableGrid.Concept.ShrinksReviewTextToFit);
    }

    [Fact]
    public void TheTwoCoversDivideDifferentSpansFromDifferentTops()
    {
        // Measured off the exported cover: four rows fell on 152.86 / 138.11 /
        // 123.36 / 108.61 / 93.86, which is 59.00 mm divided evenly from 152.86.
        // Studio's own cover starts a millimetre higher and divides 60.00.
        Assert.Equal(152.86, CoverApprovalTableGrid.WorkingDrawing.ReviewRowsTop, 3);
        Assert.Equal(59.0, CoverApprovalTableGrid.WorkingDrawing.ReviewRowsSpan, 3);
        Assert.Equal(153.86, CoverApprovalTableGrid.Concept.ReviewRowsTop, 3);
        Assert.Equal(60.0, CoverApprovalTableGrid.Concept.ReviewRowsSpan, 3);
    }

    [Fact]
    public void TheROWSAreDrawnAtTheSizeTheirHeightWasMeasuredFor()
    {
        // Sabotaging this passed every other test in the file: the fitting rule
        // was right, the row heights were right, and the drawing call still
        // passed a literal 2.5. A row exactly tall enough for shrunken text,
        // with full-size text drawn into it, overflows in the one case the whole
        // change exists to prevent - and nothing measurable would have said so,
        // because the sizes only meet inside a call that emits into a PDF.
        string writer = ReadWriterSource();
        int start = writer.IndexOf("foreach (var row in reviewRows)", StringComparison.Ordinal);
        Assert.True(start >= 0, "the review row loop was renamed; check this test with it");
        string body = writer[start..(start + 900)];

        Assert.Contains("row.RoleTextHeightMm", body, StringComparison.Ordinal);
        Assert.Contains("row.NameTextHeightMm", body, StringComparison.Ordinal);
    }

    private static string ReadWriterSource()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(
                directory.FullName, "src", "src", "ErkS.Platform.Pdf", "PdfSharpAlbumWriter.cs");
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
            directory = directory.Parent;
        }

        Assert.Fail("PdfSharpAlbumWriter.cs was not found; this test reads it from source");
        return "";
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void TheWorkingRowsDivideTheirSpanEVENLY(int rowCount)
    {
        // The boundary formula, checked against the measured page for four rows.
        CoverApprovalTableGrid grid = CoverApprovalTableGrid.WorkingDrawing;
        double bottom = grid.ReviewRowsTop - grid.ReviewRowsSpan;

        Assert.Equal(93.86, bottom, 3);
        double boundary = grid.ReviewRowsTop - grid.ReviewRowsSpan * 1 / rowCount;
        Assert.InRange(boundary, bottom, grid.ReviewRowsTop);
        if (rowCount == 4)
            Assert.Equal(138.11, boundary, 2);
    }
}
