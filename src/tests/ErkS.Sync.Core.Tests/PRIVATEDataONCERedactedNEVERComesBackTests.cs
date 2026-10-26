using System.IO.Compression;
using System.Security.Cryptography;

namespace ErkS.Sync.Core.Tests;

/// <summary>
/// Owner, 2026-10-26 (via Erk-S Sync): «Хамтрагч онлайн болоход түүний машин дээрх имэйлтэй хуучин
/// багцыг Erk-S Sync өөрөө цэвэрлэдэг, серверт дахин илгээдэггүй байх аргыг … гарга». A redaction
/// names a path, the old content's hash and the clean one's: the old content is removed here, never
/// indexed, never fetched again; a manifest with an e-mail in it is never published; old checkpoints
/// can be pruned on every machine.
/// </summary>
public sealed class PRIVATEDataONCERedactedNEVERComesBackTests
{
    private const string Writer = "seat-1957_d11188802";

    [Fact]
    public void ARedactionRemovesTheOldContentAndItsCopiesHere()
    {
        using var folder = new TemporaryFolder();
        using var home = new TemporaryFolder();
        string root = folder.Folder("Erin");
        var syncHome = new SyncHome(home.Path);
        string log = Path.Combine(root, ".erks-collab", "file-1", "log", Writer);
        Directory.CreateDirectory(log);
        string old = Path.Combine(log, "00000002.cgacs");
        File.WriteAllText(old, "with a.person@example.mn");
        string oldSha = Sha(old);
        // A copy kept beside it, and what CGA unpacked from it.
        File.Copy(old, Path.Combine(log, "00000002 (Erk-S зөрчил abc 20261026-2150).cgacs"));
        string unpacked = Path.Combine(home.Path, "collab", "file-1", "in", Writer, "00000002");
        Directory.CreateDirectory(unpacked);
        File.WriteAllText(Path.Combine(unpacked, "manifest.json"), "with a.person@example.mn");
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        SyncRedaction entry = SyncRedactions.Add(root, @".erks-collab\file-1\log\" + Writer + @"\00000002.cgacs", oldSha, "clean", "device-1", key.SignData, DateTimeOffset.UtcNow);
        SyncRedactions.Apply(root, syncHome, SyncRedactions.Load(root));

        Assert.False(File.Exists(old));
        Assert.Empty(Directory.EnumerateFiles(log));
        Assert.False(Directory.Exists(unpacked));
        Assert.True(key.VerifyData(SyncRedactions.SignedBytes(entry), Convert.FromBase64String(entry.Sig), HashAlgorithmName.SHA256));
        Assert.Contains(oldSha, SyncRedactions.OldHashes(SyncRedactions.Load(root)));
    }

    [Fact]
    public void ARedactedContentIsNeverFetchedNorKeptAsAConflict()
    {
        string path = Path.Combine(".erks-collab", "file-1", "log", Writer, "00000002.cgacs");
        var redacted = new HashSet<string> { "old" };
        // The partner still offers the old content: nothing comes, not even a conflict copy.
        SyncTransferPlan plan = SyncTransferPlanner.Plan(Index(Entry(path, "clean")), Index(Entry(path, "old")), new SyncVersionLedger(), redacted: redacted);
        Assert.Empty(plan.Actions);
        plan = SyncTransferPlanner.Plan(Index(), Index(Entry(path, "old")), new SyncVersionLedger(), redacted: redacted);
        Assert.Empty(plan.Actions);
        // The clean content still travels to a machine that lost the old one.
        plan = SyncTransferPlanner.Plan(Index(), Index(Entry(path, "clean")), new SyncVersionLedger(), redacted: redacted);
        Assert.Equal(SyncTransferKind.Fetch, Assert.Single(plan.Actions).Kind);
        // A content the writer put back is authoritative: a partner's other old copy is dropped, not kept beside.
        plan = SyncTransferPlanner.Plan(Index(Entry(path, "restored")), Index(Entry(path, "stale")), new SyncVersionLedger(), restored: new HashSet<string> { "restored" });
        Assert.Empty(plan.Actions);
    }

    [Fact]
    public void AManifestWithAnEmailIsNotPublished()
    {
        using var folder = new TemporaryFolder();
        using var scratch = new TemporaryFolder();
        string root = folder.Folder("Erin");
        string manifest = Path.Combine(scratch.Path, "m.json");
        string entities = Path.Combine(scratch.Path, "e.dwg");
        File.WriteAllText(manifest, "{\"author\":\"a.person@example.mn\"}");
        File.WriteAllText(entities, "e");

        var refused = Assert.Throws<InvalidOperationException>(() => SyncCollab.Publish(root, "file-1", Writer, manifest, entities));
        Assert.Contains("Хувийн мэдээлэл", refused.Message);
        Assert.False(Directory.Exists(Path.Combine(root, ".erks-collab", "file-1", "log", Writer)) &&
            Directory.EnumerateFiles(Path.Combine(root, ".erks-collab", "file-1", "log", Writer)).Any());
    }

    [Fact]
    public void OldCheckpointsArePrunedOnEveryMachineOnceTheNewestHoldsThem()
    {
        using var folderA = new TemporaryFolder();
        using var folderB = new TemporaryFolder();
        using var homeA = new TemporaryFolder();
        using var homeB = new TemporaryFolder();
        string a = folderA.Folder("Erin");
        string b = folderB.Folder("Erin");
        foreach (string root in new[] { a, b })
        {
            SyncCollab.Register(root, Path.Combine(root, "plan.dwg"), "file-1", "", "v1", DateTimeOffset.UtcNow);
            for (int n = 8; n <= 11; n++)
                Checkpoint(root, n, n);
        }
        string replaced = Path.Combine(homeB.Path, "collab", "file-1", "replaced");
        Directory.CreateDirectory(replaced);
        File.WriteAllText(Path.Combine(replaced, "old-central.dwg"), "named");

        Assert.Equal(11, SyncCollab.PruneCheckpoints(a, new SyncHome(homeA.Path), "file-1", keep: 1, purgeReplaced: true, "device-a"));
        // The note travels like any file; B applies it on its next pass.
        CopyTree(Path.Combine(a, ".erks-collab", "file-1", "prune"), Path.Combine(b, ".erks-collab", "file-1", "prune"));
        IReadOnlySet<string> retired = SyncCollab.Maintain(b, new SyncHome(homeB.Path));

        foreach (string root in new[] { a, b })
        {
            Assert.Equal(["000011.dwg", "000011.json"], Directory.EnumerateFiles(Path.Combine(root, ".erks-collab", "file-1", "checkpoints")).Select(Path.GetFileName).Order());
        }
        Assert.Contains(Path.Combine(".erks-collab", "file-1", "checkpoints", "000010.dwg"), retired);
        Assert.False(File.Exists(Path.Combine(replaced, "old-central.dwg")));

        // Refused when the newest does not hold what an older one held.
        Checkpoint(a, 12, 5);
        Assert.Throws<InvalidOperationException>(() => SyncCollab.PruneCheckpoints(a, new SyncHome(homeA.Path), "file-1", keep: 1, purgeReplaced: false, "device-a"));
    }

    private static void Checkpoint(string root, int number, long through)
    {
        string checkpoints = Path.Combine(root, ".erks-collab", "file-1", "checkpoints");
        Directory.CreateDirectory(checkpoints);
        File.WriteAllText(Path.Combine(checkpoints, number.ToString("D6") + ".dwg"), "dwg " + number);
        File.WriteAllText(Path.Combine(checkpoints, number.ToString("D6") + ".json"), $"{{\"checkpoint\":{number},\"through\":{{\"{Writer}\":{through}}}}}");
    }

    private static void CopyTree(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (string file in Directory.EnumerateFiles(from))
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)), overwrite: true);
    }

    private static string Sha(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    private static SyncIndex Index(params SyncIndexEntry[] entries) => new() { FolderId = "f-1", Entries = entries };

    private static SyncIndexEntry Entry(string path, string sha) =>
        new(path, 1, DateTime.UtcNow, SyncFileKinds.Of(path), sha, [sha]) { Version = new SyncFileVersion { Sha256 = sha, Writer = "peer", Seq = 1 } };
}
