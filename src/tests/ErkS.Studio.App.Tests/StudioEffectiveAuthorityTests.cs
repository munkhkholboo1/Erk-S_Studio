using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// "A bot session gets the seat's assignment; a personal session gets the
/// person's own participation. No union, no maximum, no fallback."
///
/// Rules of this exact shape were wrong three times this week while they lived
/// inside methods that build WPF controls. This one is written where it can be
/// stated.
/// </summary>
public sealed class StudioEffectiveAuthorityTests
{
    private static readonly string[] PersonalAdmin =
        ["project.read", "team.manage", "project.delete", "concept.write"];
    private static readonly string[] SeatDraughtsman =
        ["project.read", "concept.write"];

    [Fact]
    public void ASeatedMachineGetsTheSEATSRights_NotThePersonsOwn()
    {
        // The person at this machine is an admin in their own right. The seat
        // they were handed is not. The seat wins - that is what a seat is.
        IReadOnlySet<string> scopes = StudioEffectiveAuthority.ScopesFor(
            StudioSessionKind.BotSeat,
            PersonalAdmin,
            SeatDraughtsman);

        Assert.DoesNotContain("team.manage", scopes);
        Assert.Contains("concept.write", scopes);
    }

    [Fact]
    public void APersonalSessionGetsThePERSONSRights_NotTheSeatsOnThisMachine()
    {
        // And the other way round: signing in as yourself on a machine that
        // holds a seat does not borrow the seat's powers either.
        IReadOnlySet<string> scopes = StudioEffectiveAuthority.ScopesFor(
            StudioSessionKind.Personal,
            SeatDraughtsman,
            ["team.manage", "album.submit"]);

        Assert.DoesNotContain("team.manage", scopes);
        Assert.DoesNotContain("album.submit", scopes);
        Assert.Contains("concept.write", scopes);
    }

    [Fact]
    public void ThereIsNoFallbackWhenTheChosenSourceIsUnknown()
    {
        // THE hole this class exists to close. "The seat has not answered yet,
        // so use the person's rights meanwhile" reads as helpfulness and is a
        // way to act with authority nobody granted.
        Assert.Empty(StudioEffectiveAuthority.ScopesFor(
            StudioSessionKind.BotSeat,
            PersonalAdmin,
            seatScopes: null));

        Assert.Empty(StudioEffectiveAuthority.ScopesFor(
            StudioSessionKind.Personal,
            personalScopes: null,
            SeatDraughtsman));
    }

    [Fact]
    public void AnEmptyAssignmentIsAnAnswer_AndItGrantsNothing()
    {
        Assert.Empty(StudioEffectiveAuthority.ScopesFor(
            StudioSessionKind.BotSeat,
            PersonalAdmin,
            seatScopes: []));
    }

    [Fact]
    public void NothingIsEverMerged_TheTwoSetsDoNotAddUp()
    {
        IReadOnlySet<string> seat = StudioEffectiveAuthority.ScopesFor(
            StudioSessionKind.BotSeat,
            ["album.submit"],
            ["concept.write"]);

        Assert.Equal(["concept.write"], seat.OrderBy(value => value, StringComparer.Ordinal));
    }

    [Fact]
    public void AScopeOutsideTheProjectNeverReachesASeat_EvenIfTheServerSendsIt()
    {
        // Both sides exclude these, on purpose: neither is load-bearing alone.
        // A machine does not leave a project on somebody's behalf, and deleting
        // one belongs to its owner.
        IReadOnlySet<string> scopes = StudioEffectiveAuthority.ScopesFor(
            StudioSessionKind.BotSeat,
            [],
            ["project.read", "project.delete", "project.leave"]);

        Assert.Equal(["project.read"], scopes);
    }

    [Fact]
    public void APersonalSessionKeepsThoseScopes_TheExclusionIsAboutSeatsOnly()
    {
        IReadOnlySet<string> scopes = StudioEffectiveAuthority.ScopesFor(
            StudioSessionKind.Personal,
            ["project.read", "project.delete"],
            []);

        Assert.Contains("project.delete", scopes);
    }

    [Fact]
    public void EveryKnownScopeIsClassified_SoANewOneCannotDriftIntoSeats()
    {
        // The allow-list only protects what somebody remembered to think about.
        // This is the part that notices when the server grows a scope: it has
        // to be put on one side or the other, deliberately.
        foreach (string scope in StudioEffectiveAuthority.KnownProjectScopes)
        {
            bool allowed = StudioEffectiveAuthority.SeatAllowedScopes.Contains(scope);
            bool excluded = StudioEffectiveAuthority.SeatExcludedScopes.Contains(scope);
            Assert.True(
                allowed ^ excluded,
                $"'{scope}' is {(allowed && excluded ? "in both lists" : "in neither list")} - " +
                "decide whether a seat may hold it.");
        }
    }

    [Fact]
    public void TheShellActuallyASKSThisRule_ItIsNotWrittenAndUnused()
    {
        // A rule nothing calls protects nothing. Four service methods sat fully
        // written and uncalled this week, and every other kind of check passed
        // on them.
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        string? source = null;
        while (directory is not null && source is null)
        {
            string candidate = Path.Combine(
                directory.FullName, "src", "src", "ErkS.Studio.App", "ShellView.BotSeat.cs");
            if (File.Exists(candidate))
                source = candidate;
            directory = directory.Parent;
        }

        Assert.NotNull(source);
        string shell = File.ReadAllText(source!);
        Assert.Contains("StudioEffectiveAuthority.Allows", shell, StringComparison.Ordinal);
    }

    [Fact]
    public void AskingForOneScopeFollowsTheSameRule()
    {
        Assert.False(StudioEffectiveAuthority.Allows(
            StudioSessionKind.BotSeat, PersonalAdmin, SeatDraughtsman, "team.manage"));
        Assert.True(StudioEffectiveAuthority.Allows(
            StudioSessionKind.BotSeat, PersonalAdmin, SeatDraughtsman, "concept.write"));
        Assert.False(StudioEffectiveAuthority.Allows(
            StudioSessionKind.BotSeat, PersonalAdmin, seatScopes: null, "concept.write"));
    }
}
