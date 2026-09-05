using System.Text;
using ErkS.Platform.Core;
using ErkS.Platform.Pdf;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// The working-drawing corner table has three identity cells - ЕГ шифр, ТГ шифр
/// and Огноо - and each was wrong in its own way while looking finished:
///
///   ЕГ шифр  printed project.Code, which is Studio's own number
///            (STUDIO-20260722-1906) and not an official cipher. The cell was
///            full, and what filled it was a different fact.
///   ТГ шифр  printed a bare label with nothing beside it.
///   Огноо    printed DateTime.Now, so the document date moved every time the
///            album was rebuilt.
///
/// The three fields that answer them were added, stored, and mapped into the
/// album model - and drawn only by DrawCanonicalHorizontalWorkingTitleBlock,
/// which NOTHING CALLS. Entered by a person, written to disk, carried all the
/// way to the writer, and printed by an unreachable method. A test of the
/// fields alone stays green through every part of that.
/// </summary>
public sealed class WorkingTitleBlockIdentityCellsTests
{
    [Fact]
    public void AnUnfilledCellKeepsItsLabel()
    {
        // A cell that drops its label as well as its value is a blank rectangle,
        // which is how these were reported in the first place: as a rendering
        // fault rather than as three fields nobody had.
        Assert.Equal("ЕГ шифр:", PdfSharpAlbumWriter.LabelledCell("ЕГ шифр", ""));
        Assert.Equal("ЕГ шифр:", PdfSharpAlbumWriter.LabelledCell("ЕГ шифр", null));
        Assert.Equal("ЕГ шифр:", PdfSharpAlbumWriter.LabelledCell("ЕГ шифр", "   "));
    }

    [Fact]
    public void AFilledCellJoinsTheValueToTheLabel()
    {
        Assert.Equal("ЕГ шифр: УБ-24/117", PdfSharpAlbumWriter.LabelledCell("ЕГ шифр", " УБ-24/117 "));
    }

    [Fact]
    public void TheSheetDateIsTheONEEntered_NotTheClock()
    {
        var project = new AlbumProject
        {
            SheetDateUtc = new DateTimeOffset(2026, 3, 14, 0, 0, 0, TimeSpan.Zero),
        };

        Assert.Equal("2026.03.14", PdfSharpAlbumWriter.SheetDateText(project));
    }

    [Fact]
    public void AnUnenteredSheetDateStaysEmptyRatherThanBecomingToday()
    {
        // Filling it from the clock is the defect, not the fallback: it turns a
        // document date into "whenever this was last regenerated", and two
        // copies of the same album then disagree about when it was issued.
        Assert.Equal("", PdfSharpAlbumWriter.SheetDateText(new AlbumProject()));
    }

    [Fact]
    public void TheBlockTheWriterACTUALLYUsesReadsTheEnteredFields()
    {
        // The rule above is only worth having where it is asked. The three
        // fields were drawn by a method with no caller while the live block
        // kept printing the project code and the current clock - and every
        // unit test of the fields stayed green through all of it.
        string body = LiveHorizontalTitleBlockBody();

        Assert.Contains("project.GeneralDesignCipher", body, StringComparison.Ordinal);
        Assert.Contains("project.TechnicalDesignCipher", body, StringComparison.Ordinal);
        Assert.Contains("SheetDateText(project)", body, StringComparison.Ordinal);
    }

    [Fact]
    public void TheBlockTheWriterACTUALLYUsesDoesNotInventEitherValue()
    {
        string body = LiveHorizontalTitleBlockBody();

        // The clock, and the Studio project number standing in for a cipher:
        // the two ways these cells were filled with something that was not the
        // fact they name.
        Assert.DoesNotContain("DateTime.Now", body, StringComparison.Ordinal);
        Assert.DoesNotContain("{project.Code}", body, StringComparison.Ordinal);
    }

    [Fact]
    public void TheConceptTablesYEARFollowsTheEnteredSheetDate()
    {
        // The same clock defect lived next door under a different name: the
        // sketch table printed {DateTime.Now:yyyy}, so a December album rebuilt
        // in January was reissued under the following year. Searching for the
        // MECHANISM rather than the wording is what found it.
        var project = new AlbumProject
        {
            SheetDateUtc = new DateTimeOffset(2024, 11, 2, 0, 0, 0, TimeSpan.Zero),
        };

        Assert.Equal(2024, PdfSharpAlbumWriter.CornerTableYear(project));
    }

