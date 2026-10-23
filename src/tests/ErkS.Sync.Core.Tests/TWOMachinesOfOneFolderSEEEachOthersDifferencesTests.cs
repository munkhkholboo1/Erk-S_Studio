using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace ErkS.Sync.Core.Tests;

/// <summary>
/// Two instances of SYN, one machine, one canonical path mapped to two physical folders: each
/// finds the other on the LAN, fetches its index and reports what differs.
///
/// 🔴 Task 1.1, acceptance: «хоёр төхөөрөмж нэг folderId, ижил каноник зам харуулна; ялгаатай
/// файлын жагсаалт гарна». Since 1.2 a file only the other holds comes over; a file both hold
/// differently, with no version in common, is still only shown (owner's decision 15).
/// </summary>
public sealed class TWOMachinesOfOneFolderSEEEachOthersDifferencesTests
{
    private const string Canonical = @"D:\Cloud Work\Erin";

    [Fact]
    public void ACanonicalPathMapsIntoTheSecondInstancesFolder()
    {
        var map = new SyncPathMap(@"D:\Cloud Work=>C:\scratch\b");

        Assert.Equal(@"C:\scratch\b\Erin", map.Physical(Canonical));
        Assert.Equal(@"E:\Other", map.Physical(@"E:\Other"));
        Assert.Equal(Canonical, new SyncPathMap("").Physical(Canonical));
    }

    [Fact]
    public async Task EachSeesTheOthersDifferencesAndADifferentFileIsNeverOverwritten()
    {
        using var rootA = new TemporaryFolder();
        using var rootB = new TemporaryFolder();
        using var homeA = new TemporaryFolder();
        using var homeB = new TemporaryFolder();
        using var a = new FakeDevice();
        using var b = new FakeDevice();
        var server = new FakeServer(Canonical, [a, b]);

        string folderA = Path.Combine(rootA.Path, "Erin");
        string folderB = Path.Combine(rootB.Path, "Erin");
        rootA.File(@"Erin\same.dwg", "1");
        rootB.File(@"Erin\same.dwg", "1");
        rootA.File(@"Erin\changed.pdf", "mine");
        rootB.File(@"Erin\changed.pdf", "theirs");
        rootB.File(@"Erin\only-b.pdf", "b");
        SyncFolderMarker.Write(folderA, Identity());
        SyncFolderMarker.Write(folderB, Identity());

        await using var engineA = new SyncEngine(new SyncHome(homeA.Path), server.Client(), a, new SyncPathMap(@"D:\Cloud Work=>" + rootA.Path));
        await using var engineB = new SyncEngine(new SyncHome(homeB.Path), server.Client(), b, new SyncPathMap(@"D:\Cloud Work=>" + rootB.Path));
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        Task runA = engineA.RunAsync(stop.Token);
        Task runB = engineB.RunAsync(stop.Token);

        SyncPeerState? seenByA = null;
        for (int attempt = 0; attempt < 60 && !(seenByA?.SameCount == 2 && seenByA.UnrelatedCount == 1); attempt++)
        {
            await Task.Delay(500);
            engineA.Poke();
            engineB.Poke();
            seenByA = engineA.LastState.Folders.SingleOrDefault()?.Peers.SingleOrDefault();
        }
        stop.Cancel();
        await Task.WhenAll(runA, runB);

        SyncFolderState folder = Assert.Single(engineA.LastState.Folders);
        Assert.True(folder.PresentHere);
        Assert.Equal(Canonical, folder.CanonicalPath);
        Assert.NotNull(seenByA?.ExchangedAtUtc);
        Assert.Equal(2, seenByA!.SameCount);
        Assert.Equal(1, seenByA.DifferentCount);
        Assert.Equal(1, seenByA.UnrelatedCount);
        Assert.Equal(0, seenByA.OnlyThereCount);
        Assert.Equal("mine", File.ReadAllText(Path.Combine(folderA, "changed.pdf")));
        Assert.Equal("theirs", File.ReadAllText(Path.Combine(folderB, "changed.pdf")));
        Assert.Equal("b", File.ReadAllText(Path.Combine(folderA, "only-b.pdf")));
        Assert.NotNull(SyncStateStore.TryLoad(new SyncHome(homeA.Path)));

        // The Twin's file: the same counts, never a file name.
        string statusPath = new SyncHome(homeA.Path).StatusPath;
        SyncStatusReport? status = SyncStatusReport.TryRead(statusPath);
        Assert.NotNull(status);
        Assert.True(status!.Running);
        SyncStatusFolder reported = Assert.Single(status.Folders);
        Assert.Equal(1, reported.PeerCount);
        Assert.Equal(1, reported.DifferingFileCount);
        Assert.True(reported.PresentHere);
        string raw = File.ReadAllText(statusPath);
        Assert.DoesNotContain("changed.pdf", raw);
        Assert.DoesNotContain("only-b", raw);
        Assert.Contains("\"differingFileCount\": 1", raw);

        await engineA.DisposeAsync();
        Assert.False(SyncStatusReport.TryRead(statusPath)!.Running);
    }

    [Fact]
    public void OnlyTheRealInstanceWritesTheTwinsStatusFile()
    {
        string? saved = Environment.GetEnvironmentVariable("ERKS_SYNC_HOME");
        try
        {
            Environment.SetEnvironmentVariable("ERKS_SYNC_HOME", null);
            Assert.EndsWith(@"\Erk-S Platform\Sync\status.json", SyncHome.Default().StatusPath);
            Environment.SetEnvironmentVariable("ERKS_SYNC_HOME", @"C:\scratch\second");
            Assert.Equal(@"C:\scratch\second\status.json", SyncHome.Default().StatusPath);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ERKS_SYNC_HOME", saved);
        }
    }

    [Fact]
    public async Task AFolderNOTOnThisDiskIsReportedNotIndexed()
    {
        using var root = new TemporaryFolder();
        using var home = new TemporaryFolder();
        using var a = new FakeDevice();
        var server = new FakeServer(Canonical, [a]);
        await using var engine = new SyncEngine(new SyncHome(home.Path), server.Client(), a, new SyncPathMap(@"D:\Cloud Work=>" + root.Path));

        await engine.PassAsync(CancellationToken.None);

        SyncFolderState folder = Assert.Single(engine.LastState.Folders);
        Assert.False(folder.PresentHere);
        Assert.Equal("ok", engine.LastState.ServerStatus);
    }

    private static SyncFolderIdentity Identity() => new()
    {
        FolderId = "f-1",
        ProjectCode = "ERIN-2026",
        ProjectId = "p-1",
        CanonicalPath = Canonical,
        CreatedAtUtc = DateTimeOffset.UtcNow,
    };

}
