using System.Text;
using ErkS.Platform.Core;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// The officials-directory screen, and the three things it must say.
///
/// 🔴 A WINDOW CANNOT BE RUN HERE, SO WHAT IS CHECKED IS WHAT IT IS WIRED TO.
/// The rules worth protecting were deliberately kept OUT of the window - the
/// replacement rule is in Core with its own tests - and what is left in the
/// screen is exactly the wiring: which catalogue, which directory, which
/// sentences reach the person. Those are the things that go wrong silently.
/// </summary>
public sealed class THEDirectoryEditorSaysWhatItKeptTests
{
    [Fact]
    public void THEScreenShowsALLTHREEStates()
    {
        // 🔴 «NOTHING FILLED IN», «THE FILE WOULD NOT READ» AND «SOME ROWS WERE
        // REFUSED» ARE THREE DIFFERENT THINGS TO DO NEXT, and every one of them
        // looks like an empty list from the screen. Showing one sentence for all
        // three is how the person is told to redo work they still have.
        // 🔴 THE ASSIGNMENT, NOT THE MENTION - AND A MUTATION WALKED THROUGH THE
        // LOOSER VERSION. Written as «the text «directory.LossMn» appears», it
        // stayed green while the sentence was replaced by an empty string,
        // because the same words survived one line below in the visibility rule.
        // An anchor that matches somewhere else is an anchor that proves nothing.
        string source = ReadAppSource("OfficialsDirectoryDialog.cs");

        Assert.Contains("sourceText.Text = directory.SourceMn;", source, StringComparison.Ordinal);
        Assert.Contains("lossText.Text = directory.LossMn;", source, StringComparison.Ordinal);
        Assert.Contains(
            "statusText.Text = directory.UnavailableReasonMn;",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void THELossNoticeIsHiddenWhenThereIsNone()
    {
        // A notice that is always on screen is furniture, and furniture is not
        // read - which is how the one time it matters goes past somebody.
        string source = ReadAppSource("OfficialsDirectoryDialog.cs");

        Assert.Contains(
            "lossText.Visibility = directory.LossMn.Length == 0",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void THEScreenREREADSAfterSavingAndReportsTheKEPTCount()
    {
        // 🔴 WHAT THE READER KEPT, NOT WHAT WAS SENT. A screen that reported its
        // own copy would tell somebody a refused row was stored - the silence
        // this whole feature was built against, arriving through the door marked
        // success.
        string source = ReadAppSource("OfficialsDirectoryDialog.cs");
        string body = MethodBody(source, "private void Save()");

        int save = body.IndexOf("directory.Save(", StringComparison.Ordinal);
        int reread = body.IndexOf("directory.All", StringComparison.Ordinal);
        Assert.True(save > 0, "the screen no longer saves through the directory");
        Assert.True(reread > save, "the screen no longer re-reads what the reader kept");
        Assert.Contains("directory.Count", body, StringComparison.Ordinal);
    }

    [Fact]
    public void NOSecondCascadeWasBuiltForThisScreen()
    {
        // 🔴 THE SAME MECHANISM UNDER A DIFFERENT NAME IS MAINTAINED TWICE AND
        // FIXED ONCE. AdministrativeUnitPicker already holds the cascade, in
        // Core, tested - this screen simply does not ask for wards.
        string source = ReadAppSource("OfficialsDirectoryDialog.cs");

        Assert.Contains("new AdministrativeUnitPicker(catalogue)", source, StringComparison.Ordinal);
        Assert.Contains("picker.ProvinceChoices()", source, StringComparison.Ordinal);
        Assert.Contains("picker.DistrictChoices()", source, StringComparison.Ordinal);

        // No ward level: the directory is keyed at the five-digit level, and
        // offering a ward would let somebody file a row nothing can look up.
        Assert.DoesNotContain("WardChoices", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ChooseWard", source, StringComparison.Ordinal);
    }

    [Fact]
    public void THESameCatalogueInstanceAsEverySchemeElse()
    {
        // A second catalogue is a second answer to «which districts exist», and
        // two screens disagreeing about that is quiet and permanent.
        string companies = ReadAppSource("ShellView.Companies.cs");
        string body = MethodBody(companies, "private void OpenOfficialsDirectory()");

        Assert.Contains("StudioOfficialsDirectory.Live", body, StringComparison.Ordinal);
        Assert.Contains("administrativeUnits", body, StringComparison.Ordinal);
        Assert.DoesNotContain("new StudioAdministrativeUnitCatalogue", body, StringComparison.Ordinal);
    }

    [Fact]
    public void AROWCannotBeStartedBeforeADistrictIsChosen()
    {
        // Collected and then discarded on save is the silent version: the person
        // has no way to know which of their rows went.
        string source = ReadAppSource("OfficialsDirectoryDialog.cs");
        string body = MethodBody(source, "private void AddRow()");

        Assert.Contains("ChosenUnitCode.Length == 0", body, StringComparison.Ordinal);
        Assert.Contains("return;", body, StringComparison.Ordinal);
    }

    [Fact]
    public void THEScreenIsREACHABLEFromTheCompaniesPage()
    {
        // 🔴 A SCREEN NOTHING OPENS IS A SCREEN THAT DOES NOT EXIST - and this
        // whole chain has already produced one hole of exactly that shape, where
        // a roster could be stored and not typed.
        string companies = ReadAppSource("ShellView.Companies.cs");

        Assert.Contains("officialsDirectoryButton.Click", companies, StringComparison.Ordinal);
        Assert.Contains(
            "actions.Children.Add(officialsDirectoryButton);",
            companies,
            StringComparison.Ordinal);
    }

    [Fact]
    public void EVERYKindOfBodyHasANameAPersonCanRead()
    {
        // 🔴 DERIVED FROM THE ENUM. A kind with no label prints as a blank row in
        // the chooser - selectable, indistinguishable from its neighbour, and
        // impossible for somebody to report.
        foreach (OfficialBodyKind kind in Enum.GetValues<OfficialBodyKind>())
        {
            string label = OfficialBodyLabels.Mongolian(kind);
            Assert.False(
                string.IsNullOrWhiteSpace(label),
                $"«{kind}» has no Mongolian name");
        }

        // And no two share one, or the chooser offers the same word twice.
        var labels = Enum.GetValues<OfficialBodyKind>()
            .Select(OfficialBodyLabels.Mongolian)
            .ToList();
        Assert.Equal(labels.Count, labels.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void THELabelIsNOTTheStoredValue()
    {
        // 🔴 THE FILE KEEPS THE ENUM'S OWN NAME; THE SCREEN SHOWS MONGOLIAN. If
        // they were the same string, renaming a label would invalidate every file
        // already written - and the reader accepts exactly one spelling.
        foreach (OfficialBodyKind kind in Enum.GetValues<OfficialBodyKind>())
        {
            Assert.NotEqual(
                kind.ToString(),
                OfficialBodyLabels.Mongolian(kind));
        }

        string written = StudioOfficialsDirectory.Serialize(
            [
                new OfficialsDirectoryEntry
                {
                    UnitCode = "01103",
                    Kind = OfficialBodyKind.PublicHealth,
                    OrganizationName = "Байгууллага",
                },
            ],
            DateTimeOffset.UnixEpoch);

        Assert.Contains("PublicHealth", written, StringComparison.Ordinal);
    }

    private static string MethodBody(string source, string signature)
    {
        string normalised = source.Replace("\r\n", "\n");
        int start = normalised.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start > 0, signature + " was not found");
        int end = normalised.IndexOf("\n    }", start, StringComparison.Ordinal);
        Assert.True(end > start, "the end of " + signature + " was not found");
        return normalised[start..end];
    }

    private static string ReadAppSource(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(
                directory.FullName, "src", "src", "ErkS.Studio.App", fileName);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate, Encoding.UTF8);
            directory = directory.Parent;
        }

        Assert.Fail(fileName + " was not found; this test reads it from source");
        return "";
    }
}
