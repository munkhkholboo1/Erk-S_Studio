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

    [Theory]
    [InlineData("Erk-S CAD")]
    [InlineData("erks cad 1.0.0")]
    [InlineData("ErkSCad")]
    public void ErkSCadLikewise(string application)
    {
        Assert.Equal(
            DesignSourceCategory.ErkSCad,
            DesignSourceCategories.Classify(application, isVisualization: false, hasLocalPayload: true));
    }

    [Fact]
    public void ALocalSourcesBadgeFollowsITSOWNKindName()
    {
        // 🔴 THE BRANCH ABOVE WAS UNREACHABLE FOR A LOCAL SOURCE WHEN IT WAS WRITTEN.
        // The workspace row does not pass the application a package reported - it
        // passes `source.Kind.ToString()`. So "Erk-S CAD" could only ever arrive
        // from a CLOUD source, and a local Erk-S CAD source wore the generic
        // «Эх үүсвэр» badge no matter what its packages said.
        //
        // That is why DesignSourceKind gained a matching value rather than the row
        // being changed to read the reported application: switching the row's input
        // would have re-badged every existing local source, whose version strings
        // ("2026.1") name no product at all.
        //
        // Written against the enum's own name so renaming the member is loud here
        // instead of quietly restoring the generic badge.
        Assert.Equal(
            DesignSourceCategory.ErkSCad,
            DesignSourceCategories.Classify(
                DesignSourceKind.ErkSCad.ToString(),
                isVisualization: false,
                hasLocalPayload: true));
    }

    [Fact]
    public void ErkSCadIsNotReadAsAutoCad()
    {
        // The AutoCAD test is a substring test and this product's name ends in the
        // same three letters. Asserted from the losing side: if the order of those
        // two branches is ever swapped, this is what says so.
        Assert.NotEqual(
            DesignSourceCategory.AutoCad,
            DesignSourceCategories.Classify(
                "Erk-S CAD for AutoCAD hosts",
                isVisualization: false,
                hasLocalPayload: true));
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
