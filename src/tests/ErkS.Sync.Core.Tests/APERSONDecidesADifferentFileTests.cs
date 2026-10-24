namespace ErkS.Sync.Core.Tests;

/// <summary>
/// Owner, 2026-10-24: «бид анхнаасаа хоосон фолдер синк хийхгүй … хамтрагч ч гэсэн хоосон биш
/// фолдер холбоно. бүүр адилхан нэртэй файл байх магадлалтай». Two non-empty copies of one
/// project meet: identical files are left alone, and every same-named file that differs is
/// shown until a person answers «Тэндхийг авах» or «Минийхийг үлдээх» - and then it moves,
/// with the version it replaces kept aside.
/// </summary>
public sealed class APERSONDecidesADifferentFileTests
{
    private const string Canonical = @"D:\Cloud Work\Erin";
    private const string Me = "AAAA0000";
    private const string Peer = "BBBB1111";

    [Fact]
    public void TakingTheirsReplacesAFileThatNeverSharedAVersion()
    {
        SyncIndex mine = Index(Entry("plan.dwg", "a", Version("a", Me)));
        SyncIndex theirs = Index(Entry("plan.dwg", "b", Version("b", Peer)));
        var decision = new SyncDecision("plan.dwg", "b", "a", SyncChoice.Theirs, DateTimeOffset.UtcNow);

        SyncTransferAction action = Assert.Single(SyncTransferPlanner.Plan(mine, theirs, new SyncVersionLedger(), [decision]).Actions);

        Assert.Equal(SyncTransferKind.Replace, action.Kind);
        Assert.True(action.KeepPrevious);
    }

    [Fact]
    public void AnAnswerAboutAnOLDERVersionDoesNotApplyToANewEdit()
    {
        SyncIndex mine = Index(Entry("plan.dwg", "a2", Version("a2", Me)));
        SyncIndex theirs = Index(Entry("plan.dwg", "b", Version("b", Peer)));
        var decision = new SyncDecision("plan.dwg", "b", "a1", SyncChoice.Theirs, DateTimeOffset.UtcNow);

        Assert.Empty(SyncTransferPlanner.Plan(mine, theirs, new SyncVersionLedger(), [decision]).Actions);
    }

    [Fact]
    public void KeepingMineMakesThisVersionTheOneTheOtherTakes()
    {
        var ledger = new SyncVersionLedger();
        ledger.Stamp(Index(Entry("plan.dwg", "a", null)), Me);

        ledger.Adopt([new SyncDecision("plan.dwg", "b", "a", SyncChoice.Mine, DateTimeOffset.UtcNow)]);
        SyncFileVersion mine = ledger.Stamp(Index(Entry("plan.dwg", "a", null)), Me).Entries[0].Version!;

        // On the other machine: its «b» is in this version's history, so this one replaces it.
        SyncTransferPlan there = SyncTransferPlanner.Plan(
            Index(Entry("plan.dwg", "b", Version("b", Peer))),
            Index(Entry("plan.dwg", "a", mine)),
            new SyncVersionLedger());
        Assert.Equal(SyncTransferKind.Replace, Assert.Single(there.Actions).Kind);
    }

