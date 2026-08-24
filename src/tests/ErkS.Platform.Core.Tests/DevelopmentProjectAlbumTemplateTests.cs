using ErkS.Platform.Core.ProjectTypes.UrbanPlanning;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// Барилгажилтын төслийн альбом.
///
/// The third member of the urban planning family. Until it existed, a project
/// at this stage fell through to the building concept album without a word,
/// and the client opened their development project to find a cover reading
/// /ЗАГВАР ЗУРАГ/.
///
/// The order is not from a clause of a standard - it is from the reference
/// album the client works from, recorded in
/// docs/DEVELOPMENT-PROJECT-ALBUM-CONTRACT.md, which AutoCAD builds from too.
/// These check this side against that document, so the two products cannot
/// drift apart without something failing here.
/// </summary>
public sealed class DevelopmentProjectAlbumTemplateTests
{
    private const string StageType = "development-project";

    private static AlbumDefinition Definition() =>
        UrbanPlanningAlbumTemplate.CreateDefinition(StageType);

    [Fact]
    public void TheStageIsNoLongerFallenThroughSilently()
    {
        Assert.True(UrbanPlanningAlbumTemplate.Supports("urban-planning", StageType));

        var project = new ProjectWorkspace();
        project.Identity.ProjectType = "urban-planning";
        project.Identity.StageCode = StageType;
        project.Identity.StageName = "Барилгажилтын төсөл";

        Assert.Null(ProjectAlbumTemplateResolver.DescribeCoverage(project).Notice);
    }

    [Fact]
    public void TheAlbumIsNamedForTheStageRatherThanTheConceptItBorrowed()
    {
        // The client's actual complaint: a development project whose cover
        // said /ЗАГВАР ЗУРАГ/.
        Assert.Equal("Барилгажилтын төсөл", Definition().Title);
        Assert.Equal(
            UrbanPlanningAlbumTemplate.DevelopmentProjectTemplateId,
            Definition().TemplateId);
    }

    [Fact]
    public void TheReferenceAlbumsThirtySlotsAreAllThere()
    {
        // Six pages Studio makes plus the thirty AutoCAD fills.
        IReadOnlyList<AlbumCompositionItem> composition = Definition().Composition;

        Assert.Equal(36, composition.Count);
        Assert.Equal(6, composition.Count(item => item.Kind == AlbumCompositionKind.Generated));
        Assert.Equal(30, composition.Count(item => item.Kind == AlbumCompositionKind.SourceSlot));
    }

    [Theory]
    [InlineData("cover")]
    [InlineData("drawing-list-and-notes")]
    [InlineData("design-organization")]
    [InlineData("planning-task")]
    public void ThePagesTheReferenceAlbumLeavesUnnumberedCarryNoNumber(string id)
    {
        // In the two sibling templates the cover is ЕТ-01. Here the reference
        // album starts the ЕТ counter at the location scheme, and following
        // the family convention instead would shift every drawing number away
        // from the album the client is holding.
        Assert.Equal("", Slot(id).Number);
    }

    [Theory]
    [InlineData("location-scheme", "ЕТ-01", "М1:200000")]
    [InlineData("site-overview", "ЕТ-02", "М1:20000")]
    [InlineData("topographic-base", "ЕТ-03", "М1:1500")]
    [InlineData("land-use-survey", "ЕТ-09", "М1:16000")]
    [InlineData("existing-engineering-preparation", "ИДБ-01", "М1:1500")]
    [InlineData("general-plan-zoning", "ЕТ-14", "М1:1500")]
    [InlineData("communications-and-signaling", "ИДБ-10", "М1:1500")]
    public void TheNumbersAndScalesMatchTheReferenceAlbum(string id, string number, string scale)
    {
        AlbumCompositionItem slot = Slot(id);

        Assert.Equal(number, slot.Number);
        Assert.Equal(scale, slot.Scale);
    }

    [Fact]
    public void ADrawingTheStandardGivesNoScaleForAsksForNone()
    {
        // A prescribed scale is checked against what arrives. A slot with none
        // must not turn into a check nobody can satisfy.
        Assert.Equal("", Slot("approved-planning-survey").Scale);
    }

