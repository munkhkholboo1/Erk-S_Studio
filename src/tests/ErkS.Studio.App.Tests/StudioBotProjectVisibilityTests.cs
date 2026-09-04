using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// The decree: a seat takes part only in the projects assigned to it, and the
/// rest are not visible. A seated machine had been showing the owner's whole
/// catalogue.
/// </summary>
public sealed class StudioBotProjectVisibilityTests
{
    private static IReadOnlySet<string> Assigned(params string[] ids) =>
        new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void AnUnseatedMachineSeesEverythingItAlwaysDid()
    {
        Assert.True(StudioBotProjectVisibility.IsVisible(
            seatedAsBot: false,
            assignedProjectIds: null,
            projectId: "srv_prj_anything"));
    }

    [Fact]
    public void ASeatSeesOnlyWhatItIsAssigned()
    {
        IReadOnlySet<string> assigned = Assigned("srv_prj_A", "srv_prj_B");

        Assert.True(StudioBotProjectVisibility.IsVisible(true, assigned, "srv_prj_A"));
        Assert.True(StudioBotProjectVisibility.IsVisible(true, assigned, "SRV_PRJ_b"));
        Assert.False(StudioBotProjectVisibility.IsVisible(true, assigned, "srv_prj_C"));
    }

    [Fact]
    public void AnUnreadAssignmentListHidesEVERYTHING_NotNothing()
    {
        // The defect this whole class exists for. "Not read yet" is the state a
        // seated machine is in before it reaches the server, and treating it as
        // "no restriction" is what showed the owner's entire catalogue on a
        // machine that had been handed to somebody else.
        Assert.False(StudioBotProjectVisibility.IsVisible(
            seatedAsBot: true,
            assignedProjectIds: null,
            projectId: "srv_prj_A"));
    }

    [Fact]
    public void ASeatAssignedNothingSeesNothing()
    {
        Assert.False(StudioBotProjectVisibility.IsVisible(true, Assigned(), "srv_prj_A"));
    }

    [Fact]
    public void AProjectWithNoIdentityIsHiddenFromASeat()
    {
        // A local-only project has no server id, so it cannot be on any
        // assignment list. Under a seat it is not visible - which is the same
        // rule, not an extra one.
        Assert.False(StudioBotProjectVisibility.IsVisible(true, Assigned("srv_prj_A"), ""));
        Assert.False(StudioBotProjectVisibility.IsVisible(true, Assigned("srv_prj_A"), null));
    }

    [Fact]
    public void ACloudProjectWithNoMirrorYetIsJudgedByTheRowItCameFrom()
    {
        // The defect. A cloud-only project has no file on this disk, and the
        // route that opens it branches away BEFORE the file-based gate - so an
        // unassigned project opened in full, album and all.
        IReadOnlySet<string> assigned = Assigned("srv_prj_A");

        Assert.True(StudioBotProjectVisibility.MayOpen(
            seatedAsBot: true, assigned, hasFile: false,
            fileIdentity: null, rowProjectId: "srv_prj_A"));
        Assert.False(StudioBotProjectVisibility.MayOpen(
            seatedAsBot: true, assigned, hasFile: false,
            fileIdentity: null, rowProjectId: "srv_prj_NEVER_ASSIGNED"));
    }

    [Fact]
    public void TheFileOnDiskOutranksTheRowThatLedToIt()
    {
        // A row came from a list. A list is not evidence, and the lists this
        // machine holds were filled while it belonged to somebody else.
        Assert.False(StudioBotProjectVisibility.MayOpen(
            seatedAsBot: true,
            Assigned("srv_prj_A"),
            hasFile: true,
            fileIdentity: "srv_prj_OTHER",
            rowProjectId: "srv_prj_A"));
    }

    [Fact]
    public void AFileThatCannotBeIdentifiedIsRefusedRatherThanFallingBackToTheRow()
    {
        // An unreadable file yields no identity. Reading the row instead would
        // open it on the strength of a list entry - the same mistake as
        // treating "not read yet" as "no restriction".
        Assert.False(StudioBotProjectVisibility.MayOpen(
            seatedAsBot: true,
            Assigned("srv_prj_A"),
            hasFile: true,
            fileIdentity: null,
            rowProjectId: "srv_prj_A"));
    }

    [Fact]
    public void AnUnseatedMachineOpensWhatItAlwaysDid()
    {
        Assert.True(StudioBotProjectVisibility.MayOpen(
            seatedAsBot: false,
            assignedProjectIds: null,
            hasFile: false,
            fileIdentity: null,
            rowProjectId: "srv_prj_anything"));
    }

    [Fact]
    public void TheTwoWaysOfSeeingNothingAreExplainedDifferently()
    {
        // "Nothing is assigned to you" and "I could not find out" are different
        // situations for the person in front of the screen: one is a fact about
        // the seat, the other is something to retry.
        Assert.Contains("уншигдаагүй", StudioBotProjectVisibility.ExplainRefusal(null));
        Assert.Contains("томилогдоогүй", StudioBotProjectVisibility.ExplainRefusal(Assigned()));
    }
}
