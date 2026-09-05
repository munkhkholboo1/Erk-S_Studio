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
    public void TheVIEWAsksForTheHeadingRatherThanBuildingOne()
    {
        string view = ReadSiteLocationView();

        Assert.Contains("SiteLocationLabels.HeadingFor(choices)", view, StringComparison.Ordinal);
        Assert.Contains("SiteLocationLabels.HeadingIsShown(choices)", view, StringComparison.Ordinal);
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
    public void TheCatalogueHasONEReplacementPoint()
    {
        // The route does not exist yet, so the list is empty - a real state,
        // reached by any offline machine once it does exist. What matters is
        // that swapping it changes one class and no rule: the picker, the
        // labels, the ordering and the restore all sit above the interface.
        string catalogue = ReadAppSource("StudioAdministrativeUnitCatalogue.cs");

        Assert.Contains("IAdministrativeUnitCatalogue", catalogue, StringComparison.Ordinal);
        Assert.Contains("public DateTimeOffset? AsOfUtc => null;", catalogue, StringComparison.Ordinal);
    }

    private static string ReadSiteLocationView() => ReadAppSource("ShellView.SiteLocation.cs");

    private static string ReadShell() => ReadAppSource("ShellView.cs");

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