    [Fact]
    public void WithNoSheetDateTheConceptTableStillPrintsAYear()
    {
        // A bare value in a ruled grid, not a labelled field: an empty cell
        // here reads as a broken table. So the clock stays as the fallback and
        // the entered date simply wins over it.
        Assert.Equal(DateTime.Now.Year, PdfSharpAlbumWriter.CornerTableYear(new AlbumProject()));
    }

    [Fact]
    public void TheRestampSignatureUsesTheSAMEYearAsTheTable()
    {
        // If the signature hashed the clock while the table drew the sheet
        // date, changing the date would redraw the table without changing the
        // signature - and a built album would keep the old year while being
        // reported as current. Read from source, because the signature is a
        // hash and cannot be asked which year went into it.
        string restamp = ReadPdfSource("PdfSharpAlbumWriter.TitleBlockRestamp.cs");

        Assert.Contains("Year = CornerTableYear(project)", restamp, StringComparison.Ordinal);
        Assert.DoesNotContain("Year = DateTime.Now", restamp, StringComparison.Ordinal);
    }

    [Fact]
    public void NoCoverPrintsAHardCodedCity()
    {
        // Every cover this program produced said «Улаанбаатар хот», including
        // one issued by a company registered anywhere else. The contract of
        // 2026-09-06 names the field: the DESIGN ORGANISATION's registered
        // city, not the project's location. PFR deleted the same constant on
        // their side (a9541d1); a constant surviving on either side makes the
        // two covers disagree.
        string writer = ReadPdfSource("PdfSharpAlbumWriter.cs");

        Assert.DoesNotContain("\"Улаанбаатар", writer, StringComparison.Ordinal);
        Assert.Contains("company.RegisteredCity", writer, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePREVIEWOfTheCoverReadsTheSameFieldAsTheWriter()
    {
        // Two drawings of one page. A constant left in the preview would show a
        // city the printed album does not contain, and the difference would be
        // found by whoever printed it rather than by whoever looked.
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        string? shell = null;
        while (directory is not null && shell is null)
        {
            string candidate = Path.Combine(
                directory.FullName, "src", "src", "ErkS.Studio.App", "ShellView.Workspaces.cs");
            if (File.Exists(candidate))
                shell = candidate;
            directory = directory.Parent;
        }

        Assert.NotNull(shell);
        string preview = File.ReadAllText(shell!, Encoding.UTF8);

        Assert.DoesNotContain("\"Улаанбаатар", preview, StringComparison.Ordinal);
        Assert.Contains("company.RegisteredCity", preview, StringComparison.Ordinal);
    }

    /// <summary>One writer source file, read from disk.</summary>
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

    /// <summary>
    /// The body of the horizontal block the writer dispatches to, read from
    /// source. Reading the file is what makes these tests able to fail: the
    /// drawing calls emit into a PDF and cannot be asked what they used.
    /// </summary>
    private static string LiveHorizontalTitleBlockBody()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        string? source = null;
        while (directory is not null && source is null)
        {
            string candidate = Path.Combine(
                directory.FullName, "src", "src", "ErkS.Platform.Pdf", "PdfSharpAlbumWriter.cs");
            if (File.Exists(candidate))
                source = candidate;
            directory = directory.Parent;
        }

        Assert.NotNull(source);
        string text = File.ReadAllText(source!, Encoding.UTF8);
        const string signature = "private static void DrawCanonicalHorizontalWorkingTitleBlockV2";
        int start = text.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(
            start >= 0,
            "DrawCanonicalHorizontalWorkingTitleBlockV2 was renamed; the dispatch in " +
            "DrawRevitWorkingTitleBlock decides which block is live - check this test with it.");
        int end = text.IndexOf(
            "internal static string LabelledCell",
            start,
            StringComparison.Ordinal);
        Assert.True(end > start, "the helpers moved; this test reads the block above them");
        return text[start..end];
    }
}
