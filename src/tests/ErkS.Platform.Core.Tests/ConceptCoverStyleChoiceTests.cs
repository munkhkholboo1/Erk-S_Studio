using System.Text;
using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// Two concept covers now exist, and the setting that chooses between them is
/// where this can go quietly wrong.
///
/// The user asked for the old cover to stay - "солигдож болдгоор хийх
/// хэрэгтэй", a setting they can change and change back. So the question is not
/// which cover is better but what happens to the twenty-four albums that were
/// built before the setting existed, and the answer has to be: nothing.
/// </summary>
public sealed class ConceptCoverStyleChoiceTests
{
    [Fact]
    public void BlankMeansTheCoverTheAlbumALREADYDraws()
    {
        // Not "the newest". A blank that chose the 2026 sheet would reprint
        // every existing album as a document its owner has never seen, the next
        // time anyone pressed build.
        Assert.False(AlbumConceptCoverStyles.UsesSheet2026(null));
        Assert.False(AlbumConceptCoverStyles.UsesSheet2026(""));
        Assert.False(AlbumConceptCoverStyles.UsesSheet2026("   "));
        Assert.Equal(AlbumConceptCoverStyles.TemplateDecides, new ProjectAlbumStyle().ConceptCover);
        Assert.Equal(AlbumConceptCoverStyles.TemplateDecides, new AlbumProject().ConceptCoverStyle);
    }

    [Fact]
    public void ChoosingTheNewSheetIsWhatTurnsItOn()
    {
        Assert.True(AlbumConceptCoverStyles.UsesSheet2026(AlbumConceptCoverStyles.Sheet2026));
        Assert.True(AlbumConceptCoverStyles.UsesSheet2026("  CONCEPT-COVER-A4-2026  "));
        Assert.False(AlbumConceptCoverStyles.UsesSheet2026(AlbumConceptCoverStyles.Classic));
    }

    [Fact]
    public void AStyleFromANewerStudioLeavesTheAlbumAsItIs()
    {
        // The same rule the corner-table setting learnt: an unrecognised value
        // is not guessed at, because guessing reprints a document.
        Assert.Equal(
            AlbumConceptCoverStyles.TemplateDecides,
            AlbumConceptCoverStyles.Normalize("concept-cover-a2-2031"));
        Assert.False(AlbumConceptCoverStyles.UsesSheet2026("concept-cover-a2-2031"));
    }

    [Fact]
    public void KnownAndNormalisedAreDifferentQuestions()
    {
        // IsKnown must not consult Normalize, or every value is "known" and the
        // question answers itself - the trap its neighbour documents.
        Assert.True(AlbumConceptCoverStyles.IsKnown(""));
        Assert.True(AlbumConceptCoverStyles.IsKnown(AlbumConceptCoverStyles.Sheet2026));
        Assert.False(AlbumConceptCoverStyles.IsKnown("concept-cover-a2-2031"));
    }

