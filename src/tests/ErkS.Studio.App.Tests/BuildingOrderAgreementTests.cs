using ErkS.Platform.Core;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// Two implementations answer "what order do the buildings go in": the album
/// sequencer, which builds the local PDF, and the cloud component order policy,
/// which places the same buildings in the shared album.
///
/// 🔴 THIS IS A TRIPWIRE, NOT A FINDING, and saying so is the point.
///
/// It was written expecting to catch a disagreement - the cloud album on the
/// user's machine had a building's pages separated from their sub-cover, and
/// two rules answering one question is the shape that produces exactly that.
/// Measured, they do NOT disagree, and the reason is worth writing down because
/// it is load-bearing and easy to remove:
///
///   * the cloud policy does not hard-code a building sequence. BuildingRank
///     sorts the groups by their own Order; the BuildingBase/BuildingStride
///     constants are a numbering stride around that rank, not the rank itself.
///   * ProjectBuildingComposition.NormalizeGroups RENUMBERS Order to 1..N on
///     every update, so two groups can never share one.
///
/// With distinct 1..N orders the two rules cannot part company. The only place
/// they could - a tie, broken by name/id on one side and by first-page position
/// on the other - is unreachable while the renumbering holds. Take the
/// renumbering away and this file goes red, which is the whole reason it exists.
/// </summary>
public sealed class BuildingOrderAgreementTests
{
    private static ProjectWorkspace ProjectWith(params (string Name, int Order)[] groups)
    {
        var project = new ProjectWorkspace();
        project.BuildingGroups = ProjectBuildingComposition.NormalizeGroups(
            groups.Select(item => new ProjectBuildingGroup { Name = item.Name, Order = item.Order }));
        return project;
    }

    private static IReadOnlyList<string> CloudBuildingSequence(ProjectWorkspace project) =>
        project.BuildingGroups
            .Select(group => new
            {
                group.Name,
                Order = StudioAlbumComponentOrderPolicy.Resolve(
                    project,
                    ProjectCloudSyncMetadata.BuildingSubCoverComponentCode(group),
                    sourceKey: "",
                    localOrder: 0,
                    sourceOrder: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)),
            })
            .OrderBy(item => item.Order)
            .Select(item => item.Name)
            .ToList();

    private static IReadOnlyList<string> LocalBuildingSequence(ProjectWorkspace project) =>
        project.BuildingGroups
            .OrderBy(group => group.Order)
            .Select(group => group.Name)
            .ToList();

    [Fact]
    public void THETwoRulesAgreeOnTheORDEROfBuildings()
    {
        ProjectWorkspace project = ProjectWith(
            ("Орон сууц-1", 1),
            ("Орон сууц-2", 2),
            ("Эмнэлэг", 3));

        Assert.Equal(LocalBuildingSequence(project), CloudBuildingSequence(project));
    }

    [Fact]
    public void REORDERINGTheGroupsMovesBOTHRulesTogether()
    {
        // Master's first mutation. A comparison that only ever looks at today's
        // data can agree by accident; changing the thing the rules read is what
        // makes the agreement mean something.
        ProjectWorkspace project = ProjectWith(
            ("Эмнэлэг", 1),
            ("Орон сууц-1", 2),
            ("Орон сууц-2", 3));

        Assert.Equal(["Эмнэлэг", "Орон сууц-1", "Орон сууц-2"], LocalBuildingSequence(project));
        Assert.Equal(LocalBuildingSequence(project), CloudBuildingSequence(project));
    }

    [Fact]
    public void ADDINGABuildingMovesBOTHRulesTogether()
    {
        // The second mutation. A new group arrives with whatever Order the
        // dialog gave it, and normalisation renumbers the set - both rules must
        // follow the new numbering rather than a remembered one.
        ProjectWorkspace project = ProjectWith(
            ("Орон сууц-1", 1),
            ("Цэцэрлэг", 2),
            ("Орон сууц-2", 3),
            ("Эмнэлэг", 4),
            ("Сургууль", 5));

        Assert.Equal(5, project.BuildingGroups.Count);
        Assert.Equal(LocalBuildingSequence(project), CloudBuildingSequence(project));
    }

    [Fact]
    public void THEAgreementRestsOnDISTINCTOrdersAndSaysSo()
    {
        // 🔴 THE POSITIVE CONTROL, and the reason this file is a tripwire rather
        // than a green tick. The two rules break ties differently - the cloud
        // policy by name then id, the sequencer by where a building's first page
        // sits - so they agree only while no two groups share an Order.
        //
        // Nothing in the types enforces that; NormalizeGroups does, by
        // renumbering to 1..N. This asserts the renumbering itself, because it
        // is what the agreement above is standing on.
        ProjectWorkspace project = ProjectWith(
            ("Гурав", 7),
            ("Нэг", 7),
            ("Хоёр", 7));

        Assert.Equal([1, 2, 3], project.BuildingGroups.Select(group => group.Order));
        Assert.Equal(
            project.BuildingGroups.Count,
            project.BuildingGroups.Select(group => group.Order).Distinct().Count());
    }

    [Fact]
    public void ASubCoverSitsWithITSOWNBuildingAndNotBetweenTwoOthers()
    {
        // The user's symptom, asked of the rule rather than of an artefact: the
        // sub-cover component of a building must sort ahead of that building's
        // own source pages and behind the previous building's.
        ProjectWorkspace project = ProjectWith(("Эхний", 1), ("Хоёрдугаар", 2));
        var noSources = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        int firstCover = StudioAlbumComponentOrderPolicy.Resolve(
            project,
            ProjectCloudSyncMetadata.BuildingSubCoverComponentCode(project.BuildingGroups[0]),
            "",
            0,
            noSources);
        int secondCover = StudioAlbumComponentOrderPolicy.Resolve(
            project,
            ProjectCloudSyncMetadata.BuildingSubCoverComponentCode(project.BuildingGroups[1]),
            "",
            0,
            noSources);

        Assert.True(firstCover < secondCover, "the first building's cover must come first");
    }
}
