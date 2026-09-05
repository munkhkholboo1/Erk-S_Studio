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

    /// <summary>
    /// The body of the horizontal block the writer dispatches to, read from
    /// source. Reading the file is what makes this test able to fail: the
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
