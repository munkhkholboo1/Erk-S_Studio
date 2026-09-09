using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// The user's report, three times over: "I cannot get out of bot state." Each
/// time the rule lived inside a method that builds WPF controls, where nothing
/// could measure it.
/// </summary>
public sealed class StudioBotMenuPlanTests
{
    [Fact]
    public void ASeatedMachineWithNoOwnerSessionIsOfferedTheDoorAndNothingElse()
    {
        IReadOnlyList<BotMenuEntry> entries =
            StudioBotMenuPlan.For(seatedAsBot: true, ownerSessionInHand: false);

        Assert.Equal([BotMenuEntry.OwnerPassport], entries);
    }

    [Fact]
    public void ASeatedMachineIsNEVERLeftWithNoWayOut()
    {
        // The defect this exists for. A machine unlocked with its PIN had every
        // bot entry hidden, and the only passport link lived on a lock screen
        // that had already been taken away.
        foreach (bool owner in new[] { true, false })
        {
            IReadOnlyList<BotMenuEntry> entries =
                StudioBotMenuPlan.For(seatedAsBot: true, ownerSessionInHand: owner);

            Assert.NotEmpty(entries);
            Assert.True(
                entries.Contains(BotMenuEntry.OwnerPassport) ||
                entries.Contains(BotMenuEntry.LeaveBotState),
                $"a seated machine with ownerSessionInHand={owner} was offered no way back: " +
                string.Join(", ", entries));
        }
    }

    [Fact]
    public void OnceTheOwnerHasSignedInTheSeatedMachineCanBeGivenBack()
    {
        IReadOnlyList<BotMenuEntry> entries =
            StudioBotMenuPlan.For(seatedAsBot: true, ownerSessionInHand: true);

        Assert.Contains(BotMenuEntry.LeaveBotState, entries);
        Assert.Contains(BotMenuEntry.ManageSeats, entries);
        // One device, one seat: a seated machine is not offered another.
        Assert.DoesNotContain(BotMenuEntry.SeatThisDevice, entries);
    }

    [Fact]
    public void THEWayBACKIntoBotStateIsOfferedOnceTheOwnerHasSignedIn()
    {
        // 🔴 THE FOURTH TIME THIS RULE WAS WRONG, AND THE OWNER FOUND IT:
        // «буцаад бот горимруугаа орох боломж алга». Signing in as the owner
        // takes the lock off a seated machine and nothing put it back - the one
        // caller that installs it runs at start-up, so the only way back was to
        // close Studio and open it again.
        IReadOnlyList<BotMenuEntry> entries =
            StudioBotMenuPlan.For(seatedAsBot: true, ownerSessionInHand: true);

        Assert.Contains(BotMenuEntry.EnterBotState, entries);
    }

    [Fact]
    public void BOTHDirectionsAreOfferedTogetherAndTheyAreDifferentSizes()
    {
        // Switching back and giving the seat up are not alternatives - a seated
        // machine with its owner present can do either, and offering only the
        // destructive one is what made «leave» the way back.
        IReadOnlyList<BotMenuEntry> entries =
            StudioBotMenuPlan.For(seatedAsBot: true, ownerSessionInHand: true);

        Assert.Contains(BotMenuEntry.EnterBotState, entries);
        Assert.Contains(BotMenuEntry.LeaveBotState, entries);

        // Asserted by NAME rather than by count, so a sixth entry added later
        // does not fail this for a reason that has nothing to do with it.
        Assert.Equal(
            [BotMenuEntry.ManageSeats, BotMenuEntry.EnterBotState, BotMenuEntry.LeaveBotState],
            entries);
    }

    [Fact]
    public void THEWayBackIsNOTOfferedWhereItWouldMeanNothing()
    {
        // No seat to return to on an unseated machine, and a machine already IN
        // bot state is behind its own lock - offering it there would be a button
        // that re-locks a locked screen.
        Assert.DoesNotContain(
            BotMenuEntry.EnterBotState,
            StudioBotMenuPlan.For(seatedAsBot: false, ownerSessionInHand: true));
        Assert.DoesNotContain(
            BotMenuEntry.EnterBotState,
            StudioBotMenuPlan.For(seatedAsBot: false, ownerSessionInHand: false));
        Assert.DoesNotContain(
            BotMenuEntry.EnterBotState,
            StudioBotMenuPlan.For(seatedAsBot: true, ownerSessionInHand: false));
    }

    [Fact]
    public void ASeatActingAsItselfIsNeverOfferedTheOwnersActions()
    {
        // "эзэмшигч ⊇ бот, бот ⊅ эзэмшигч". Releasing and deleting seats,
        // changing the PIN, inviting members - none of it belongs to the seat,
        // and leaving bot state is the same authority from the other end.
        IReadOnlyList<BotMenuEntry> entries =
            StudioBotMenuPlan.For(seatedAsBot: true, ownerSessionInHand: false);

        Assert.DoesNotContain(BotMenuEntry.ManageSeats, entries);
        Assert.DoesNotContain(BotMenuEntry.LeaveBotState, entries);
        Assert.DoesNotContain(BotMenuEntry.SeatThisDevice, entries);
    }

    [Fact]
    public void AnUnseatedMachineIsOfferedWhatItAlwaysWas()
    {
        IReadOnlyList<BotMenuEntry> entries =
            StudioBotMenuPlan.For(seatedAsBot: false, ownerSessionInHand: true);

        Assert.Equal([BotMenuEntry.ManageSeats, BotMenuEntry.SeatThisDevice], entries);
    }

    [Fact]
    public void TheOwnerDoorIsNotOfferedWhereItWouldMeanNothing()
    {
        // On a machine holding no seat there is no seat to step out of, so the
        // passport entry would be a second sign-in button next to the real one.
        Assert.DoesNotContain(
            BotMenuEntry.OwnerPassport,
            StudioBotMenuPlan.For(seatedAsBot: false, ownerSessionInHand: false));
    }
}
