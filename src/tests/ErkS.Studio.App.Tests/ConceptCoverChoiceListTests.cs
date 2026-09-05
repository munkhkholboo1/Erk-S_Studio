using ErkS.Platform.Core;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// The entries offered in the concept-cover picker.
///
/// The list matters more than a list usually would because of its first row:
/// that is what a project which has never chosen shows, and choosing it must
/// leave the album printing exactly what it prints today. Twenty-four projects
/// on disk predate the setting.
/// </summary>
public sealed class ConceptCoverChoiceListTests
{
    [Fact]
    public void TheFirstEntryIsTheONEThatChangesNothing()
    {
        Assert.Equal(
            AlbumConceptCoverStyles.TemplateDecides,
            ProjectConceptCoverChoices.All[0].Value);
        Assert.False(AlbumConceptCoverStyles.UsesSheet2026(
            ProjectConceptCoverChoices.All[0].Value));
    }

    [Fact]
    public void EveryEntryIsAValueThisBuildRecognises()
    {
        // An entry the normaliser does not know would be offered, chosen, and
        // then silently turned back into "leave it alone" - a control that
        // appears to work and does nothing.
        Assert.All(
            ProjectConceptCoverChoices.All,
            choice => Assert.True(
                AlbumConceptCoverStyles.IsKnown(choice.Value),
                choice.Value));
    }

    [Fact]
    public void AStoredValueComesBackAsItsOwnEntry()
    {
        Assert.Equal(
            AlbumConceptCoverStyles.Sheet2026,
            ProjectConceptCoverChoices.Resolve(AlbumConceptCoverStyles.Sheet2026).Value);

        // Including a value from a newer Studio: it resolves to the entry that
        // leaves the album alone rather than throwing on open.
        Assert.Equal(
            AlbumConceptCoverStyles.TemplateDecides,
            ProjectConceptCoverChoices.Resolve("concept-cover-a2-2031").Value);
    }

    [Fact]
    public void TheHintNamesTheCONSEQUENCE_NotTheChoice()
    {
        // What cannot be seen from the settings page is which of two different
        // drawings the album comes out with - including that ХЯНАСАН is still
        // printed empty, which somebody would otherwise report as a fault.
        string newSheet = ProjectConceptCoverChoices.Explain(AlbumConceptCoverStyles.Sheet2026);

        Assert.Contains("ЗӨВШИЛЦСӨН", newSheet, StringComparison.Ordinal);
        Assert.Contains("ХЯНАСАН", newSheet, StringComparison.Ordinal);
        Assert.Contains("хоосон", newSheet, StringComparison.Ordinal);
    }
}
