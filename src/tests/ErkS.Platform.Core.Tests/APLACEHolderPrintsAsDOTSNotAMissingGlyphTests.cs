using ErkS.Platform.Pdf;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// A blank the owner still has to fill («⚠» in the note) prints as «……»: the
/// sheet font has no ⚠ glyph and it printed as «□» (PFR, Batnasan, 2026-10-20).
/// </summary>
public sealed class APLACEHolderPrintsAsDOTSNotAMissingGlyphTests
{
    [Fact]
    public void TheWarningSignBecomesThePlaceholder()
    {
        string printed = PdfSharpAlbumWriter.PrintableNote("Ажлын зургийг [⚠ АТД-ын дугаар] даалгаврын дагуу; ⚠ огноо.");

        Assert.DoesNotContain("⚠", printed, StringComparison.Ordinal);
        Assert.Equal("Ажлын зургийг […… АТД-ын дугаар] даалгаврын дагуу; …… огноо.", printed);
    }

    [Fact]
    public void TheNoteIsPrintedFromThePrintableText()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src", "ErkS.Platform.Pdf")))
            directory = directory.Parent;
        string writer = File.ReadAllText(Path.Combine(directory!.FullName, "src", "ErkS.Platform.Pdf", "PdfSharpAlbumWriter.FrontMatter.cs"));

        int count = writer.IndexOf("int pending = text.Count(character => character == '⚠');", StringComparison.Ordinal);
        int printable = writer.IndexOf("text = PrintableNote(text);", StringComparison.Ordinal);
        int lines = writer.IndexOf("NoteLines(gfx, text, width, size)", StringComparison.Ordinal);
        Assert.True(count > 0 && printable > count && lines > printable);
    }
}
