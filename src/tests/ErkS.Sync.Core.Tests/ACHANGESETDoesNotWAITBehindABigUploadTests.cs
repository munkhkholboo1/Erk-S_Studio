using System.Diagnostics;

namespace ErkS.Sync.Core.Tests;

/// <summary>
/// Erk-S Sync, 2026-10-28: a 14 GB upload holds a pass for up to 10 minutes, and a CGA changeset made then
/// would wait behind it (owner: a changeset travels «үүсмэгц шууд»). A changeset made here makes the long
/// upload give way at once; the next pass sends the shared files first.
/// </summary>
public sealed class ACHANGESETDoesNotWAITBehindABigUploadTests
{
    private const string Canonical = @"D:\Cloud Work\Erin";

    [Fact]
    public async Task APublishedCHANGESETReachesTheServerWHILEABigFileIsStillGoingUp()
    {
        using var root = new TemporaryFolder();
        using var home = new TemporaryFolder();
        using var scratch = new TemporaryFolder();
        using var a = new FakeDevice();
        using var b = new FakeDevice();
        var server = new FakeServer(Canonical, [a, b]) { Relay = true, PutDelay = TimeSpan.FromMilliseconds(700) };
        string folder = root.Folder("Erin");
        SyncFolderMarker.Write(folder, new SyncFolderIdentity { FolderId = "f-1", ProjectCode = "ERIN-2026", CanonicalPath = Canonical });
        var bytes = new byte[SyncIndex.ChunkSize * 14 + 1];
        new Random(3).NextBytes(bytes);
        File.WriteAllBytes(Path.Combine(folder, "big.dwg"), bytes);
        File.WriteAllText(Path.Combine(folder, "plan.dwg"), "base");
        var syncHome = new SyncHome(home.Path);
        await using var engine = new SyncEngine(syncHome, server.Client(), a, new SyncPathMap(@"D:\Cloud Work=>" + root.Path))
        {
            UploadLanes = 1,
            Period = TimeSpan.FromHours(1),
        };
        using var stop = new CancellationTokenSource();
        Task run = engine.RunAsync(stop.Token);
        var clock = Stopwatch.StartNew();
        while (server.ChunkPutOrder.Count == 0 && clock.Elapsed < TimeSpan.FromSeconds(20))
            await Task.Delay(50);
        Assert.NotEmpty(server.ChunkPutOrder); // the big upload is under way

        SyncCollabFile shared = SyncCollab.Register(folder, Path.Combine(folder, "plan.dwg"), "", "", "0.081", DateTimeOffset.UtcNow, "seat-a_d1");
        string manifest = Path.Combine(scratch.Path, "m.json");
        File.WriteAllText(manifest, "{\"schema\":2}");
        SyncCollab.Publish(folder, shared.FileId, "seat-a_d1", manifest, "", syncHome);
        string changeset = Path.Combine(folder, SyncCollab.DirectoryName, shared.FileId, "log", "seat-a_d1", "00000001.cgacs");
        string changesetSha = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(changeset)));
        clock.Restart();
        engine.Hurry();

        while (!server.ChunkPutOrder.Contains(changesetSha) && clock.Elapsed < TimeSpan.FromSeconds(15))
            await Task.Delay(50);
        TimeSpan took = clock.Elapsed;
        int bigPiecesThen = server.ChunkPutOrder.Count(sha => sha != changesetSha);
        await stop.CancelAsync();
        await run;

        Assert.Contains(changesetSha, server.ChunkPutOrder);
        Assert.True(took < TimeSpan.FromSeconds(5), $"the changeset took {took.TotalSeconds:0.0} s");
        Assert.True(bigPiecesThen < 15, "it went before the big file was done");
    }
}
