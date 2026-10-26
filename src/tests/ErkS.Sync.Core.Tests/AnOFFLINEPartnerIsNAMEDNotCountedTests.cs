namespace ErkS.Sync.Core.Tests;

/// <summary>
/// Owner, 2026-10-26: the tray said «99.2% синк хийгдсэн» and never moved - a partner was offline
/// with one DWG not yet with them. Say who, since when, and what waits; do not hold the number.
/// </summary>
public sealed class AnOFFLINEPartnerIsNAMEDNotCountedTests
{
    [Fact]
    public void TheOfflinePartnerIsNamedAndTheOnlineOnesGiveThePercent()
    {
        DateTimeOffset now = new(2026, 10, 26, 4, 0, 0, TimeSpan.Zero);
        SyncPeerState offline = new()
        {
            DeviceKeyId = "0A55EFD7A778",
            AccountEmail = "budbayasgalan0321@example.mn",
            ExchangedAtUtc = now.AddHours(-13),
            ServerLastSeenAtUtc = new DateTimeOffset(2026, 10, 25, 14, 49, 0, TimeSpan.Zero),
            NeededFiles = 1,
            NeededBytes = 1_195_000,
            HereBytes = 140_000_000,
        };
        SyncPeerState online = new() { DeviceKeyId = "B11EB7BD361E", ExchangedAtUtc = now, ServerLastSeenAtUtc = now.AddMinutes(-1), SyncedPercent = 100 };

        Assert.Equal("✓ бүрэн синк хийгдсэн", SyncInStep.Words([offline, online], now));
        string line = SyncInStep.Behind([offline, online], now).Single();
        Assert.StartsWith("budbayasgalan0321 офлайн (", line);
        Assert.EndsWith("— 1 файл (1.1 МБ) хүлээгдэж байна", line);
        // Everyone offline (owner, 2026-10-26): an offline member's copies never hold this machine's number.
        Assert.Equal("✓ бүрэн синк хийгдсэн", SyncInStep.Words([offline], now));
        // What a partner still needs FROM here is not this machine's percent either.
        SyncPeerState waitingForMine = online with { SyncedPercent = 97.5, OnlyHereCount = 36 };
        Assert.Equal("✓ бүрэн синк хийгдсэн", SyncInStep.Words([waitingForMine], now));
    }
}
