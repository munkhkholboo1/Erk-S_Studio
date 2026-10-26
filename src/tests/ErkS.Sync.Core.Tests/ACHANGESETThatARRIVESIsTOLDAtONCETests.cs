namespace ErkS.Sync.Core.Tests;

/// <summary>
/// Owner, 2026-10-26: «байж болох хамгийн хурдан» (_shared/syn-push-latency-plan-2026-10-26.md). A
/// member's new changeset is told to CGA as it arrives (collab-subscribe), and a Generate's pull runs a
/// pass first so it takes the newest work.
/// </summary>
public sealed class ACHANGESETThatARRIVESIsTOLDAtONCETests
{
    private const string Canonical = @"D:\Cloud Work\Erin";

    [Fact]
    public async Task AnotherMembersChangesetIsToldOnceAndOwnOnesNever()
    {
        using var rootA = new TemporaryFolder();
        using var rootB = new TemporaryFolder();
        using var homeA = new TemporaryFolder();
        using var homeB = new TemporaryFolder();
        using var scratch = new TemporaryFolder();
        using var a = new FakeDevice();
        using var b = new FakeDevice();
        var server = new FakeServer(Canonical, [a, b]) { Relay = true };
        string folderA = rootA.Folder("Erin"), folderB = rootB.Folder("Erin");
        var identity = new SyncFolderIdentity { FolderId = "f-1", ProjectCode = "ERIN-2026", CanonicalPath = Canonical };
        SyncFolderMarker.Write(folderA, identity);
        SyncFolderMarker.Write(folderB, identity);
        File.WriteAllText(Path.Combine(folderA, "plan.dwg"), "base");
        await using var engineA = new SyncEngine(new SyncHome(homeA.Path), server.Client(), a, new SyncPathMap(@"D:\Cloud Work=>" + rootA.Path));
        await using var engineB = new SyncEngine(new SyncHome(homeB.Path), server.Client(), b, new SyncPathMap(@"D:\Cloud Work=>" + rootB.Path));
        var told = new List<SyncCollabArrival>();
        engineB.CollabArrived += told.Add;

        SyncCollab.Register(folderA, Path.Combine(folderA, "plan.dwg"), "file-1", "", "v1", DateTimeOffset.UtcNow, "seat-a_d1");
        SyncCollab.Publish(folderA, "file-1", "seat-a_d1", Write(scratch, "m1.json", "m1"), Write(scratch, "e1.dwg", "e1"));
        await engineA.PassAsync(CancellationToken.None);
        await engineB.PassAsync(CancellationToken.None);
        await engineB.PassAsync(CancellationToken.None); // primes: what was there before is not news
        Assert.Empty(told);

        SyncCollab.Publish(folderA, "file-1", "seat-a_d1", Write(scratch, "m2.json", "m2"), Write(scratch, "e2.dwg", "e2"));
        await engineA.PassAsync(CancellationToken.None);
        await engineB.PassAsync(CancellationToken.None);

        SyncCollabArrival arrival = Assert.Single(told);
        Assert.Equal(("file-1", "seat-a_d1", 2L), (arrival.FileId, arrival.Writer, arrival.Seq));
        Assert.Equal(Path.Combine(folderB, "plan.dwg"), arrival.Path);
        await engineB.PassAsync(CancellationToken.None);
        Assert.Single(told); // told once
    }

    [Fact]
    public async Task ARefreshWaitsForThePassItAskedFor()
    {
        using var root = new TemporaryFolder();
        using var home = new TemporaryFolder();
        using var a = new FakeDevice();
        var server = new FakeServer(Canonical, [a]) { Relay = true };
        SyncFolderMarker.Write(root.Folder("Erin"), new SyncFolderIdentity { FolderId = "f-1", ProjectCode = "ERIN-2026", CanonicalPath = Canonical });
        await using var engine = new SyncEngine(new SyncHome(home.Path), server.Client(), a, new SyncPathMap(@"D:\Cloud Work=>" + root.Path)) { Period = TimeSpan.FromHours(1) };
        using var stop = new CancellationTokenSource();
        Task run = engine.RunAsync(stop.Token);
        await Task.Delay(300);

        Assert.True(await engine.RefreshAsync(TimeSpan.FromSeconds(10), CancellationToken.None));
        await stop.CancelAsync();
        await run;
    }

    /// <summary>Erk-S Sync, 2026-10-27: an open Studio kept saying «no update» - the server's «release» event is told on at once.</summary>
    [Fact]
    public async Task APUBLISHEDVersionIsToldWhenItIsPublished()
    {
        using var home = new TemporaryFolder();
        using var a = new FakeDevice();
        var server = new FakeServer(Canonical, [a]);
        await using var engine = new SyncEngine(new SyncHome(home.Path), server.Client(), a);
        var told = new List<(string Product, string Version)>();
        engine.ReleasePublished += (product, version) => told.Add((product, version));

        engine.Heard("{\"type\":\"release\",\"productCode\":\"ErkS.Studio\",\"version\":\"v0.001.122\"}"u8.ToArray());
        engine.Heard("{\"type\":\"release\",\"productCode\":\"ErkS.Studio\"}"u8.ToArray()); // no version: nothing

        Assert.Equal([("ErkS.Studio", "v0.001.122")], told);
        Assert.Equal("v0.001.122", engine.Releases["ErkS.Studio"]);
    }

    private static string Write(TemporaryFolder scratch, string name, string text)
    {
        string path = Path.Combine(scratch.Path, name);
        File.WriteAllText(path, text);
        return path;
    }
}
