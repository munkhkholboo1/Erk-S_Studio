namespace ErkS.Sync.Core.Tests;

/// <summary>
/// 🔴 Owner, 2026-10-24: «бид чинь cloud sync хийх гээд байгаа шүү дээ». Будбаясгалан joined the
/// folder from another network and SYN 1.2, LAN-only, showed «LAN дээр олдсонгүй» and sent
/// nothing. Now each machine puts its index and the pieces of the files that travel on the
/// server, and a partner who cannot be heard on the LAN takes them from there - whole file or
/// nothing, as on the LAN. A Revit central model still never goes.
/// </summary>
public sealed class APARTNERONAnotherNetworkSYNCSThroughTheServerTests
{
    private const string Canonical = @"D:\Cloud Work\Erin";

    [Fact]
    public async Task AFileReachesAPartnerWhoIsNotOnTheLan()
    {
        using var rootA = new TemporaryFolder();
        using var rootB = new TemporaryFolder();
        using var homeA = new TemporaryFolder();
        using var homeB = new TemporaryFolder();
        using var a = new FakeDevice();
        using var b = new FakeDevice();
        var server = new FakeServer(Canonical, [a, b]) { Relay = true };
        string folderA = rootA.Folder("Erin");
        string folderB = rootB.Folder("Erin");
        SyncFolderMarker.Write(folderA, Identity());
        SyncFolderMarker.Write(folderB, Identity());
        byte[] model = new byte[SyncIndex.ChunkSize + 10];
        new Random(3).NextBytes(model);
        await File.WriteAllBytesAsync(Path.Combine(folderA, "Sheets.rvt"), model);
        File.WriteAllText(Path.Combine(folderA, "plan.dwg"), "v1");
        // A central model and its data stay on their machine by rule.
        File.WriteAllText(Path.Combine(folderA, "NP.rvt"), "central");
        Directory.CreateDirectory(Path.Combine(folderA, "NP_backup"));
        File.WriteAllText(Path.Combine(folderA, "NP_backup", "1_2.rws"), "central data");

        // No RunAsync: neither machine is on a LAN - only passes against the server.
        await using var engineA = new SyncEngine(new SyncHome(homeA.Path), server.Client(), a, new SyncPathMap(@"D:\Cloud Work=>" + rootA.Path));
        await using var engineB = new SyncEngine(new SyncHome(homeB.Path), server.Client(), b, new SyncPathMap(@"D:\Cloud Work=>" + rootB.Path));
        await engineA.PassAsync(CancellationToken.None);
        await engineB.PassAsync(CancellationToken.None);

        Assert.Equal(model, await File.ReadAllBytesAsync(Path.Combine(folderB, "Sheets.rvt")));
        Assert.Equal("v1", File.ReadAllText(Path.Combine(folderB, "plan.dwg")));
        Assert.False(File.Exists(Path.Combine(folderB, "NP.rvt")));
        Assert.False(Directory.Exists(Path.Combine(folderB, "NP_backup")));
        SyncPeerState seen = engineB.LastState.Folders.Single().Peers.Single();
        Assert.Equal("server", seen.Route);

        // Erk-S Central: A serves its central; B, which has none, reaches it as a member.
        SyncCentralState served = engineA.LastState.Folders.Single().Centrals.Single();
        Assert.Equal(("host", "NP.rvt"), (served.Role, served.RelativePath));
        // Not served until a person turns it on (Master's safeguard 1: the real central needs the owner's consent).
        Assert.Contains("асаагүй", served.LastError, StringComparison.Ordinal);
        SyncCentralSwitch.Set(new SyncHome(homeA.Path), served.CentralId, on: true);
        await engineA.PassAsync(CancellationToken.None);
        Assert.DoesNotContain("асаагүй", engineA.LastState.Folders.Single().Centrals.Single().LastError, StringComparison.Ordinal);
        SyncCentralState reached = engineB.LastState.Folders.Single().Centrals.Single();
        Assert.Equal(("member", served.CentralId), (reached.Role, reached.CentralId));
        Assert.Equal(2, seen.ReceivedCount);

        // An edit on A goes the same way.
        File.WriteAllText(Path.Combine(folderA, "plan.dwg"), "v2");
        await engineA.PassAsync(CancellationToken.None);
        await engineB.PassAsync(CancellationToken.None);
        Assert.Equal("v2", File.ReadAllText(Path.Combine(folderB, "plan.dwg")));

        // Once both hold the same, the partner reads 100%, the central model apart.
        await engineB.PassAsync(CancellationToken.None);
        SyncPeerState inStep = engineB.LastState.Folders.Single().Peers.Single();
        Assert.Equal(100, inStep.SyncedPercent);
        Assert.Equal(2, inStep.NotCarriedCount);
    }

    [Fact]
    public async Task AServerWithoutTheRelaySaysSoAndNothingBreaks()
    {
        using var rootA = new TemporaryFolder();
        using var homeA = new TemporaryFolder();
        using var a = new FakeDevice();
        using var b = new FakeDevice();
        var server = new FakeServer(Canonical, [a, b]);
        string folderA = rootA.Folder("Erin");
        SyncFolderMarker.Write(folderA, Identity());
        File.WriteAllText(Path.Combine(folderA, "plan.dwg"), "v1");

        await using var engineA = new SyncEngine(new SyncHome(homeA.Path), server.Client(), a, new SyncPathMap(@"D:\Cloud Work=>" + rootA.Path));
        await engineA.PassAsync(CancellationToken.None);

        SyncFolderState folder = engineA.LastState.Folders.Single();
        Assert.True(folder.PresentHere);
        Assert.Contains("cloud sync", folder.LastError, StringComparison.Ordinal);
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