    [Fact]
    public void EachMarkIsNumberedFromOneWithoutGaps()
    {
        // The counters are per mark, so the unnumbered opening pages cannot
        // push the drawings along, however many pages the planning task turns
        // out to have.
        foreach (string mark in new[] { "ЕТ", "ИДБ" })
        {
            int[] numbers = [.. Definition().Composition
                .Select(item => item.Number)
                .Where(number => number.StartsWith(mark + "-", StringComparison.Ordinal))
                .Select(number => int.Parse(number[(mark.Length + 1)..]))
                .OrderBy(value => value)];

            Assert.Equal(Enumerable.Range(1, numbers.Length), numbers);
        }
    }

    [Fact]
    public void TheTwoHalvesOfTheAlbumAreTheSections()
    {
        // Not the ЕТ and ИДБ marks: both halves contain both marks, so marks
        // cannot divide the album.
        IReadOnlyList<AlbumCompositionItem> composition = Definition().Composition;

        Assert.Equal(16, composition.Count(item =>
            item.SectionTitle == DevelopmentProjectDrawingSequence.SurveySectionTitle));
        Assert.Equal(14, composition.Count(item =>
            item.SectionTitle == DevelopmentProjectDrawingSequence.SolutionSectionTitle));
        Assert.All(
            composition.Where(item => item.Kind == AlbumCompositionKind.Generated),
            item => Assert.Equal("", item.SectionTitle));
    }

    [Fact]
    public void BothHalvesContainBothMarks()
    {
        // The reason marks cannot be the sections, stated as a check so that
        // anyone tempted to go back to marks finds out here.
        foreach (string section in new[]
                 {
                     DevelopmentProjectDrawingSequence.SurveySectionTitle,
                     DevelopmentProjectDrawingSequence.SolutionSectionTitle,
                 })
        {
            List<string> numbers = [.. Definition().Composition
                .Where(item => item.SectionTitle == section)
                .Select(item => item.Number)];

            Assert.Contains(numbers, number => number.StartsWith("ЕТ-", StringComparison.Ordinal));
            Assert.Contains(numbers, number => number.StartsWith("ИДБ-", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void NoReferenceGridIsForcedOnThisAlbum()
    {
        // The two siblings are working drawings and fix an A3-module page with
        // an etalon band. This one is composed and lets the project decide, so
        // a corner table can be chosen without a grid arriving with it.
        Assert.Null(Definition().GeneratedPageFormat);
    }

    [Fact]
    public void ThePlanningTaskIsOneSlotBecauseItsLengthVaries()

    {
        // "АТД янз бүр байдаг. Даалгаврын хуудасны тооноос хамаарч хэдэн
        // хуудас үүсгэхээ студио өөрөө шийддэг." The reference album's four
        // pages were that document's length, not a rule.
        AlbumCompositionItem task = Slot("planning-task");

        Assert.Equal(AlbumGeneratedPageKind.PlanningTask, task.GeneratedPageKind);
        Assert.Single(Definition().Composition, item => item.Id == "planning-task");
    }

    [Fact]
    public void TheOrganisationsCertificateAndLicenceAreStudioPages()
    {
        // «Байгууллагын гэрчилгээ, Байгууллагын тусгай зөвшөөрөл … Эдгээр
        // хуудаснууд студио талд үүснэ.» One slot draws both, the same way the
        // concept album already does - two entries would have meant two
        // mechanisms for one pair of documents.
        AlbumCompositionItem slot = Slot("design-organization");

        Assert.Equal(AlbumGeneratedPageKind.DesignOrganization, slot.GeneratedPageKind);
        Assert.Equal(AlbumCompositionKind.Generated, slot.Kind);
    }

    [Fact]
    public void TheOrganisationPagesDoNotDisturbTheDrawingNumbers()
    {
        // They carry no mark, so inserting them cannot push ЕТ-01 along -
        // which is the whole reason the counters are per mark.
        Assert.Equal("", Slot("design-organization").Number);
        Assert.Equal("ЕТ-01", Slot("location-scheme").Number);
        Assert.Equal("ЕТ-03", Slot("topographic-base").Number);
    }

    [Fact]
    public void TheSiblingTemplatesAreUntouched()
    {
        // Adding a third member must not renumber the other two.
        AlbumDefinition masterPlan = UrbanPlanningAlbumTemplate.CreateDefinition("master-plan");

        Assert.Equal("ЕТ-01", masterPlan.Composition.Single(item => item.Id == "cover").Number);
        Assert.NotNull(masterPlan.GeneratedPageFormat);
    }

    private static AlbumCompositionItem Slot(string id) =>
        Definition().Composition.Single(item => item.Id == id);
}
