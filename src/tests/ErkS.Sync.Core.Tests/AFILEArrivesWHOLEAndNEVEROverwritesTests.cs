using System.Security.Cryptography;
using System.Text;

namespace ErkS.Sync.Core.Tests;

/// <summary>
/// SYN 1.2 - files move between a folder's machines.
///
/// 🔴 Acceptance (plan §4.8): «1 GB RVT/DWG-д өөрчлөгдсөн хэсэг л дамжина; хагас дамжсан файл
/// хэзээ ч харагдахгүй; офлайн хоёр засвар → 2 хувилбар хадгалагдана». And the owner's rule
/// over all of it: nothing is ever silently overwritten.
/// </summary>
public sealed class AFILEArrivesWHOLEAndNEVEROverwritesTests
{
    private const string Canonical = @"D:\Cloud Work\Erin";
    private const string Me = "AAAA0000";
    private const string Peer = "BBBB1111";

    // ---- the planner ---------------------------------------------------------------------

    [Fact]
    public void ANewerVersionReplacesTheOneItCameAfter()
    {
        SyncFileVersion v1 = Version("v1", Me, 1);
        SyncIndexEntry mine = Entry("plan.dwg", "v1", v1);
        SyncIndexEntry theirs = Entry("plan.dwg", "v2", v1.Next("v2", Peer));

        SyncTransferAction action = Assert.Single(Plan([mine], [theirs]).Actions);

        Assert.Equal(SyncTransferKind.Replace, action.Kind);
    }

    [Fact]
    public void AnOlderPeerVersionChangesNothingHere()
    {
        SyncFileVersion v1 = Version("v1", Peer, 1);
        SyncTransferPlan plan = Plan([Entry("plan.dwg", "v2", v1.Next("v2", Me))], [Entry("plan.dwg", "v1", v1)]);

        Assert.Empty(plan.Actions);
        Assert.Equal(1, plan.AheadHere);
    }

    [Fact]
    public void TwoEditsOfOneVersionAreBothKept()
    {
        SyncFileVersion v1 = Version("v1", Me, 1);
        SyncTransferPlan plan = Plan(
            [Entry("plan.dwg", "mine2", v1.Next("mine2", Me))],
            [Entry("plan.dwg", "theirs2", v1.Next("theirs2", Peer))]);

        Assert.Equal(SyncTransferKind.KeepBoth, Assert.Single(plan.Actions).Kind);
    }

    [Fact]
    public void ACopyThatNeverSharedAVersionIsOnlyShown()
    {
        SyncTransferPlan plan = Plan(
            [Entry("plan.dwg", "a", Version("a", Me, 1))],
            [Entry("plan.dwg", "b", Version("b", Peer, 1))]);

        Assert.Empty(plan.Actions);
        Assert.Equal(1, plan.Unrelated);
    }

    [Fact]
    public void AFileOnlyThePeerHoldsComes_UnlessItWasDeletedHere()
    {
        SyncFileVersion v1 = Version("v1", Peer, 1);
        Assert.Equal(SyncTransferKind.Fetch, Assert.Single(Plan([], [Entry("new.pdf", "v1", v1)]).Actions).Kind);

        var ledger = new SyncVersionLedger();
        ledger.Applied("old.pdf", v1);
        ledger.Stamp(new SyncIndex { Entries = [] }, Me); // gone from this disk
        SyncTransferPlan plan = SyncTransferPlanner.Plan(Index(), Index(Entry("old.pdf", "v1", v1)), ledger);

        Assert.Empty(plan.Actions);
        Assert.Equal(1, plan.DeletedHere);
    }

    [Fact]
    public void ARevitCentralNeverTravelsFileByFile()
    {
        SyncIndexEntry central = Entry("Model.rvt", "v1", Version("v1", Peer, 1)) with { Kind = SyncFileKind.RevitCentral };
        SyncIndexEntry data = Entry(@"Model_backup\incr.dat", "v1", Version("v1", Peer, 1)) with { Kind = SyncFileKind.RevitCentralData };

        SyncTransferPlan plan = Plan([], [central, data]);

        Assert.Empty(plan.Actions);
        Assert.Equal(2, plan.NotCarried);
    }

