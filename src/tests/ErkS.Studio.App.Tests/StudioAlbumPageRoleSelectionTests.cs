using ErkS.Platform.Core;

namespace ErkS.Studio.App.Tests;

public sealed class StudioAlbumPageRoleSelectionTests
{
    [Fact]
    public void Resolve_ReportsUniformParticipantAcrossSourceAndGeneratedPages()
    {
        var source = new AlbumPageDefinition
        {
            RoleAssignments = [Assignment("member-01")],
        };
        var generated = new AlbumCompositionItem
        {
            RoleAssignments = [Assignment("member-01")],
        };

        StudioAlbumPageRoleSelectionState state = StudioAlbumPageRoleSelection.Resolve(
            [source, generated],
            AlbumPageRoleCodes.Architect);

        Assert.True(state.HasTargets);
        Assert.False(state.IsMixed);
        Assert.Equal("member-01", state.ParticipantId);
    }

    [Fact]
    public void Resolve_ReportsAHandWrittenNameAndTellsTwoApart()
    {
        AlbumPageRoleAssignment Manual(string name) => new()
        {
            RoleCode = AlbumPageRoleCodes.Architect,
            FullName = name,
        };
        var first = new AlbumPageDefinition { RoleAssignments = [Manual("М.Баасанжав")] };
        var second = new AlbumCompositionItem { RoleAssignments = [Manual("М.Баасанжав")] };
        var other = new AlbumPageDefinition { RoleAssignments = [Manual("Б.Болд")] };

        StudioAlbumPageRoleSelectionState same = StudioAlbumPageRoleSelection.Resolve(
            [first, second], AlbumPageRoleCodes.Architect);
        StudioAlbumPageRoleSelectionState mixed = StudioAlbumPageRoleSelection.Resolve(
            [first, other], AlbumPageRoleCodes.Architect);

        Assert.False(same.IsMixed);
        Assert.Null(same.ParticipantId);
        Assert.Equal("М.Баасанжав", same.ManualName);
        Assert.True(mixed.IsMixed);
    }

    [Fact]
    public void Resolve_ReportsMixedWhenSelectedPagesUseDifferentPeopleOrInheritance()
    {
        var assigned = new AlbumPageDefinition
        {
            RoleAssignments = [Assignment("member-01")],
        };
        var inherited = new AlbumPageDefinition();

        StudioAlbumPageRoleSelectionState state = StudioAlbumPageRoleSelection.Resolve(
            [assigned, inherited],
            AlbumPageRoleCodes.Architect);

        Assert.True(state.HasTargets);
        Assert.True(state.IsMixed);
        Assert.Null(state.ParticipantId);
    }

    private static AlbumPageRoleAssignment Assignment(string participantId) => new()
    {
        RoleCode = AlbumPageRoleCodes.Architect,
        ParticipantId = participantId,
    };
}
