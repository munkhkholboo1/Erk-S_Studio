namespace ErkS.Sync.Core.Tests;

/// <summary>
/// Erk-S Sync, 2026-10-28: over a phone, 674 MB came in five minutes and the folder grew 6 MB - a transfer cut
/// off threw its half-file away and the next pass fetched it all again. The pieces stay in «&lt;sha&gt;.part» and
/// the next pass goes on from them: what crosses the wire is the file once (plus the few asked ahead).
/// </summary>
public sealed class AHALFFetchedFileGOESOnTests
{
    private const string Canonical = @"D:\Cloud Work\Erin";

    [Fact]
    public async Task ACutOffFILEIsNotFETCHEDAgain()
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
        var bytes = new byte[SyncIndex.ChunkSize * 10 + 7];
        new Random(11).NextBytes(bytes);
        File.WriteAllBytes(Path.Combine(folderA, "model.rvt"), bytes);
        await using (var engineA = new SyncEngine(new SyncHome(homeA.Path), server.Client(), a, new SyncPathMap(@"D:\Cloud Work=>" + rootA.Path)))
            await engineA.PassAsync(CancellationToken.None);

        var homeOfB = new SyncHome(homeB.Path);
        // The link breaks at the fifth piece: pieces 0-3 come, the rest are refused (no clock - a 3 s window
        // with slow gets lost the race on a loaded machine and left no piece at all).
        HashSet<string> cutOff = Enumerable.Range(4, 7)
            .Select(index => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
                bytes.AsSpan(index * SyncIndex.ChunkSize, Math.Min(SyncIndex.ChunkSize, bytes.Length - index * SyncIndex.ChunkSize)))))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        server.RefuseChunk = cutOff.Contains;
        await using (var cut = new SyncEngine(homeOfB, server.Client(), b, new SyncPathMap(@"D:\Cloud Work=>" + rootB.Path)) { TransferWindow = TimeSpan.FromSeconds(20) })
            await cut.PassAsync(CancellationToken.None);
        Assert.False(File.Exists(Path.Combine(folderB, "model.rvt")));
        Assert.True(Directory.GetFiles(Path.Combine(folderB, SyncExclusions.MarkerDirectoryName, "tmp"), "*.part").Length > 0, $"no part; gets {server.ChunkGets}, tmp has [{string.Join(",", Directory.Exists(Path.Combine(folderB, ".erks-sync", "tmp")) ? Directory.GetFiles(Path.Combine(folderB, ".erks-sync", "tmp")).Select(Path.GetFileName) : [])}]");

        server.RefuseChunk = null;
        await using (var again = new SyncEngine(homeOfB, server.Client(), b, new SyncPathMap(@"D:\Cloud Work=>" + rootB.Path)))
            await again.PassAsync(CancellationToken.None);

        Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(folderB, "model.rvt")));
        Assert.True(server.ChunkGets <= 11 + SyncFileAssembler.FetchAhead, $"{server.ChunkGets} pieces fetched for an 11-piece file");
        Assert.Empty(Directory.GetFiles(Path.Combine(folderB, SyncExclusions.MarkerDirectoryName, "tmp"), "*.part"));
    }
}
