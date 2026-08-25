using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

public sealed class DesignSourceCategoriesTests
{
    [Theory]
    [InlineData("Revit")]
    [InlineData("revit")]
    [InlineData("Autodesk Revit 2026")]
    public void RevitIsRecognisedHoweverItNamesItself(string application)
    {
        Assert.Equal(
            DesignSourceCategory.Revit,
            DesignSourceCategories.Classify(application, isVisualization: false, hasLocalPayload: true));
    }

    [Theory]
    [InlineData("AutoCAD")]
    [InlineData("autocad 2026")]
    [InlineData("ACAD")]
    public void AutoCadLikewise(string application)
    {
        Assert.Equal(
            DesignSourceCategory.AutoCad,
            DesignSourceCategories.Classify(application, isVisualization: false, hasLocalPayload: true));
    }

    [Fact]
    public void TheVisualizationSourceOutranksWhateverProducedItsImages()
    {
        Assert.Equal(
            DesignSourceCategory.Visualization,
            DesignSourceCategories.Classify("Revit", isVisualization: true, hasLocalPayload: true));
    }

    [Fact]
    public void AKnownApplicationIsShownEvenWhenTheSourceIsSomebodyElses()
    {
        // "Revit" tells the reader more than "somebody else's" - and whose it
        // is already heads the group the row sits in.
        Assert.Equal(
            DesignSourceCategory.Revit,
            DesignSourceCategories.Classify("Revit", isVisualization: false, hasLocalPayload: false));
    }

    [Fact]
    public void NothingLocalAndNothingNamedReadsAsCloud()
    {
        Assert.Equal(
            DesignSourceCategory.Cloud,
            DesignSourceCategories.Classify("", isVisualization: false, hasLocalPayload: false));
    }

    [Fact]
    public void NothingNamedButLocalStaysUnknownRatherThanGuessing()
    {
        Assert.Equal(
            DesignSourceCategory.Unknown,
            DesignSourceCategories.Classify(null, isVisualization: false, hasLocalPayload: true));
    }

    [Fact]
    public void EveryCategoryHasSomethingToShow()
    {
        foreach (DesignSourceCategory category in Enum.GetValues<DesignSourceCategory>())
            Assert.False(string.IsNullOrWhiteSpace(DesignSourceCategories.Label(category)));
    }
}