    [Fact]
    public void AConflictCopyIsKeptOnce()
    {
        SyncFileVersion v1 = Version("v1", Me, 1);
        var ledger = new SyncVersionLedger();
        ledger.Keep("plan.dwg", "theirs2");

        SyncTransferPlan plan = SyncTransferPlanner.Plan(
            Index(Entry("plan.dwg", "mine2", v1.Next("mine2", Me))),
            Index(Entry("plan.dwg", "theirs2", v1.Next("theirs2", Peer))),
            ledger);

        Assert.Empty(plan.Actions);
        Assert.True(SyncExclusions.IsExcludedFile(Path.GetFileName(SyncExclusions.ConflictCopyPath(@"a\plan.dwg", Peer, DateTimeOffset.UtcNow))));
    }

    [Fact]
    public void HowFarInStepIsCountedInBytes()
    {
        SyncIndex mine = Index(Sized("a.dwg", "1", 300), Sized("b.rvt", "x", 100), Sized("only-here.pdf", "h", 50));
        SyncIndex theirs = Index(Sized("a.dwg", "1", 300), Sized("b.rvt", "y", 150), Sized("only-there.pdf", "t", 50));

        SyncIndexDifference difference = SyncIndexComparison.Compare(mine, theirs);

        Assert.Equal(300, difference.SameBytes);
        Assert.Equal(300 + 150 + 50 + 50, difference.UnionBytes);
        Assert.Equal(54.5, difference.SyncedPercent);
        Assert.Equal(100, SyncIndexComparison.Compare(Index(), Index()).SyncedPercent);
    }

    private static SyncIndexEntry Sized(string path, string sha, long size) =>
        new(path, size, DateTime.UtcNow, SyncFileKinds.Of(path), sha, [sha]);

    // ---- the ledger ----------------------------------------------------------------------

    [Fact]
    public void TheLedgerNumbersALocalEditAndKeepsAnArrivedVersion()
    {
        var ledger = new SyncVersionLedger();
        SyncIndex first = ledger.Stamp(Index(Entry("plan.dwg", "v1", null)), Me);
        Assert.Equal(1, first.Entries[0].Version!.Seq);

        SyncIndex edited = ledger.Stamp(Index(Entry("plan.dwg", "v2", null)), Me);
        SyncFileVersion version = edited.Entries[0].Version!;
        Assert.Equal(2, version.Seq);
        Assert.Equal(["v1"], version.History);
        Assert.Equal(Me, version.Writer);

        SyncFileVersion arrived = version.Next("v3", Peer);
        ledger.Applied("plan.dwg", arrived);
        Assert.Equal(arrived, ledger.Stamp(Index(Entry("plan.dwg", "v3", null)), Me).Entries[0].Version);
    }

    // ---- the assembler -------------------------------------------------------------------

