namespace ErkS.Studio.App.Tests;

/// <summary>
/// Erk-S Sync, 2026-10-27 (owner): a member is invited from outside, through the bridge, with the same
/// function as «Гишүүн урих». The bridge invites only with roles the project offers.
/// </summary>
public sealed class ATEAMMemberIsINVITEDThroughTheBridgeTests
{
    private static readonly IReadOnlyList<StudioProjectRole> Offered =
    [
        new StudioProjectRole { Code = "Architect", Label = "Архитектор" },
        new StudioProjectRole { Code = "Engineer", Label = "Инженер" },
    ];

    [Fact]
    public void OnlyOfferedROLESAreTaken()
    {
        Assert.Equal(["Architect"], StudioBridgeTeam.ResolveRoles("architect", Offered));
        Assert.Equal(["Architect", "Engineer"], StudioBridgeTeam.ResolveRoles("Architect, Engineer,architect", Offered));
        Assert.Null(StudioBridgeTeam.ResolveRoles("Owner", Offered));
        Assert.Null(StudioBridgeTeam.ResolveRoles(null, Offered));
    }

    [Fact]
    public void TheTEAMActionsAreInTheCATALOG()
    {
        Assert.True(StudioBridgeActionCatalog.Find("team-invite")!.Mutates);
        Assert.True(StudioBridgeActionCatalog.Find("team-accept")!.Mutates);
        Assert.False(StudioBridgeActionCatalog.Find("team-list")!.Mutates);
        Assert.False(StudioBridgeActionCatalog.Find("team-invitations")!.Mutates);
    }
}
