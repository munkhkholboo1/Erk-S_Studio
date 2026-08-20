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
