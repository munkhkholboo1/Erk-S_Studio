using ErkS.Platform.Contracts;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// The album says what it could not place, and where to go and fix it.
///
/// Written against the shape of the user's real report: a sheet created by
/// AutoCAD's new-sheet command, left unnamed and uncategorised, which the album
/// could not place and put at the end of its section without saying so.
/// </summary>
public sealed class AlbumCompletenessReportTests
{
    private static AlbumDefinition Concept()
    {
        var definition = new AlbumDefinition();
        BuildingArchitectureConceptAlbumTemplate.Ensure(definition);
        return definition;
    }

    [Fact]
    public void ANUnplaceablePageIsREPORTEDRatherThanQuietlySortedLast()
    {
        // 🔴 THE WHOLE POINT. The rank that sends this page to the end already
        // existed and was already correct; what did not exist was anybody
        // asking it out loud. Silence is what left the user unable to find the
        // page's origin at all.
        AlbumDefinition definition = Concept();
        var library = new SheetLibrary();
        definition.Pages.Add(new AlbumPageDefinition
        {
            SheetKey = "acad-source|new",
            ContentKindOverride = "Ангилаагүй",
        });

        AlbumCompletenessReport report = AlbumCompletenessReport.Create(definition, library);

        Assert.Single(report.UnplacedPages);
        Assert.True(report.HasSomethingToSay);
        Assert.NotEqual("", report.UnplacedNoticeMn);
        Assert.Contains("1", report.UnplacedNoticeMn, StringComparison.Ordinal);
    }

    [Fact]
    public void THENoticeNamesWHERETheFixIsMadeNotJustTheCount()
    {
        // «3 хуудас ангилагдаагүй» is half a sentence: it says a person has a
        // problem and not what to do about it. The program the sheet was drawn
        // in is the other half, and it is already known here.
        var page = new UnplacedAlbumPage("k", "04", "ШИНЭ ХУУДАС", "Ангилаагүй", SheetSourceApplication.AutoCad);
        var report = new AlbumCompletenessReport([page], [], 9, 9);

        Assert.Contains("AutoCAD", report.UnplacedNoticeMn, StringComparison.Ordinal);
        Assert.Contains("ШИНЭ ХУУДАС", report.UnplacedNoticeMn, StringComparison.Ordinal);
        // ...and it must actually say what to do, not merely name the program.
        Assert.Contains("зургийн төрөл", report.UnplacedNoticeMn, StringComparison.Ordinal);
    }

    [Fact]
    public void APLACEABLEPageIsNotReported()
    {
        // The positive control for the notice: it has to be able to stay
        // silent, or "nothing to report" and "not looking" are the same state.
        AlbumDefinition definition = Concept();
        definition.Pages.Add(new AlbumPageDefinition
        {
            SheetKey = "revit|s1",
            ContentKindOverride = "Огтлол",
        });

        AlbumCompletenessReport report = AlbumCompletenessReport.Create(definition, new SheetLibrary());

        Assert.Empty(report.UnplacedPages);
        Assert.Equal("", report.UnplacedNoticeMn);
    }

    [Fact]
    public void ASlotIDIsPLACEABLEAndSoIsNotReported()
    {
        // The two vocabularies again: a sheet declaring "sections" is placed
        // perfectly well, and reporting it as a problem would send someone to
        // AutoCAD to fix something that is not broken.
        AlbumDefinition definition = Concept();
        definition.Pages.Add(new AlbumPageDefinition
        {
            SheetKey = "acad|s1",
            ContentKindOverride = "sections",
        });

        Assert.Empty(AlbumCompletenessReport.Create(definition, new SheetLibrary()).UnplacedPages);
    }

    [Fact]
    public void EMPTYSectionsAreNamedNotCounted()
    {
        // «бүрдэл 10/13» is a number nobody can act on: three are missing and
        // there is no way to learn which three, so no way to judge whether it
        // matters. The names are known here already.
        AlbumDefinition definition = Concept();
        definition.Pages.Add(new AlbumPageDefinition
        {
            SheetKey = "revit|s1",
            TemplateSlotId = "sections",
            ContentKindOverride = "Огтлол",
        });

        AlbumCompletenessReport report = AlbumCompletenessReport.Create(definition, new SheetLibrary());

        Assert.Contains(report.EmptySlots, slot => slot.Title.Contains("ДАВХРЫН", StringComparison.Ordinal));
        Assert.DoesNotContain(report.EmptySlots, slot => slot.Title.Contains("ОГТЛОЛ", StringComparison.Ordinal));
        Assert.Contains("ДАВХРЫН", report.EmptySlotsNoticeMn, StringComparison.Ordinal);
        Assert.Contains($"{report.FilledSlotCount}/{report.TotalSlotCount}", report.EmptySlotsNoticeMn, StringComparison.Ordinal);
    }

    [Fact]
    public void GENERATEDSectionsAreNOTCountedAsMissing()
    {
        // 🔴 Studio draws the cover, the organisation page, the planning task
        // and the site plan itself, at build time; they hold no page
        // beforehand. Calling those four "missing" would bury three real gaps
        // among four false ones - which reports nothing, expensively.
        AlbumDefinition definition = Concept();
        AlbumCompletenessReport report = AlbumCompletenessReport.Create(definition, new SheetLibrary());

        Assert.All(
            report.EmptySlots,
            slot => Assert.DoesNotContain(
                definition.Composition.Where(item => item.Kind == AlbumCompositionKind.Generated),
                generated => generated.Title.Equals(slot.Title, StringComparison.Ordinal)));

        Assert.Equal(
            definition.Composition.Count(item => item.Kind == AlbumCompositionKind.SourceSlot),
            report.TotalSlotCount);
    }

    [Fact]
    public void AFullAlbumSaysNOTHING()
    {
        // The silence control for the whole report. A message on every visit
        // teaches people to ignore messages, and the one that matters then goes
        // past unread.
        AlbumDefinition definition = Concept();
        foreach (AlbumCompositionItem slot in definition.Composition
                     .Where(item => item.Kind == AlbumCompositionKind.SourceSlot))
        {
            definition.Pages.Add(new AlbumPageDefinition
            {
                SheetKey = "src|" + slot.Id,
                TemplateSlotId = slot.Id,
                ContentKindOverride = slot.Id,
            });
        }

        AlbumCompletenessReport report = AlbumCompletenessReport.Create(definition, new SheetLibrary());

        Assert.False(report.HasSomethingToSay);
        Assert.Equal("", report.UnplacedNoticeMn);
        Assert.Equal("", report.EmptySlotsNoticeMn);
    }

    [Fact]
    public void MANYUnplacedPagesAreSummarisedRatherThanListedInFull()
    {
        // A notice naming forty sheets is a notice nobody finishes reading.
        // Three examples and a count keeps it actionable.
        var pages = Enumerable.Range(1, 12)
            .Select(index => new UnplacedAlbumPage(
                "k" + index, index.ToString("D2"), "Хуудас " + index, "", SheetSourceApplication.Revit))
            .ToList();
        var report = new AlbumCompletenessReport(pages, [], 9, 9);

        Assert.Contains("12", report.UnplacedNoticeMn, StringComparison.Ordinal);
        Assert.Contains("Хуудас 1", report.UnplacedNoticeMn, StringComparison.Ordinal);
        Assert.DoesNotContain("Хуудас 12", report.UnplacedNoticeMn, StringComparison.Ordinal);
    }
}