    [Fact]
    public async Task OnlyTheChangedPieceTravelsAndTheFileSwitchesWhole()
    {
        using var root = new TemporaryFolder();
        byte[] original = Bytes(SyncIndex.ChunkSize * 2 + 100, seed: 1);
        string path = Path.Combine(root.Path, "model.rvt");
        await File.WriteAllBytesAsync(path, original);
        SyncIndex mine = SyncIndexBuilder.Build(root.Path, "f-1");
        new SyncVersionLedger().Stamp(mine, Me);

        byte[] changed = (byte[])original.Clone();
        changed[SyncIndex.ChunkSize + 5] ^= 0xFF; // only the second piece differs
        SyncIndexEntry theirs = EntryOf("model.rvt", changed) with { Version = Version(Sha(original), Me, 1).Next(Sha(changed), Peer) };
        var asked = new List<int>();

        SyncApplyResult result = await SyncFileAssembler.ApplyAsync(
            root.Path,
            new SyncTransferAction(SyncTransferKind.Replace, theirs, mine.Entries.Single()),
            mine,
            (piece, _) =>
            {
                asked.Add(piece);
                return Task.FromResult(Piece(changed, piece));
            },
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        Assert.Equal(SyncApplyOutcome.Applied, result.Outcome);
        Assert.Equal([1], asked);
        Assert.Equal(2, result.ChunksReused);
        Assert.Equal(changed, await File.ReadAllBytesAsync(path));
        Assert.Empty(Directory.GetFiles(Path.Combine(root.Path, SyncExclusions.MarkerDirectoryName, "tmp")));
    }

    [Fact]
    public async Task ABadPieceLeavesTheFileAsItWas()
    {
        using var root = new TemporaryFolder();
        string path = root.File("plan.dwg", "original");
        SyncIndex mine = SyncIndexBuilder.Build(root.Path, "f-1");
        byte[] next = Encoding.UTF8.GetBytes("next version");
        SyncIndexEntry theirs = EntryOf("plan.dwg", next) with { Version = Version("x", Peer, 2) };

        await Assert.ThrowsAsync<InvalidDataException>(() => SyncFileAssembler.ApplyAsync(
            root.Path,
            new SyncTransferAction(SyncTransferKind.Replace, theirs, mine.Entries.Single()),
            mine,
            (_, _) => Task.FromResult(Encoding.UTF8.GetBytes("tampered!!!!")),
            DateTimeOffset.UtcNow,
            CancellationToken.None));

        Assert.Equal("original", await File.ReadAllTextAsync(path));
        Assert.Empty(Directory.GetFiles(Path.Combine(root.Path, SyncExclusions.MarkerDirectoryName, "tmp")));
    }

    [Fact]
    public async Task AFileOpenInAProgramWaits()
    {
        using var root = new TemporaryFolder();
        string path = root.File("plan.dwg", "original");
        SyncIndex mine = SyncIndexBuilder.Build(root.Path, "f-1");
        byte[] next = Encoding.UTF8.GetBytes("next");
        SyncIndexEntry theirs = EntryOf("plan.dwg", next) with { Version = Version("x", Peer, 2) };

        SyncApplyResult result;
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read)) // AutoCAD holds it
        {
            result = await SyncFileAssembler.ApplyAsync(
                root.Path,
                new SyncTransferAction(SyncTransferKind.Replace, theirs, mine.Entries.Single()),
                mine,
                (piece, _) => Task.FromResult(Piece(next, piece)),
                DateTimeOffset.UtcNow,
                CancellationToken.None);
        }

