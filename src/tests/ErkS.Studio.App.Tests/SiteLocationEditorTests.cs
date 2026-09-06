using System.Text;
using ErkS.Platform.Core;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// The three location combo boxes, checked for the two ways this kind of editor
/// goes wrong.
///
/// FIRST, it can decide things. Rules written into view-assembly code on this
/// platform have been wrong four times over, because nothing can reach them to
/// measure them - so this view is required to be a display of the picker and
/// nothing else. «Capital means khoroo» written here would be wrong for Erdenet
/// and Darkhan and no test would ever say so.
///
/// SECOND, it can be built and never connected. That is the defect this session
/// has found in five separate places, and an editor that collects a location
/// nobody stores is exactly its shape.
/// </summary>
public sealed class SiteLocationEditorTests
{
    [Fact]
    public void TheEditorDECIDESNothing()
    {
        // No label worked out, no unit filtered, no cascade cleared here. Every
        // one of those is a rule, and rules live where they can be tested.
        string view = ReadSiteLocationView();

        Assert.DoesNotContain("Хороо", view, StringComparison.Ordinal);
        Assert.DoesNotContain("Баг", view, StringComparison.Ordinal);
        Assert.DoesNotContain("Дүүрэг", view, StringComparison.Ordinal);
        Assert.DoesNotContain("Capital", view, StringComparison.Ordinal);
        Assert.DoesNotContain(".Where(", view, StringComparison.Ordinal);
        Assert.DoesNotContain(".OrderBy(", view, StringComparison.Ordinal);
    }

    [Fact]
    public void AHeadingNoRuleCouldHaveINVENTEDComesBackUnchanged()
    {
        // THE MECHANISM CHECK, beside the name check rather than instead of it.
        //
        // Forbidding the words «Хороо», «Баг» and «Дүүрэг» in the view catches
        // the obvious regression and searches by NAME - a heading computed
        // under a different spelling, read from a resource or built by
        // concatenation would walk straight past it.
        //
        // This asks the question the other way round: hand it a heading nothing
        // could have derived and see whether that exact string survives. Any
        // computation at all fails, and the test needs to know none of the
        // legitimate words to say so.
        var choices = new AdministrativeUnitChoices("ZZZ-ТЕСТ-9137", []);

        Assert.Equal("ZZZ-ТЕСТ-9137", SiteLocationLabels.HeadingFor(choices));
        Assert.True(SiteLocationLabels.HeadingIsShown(choices));
    }

    [Fact]
    public void ALevelWithNoParentChosenHasNoHeadingToShow()
    {
        var empty = new AdministrativeUnitChoices("", []);

        Assert.Equal("", SiteLocationLabels.HeadingFor(empty));
        Assert.False(SiteLocationLabels.HeadingIsShown(empty));
    }

    [Fact]
    public void ANEMPTYLevelSaysWHICHKindOfEmptyItIs()
    {
        // Two states, one appearance: a combo with nothing in it. One means the
        // level above has not been chosen; the other means the catalogue is
        // complete and this unit genuinely has nothing under it - three real
        // sums, «Баг» with no bags published.
        //
        // Told apart in words, because told apart nowhere else the reader
        // concludes the download failed.
        var waiting = new AdministrativeUnitChoices("", [], ParentIsChosen: false);
        var emptyByData = new AdministrativeUnitChoices("Баг", [], ParentIsChosen: true);

        Assert.Equal("", SiteLocationLabels.EmptyNoticeFor(waiting));
        Assert.Contains("Баг", SiteLocationLabels.EmptyNoticeFor(emptyByData), StringComparison.Ordinal);
    }

    [Fact]
    public void ALevelThatHasUnitsSaysNothingExtra()
    {
        var filled = new AdministrativeUnitChoices(
            "Хороо",
            [new AdministrativeUnit("5110151", "Khoroo", "51101", "1-р хороо", "", false)]);

        Assert.Equal("", SiteLocationLabels.EmptyNoticeFor(filled));
    }

    [Fact]
    public void TheVIEWAsksForTheHeadingRatherThanBuildingOne()
    {
        string view = ReadSiteLocationView();

        // Now DisplayHeadingFor, which also answers what to show while nothing
        // above has been chosen - the case that left two boxes nameless on
        // screen. Still a question asked of the labels rather than a decision
        // made here: the view supplies the provisional words as an argument and
        // does not choose between them and the published heading.
        Assert.Contains("SiteLocationLabels.DisplayHeadingFor(choices, provisionalHeading)", view, StringComparison.Ordinal);
        Assert.Contains("SiteLocationLabels.DistrictProvisionalMn", view, StringComparison.Ordinal);
        Assert.Contains("SiteLocationLabels.WardProvisionalMn", view, StringComparison.Ordinal);
    }

    [Fact]
    public void TheHeadingsAndListsCOMEFromThePicker()
    {
        string view = ReadSiteLocationView();

        // The heading itself moved to SiteLocationLabels so it could be measured
        // rather than only described - see the test above it.
        Assert.Contains("sitePicker.ProvinceChoices()", view, StringComparison.Ordinal);
        Assert.Contains("sitePicker.DistrictChoices()", view, StringComparison.Ordinal);
        Assert.Contains("sitePicker.WardChoices()", view, StringComparison.Ordinal);
        Assert.Contains("sitePicker.UnavailableMessageMn", view, StringComparison.Ordinal);
    }

