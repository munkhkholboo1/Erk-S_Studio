namespace ErkS.Sync.Core.Tests;

/// <summary>
/// Erk-S Sync, 2026-10-28: SYN 143 said 5 differing files for a folder where 4 were the lagging laptop's
/// old copies (owner, 2026-10-26: «программаа шинэчлээгүй … гишүүн бусад гишүүдийн ажиллагаанд нөлөөлөх
/// учиргүй»). The folder's count and the Twin leave the lagging member out; its own row still says them.
/// A server out of reach says why (tls, dns …), not only «offline».
/// </summary>
public sealed class ALAGGINGMembersOLDFilesAreNOTADifferenceTests
{
    [Fact]
    public void TheFOLDERCountLeavesTheLaggingMemberOut()
    {
        var state = new SyncState
        {
            ServerStatus = "offline",
            ServerReason = "tls",
            Folders =
            [
                new SyncFolderState
                {
                    FolderId = "f-1",
                    Peers =
                    [
                        new SyncPeerState { DeviceKeyId = "laptop", Lagging = true, DifferentCount = 4 },
                        new SyncPeerState { DeviceKeyId = "desktop", DifferentCount = 1 },
                        new SyncPeerState { DeviceKeyId = "other", DifferentCount = 0 },
                    ],
                },
            ],
        };

        SyncStatusReport report = SyncStatusReport.Of(state, running: true);

        Assert.Equal(1, report.Folders[0].DifferingFileCount);
        Assert.Equal(4, report.Folders[0].Peers.Single(peer => peer.DeviceKeyId == "laptop").DifferentCount);
        Assert.False(report.ServerReachable);
        Assert.Equal("tls", report.ServerReason);
    }
}
