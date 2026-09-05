using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// Seating a machine without a registered device key cannot be undone by the
/// person who did it: the seat erases the owner credential, so the machine has
/// no session left to register with, and after a restart it cannot prove itself
/// to the server at all. The requirement was a side effect of a sign-in step
/// that returns early when somebody is already signed in - the exact shape of a
/// guard that everything walks past.
/// </summary>
public sealed class StudioBotSeatingRequirementsTests
{
    [Fact]
    public void AMachineWithAnUnregisteredKeyIsNotSeated_EvenWithTheOwnerRightThere()
    {
        // THE bypass. Signed in already, so the sign-in step returns early and
        // never registers anything; every other condition is satisfied.
        Assert.Equal(
            BotSeatingRefusal.DeviceKeyNotRegistered,
            StudioBotSeatingRequirements.Check(
                alreadySeated: false,
                ownerSignedIn: true,
                deviceKeyRegistered: false));
    }

    [Fact]
    public void WithTheOwnerSignedInAndTheKeyRegisteredNothingStandsInTheWay()
    {
        Assert.Equal(
            BotSeatingRefusal.None,
            StudioBotSeatingRequirements.Check(
                alreadySeated: false,
                ownerSignedIn: true,
                deviceKeyRegistered: true));
    }

    [Fact]
    public void OneDeviceOneSeat()
    {
        Assert.Equal(
            BotSeatingRefusal.AlreadySeated,
            StudioBotSeatingRequirements.Check(
                alreadySeated: true,
                ownerSignedIn: true,
                deviceKeyRegistered: true));
    }

    [Fact]
    public void SeatsBelongToTheOwner()
    {
        Assert.Equal(
            BotSeatingRefusal.OwnerNotSignedIn,
            StudioBotSeatingRequirements.Check(
                alreadySeated: false,
                ownerSignedIn: false,
                deviceKeyRegistered: true));
    }

    [Fact]
    public void NoRefusalIsSilent_AndNoneOfThemJustSayItFailed()
    {
        // "Болсонгүй" is not something a person can act on. Each refusal has to
        // name what happened AND what to do; the key one has to say plainly that
        // it cannot be repaired afterwards, because that is why it refuses now
        // instead of letting the seat be created.
        foreach (BotSeatingRefusal refusal in Enum.GetValues<BotSeatingRefusal>())
        {
            string described = StudioBotSeatingRequirements.Describe(refusal);
            if (refusal == BotSeatingRefusal.None)
            {
                Assert.Equal("", described);
                continue;
            }

            Assert.False(string.IsNullOrWhiteSpace(described), $"{refusal} said nothing");
            Assert.True(described.Length > 40, $"{refusal} said only: {described}");
        }

        Assert.Contains(
            "засах боломжгүй",
            StudioBotSeatingRequirements.Describe(BotSeatingRefusal.DeviceKeyNotRegistered));
        Assert.Contains(
            "дахин нэвтэр",
            StudioBotSeatingRequirements.Describe(BotSeatingRefusal.DeviceKeyNotRegistered));
    }
}
