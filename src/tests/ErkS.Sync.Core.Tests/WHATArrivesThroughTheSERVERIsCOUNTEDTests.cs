namespace ErkS.Sync.Core.Tests;

/// <summary>
/// Erk-S Sync, 2026-10-28: the laptop took 601 files through the server and its row said received 0, route
/// "": a transfer cut off by its window threw before it reported. What arrived is counted however the
/// transfer ends, and the row says the road.
/// </summary>
public sealed class WHATArrivesThroughTheSERVERIsCOUNTEDTests
{
    private const string Canonical = @"D:\Cloud Work\Erin";

    [Fact]
    public async Task ATransferCUTOFFStillCountsWhatCAME()
    {
        using var rootA = new TemporaryFolder();
        using var rootB = new TemporaryFolder();
        using var homeA = new TemporaryFolder();
        using var homeB = new TemporaryFolder();
        using var a = new FakeDevice();
        using var b = new FakeDevice();
        var server = new FakeServer(Canonical, [a, b]) { Relay = true };
        var identity = new SyncFolderIdentity { FolderId = "f-1", ProjectCode = "ERIN-2026", CanonicalPath = Canonical };
        string folderA = rootA.Folder("Erin"), folderB = rootB.Folder("Erin");
        SyncFolderMarker.Write(folderA, identity);
        SyncFolderMarker.Write(folderB, identity);
        for (int index = 0; index < 4; index++)
            File.WriteAllText(Path.Combine(folderA, $"sheet-{index}.pdf"), new string((char)('a' + index), 100 + index));
        await using var engineA = new SyncEngine(new SyncHome(homeA.Path), server.Client(), a, new SyncPathMap(@"D:\Cloud Work=>" + rootA.Path));
        await engineA.PassAsync(CancellationToken.None);

        server.GetDelay = TimeSpan.FromMilliseconds(800);
        var homeOfB = new SyncHome(homeB.Path);
        await using var engineB = new SyncEngine(homeOfB, server.Client(), b, new SyncPathMap(@"D:\Cloud Work=>" + rootB.Path))
        {
            TransferWindow = TimeSpan.FromMilliseconds(1300),
        };
        await engineB.PassAsync(CancellationToken.None);

        SyncPeerState row = engineB.LastState.Folders.Single().Peers.Single();
        int arrived = Directory.GetFiles(folderB, "sheet-*.pdf").Length;
        Assert.InRange(arrived, 1, 3); // cut off midway
        Assert.Equal(arrived, row.ReceivedCount);
        Assert.Equal("server", row.Route);
        long sizes = Directory.GetFiles(folderB, "sheet-*.pdf").Sum(path => new FileInfo(path).Length);
        Assert.Equal(sizes, row.BytesReceived); // the files' bytes, not 4 MiB pieces
        SyncFolderState shown = engineB.LastState.Folders.Single();
        Assert.Equal(arrived, Directory.GetFiles(folderB, "sheet-*.pdf").Length);
        Assert.True(shown.FileCount >= arrived, $"folder shows {shown.FileCount} files, {arrived} came"); // counted as they came

        // A restart does not start the count again (Erk-S Sync, 2026-10-28).
        await using var again = new SyncEngine(homeOfB, server.Client(), b, new SyncPathMap(@"D:\Cloud Work=>" + rootB.Path));
        server.GetDelay = TimeSpan.Zero;
        await again.PassAsync(CancellationToken.None);
        SyncPeerState after = again.LastState.Folders.Single().Peers.Single();
        Assert.Equal(4, Directory.GetFiles(folderB, "sheet-*.pdf").Length);
        Assert.Equal(4, after.ReceivedCount);
        Assert.Equal(100, SyncStatusReport.Of(again.LastState, running: true).Folders.Single().Peers.Single().SyncedPercent);
    }
}
