namespace ErkS.Sync.Core.Tests;

/// <summary>
/// Erk-S Sync, 2026-10-28 (AC/2026/01, 13 339 files, 19 GB): the pieces went up in the server's hash order,
/// so after 1.3 GB not one file was whole and the laptop - which takes only whole files - took nothing for
/// an hour while reading «100%». Pieces now go file by file, the small files first, and files still on their
/// way count as waited for.
/// </summary>
public sealed class AFILEGoesUpWHOLEBeforeTheNextTests
{
    private const string Canonical = @"D:\Cloud Work\Erin";

    [Fact]
    public async Task PIECESGoFileByFileSmallFirst()
    {
        using var root = new TemporaryFolder();
        using var home = new TemporaryFolder();
        using var a = new FakeDevice();
        using var b = new FakeDevice();
        var server = new FakeServer(Canonical, [a, b]) { Relay = true, ReverseMissing = true };
        string folder = root.Folder("Erin");
        SyncFolderMarker.Write(folder, new SyncFolderIdentity { FolderId = "f-1", ProjectCode = "ERIN-2026", CanonicalPath = Canonical });
        var random = new Random(7);
        byte[] Bytes(int length)
        {
            var data = new byte[length];
            random.NextBytes(data);
            return data;
        }
        File.WriteAllBytes(Path.Combine(folder, "big.dwg"), Bytes(SyncIndex.ChunkSize * 2 + 100));
        File.WriteAllBytes(Path.Combine(folder, "mid.pdf"), Bytes(5000));
        File.WriteAllBytes(Path.Combine(folder, "small.txt"), Bytes(10));
        await using var engine = new SyncEngine(new SyncHome(home.Path), server.Client(), a, new SyncPathMap(@"D:\Cloud Work=>" + root.Path)) { UploadLanes = 1 };

        await engine.PassAsync(CancellationToken.None);

        SyncIndex index = SyncIndexStore.TryLoad(new SyncHome(home.Path).IndexPath("f-1"))!;
        IReadOnlyList<string> Pieces(string name) => index.Entries.Single(entry => entry.RelativePath == name).ChunkSha256;
        Assert.Equal([.. Pieces("small.txt"), .. Pieces("mid.pdf"), .. Pieces("big.dwg")], server.ChunkPutOrder);
    }

    [Fact]
    public void FilesSTILLOnTheirWayHoldThePercentBelow100()
    {
        Assert.Equal(0, new SyncPeerState { HereBytes = 0, ServerIncompleteCount = 3, ServerIncompleteBytes = 3000 }.InPercent);
        Assert.Equal(50, new SyncPeerState { HereBytes = 1000, NeededBytes = 0, ServerIncompleteCount = 1, ServerIncompleteBytes = 1000 }.InPercent);
        Assert.Equal(100, new SyncPeerState { HereBytes = 1000 }.InPercent);
        Assert.True(new SyncPeerState { HereBytes = 1000, ServerIncompleteCount = 1, ServerIncompleteBytes = 0 }.InPercent < 100);
    }
}
