namespace ErkS.Sync.Core.Tests;

/// <summary>
/// 🔴 Owner, 2026-10-24: the laptop's tray read «0% синк хийгдсэн» while it was fully in step
/// with the owner's machine - the third partner, not on 0.001.74 yet, had no index and was
/// counted as 0%. A partner not yet seen is waiting, not behind.
/// </summary>
public sealed class APARTNERNOTYetJoinedIsWaitingNotZeroTests
{
    [Fact]
    public void APartnerWithoutAnIndexIsWaiting()
    {
        SyncPeerState[] peers =
        [
            new() { DeviceKeyId = "a", SyncedPercent = 100, ExchangedAtUtc = DateTimeOffset.UtcNow },
            new() { DeviceKeyId = "b", SyncedPercent = 0, ExchangedAtUtc = null },
        ];

        Assert.Equal((100.0, 1), SyncInStep.Of(peers));
        Assert.Equal("✓ бүрэн синк хийгдсэн · 1 хамтрагч холбогдохыг хүлээж байна", SyncInStep.Words(peers));
    }

    [Fact]
    public void AKnownPartnerBehindStillShowsItsPercent() =>
        Assert.Equal("40% синк хийгдсэн", SyncInStep.Words([new SyncPeerState { HereBytes = 40, NeededBytes = 60, NeededFiles = 3, ExchangedAtUtc = DateTimeOffset.UtcNow }]));
}
