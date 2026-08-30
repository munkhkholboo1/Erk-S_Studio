using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// The order of the four general-plan sheets in a concept album.
/// </summary>
/// <remarks>
/// This order is required from outside the codebase, and it reads like an
/// error, which is a bad combination to leave to a comment alone.
///
/// The city-planning standards authority requires the movement scheme first.
/// Left to itself the album would open the section with the general plan - the
/// sheet the other three elaborate - and PFA, reading the same instinct, built
/// its sender in that order and reported ours as wrong.
///
/// So the requirement is pinned here rather than only described. A comment can
/// be deleted by someone who believes they are tidying up; a test states the
/// consequence at the moment of the change.
/// </remarks>
public sealed class ConceptGeneralPlanSheetOrderTests
{
    [Fact]
    public void TheMovementSchemeComesFirstAndTheGeneralPlanLast()
    {
        AlbumDefinition album = BuildingArchitectureConceptAlbumTemplate.CreateDefinition("test");

        string[] generalPlanSheets = album.Composition
            .Where(item => item.Id is "traffic-scheme" or "landscaping" or "solar-study" or "master-plan")
            .OrderBy(item => item.Order)
            .Select(item => item.Id)
            .ToArray();

        Assert.Equal(
            ["traffic-scheme", "landscaping", "solar-study", "master-plan"],
            generalPlanSheets);
    }

    [Fact]
    public void ReorderingThemNeedsMoreThanAnOpinion()
    {
        AlbumDefinition album = BuildingArchitectureConceptAlbumTemplate.CreateDefinition("test");

        AlbumCompositionItem movement =
            album.Composition.Single(item => item.Id == "traffic-scheme");
        AlbumCompositionItem generalPlan =
            album.Composition.Single(item => item.Id == "master-plan");

        Assert.True(
            movement.Order < generalPlan.Order,
            "ХӨДӨЛГӨӨНИЙ СХЕМ must precede ЕРӨНХИЙ ТӨЛӨВЛӨГӨӨ. This looks "
            + "backwards and is not: the city-planning standards authority "
            + "requires the movement scheme first, and the album follows the "
            + "authority rather than the reading order. Confirmed with the "
            + "user on 2026-08-29 after PFA reported our order as wrong. "
            + "Changing it means the albums stop complying - check with the "
            + "user, not with the code.");
    }
}
