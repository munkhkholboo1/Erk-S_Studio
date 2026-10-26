using System.IO.Compression;

namespace ErkS.Sync.Core.Tests;

/// <summary>
/// Erk-S Sync, 2026-10-26 (fileId c18f…): the desktop's changesets 1–3 had been retired from its log
/// before 0.001.97, Publish counted only the files left and numbered the next one 1 again; the
/// laptop's log 00000001 was overwritten with it and its ack (3) skipped it - the two DWGs parted.
/// A number is never given twice, a changeset file is never replaced, and the writer's machine
/// puts a reused number right by itself.
/// </summary>
public sealed class ACHANGESETNumberIsNEVERReusedTests
{
    private const string Writer = "seat-1957_d11188802";

    [Fact]
    public void ANumberSeenAnywhereIsNotGivenAgain()
    {
        using var folder = new TemporaryFolder();
        using var home = new TemporaryFolder();
        using var scratch = new TemporaryFolder();
        string root = folder.Folder("Erin");
        var syncHome = new SyncHome(home.Path);
        SyncCollab.Register(root, Path.Combine(root, "plan.dwg"), "file-1", "", "v1", DateTimeOffset.UtcNow, Writer);
        for (int seq = 1; seq <= 3; seq++)
            Assert.Equal(seq, SyncCollab.Publish(root, "file-1", Writer, Write(scratch, $"m{seq}.json", $"m{seq}"), Write(scratch, $"e{seq}.dwg", $"e{seq}"), syncHome));
        // Retired by an older SYN: the log is empty again.
        foreach (string file in Directory.EnumerateFiles(Path.Combine(SyncCollab.FileRoot(root, "file-1"), "log", Writer)))
            File.Delete(file);

        Assert.Equal(4, SyncCollab.Publish(root, "file-1", Writer, Write(scratch, "m4.json", "m4"), Write(scratch, "e4.dwg", "e4"), syncHome));
    }

    [Fact]
    public void APartnersAckAloneKeepsTheNumberAhead()
    {
        using var folder = new TemporaryFolder();
        using var scratch = new TemporaryFolder();
        string root = folder.Folder("Erin");
        SyncCollab.Register(root, Path.Combine(root, "plan.dwg"), "file-1", "", "v1", DateTimeOffset.UtcNow, Writer);
        string acks = Path.Combine(SyncCollab.FileRoot(root, "file-1"), "acks");
        Directory.CreateDirectory(acks);
        File.WriteAllText(Path.Combine(acks, "seat-2_d22222222.json"), $"{{\"{Writer}\": 3}}");

        Assert.Equal(4, SyncCollab.Publish(root, "file-1", Writer, Write(scratch, "m.json", "m"), Write(scratch, "e.dwg", "e")));
    }

    [Fact]
    public void AChangesetFileIsNeverReplacedAndAMissingOneAlwaysComes()
    {
        string path = Path.Combine(".erks-collab", "file-1", "log", Writer, "00000001.cgacs");
        SyncFileVersion old = new() { Sha256 = "old", Writer = "laptop", Seq = 1 };
        SyncFileVersion reused = old.Next("new", "desktop"); // the history says it came after - it did not

        SyncTransferPlan plan = SyncTransferPlanner.Plan(Index(Entry(path, "old", old)), Index(Entry(path, "new", reused)), new SyncVersionLedger());
        Assert.Equal(SyncTransferKind.KeepBoth, Assert.Single(plan.Actions).Kind);

        // The writer's own repair names the content that belongs at the number: that one comes.
        plan = SyncTransferPlanner.Plan(Index(Entry(path, "new", reused)), Index(Entry(path, "old2", reused.Next("old2", "desktop"))), new SyncVersionLedger(), restored: new HashSet<string> { "old2" });
        SyncTransferAction restore = Assert.Single(plan.Actions);
        Assert.Equal((SyncTransferKind.Replace, true), (restore.Kind, restore.KeepPrevious));

        // Deleted here by an older SYN: a changeset still comes back.
        var ledger = new SyncVersionLedger();
        ledger.Applied(path, old);
        ledger.Stamp(new SyncIndex { Entries = [] }, "laptop");
        Assert.Equal(SyncTransferKind.Fetch, Assert.Single(SyncTransferPlanner.Plan(Index(), Index(Entry(path, "old", old)), ledger).Actions).Kind);
    }

    [Fact]
    public void TheWritersMachinePutsAReusedNumberRight()
    {
        using var folder = new TemporaryFolder();
        using var home = new TemporaryFolder();
        using var scratch = new TemporaryFolder();
        string root = folder.Folder("Erin");
        var syncHome = new SyncHome(home.Path);
        SyncCollab.Register(root, Path.Combine(root, "plan.dwg"), "file-1", "", "v1", DateTimeOffset.UtcNow, Writer);
        // What CGA took back of its own 1–3 is kept under the Sync home (pull with its ack).
        for (int seq = 1; seq <= 3; seq++)
            Unpacked(syncHome, seq, $"old m{seq}", $"old e{seq}");
        // The log holds only the reused 1, with the new changeset in it.
        string log = Path.Combine(SyncCollab.FileRoot(root, "file-1"), "log", Writer);
        Directory.CreateDirectory(log);
        Pack(Path.Combine(log, "00000001.cgacs"), "new m", "new e");
        SyncCollab.SaveWriter(syncHome, "file-1", Writer);

        SyncCollab.Maintain(root, syncHome);

        Assert.Equal(("old m1", "old e1"), Read(Path.Combine(log, "00000001.cgacs")));
        Assert.Equal(("old m3", "old e3"), Read(Path.Combine(log, "00000003.cgacs")));
        Assert.Equal(("new m", "new e"), Read(Path.Combine(log, "00000004.cgacs")));
        Assert.Equal(3, SyncCollab.Restored(root).Count);
        // Done once: the next pass changes nothing, the next number is 5.
        SyncCollab.Maintain(root, syncHome);
        Assert.False(File.Exists(Path.Combine(log, "00000005.cgacs")));
        Assert.Equal(5, SyncCollab.Publish(root, "file-1", Writer, Write(scratch, "m.json", "m"), Write(scratch, "e.dwg", "e"), syncHome));
    }

    private static void Unpacked(SyncHome home, int seq, string manifest, string entities)
    {
        string directory = Path.Combine(home.Root, "collab", "file-1", "in", Writer, seq.ToString("D8"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "manifest.json"), manifest);
        File.WriteAllText(Path.Combine(directory, "entities.dwg"), entities);
    }

    private static void Pack(string path, string manifest, string entities)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open()))
            writer.Write(manifest);
        using (var writer = new StreamWriter(zip.CreateEntry("entities.dwg").Open()))
            writer.Write(entities);
    }

    private static (string Manifest, string Entities) Read(string path)
    {
        using ZipArchive zip = ZipFile.OpenRead(path);
        using var manifest = new StreamReader(zip.GetEntry("manifest.json")!.Open());
        using var entities = new StreamReader(zip.GetEntry("entities.dwg")!.Open());
        return (manifest.ReadToEnd(), entities.ReadToEnd());
    }

    private static SyncIndex Index(params SyncIndexEntry[] entries) => new() { FolderId = "f-1", Entries = entries };

    private static SyncIndexEntry Entry(string path, string sha, SyncFileVersion? version) =>
        new(path, 1, DateTime.UtcNow, SyncFileKinds.Of(path), sha, [sha]) { Version = version };

    private static string Write(TemporaryFolder scratch, string name, string text)
    {
        string path = Path.Combine(scratch.Path, name);
        File.WriteAllText(path, text);
        return path;
    }
}
