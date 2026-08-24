namespace ErkS.Platform.Core.Tests;

/// <summary>
/// A stage with no album template of its own.
///
/// Development project was the stage that prompted this: it fell through to
/// the building concept album without a word, and the client opened their
/// project to find a cover reading /ЗАГВАР ЗУРАГ/. That album is perfectly
/// usable, which is exactly why the substitution went unnoticed - the pages
/// number, the corner block draws, nothing errors. Only someone who knew what
/// the stage should have looked like could tell it was wrong.
///
/// That stage now has a template. The notice remains, because the next stage
/// nobody has built yet will land in exactly the same place.
///
/// CreateDefinition has to return something, so it cannot refuse. This is the
/// separate answer to "was it the right one".
/// </summary>
public sealed class ProjectAlbumTemplateCoverageTests
{
    [Fact]
    public void AStageWithNoTemplateIsReportedAsUncovered()
    {
        // This used to be development-project - the user's own project, and
        // the reason this notice exists. That stage now has a template of its
        // own, so the example moved to one that still does not: the mechanism
        // has to keep working for the next stage nobody has built yet.
        ProjectAlbumTemplateCoverage coverage = Describe(
            "urban-planning",
            "detailed-plan",
            "Хэсэгчилсэн нарийвчилсан төлөвлөгөө");

        Assert.False(coverage.HasTemplateForStage);
        Assert.NotNull(coverage.Notice);
    }

    [Fact]
    public void TheNoticeNamesTheStageAndWhatWillBeWrong()
    {
        // A warning that does not say what is affected is a warning nobody can
        // act on, which is as good as silence.
        string notice = Require(Describe(
            "urban-planning",
            "detailed-plan",
            "Хэсэгчилсэн нарийвчилсан төлөвлөгөө"));

        Assert.Contains("Хэсэгчилсэн нарийвчилсан төлөвлөгөө", notice);
        Assert.Contains("хуудасны бүрдэл", notice);
    }

    [Fact]
    public void TheNoticeDoesNotBlameTheUser()
    {
        // Nothing was done wrong here - the template simply does not exist
        // yet. Wording that implies a mistake sends people looking for a
        // setting that is not there.
        string notice = Require(Describe(
            "urban-planning",
            "detailed-plan",
            "Хэсэгчилсэн нарийвчилсан төлөвлөгөө"));

        Assert.Contains("хараахан байхгүй", notice);
    }

    [Theory]
    [InlineData("master-plan")]
    [InlineData("partial-plan")]
    public void AStageWithItsOwnTemplateSaysNothing(string stageCode)
    {
        // The notice has to stay rare, or it stops being read.
        ProjectAlbumTemplateCoverage coverage = Describe(
            "urban-planning",
            stageCode,
            stageCode);

        Assert.True(coverage.HasTemplateForStage);
        Assert.Null(coverage.Notice);
    }

    [Fact]
    public void ABuildingConceptProjectIsCoveredRatherThanFallenBackOn()
    {
        // The concept album is a template in its own right. Treating every
        // project that reaches it as a fallback would warn the majority of
        // users about nothing.
        ProjectAlbumTemplateCoverage coverage = Describe(
            ProjectWorkspace.BuildingArchitectureConcept,
            "ConceptDesign",
            "Загвар зураг");

        Assert.True(coverage.HasTemplateForStage);
        Assert.Null(coverage.Notice);
    }

    [Fact]
    public void TheStageCodeStandsInWhenTheStageHasNoName()
    {
        // Better an identifier than an empty pair of quotation marks.
        ProjectAlbumTemplateCoverage coverage = Describe(
            "urban-planning",
            "detailed-plan",
            "");

        Assert.Equal("detailed-plan", coverage.StageLabel);
        Assert.Contains("detailed-plan", Require(coverage));
    }

    private static ProjectAlbumTemplateCoverage Describe(
        string projectType,
        string stageCode,
        string stageName)
    {
        var project = new ProjectWorkspace();
        project.Identity.ProjectType = projectType;
        project.Identity.StageCode = stageCode;
        project.Identity.StageName = stageName;
        return ProjectAlbumTemplateResolver.DescribeCoverage(project);
    }

    private static string Require(ProjectAlbumTemplateCoverage coverage)
    {
        Assert.NotNull(coverage.Notice);
        return coverage.Notice!;
    }
}