    [Fact]
    public void TheWriterACTUALLYAsksTheSetting()
    {
        // The drawing routine existed for a whole commit before anything called
        // it. Written and unreachable is the defect this codebase found four
        // times over one night; a test of the setting alone stays green through
        // it, because the setting is not what draws.
        string writer = ReadWriterSource();

        Assert.Contains(
            "AlbumConceptCoverStyles.LayoutFor(request.Project.ConceptCoverStyle)",
            writer,
            StringComparison.Ordinal);
        Assert.Contains(
            "DrawConceptCoverSheet2026(document, request, item, layout);",
            writer,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ONEDrawingRoutineServesBOTHSheets()
    {
        // 🔴 THE REQUIREMENT THAT SHAPED THE WHOLE CHANGE. Copying
        // DrawConceptCoverSheet2026 and editing its constants was the quick way
        // to get A3, and it is the defect this codebase has now catalogued
        // several times: two copies means every later fix has to be made twice,
        // and one of the two is forgotten.
        //
        // Counted rather than described, because a second copy is exactly what
        // a reader skimming for it would miss.
        string writer = ReadWriterSource();
        string sheet = ReadConceptCoverSource();

        Assert.Equal(1, Occurrences(sheet, "private static void DrawConceptCoverSheet2026("));
        Assert.Equal(1, Occurrences(writer + sheet, "DrawConceptCoverSheet2026(document, request, item, layout);"));

        // And the sheet size reaches the drawing as DATA, never as a branch
        // inside it. A single `if (a3)` here would be the copy back again, in
        // one method instead of two files.
        Assert.DoesNotContain("ConceptCoverLayout.A3", sheet, StringComparison.Ordinal);
        Assert.DoesNotContain("Sheet2026A3", sheet, StringComparison.Ordinal);
    }

    [Fact]
    public void THEA3SheetIsChosenTheSameWayAndBlankStillMeansNEITHER()
    {
        Assert.Equal(ConceptCoverLayout.A4, AlbumConceptCoverStyles.LayoutFor(AlbumConceptCoverStyles.Sheet2026));
        Assert.Equal(ConceptCoverLayout.A3, AlbumConceptCoverStyles.LayoutFor(AlbumConceptCoverStyles.Sheet2026A3));
        Assert.Equal(ConceptCoverLayout.A3, AlbumConceptCoverStyles.LayoutFor("  CONCEPT-COVER-A3-2026  "));

        // The two dozen albums on disk are still untouched by a blank, and an
        // unrecognised style still draws what it draws today.
        Assert.Null(AlbumConceptCoverStyles.LayoutFor(null));
        Assert.Null(AlbumConceptCoverStyles.LayoutFor(""));
        Assert.Null(AlbumConceptCoverStyles.LayoutFor(AlbumConceptCoverStyles.Classic));
        Assert.Null(AlbumConceptCoverStyles.LayoutFor("concept-cover-a2-2031"));
        Assert.True(AlbumConceptCoverStyles.IsKnown(AlbumConceptCoverStyles.Sheet2026A3));
    }

    private static int Occurrences(string text, string needle) =>
        text.Split(needle).Length - 1;

    private static string ReadConceptCoverSource()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(
                directory.FullName,
                "src",
                "src",
                "ErkS.Platform.Pdf",
                "PdfSharpAlbumWriter.ConceptCover2026.cs");
            if (File.Exists(candidate))
                return File.ReadAllText(candidate, Encoding.UTF8);
            directory = directory.Parent;
        }

        Assert.Fail("PdfSharpAlbumWriter.ConceptCover2026.cs was not found; this test reads it from source");
        return "";
    }

    [Fact]
    public void TheSettingReachesTheWriterFromTheProjectFile()
    {
        // Between the file and the writer sits the mapping into AlbumProject.
        // Forgetting that line leaves the setting readable, storable, editable
        // - and with no effect at all.
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        string? appState = null;
        while (directory is not null && appState is null)
        {
            string candidate = Path.Combine(
                directory.FullName, "src", "src", "ErkS.Studio.App", "AppState.cs");
            if (File.Exists(candidate))
                appState = File.ReadAllText(candidate, Encoding.UTF8);
            directory = directory.Parent;
        }

        Assert.NotNull(appState);
        Assert.Contains("ConceptCoverStyle = AlbumConceptCoverStyles.Normalize(", appState!, StringComparison.Ordinal);
        Assert.Contains("Project.AlbumStyle.ConceptCover", appState!, StringComparison.Ordinal);
    }

    [Fact]
    public void TheChoiceIsREACHABLEFromTheProjectPage()
    {
        // «Солигдож болдгоор хийх хэрэгтэй» - a setting the user changes and
        // changes back. A value that can only be set by editing the project
        // file does not meet that, and it is also how the two covers would
        // never be compared side by side.
        //
        // Four connections, each forgotten somewhere in this codebase already:
        // offered, filled from the project, written back on save, and written
        // back on the OTHER save path - there are two, and they must not drift.
        string shell = ReadShellSource();

        Assert.Contains("conceptCoverBox.ItemsSource = ProjectConceptCoverChoices.All;", shell, StringComparison.Ordinal);
        Assert.Contains("ProjectConceptCoverChoices.Resolve(project.AlbumStyle.ConceptCover)", shell, StringComparison.Ordinal);
        Assert.Equal(
            2,
            shell.Split("ApplySelectedConceptCover()").Length - 1 - 1);
    }

    private static string ReadShellSource()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(
                directory.FullName, "src", "src", "ErkS.Studio.App", "ShellView.cs");
            if (File.Exists(candidate))
                return File.ReadAllText(candidate, Encoding.UTF8);
            directory = directory.Parent;
        }

        Assert.Fail("ShellView.cs was not found; this test reads it from source");
        return "";
    }

    private static string ReadWriterSource()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(
                directory.FullName, "src", "src", "ErkS.Platform.Pdf", "PdfSharpAlbumWriter.cs");
            if (File.Exists(candidate))
                return File.ReadAllText(candidate, Encoding.UTF8);
            directory = directory.Parent;
        }

        Assert.Fail("PdfSharpAlbumWriter.cs was not found; this test reads it from source");
        return "";
    }
}
