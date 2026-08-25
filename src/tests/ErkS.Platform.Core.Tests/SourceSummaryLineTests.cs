using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// Built from the rows the user actually has open, not from short invented
/// ones - the complaint was that real rows did not fit, and a test with
/// "Source A | Local" would have passed while the screen stayed broken.
/// </summary>
public sealed class SourceSummaryLineTests
{
    [Fact]
    public void ARealRevitRowLosesTheFactsTheRowShowsElsewhere()
    {
        string summary = SourceSummaryLine.Compose(
            "Erin_Apartment_Type_1_Sheet.rvt | Revit | Архитектур | Локал | Холбогдсон | Альбум #3",
            categoryLabel: "Revit",
            application: "Revit");

        // The badge says Revit; the line says everything else.
        Assert.DoesNotContain("Revit", summary);
        Assert.Contains("Архитектур", summary);
        Assert.Contains("Альбум #3", summary);
        Assert.DoesNotContain("|", summary);
    }

    [Fact]
    public void TheOwnersAddressIsDroppedBecauseTheGroupHeadingCarriesIt()
    {
        string summary = SourceSummaryLine.Compose(
            "Cloud | tungalagtuul.telmen@erk-s.mn | 3 sheet | Орон сууц-1 | #1000 | Зөвхөн харах",
            categoryLabel: "Үүлнээс");

        Assert.DoesNotContain("@", summary);
        Assert.Contains("3 sheet", summary);
        Assert.Contains("Зөвхөн харах", summary);
    }

    [Fact]
    public void ALongRowStaysWholeRatherThanBeingCutHere()
    {
        // Shortening belongs to the layout, which wraps. Cutting the text at
        // its source would lose the end of it for every reader, at every
        // window width - which is how the row became unreadable in the first
        // place.
        const string detail =
            "Erin_Apartment_Type_1_Sheet.rvt | Revit | Архитектур төлөвлөлт | "
            + "Локал | Холбогдсон | Альбум #3 | Сүүлд 2026-08-25 03:14";

        string summary = SourceSummaryLine.Compose(detail, "Revit", "Revit");

        Assert.DoesNotContain("…", summary);
        Assert.Contains("Сүүлд 2026-08-25 03:14", summary);
        Assert.True(summary.Length > 60);
    }

    [Fact]
    public void NothingToShowIsAnEmptyLineRatherThanSeparators()
    {
        Assert.Equal("", SourceSummaryLine.Compose("Revit", "Revit", "Revit"));
        Assert.Equal("", SourceSummaryLine.Compose(null));
        Assert.Equal("", SourceSummaryLine.Compose(" |  | "));
    }

    [Fact]
    public void AFactThatMerelyContainsTheKindIsKept()
    {
        // "Revit" is dropped; "Revit 2026 багц" is a different fact and stays.
        string summary = SourceSummaryLine.Compose(
            "Revit | Revit 2026 багц | Локал",
            categoryLabel: "Revit",
            application: "Revit");

        Assert.Contains("Revit 2026 багц", summary);
        Assert.Contains("Локал", summary);
    }
}
