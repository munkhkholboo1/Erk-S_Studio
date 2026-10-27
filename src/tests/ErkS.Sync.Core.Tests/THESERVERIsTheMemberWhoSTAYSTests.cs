namespace ErkS.Sync.Core.Tests;

/// <summary>
/// Owner, 2026-10-27 (_shared/syn-durability-offline-2026-10-27.md): what a member sent is the others'
/// once it is WHOLE on the server - its author may go offline; nothing half-arrived is ever shown.
/// </summary>
public sealed class THESERVERIsTheMemberWhoSTAYSTests
{
    private const string Canonical = @"D:\Cloud Work\Erin";

    [Fact]
    public async Task AnUploadCUTHalfWayShowsNothingAndTheAUTHORKnowsItIsUnsent()
    {
        using var rootA = new TemporaryFolder();
        using var rootB = new TemporaryFolder();
        using var homeA = new TemporaryFolder();
        using var homeB = new TemporaryFolder();
        using var a = new FakeDevice();
        using var b = new FakeDevice();
        var server = new FakeServer(Canonical, [a, b]) { Relay = true, DropChunkPuts = true, MarksIncomplete = true };
        string folderA = rootA.Folder("Erin");
        string folderB = rootB.Folder("Erin");
        SyncFolderMarker.Write(folderA, Identity());
        SyncFolderMarker.Write(folderB, Identity());
        File.WriteAllText(Path.Combine(folderA, "plan.dwg"), "the author's work");
        await using var engineA = new SyncEngine(new SyncHome(homeA.Path), server.Client(), a, new SyncPathMap(@"D:\Cloud Work=>" + rootA.Path));
        await using var engineB = new SyncEngine(new SyncHome(homeB.Path), server.Client(), b, new SyncPathMap(@"D:\Cloud Work=>" + rootB.Path));

        // The network cuts while the pieces go: the index is there, the pieces are not.
        await engineA.PassAsync(CancellationToken.None);
        Assert.DoesNotContain("plan.dwg", engineA.OnServer("f-1"));
        await engineB.PassAsync(CancellationToken.None);
        Assert.False(File.Exists(Path.Combine(folderB, "plan.dwg"))); // nothing half-arrived
        Assert.Empty(Directory.EnumerateFiles(folderB, "*.dwg", SearchOption.AllDirectories));
        Assert.Equal(1, engineB.LastState.Folders.Single().Peers.Single().ServerIncompleteCount); // said, not waited for

        // The author is back online: the pieces go, and from now on the others take it even with A gone.
        server.DropChunkPuts = false;
        await engineA.PassAsync(CancellationToken.None);
        Assert.Contains("plan.dwg", engineA.OnServer("f-1"));
        await engineB.PassAsync(CancellationToken.None);
        Assert.Equal("the author's work", File.ReadAllText(Path.Combine(folderB, "plan.dwg")));
        Assert.Equal(0, engineB.LastState.Folders.Single().Peers.Single().ServerIncompleteCount);
    }

    [Fact]
    public void AWritersRunOnTheSERVERStopsAtItsFirstGap()
    {
        using var root = new TemporaryFolder();
        string log = Path.Combine(root.Path, SyncCollab.DirectoryName, "file-1", "log", "seat-a");
        Directory.CreateDirectory(log);
        foreach (int seq in new[] { 1, 2, 3, 4 })
            File.WriteAllText(Path.Combine(log, seq + ".cgacs"), "x");
        var status = new SyncCollabStatus("file-1", 0, [new SyncCollabWriter("seat-a", 4, 4, 0, false)]) { OwnWriter = "seat-a" };
        var onServer = new HashSet<string>(SyncPath.Comparer)
        {
            Path.Combine(SyncCollab.DirectoryName, "file-1", "log", "seat-a", "1.cgacs"),
            Path.Combine(SyncCollab.DirectoryName, "file-1", "log", "seat-a", "2.cgacs"),
            Path.Combine(SyncCollab.DirectoryName, "file-1", "log", "seat-a", "4.cgacs"),
        };

        SyncCollabStatus seen = SyncCollab.WithServer(status, root.Path, onServer);

        Assert.Equal(2, seen.OnServerThrough["seat-a"]);
        Assert.Equal(2, seen.UnsentToServer);
    }

    [Theory]
    [InlineData(new long[] { 3, 2 }, "d1", new long[] { 3, 2 }, "d1", "same")]
    [InlineData(new long[] { 3, 2 }, "d1", new long[] { 3, 2 }, "d2", "diverged")]
    [InlineData(new long[] { 3, 2 }, "d1", new long[] { 2, 2 }, "d0", "behind")]
    [InlineData(new long[] { 3, 2 }, "d1", new long[] { 4, 2 }, "d9", "ahead")]
    [InlineData(new long[] { 3, 2 }, "d1", new long[] { 4, 1 }, "d9", "different")]
    public void TwoDRAWINGSAreComparedByWhatTheyHoldThenByTheirDigest(long[] mine, string myDigest, long[] theirs, string theirDigest, string expected)
    {
        static SyncCollabReport Report(long[] applied, string digest) =>
            new("w", new Dictionary<string, long> { ["seat-a"] = applied[0], ["seat-b"] = applied[1] }, digest, DateTimeOffset.UtcNow);

        Assert.Equal(expected, SyncCollab.Compare(Report(mine, myDigest), Report(theirs, theirDigest)));
    }

    private static SyncFolderIdentity Identity() => new()
    {
        FolderId = "f-1",
        ProjectCode = "ERIN-2026",
        CanonicalPath = Canonical,
    };
}