    [Fact]
    public void TheEditorIsBUILT_BOUNDAndSAVED()
    {
        // Three separate connections, and each has been forgotten somewhere in
        // this codebase already: a control that is never added, a control that
        // is never filled from the project, and a control whose value is never
        // read back. All three look identical from inside the editor.
        string shell = ReadShell();

        Assert.Contains("form.Children.Add(BuildSiteLocationEditor());", shell, StringComparison.Ordinal);
        Assert.Contains("BindSiteLocationEditor();", shell, StringComparison.Ordinal);
        Assert.Contains("basis.SiteLocation = CaptureSiteLocationDraft();", shell, StringComparison.Ordinal);
    }

    [Fact]
    public void TheTypedAddressSurvivesBesideIt()
    {
        // The chosen location does not replace the typed line: every project on
        // disk has only the typed one, and it is what shows while no catalogue
        // has been downloaded.
        string shell = ReadShell();

        Assert.Contains("basis.SiteAddress = siteAddressBox.Text.Trim();", shell, StringComparison.Ordinal);
        Assert.Contains("siteAddressBox.Text = basis.SiteAddress;", shell, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCatalogueIsSTILLTheOnlyThingThatKnowsWhereUnitsComeFrom()
    {
        // The interface earned its keep here. The fetch, the cache and five
        // failure sentences arrived in ONE class, and the picker, the labels, the
        // ordering and the restore above it did not change a line - they had been
        // finished and tested against fixtures months of decisions earlier.
        //
        // What this guards is the drift back: a view or a picker that starts
        // asking about HTTP, caching or staleness has moved a rule to where
        // nothing can reach it, which is where four of this platform's rules went
        // to stop being testable.
        string view = ReadSiteLocationView();

        Assert.DoesNotContain("HttpClient", view, StringComparison.Ordinal);
        Assert.DoesNotContain("administrative-divisions", view, StringComparison.Ordinal);
        Assert.Contains("IAdministrativeUnitCatalogue", ReadCoreSource("AdministrativeUnitPicker.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void TheMessageBeforeAnyDownloadSaysNOTYETRatherThanNEVER()
    {
        // The first wording promised that connecting would make this work while
        // no code fetched anything - «Холбогдсоны дараа сонгох боломжтой болно».
        // The second said this build never fetches, which was true then and is
        // false now. This is the third, and it is the one that has to move with
        // the code: not downloaded YET.
        //
        // Asked of the VALUE, not of the file. An earlier version searched the
        // source text and went red on this test's own explanation quoting the old
        // wording - the check has to look at what is shown, not at what is
        // written about it.
        string shown = StudioAdministrativeUnitCatalogue.Live.UnavailableReasonMn;

        Assert.DoesNotContain("Холбогдсоны дараа", shown, StringComparison.Ordinal);
        Assert.DoesNotContain("энэ хувилбар", shown, StringComparison.Ordinal);
        Assert.Contains("хараахан татагдаагүй", shown, StringComparison.Ordinal);
    }

    [Fact]
    public void TheEditorACTUALLYAsksForTheCatalogueAndREBUILDSAfterwards()
    {
        // Two connections, and the second is the one that would be left out.
        //
        // A stored project keeps CODES; restoring one means finding those codes
        // in a list. The list arrives after the picker was built, so a picker
        // that is not rebuilt shows three empty boxes over a catalogue that is
        // fully loaded - indistinguishable, on screen, from the download having
        // failed. That is this session's recurring shape: both halves healthy,
        // the step that joins them owned by nobody.
        string view = ReadSiteLocationView();

        Assert.Contains("_ = EnsureAdministrativeUnitsAsync();", view, StringComparison.Ordinal);
        Assert.Contains("administrativeUnits.LoadAsync(account.SuggestedServerUrl)", view, StringComparison.Ordinal);
        Assert.Contains("BindSiteLocationEditor();", view, StringComparison.Ordinal);
    }

    [Fact]
    public void WhereTheListCameFromIsSHOWNEvenWhenTheBoxesAreFULL()
    {
        // UnavailableReasonMn is hidden the moment there is something to choose
        // from, which is right - it exists to explain an empty box. Serving a
        // cached copy is precisely the case where there IS something to choose
        // from and the reader still has to be told, so it travels on its own
        // channel and is shown whenever it has anything to say.
        string view = ReadSiteLocationView();

        Assert.Contains("siteLocationSource.Text = administrativeUnits.SourceNoticeMn;", view, StringComparison.Ordinal);
        Assert.Contains("panel.Children.Add(siteLocationSource);", view, StringComparison.Ordinal);
    }

    private static string ReadSiteLocationView() => ReadAppSource("ShellView.SiteLocation.cs");

    private static string ReadShell() => ReadAppSource("ShellView.cs");

    private static string ReadCoreSource(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(
                directory.FullName, "src", "src", "ErkS.Platform.Core", fileName);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate, Encoding.UTF8);
            directory = directory.Parent;
        }

        Assert.Fail(fileName + " was not found; this test reads it from source");
        return "";
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
