using System.Security.Cryptography;

namespace ErkS.Sync.Core.Tests;

/// <summary>
/// Erk-S Sync, 2026-10-26: two redactions recorded before the server had the route never went - a
/// failure was cached for the session and the error swallowed. Each pass sends what the server lacks;
/// what is still unsent, and why, is in the folder's status.
/// </summary>
public sealed class AREDACTIONRecordedOFFLINEReachesTheSERVERTests
{
    private const string Canonical = @"D:\Cloud Work\Erin";

    [Fact]
    public async Task ARedactionTheServerCouldNotTakeGoesOnceItCan()
    {
        using var root = new TemporaryFolder();
        using var home = new TemporaryFolder();
        using var a = new FakeDevice();
        using var b = new FakeDevice();
        var server = new FakeServer(Canonical, [a, b]) { Relay = true };
        string folder = root.Folder("Erin");
        SyncFolderMarker.Write(folder, new SyncFolderIdentity { FolderId = "f-1", ProjectCode = "ERIN-2026", CanonicalPath = Canonical });
        await using var engine = new SyncEngine(new SyncHome(home.Path), server.Client(), a, new SyncPathMap(@"D:\Cloud Work=>" + root.Path));
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        SyncRedactions.Add(folder, @".erks-collab\file-1\log\w\00000002.cgacs", new string('a', 64), new string('b', 64), a.DeviceKeyId, key.SignData, DateTimeOffset.UtcNow);

        await engine.PassAsync(CancellationToken.None);
        SyncFolderState before = engine.LastState.Folders.Single();
        Assert.Equal(1, before.RedactionsUnsent);
        Assert.Contains("404", before.RedactionError);
        Assert.Empty(server.Redactions);

        server.RedactionsRoute = true; // deployed - same session, no restart
        await engine.PassAsync(CancellationToken.None);

        Assert.Single(server.Redactions);
        Assert.Equal(0, engine.LastState.Folders.Single().RedactionsUnsent);
        await engine.PassAsync(CancellationToken.None);
        Assert.Single(server.Redactions); // held by the server now: not sent again
    }

    [Fact]
    public async Task ACurrentClientRefusedAsOutdatedOpensANewSessionAndWrites()
    {
        using var rootA = new TemporaryFolder();
        using var home = new TemporaryFolder();
        using var a = new FakeDevice();
        using var b = new FakeDevice();
        var server = new FakeServer(Canonical, [a, b]) { Relay = true, OutdatedWrites = 1 };
        string folder = rootA.Folder("Erin");
        SyncFolderMarker.Write(folder, new SyncFolderIdentity { FolderId = "f-1", ProjectCode = "ERIN-2026", CanonicalPath = Canonical });
        File.WriteAllText(Path.Combine(folder, "plan.pdf"), "plan");
        var client = new SyncServerClient(new HttpClient(server, disposeHandler: false) { BaseAddress = new Uri("https://erk-s.test") }) { ClientVersion = "0.001.110" };
        await using var engine = new SyncEngine(new SyncHome(home.Path), client, a, new SyncPathMap(@"D:\Cloud Work=>" + rootA.Path));

        await engine.PassAsync(CancellationToken.None);
        int sessions = server.Sessions;
        await engine.PassAsync(CancellationToken.None);

        Assert.True(server.Sessions > sessions, "a new session was not opened after 426");
        Assert.NotNull(await server.Client().GetIndexAsync("t-" + b.DeviceKeyId, "f-1", a.DeviceKeyId));
    }
}