        Assert.Equal(SyncApplyOutcome.Waiting, result.Outcome);
        Assert.Equal("open_here", result.Reason);
        Assert.Equal("original", await File.ReadAllTextAsync(path));
    }

    // ---- two machines --------------------------------------------------------------------

    [Fact]
    public async Task TwoMachinesCarryAFileItsEditAndKeepBothOfferedEdits()
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
        SyncFolderMarker.Write(folderA, Identity());
        SyncFolderMarker.Write(folderB, Identity());
        byte[] model = Bytes(SyncIndex.ChunkSize * 2 + 10, seed: 7);
        await File.WriteAllBytesAsync(Path.Combine(folderA, "model.rvt"), model);
        File.WriteAllText(Path.Combine(folderA, "plan.dwg"), "v1");

        // 1. B starts empty: both files arrive.
        await RunUntilAsync(server, homeA, homeB, rootA, rootB, () =>
            File.Exists(Path.Combine(folderB, "model.rvt")) && File.Exists(Path.Combine(folderB, "plan.dwg")));
        Assert.Equal(model, await File.ReadAllBytesAsync(Path.Combine(folderB, "model.rvt")));

        // 2. A edits one piece of the model: B takes the newer version, one piece over the wire.
        model[5] ^= 0x55;
        await File.WriteAllBytesAsync(Path.Combine(folderA, "model.rvt"), model);
        SyncPeerState? bSawA = null;
        await RunUntilAsync(server, homeA, homeB, rootA, rootB, () =>
            File.ReadAllBytes(Path.Combine(folderB, "model.rvt")).SequenceEqual(model),
            engineB => bSawA = engineB.LastState.Folders.SingleOrDefault()?.Peers.FirstOrDefault() ?? bSawA);
        Assert.Equal(SyncIndex.ChunkSize, bSawA!.BytesReceived);

        // 2b. «хэдэн хувьтай синк хийгдсэн»: once both hold the same, the peer reads 100%.
        SyncPeerState? inStep = null;
        await RunUntilAsync(server, homeA, homeB, rootA, rootB, () => inStep?.SyncedPercent == 100,
            engineB => inStep = engineB.LastState.Folders.SingleOrDefault()?.Peers.FirstOrDefault() ?? inStep);
        Assert.Equal(inStep!.UnionBytes, inStep.SyncedBytes);
        Assert.False(inStep.Transferring);

        // 3. Both edit the same version offline: neither is overwritten, each keeps the other's beside.
        File.WriteAllText(Path.Combine(folderA, "plan.dwg"), "edited on A");
        File.WriteAllText(Path.Combine(folderB, "plan.dwg"), "edited on B");
        await RunUntilAsync(server, homeA, homeB, rootA, rootB, () =>
            Directory.GetFiles(folderA, "plan (Erk-S зөрчил *").Length == 1 &&
            Directory.GetFiles(folderB, "plan (Erk-S зөрчил *").Length == 1);
        Assert.Equal("edited on A", File.ReadAllText(Path.Combine(folderA, "plan.dwg")));
        Assert.Equal("edited on B", File.ReadAllText(Path.Combine(folderB, "plan.dwg")));
        Assert.Equal("edited on B", File.ReadAllText(Directory.GetFiles(folderA, "plan (Erk-S зөрчил *").Single()));
        Assert.Equal("edited on A", File.ReadAllText(Directory.GetFiles(folderB, "plan (Erk-S зөрчил *").Single()));
        Assert.Equal(1, SyncStatusReport.TryRead(new SyncHome(homeA.Path).StatusPath)!.Folders.Single().ConflictCount);
    }

    /// <summary>Runs both machines from their saved state until <paramref name="done"/>, then stops them.</summary>
    private static async Task RunUntilAsync(
        FakeServer server,
        TemporaryFolder homeA,
        TemporaryFolder homeB,
        TemporaryFolder rootA,
        TemporaryFolder rootB,
        Func<bool> done,
        Action<SyncEngine>? watchB = null)
    {
        FakeDevice[] devices = server.Devices;
        await using var engineA = new SyncEngine(new SyncHome(homeA.Path), server.Client(), devices[0], new SyncPathMap(@"D:\Cloud Work=>" + rootA.Path));
        await using var engineB = new SyncEngine(new SyncHome(homeB.Path), server.Client(), devices[1], new SyncPathMap(@"D:\Cloud Work=>" + rootB.Path));
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        Task runA = engineA.RunAsync(stop.Token);
        Task runB = engineB.RunAsync(stop.Token);
        bool reached = false;
        for (int attempt = 0; attempt < 120 && !reached; attempt++)
        {
            await Task.Delay(500);
            engineA.Poke();
            engineB.Poke();
            watchB?.Invoke(engineB);
            reached = done();
        }
        await stop.CancelAsync();
        await Task.WhenAll(runA, runB);
        Assert.True(reached, "the two machines did not get there in time");
    }

    // ---- helpers -------------------------------------------------------------------------

    private static SyncTransferPlan Plan(SyncIndexEntry[] mine, SyncIndexEntry[] theirs) =>
        SyncTransferPlanner.Plan(Index(mine), Index(theirs), new SyncVersionLedger());

    private static SyncIndex Index(params SyncIndexEntry[] entries) => new() { FolderId = "f-1", Entries = entries };

    private static SyncFileVersion Version(string sha, string writer, int seq) => new() { Sha256 = sha, Writer = writer, Seq = seq };

    private static SyncIndexEntry Entry(string path, string sha, SyncFileVersion? version) =>
        new(path, 1, DateTime.UtcNow, SyncFileKinds.Of(path), sha, [sha]) { Version = version };

    private static SyncIndexEntry EntryOf(string path, byte[] content)
    {
        var pieces = new List<string>();
        for (int index = 0; index * (long)SyncIndex.ChunkSize < content.Length; index++)
            pieces.Add(Convert.ToHexStringLower(SHA256.HashData(Piece(content, index))));
        return new SyncIndexEntry(path, content.Length, DateTime.UtcNow, SyncFileKinds.Of(path), Sha(content), pieces);
    }

    private static byte[] Piece(byte[] content, int index)
    {
        int offset = index * SyncIndex.ChunkSize;
        return content.AsSpan(offset, Math.Min(SyncIndex.ChunkSize, content.Length - offset)).ToArray();
    }

    private static string Sha(byte[] content) => Convert.ToHexStringLower(SHA256.HashData(content));

    private static byte[] Bytes(int length, int seed)
    {
        byte[] bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        return bytes;
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
