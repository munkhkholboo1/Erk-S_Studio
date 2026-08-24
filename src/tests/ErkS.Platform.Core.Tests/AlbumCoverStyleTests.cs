using ErkS.Platform.Core.ProjectTypes.UrbanPlanning;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// What an album cover calls itself.
///
/// The line was written out twice - once in the PDF and once in Studio's
/// preview of the same page - and both said /ЗАГВАР ЗУРАГ/ whatever the
/// project was. A client opened their development project and found the album
/// calling itself a concept design. The client's own instruction was as small
/// as it sounds: «Энэ нүүр /ЗАГВАР ЗУРАГ/ гэдгийг л /БАРИЛГАЖИЛТЫН ТӨСӨЛ/
/// болгочихвол болчихно шүү.»
/// </summary>
public sealed class AlbumCoverStyleTests
{
    [Fact]
    public void ADevelopmentProjectSaysWhatItIs()
    {
        Assert.Equal(
            "/БАРИЛГАЖИЛТЫН ТӨСӨЛ/",
            AlbumCoverStyle.Resolve(
                UrbanPlanningAlbumTemplate.DevelopmentProjectTemplateId,
                drawsWorkingDrawingEtalon: false));
    }

    [Fact]
    public void AConceptAlbumIsUnchanged()
    {
        // Every album already in the world is one of these, and none of their
        // covers may move.
        Assert.Equal(
            "/ЗАГВАР ЗУРАГ/",
            AlbumCoverStyle.Resolve(
                "building-architecture-concept-v1",
                drawsWorkingDrawingEtalon: false));
    }

    [Fact]
    public void AWorkingDrawingSheetKeepsItsOwnWording()
    {
        // Decided by the page rather than by the template, and unchanged.
        Assert.Equal(
            "БАРИЛГА АРХИТЕКТУРЫН ХЭСЭГ-БА",
            AlbumCoverStyle.Resolve(
                UrbanPlanningAlbumTemplate.DevelopmentProjectTemplateId,
                drawsWorkingDrawingEtalon: true));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("some-template-nobody-has-written-yet")]
    public void AnAlbumThisVersionDoesNotRecogniseKeepsTheWordingItAlreadyHad(string? templateId)
    {
        // Deriving this from the stage name would have been shorter and would
        // have let an album rename itself the moment somebody edited a label.
        // A cover leaves the office; it changes when a template says so.
        Assert.Equal(
            "/ЗАГВАР ЗУРАГ/",
            AlbumCoverStyle.Resolve(templateId, drawsWorkingDrawingEtalon: false));
    }

    [Fact]
    public void ADevelopmentProjectUsesTheApprovalCover()
    {
        // The client asked for the concept cover with one line changed. The
        // writer instead picked a cover by asking which template this was and
        // falling through to a plain portrait title page when it recognised
        // none - so the album opened with an A4 sheet nobody had designed.
        Assert.True(AlbumCoverStyle.UsesApprovalCover(
            UrbanPlanningAlbumTemplate.DevelopmentProjectTemplateId));
    }

    [Fact]
    public void TheConceptAlbumStillUsesIt()
    {
        Assert.True(AlbumCoverStyle.UsesApprovalCover("building-architecture-concept-v1"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("urban-planning-master-plan-v1")]
    public void AnAlbumWithItsOwnCoverIsNotGivenThisOne(string? templateId)
    {
        // The two working drawing families draw their own covers from their
        // page format. Claiming them here would replace a cover that works.
        Assert.False(AlbumCoverStyle.UsesApprovalCover(templateId));
    }

    [Fact]
    public void TheTemplateIdIsMatchedWithoutRegardToCasingOrSpacing()
    {
        Assert.Equal(
            "/БАРИЛГАЖИЛТЫН ТӨСӨЛ/",
            AlbumCoverStyle.Resolve(
                "  URBAN-PLANNING-DEVELOPMENT-PROJECT-V1  ",
                drawsWorkingDrawingEtalon: false));
    }
}