    [Fact]
    public async Task TwoNonEmptyCopiesMeet_IdenticalStays_DecidedMoves_ReplacedIsKept()
    {
        using var rootA = new TemporaryFolder();
        using var rootB = new TemporaryFolder();
        using var homeA = new TemporaryFolder();
        using var homeB = new TemporaryFolder();
        using var a = new FakeDevice();
        using var b = new FakeDevice();
        var server = new FakeServer(Canonical, [a, b]);
        string folderA = rootA.Folder("Erin");
        string folderB = rootB.Folder("Erin");
        foreach ((TemporaryFolder root, string folder, string side) in new[] { (rootA, folderA, "A"), (rootB, folderB, "B") })
        {
            root.File(@"Erin\same.dwg", "identical");
            root.File(@"Erin\plan.dwg", "plan from " + side);
            root.File(@"Erin\facade.dwg", "facade from " + side);
            SyncFolderMarker.Write(folder, Identity());
        }

        // 1. They meet: nothing is overwritten, the two different files are shown.
        await RunUntilAsync(server, homeA, homeB, rootA, rootB, engines =>
            engines.B.LastState.Folders.SingleOrDefault()?.Peers.SingleOrDefault()?.UnrelatedCount == 2);
        Assert.Equal("plan from B", File.ReadAllText(Path.Combine(folderB, "plan.dwg")));

        // 2. On B the person answers: plan.dwg - take A's; on A: facade.dwg - keep A's.
        SyncHome bHome = new(homeB.Path), aHome = new(homeA.Path);
        SyncIndex bIndex = SyncIndexStore.TryLoad(bHome.IndexPath("f-1"))!;
        SyncIndex aSeenByB = SyncIndexStore.TryLoad(bHome.PeerIndexPath("f-1", a.DeviceKeyId))!;
        SyncDecisionStore.Add(bHome.DecisionsPath("f-1"), SyncDecisionStore.ForDifferences(bIndex, aSeenByB, SyncChoice.Theirs, ["plan.dwg"]));
        SyncIndex aIndex = SyncIndexStore.TryLoad(aHome.IndexPath("f-1"))!;
        SyncIndex bSeenByA = SyncIndexStore.TryLoad(aHome.PeerIndexPath("f-1", b.DeviceKeyId))!;
        SyncDecisionStore.Add(aHome.DecisionsPath("f-1"), SyncDecisionStore.ForDifferences(aIndex, bSeenByA, SyncChoice.Mine, ["facade.dwg"]));

        await RunUntilAsync(server, homeA, homeB, rootA, rootB, _ =>
            File.ReadAllText(Path.Combine(folderB, "plan.dwg")) == "plan from A" &&
            File.ReadAllText(Path.Combine(folderB, "facade.dwg")) == "facade from A");

        Assert.Equal("identical", File.ReadAllText(Path.Combine(folderB, "same.dwg")));
        Assert.Equal("plan from A", File.ReadAllText(Path.Combine(folderA, "plan.dwg")));
        string[] kept = Directory.GetFiles(Path.Combine(folderB, SyncExclusions.MarkerDirectoryName, "replaced"), "*.dwg", SearchOption.AllDirectories);
        Assert.Contains(kept, path => File.ReadAllText(path) == "plan from B");
        Assert.Contains(kept, path => File.ReadAllText(path) == "facade from B");
    }

    private static async Task RunUntilAsync(
        FakeServer server,
        TemporaryFolder homeA,
        TemporaryFolder homeB,
        TemporaryFolder rootA,
        TemporaryFolder rootB,
        Func<(SyncEngine A, SyncEngine B), bool> done)
    {
        await using var engineA = new SyncEngine(new SyncHome(homeA.Path), server.Client(), server.Devices[0], new SyncPathMap(@"D:\Cloud Work=>" + rootA.Path));
        await using var engineB = new SyncEngine(new SyncHome(homeB.Path), server.Client(), server.Devices[1], new SyncPathMap(@"D:\Cloud Work=>" + rootB.Path));
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        Task runA = engineA.RunAsync(stop.Token);
        Task runB = engineB.RunAsync(stop.Token);
        bool reached = false;
        for (int attempt = 0; attempt < 120 && !reached; attempt++)
        {
            await Task.Delay(500);
            engineA.Poke();
            engineB.Poke();
            reached = done((engineA, engineB));
        }
        await stop.CancelAsync();
        await Task.WhenAll(runA, runB);
        Assert.True(reached, "the two machines did not get there in time");
    }

    private static SyncIndex Index(params SyncIndexEntry[] entries) => new() { FolderId = "f-1", Entries = entries };

    private static SyncFileVersion Version(string sha, string writer) => new() { Sha256 = sha, Writer = writer, Seq = 1 };

    private static SyncIndexEntry Entry(string path, string sha, SyncFileVersion? version) =>
        new(path, 1, DateTime.UtcNow, SyncFileKinds.Of(path), sha, [sha]) { Version = version };

    private static SyncFolderIdentity Identity() => new()
    {
        FolderId = "f-1",
        ProjectCode = "ERIN-2026",
        ProjectId = "p-1",
        CanonicalPath = Canonical,
        CreatedAtUtc = DateTimeOffset.UtcNow,
    };
}
