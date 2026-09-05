using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

[Collection(StudioDataRootCollection.Name)]
public sealed class StudioBotDeviceStateTests : IDisposable
{
    private readonly string dataRoot = Path.Combine(
        Path.GetTempPath(),
        "erks-bot-device-state-tests",
        Guid.NewGuid().ToString("N"));
    private readonly string? previousRoot =
        Environment.GetEnvironmentVariable("ERKS_STUDIO_DATA_ROOT");

    public StudioBotDeviceStateTests()
    {
        Directory.CreateDirectory(dataRoot);
        Environment.SetEnvironmentVariable("ERKS_STUDIO_DATA_ROOT", dataRoot);
    }

    [Fact]
    public void SeatIdentityFallsBackToTheBotIdWhenThereIsNoInternalEmail()
    {
        // The seat needs an address to own and receive as. An internal e-mail
        // is optional on the server, so the bot id stands in - the comparison
        // is a string match, not a mailbox.
        Assert.Equal(
            "bot_7f3a91c4e85b4d2f",
            StudioBotDeviceState.ResolveSeatIdentity("bot_7f3a91c4e85b4d2f", ""));
        Assert.Equal(
            "zurag@erk-s.local",
            StudioBotDeviceState.ResolveSeatIdentity("bot_7f3a91c4e85b4d2f", " Zurag@Erk-S.local "));
    }

    [Fact]
    public void AnUnseatedMachineReadsNoSeat()
    {
        Assert.Null(StudioBotDeviceStateStore.Read());
    }

    [Fact]
    public void ALockedSeatYieldsNoIdentityUntilTheRightPinOpensIt()
    {
        // The point of sealing: a seated machine that has not been unlocked
        // does not act as the seat at all, so a copied state file is not a
        // working seat.
        var seat = new StudioBotDeviceState
        {
            BotId = "bot_7f3a91c4e85b4d2f",
            OrganizationId = "org_2a91f4c7",
            DisplayName = "Зураг-1",
            SealedSeat = Convert.ToBase64String(StudioBotPinVault.Seal(
                "bot_7f3a91c4e85b4d2f",
                "1234",
                "zurag@erk-s.local").Blob),
        };

        Assert.Null(seat.TryUnlock("1235"));
        Assert.Equal("zurag@erk-s.local", seat.TryUnlock("1234"));
    }

    [Fact]
    public void TheSeatSurvivesAWriteAndReadBack()
    {
        var seat = new StudioBotDeviceState
        {
            BotId = "bot_7f3a91c4e85b4d2f",
            OrganizationId = "org_2a91f4c7",
            DisplayName = "Зураг-1",
            SealedSeat = Convert.ToBase64String(StudioBotPinVault.Seal("bot_7f3a91c4e85b4d2f", "1234", "zurag@erk-s.local").Blob),
            EnteredAtUtc = DateTimeOffset.UnixEpoch,
            EnteredByEmail = "owner@erk-s.local",
        };

        StudioBotDeviceStateStore.Write(seat);
        StudioBotDeviceState? read = StudioBotDeviceStateStore.Read();

        Assert.NotNull(read);
        Assert.Equal("bot_7f3a91c4e85b4d2f", read!.BotId);
        Assert.Equal("org_2a91f4c7", read.OrganizationId);
        Assert.Equal("zurag@erk-s.local", read.TryUnlock("1234"));
        Assert.True(read.IsSeated);

        StudioBotDeviceStateStore.Clear();
        Assert.Null(StudioBotDeviceStateStore.Read());
    }

    [Fact]
    public void AnUnreadableSeatFileIsNotTreatedAsAnUnseatedMachine_ButIsKept()
    {
        // Reading returns no seat, which is the safe runtime answer. The file
        // is deliberately NOT deleted: unseating a machine somebody handed over
        // is not a repair, and the file is the only evidence of what happened.
        File.WriteAllText(StudioBotDeviceStateStore.StatePath, "{ not json");

        Assert.Null(StudioBotDeviceStateStore.Read());
        Assert.True(File.Exists(StudioBotDeviceStateStore.StatePath));
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("ERKS_STUDIO_DATA_ROOT", previousRoot);
        try
        {
            if (Directory.Exists(dataRoot))
                Directory.Delete(dataRoot, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
