using System.Text;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// There is one concept cover and no way to choose another.
///
/// 🔴 THIS FILE USED TO TEST THE CHOICE ITSELF - that the list offered three
/// covers and explained each. The owner removed the whole question: «Толгой
/// эргүүлсэн өмнөх хувилбар болон А4 хувилбарыг бүрэн хасаад зөвхөн шинэ А3
/// форматыг загвар зурагт СОНГОЛТГҮЙ үүсгэдэг болго.» · «А4 нүүр хуудас гэж
/// байхгүй ээ. Хэрэглэгдэхгүй».
///
/// A test named for a feature that no longer exists is a stale claim that stays
/// green, so the file now asserts the REMOVAL - and asserts it by counting what
/// can still reach the drawing, because «the word A4 is absent» would prove
/// nothing about which sheet is drawn.
/// </summary>
public sealed class ConceptCoverChoiceListTests
{
    [Fact]
    public void EXACTLYOneCallerDrawsTheCoverAndItPassesA3()
    {
        // 🔴 COUNTED, NOT DESCRIBED. A second caller - a leftover branch, a
        // migration path, a preview - is how the old sheet comes back for
        // somebody without anyone noticing. The count is the claim.
        string writer = ReadSource("ErkS.Platform.Pdf", "PdfSharpAlbumWriter.cs");
        string sheet = ReadSource("ErkS.Platform.Pdf", "PdfSharpAlbumWriter.ConceptCover2026.cs");

        Assert.Equal(1, Occurrences(writer + sheet, "DrawConceptCoverSheet2026(document, request, item,"));
        Assert.Contains(
            "DrawConceptCoverSheet2026(document, request, item, ConceptCoverLayout.A3);",
            writer,
            StringComparison.Ordinal);
    }

    [Fact]
    public void NOTHINGReadsAStoredCoverStyleToDecideWhatToDraw()
    {
        // The setting could still sit in old project files; what matters is that
        // no drawing decision consults it. A project that once chose the A4 sheet
        // gets the measured A3 like everybody else.
        string writer = ReadSource("ErkS.Platform.Pdf", "PdfSharpAlbumWriter.cs");

        Assert.DoesNotContain("LayoutFor(", writer, StringComparison.Ordinal);
        Assert.DoesNotContain("ConceptCoverStyle", writer, StringComparison.Ordinal);
    }

    [Fact]
    public void THEChoiceSURFACEIsGoneFromTheShell()
    {
        // The combo box, its hint and the type behind them. Leaving a disabled
        // control that silently always means A3 would be the choice pretending
        // not to be one.
        string shell = ReadSource("ErkS.Studio.App", "ShellView.cs");

        Assert.DoesNotContain("conceptCoverBox", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("ProjectConceptCoverChoices", shell, StringComparison.Ordinal);
        Assert.False(
            SourceExists("ErkS.Studio.App", "ProjectConceptCoverChoice.cs"),
            "the cover-choice type is still in the project");
    }

    private static int Occurrences(string text, string needle) => text.Split(needle).Length - 1;

    private static bool SourceExists(string project, string fileName) =>
        TryFindSource(project, fileName) is not null;

    private static string ReadSource(string project, string fileName)
    {
        string? path = TryFindSource(project, fileName);
        Assert.True(path is not null, fileName + " was not found; this test reads it from source");
        return File.ReadAllText(path!, Encoding.UTF8);
    }

    private static string? TryFindSource(string project, string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "src", "src", project, fileName);
            if (File.Exists(candidate))
                return candidate;
            if (Directory.Exists(Path.Combine(directory.FullName, "src", "src", project)))
                return null;
            directory = directory.Parent;
        }

        return null;
    }
}
