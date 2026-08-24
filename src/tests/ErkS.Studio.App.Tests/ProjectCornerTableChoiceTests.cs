using ErkS.Platform.Core;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// The picker on the project information page.
///
/// A title block is identified by its size on the page - that is how the
/// people using this talk about it - so the labels carry the measurements
/// rather than a name only Studio would recognise.
/// </summary>
public sealed class ProjectCornerTableChoiceTests
{
    [Fact]
    public void EveryStoredStyleHasSomethingToShowForIt()
    {
        // A value with no entry would leave the picker blank and the user
        // unable to tell what their project is set to.
        foreach (string value in new[]
                 {
                     AlbumCornerTableStyles.TemplateDecides,
                     AlbumCornerTableStyles.Concept,
                     AlbumCornerTableStyles.WorkingDrawing,
                 })
        {
            Assert.Equal(value, ProjectCornerTableChoices.Resolve(value).Value);
        }
    }

    [Fact]
    public void AnUnknownStoredValueLandsOnTheTemplateEntry()
    {
        // Rather than throwing while a project is being opened.
        Assert.Equal(
            AlbumCornerTableStyles.TemplateDecides,
            ProjectCornerTableChoices.Resolve("gost-185x55").Value);
    }

    [Fact]
    public void TheLabelsCarryTheMeasurements()
    {
        Assert.Contains(
            ProjectCornerTableChoices.All,
            choice => choice.Label.Contains("190×28", StringComparison.Ordinal));
        Assert.Contains(
            ProjectCornerTableChoices.All,
            choice => choice.Label.Contains("180×36", StringComparison.Ordinal));
    }

    [Fact]
    public void TheHintSaysTheOtherProductsFollowToo()
    {
        // Nothing else on the page shows that this choice leaves Studio.
        Assert.Contains("AutoCAD", ProjectCornerTableChoices.Explain(AlbumCornerTableStyles.Concept));
        Assert.Contains("Revit", ProjectCornerTableChoices.Explain(AlbumCornerTableStyles.WorkingDrawing));
    }

    [Fact]
    public void TheWorkingDrawingHintSaysTheGridDoesNotComeWithIt()
    {
        // The larger block normally travels with a reference grid. Choosing it
        // here does not bring one, and that is the surprise worth heading off.
        Assert.Contains(
            "тор",
            ProjectCornerTableChoices.Explain(AlbumCornerTableStyles.WorkingDrawing));
    }

    [Fact]
    public void BothConcreteChoicesSayTheyOnlyAffectNewSheets()
    {
        // AutoCAD freezes the style into a sheet when the sheet is created, so
        // a frame already drawn cannot change under whoever drew it. That is
        // right, and it means switching the style moves nothing already on the
        // page - which looks exactly like a setting that does not work.
        foreach (string value in new[]
                 { AlbumCornerTableStyles.Concept, AlbumCornerTableStyles.WorkingDrawing })
        {
            Assert.Contains("шинээр үүсгэх хуудсанд", ProjectCornerTableChoices.Explain(value));
        }
    }

    [Fact]
    public void TheDefaultEntryPromisesNothingChanges()
    {
        Assert.Contains(
            "өөрчлөгдөхгүй",
            ProjectCornerTableChoices.Explain(AlbumCornerTableStyles.TemplateDecides));
    }
}
