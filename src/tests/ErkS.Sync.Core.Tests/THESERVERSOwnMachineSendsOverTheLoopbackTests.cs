namespace ErkS.Sync.Core.Tests;

/// <summary>
/// Erk-S Sync, 2026-11-05 (SRV 95f332a, bea4836): the owner's desktop runs the server and an Erk-S Sync, and 19 GB of
/// Altai went out through the Cloudflare tunnel and came straight back. A machine that PROVES it is the server
/// (the same HMAC from erk-s.mn and from the loopback port) sends and takes the pieces over the loopback; anything
/// else - an impostor on the port, the loopback down - goes the normal road, silently.
/// </summary>
public sealed class THESERVERSOwnMachineSendsOverTheLoopbackTests
{
    private const string Canonical = @"D:\Cloud Work\Erin";

    private static (string A, string B) Folders(TemporaryFolder rootA, TemporaryFolder rootB)
    {
        var identity = new SyncFolderIdentity { FolderId = "f-1", ProjectCode = "ERIN-2026", CanonicalPath = Canonical };
        string folderA = rootA.Folder("Erin"), folderB = rootB.Folder("Erin");
        SyncFolderMarker.Write(folderA, identity);
        SyncFolderMarker.Write(folderB, identity);
        File.WriteAllText(Path.Combine(folderB, "notes.txt"), "B's own");
        return (folderA, folderB);
    }

    private static SyncEngine Engine(TemporaryFolder home, FakeServer server, FakeDevice device, TemporaryFolder root) =>
        new(new SyncHome(home.Path), server.Client(), device, new SyncPathMap(@"D:\Cloud Work=>" + root.Path))
        {
            LoopbackClient = server.LoopbackClient,
        };

    [Fact]
    public async Task APROVENLoopbackCarriesEveryPieceBothWays()
    {
        using var rootA = new TemporaryFolder();
        using var rootB = new TemporaryFolder();
        using var homeA = new TemporaryFolder();
        using var homeB = new TemporaryFolder();
        using var a = new FakeDevice();
        using var b = new FakeDevice();
        var server = new FakeServer(Canonical, [a, b]) { Relay = true, OnThisMachine = true };
        (string folderA, string folderB) = Folders(rootA, rootB);
        File.WriteAllText(Path.Combine(folderA, "plan.pdf"), "the plan");

        await using SyncEngine engineA = Engine(homeA, server, a, rootA);
        await engineA.PassAsync(CancellationToken.None);
        await using SyncEngine engineB = Engine(homeB, server, b, rootB);
        await engineB.PassAsync(CancellationToken.None);

        Assert.Equal("the plan", File.ReadAllText(Path.Combine(folderB, "plan.pdf")));
        Assert.True(server.LoopbackPieceRequests > 0);
        Assert.Equal(0, server.PublicPieceRequests);
        Assert.Equal("loopback", engineB.LastState.Folders.Single().Peers.Single().Route);
    }

    [Fact]
    public async Task ANOTHERProcessOnThePortIsNeverGivenTheToken()
    {
        using var rootA = new TemporaryFolder();
        using var rootB = new TemporaryFolder();
        using var homeA = new TemporaryFolder();
        using var homeB = new TemporaryFolder();
        using var a = new FakeDevice();
        using var b = new FakeDevice();
        var server = new FakeServer(Canonical, [a, b]) { Relay = true, OnThisMachine = true, ImpostorOnLoopback = true };
        (string folderA, string folderB) = Folders(rootA, rootB);
        File.WriteAllText(Path.Combine(folderA, "plan.pdf"), "the plan");

        await using SyncEngine engineA = Engine(homeA, server, a, rootA);
        await engineA.PassAsync(CancellationToken.None);
        await using SyncEngine engineB = Engine(homeB, server, b, rootB);
        await engineB.PassAsync(CancellationToken.None);

        Assert.Equal("the plan", File.ReadAllText(Path.Combine(folderB, "plan.pdf")));
        Assert.Equal(0, server.LoopbackPieceRequests);
        Assert.Equal("server", engineB.LastState.Folders.Single().Peers.Single().Route);
    }

    [Fact]
    public async Task ALOOPBACKThatGoesDownFallsBackAtOnce()
    {
        using var rootA = new TemporaryFolder();
        using var rootB = new TemporaryFolder();
        using var homeA = new TemporaryFolder();
        using var homeB = new TemporaryFolder();
        using var a = new FakeDevice();
        using var b = new FakeDevice();
        var server = new FakeServer(Canonical, [a, b]) { Relay = true, OnThisMachine = true };
        (string folderA, string folderB) = Folders(rootA, rootB);
        File.WriteAllText(Path.Combine(folderA, "plan.pdf"), "the plan");

        await using SyncEngine engineA = Engine(homeA, server, a, rootA);
        await engineA.PassAsync(CancellationToken.None);
        await using SyncEngine engineB = Engine(homeB, server, b, rootB);
        server.LoopbackDown = true; // proven, then gone before the first piece
        server.OnFirstChunkGet = null;
        await engineB.PassAsync(CancellationToken.None);

        Assert.Equal("the plan", File.ReadAllText(Path.Combine(folderB, "plan.pdf")));
        Assert.True(server.PublicPieceRequests > 0);
    }

    [Fact]
    public async Task AFILEMadeWholeMidTransferIsTakenInTheSamePass()
    {
        using var rootA = new TemporaryFolder();
        using var rootB = new TemporaryFolder();
        using var homeA = new TemporaryFolder();
        using var homeB = new TemporaryFolder();
        using var a = new FakeDevice();
        using var b = new FakeDevice();
        var server = new FakeServer(Canonical, [a, b]) { Relay = true };
        (string folderA, string folderB) = Folders(rootA, rootB);
        File.WriteAllText(Path.Combine(folderA, "first.pdf"), "first");

        await using var engineA = new SyncEngine(new SyncHome(homeA.Path), server.Client(), a, new SyncPathMap(@"D:\Cloud Work=>" + rootA.Path));
        await engineA.PassAsync(CancellationToken.None);
        string withFirst = server.IndexOf("t-" + a.DeviceKeyId);
        File.WriteAllText(Path.Combine(folderA, "second.pdf"), "second");
        await engineA.PassAsync(CancellationToken.None);
        string withBoth = server.IndexOf("t-" + a.DeviceKeyId);
        server.SetIndex("t-" + a.DeviceKeyId, withFirst); // B starts while only the first is whole

        await using var engineB = new SyncEngine(new SyncHome(homeB.Path), server.Client(), b, new SyncPathMap(@"D:\Cloud Work=>" + rootB.Path));
        server.OnFirstChunkGet = () =>
        {
            // The second file became whole while B takes the first: the server tells it (SRV 95f332a).
            server.SetIndex("t-" + a.DeviceKeyId, withBoth);
            engineB.Heard(System.Text.Encoding.UTF8.GetBytes(
                $$"""{"type":"index","folderId":"f-1","deviceKeyId":"{{a.DeviceKeyId}}","indexVersion":"x","missingPieces":0}"""));
        };
        await engineB.PassAsync(CancellationToken.None);

        Assert.Equal("first", File.ReadAllText(Path.Combine(folderB, "first.pdf")));
        Assert.Equal("second", File.ReadAllText(Path.Combine(folderB, "second.pdf")));
    }
}
