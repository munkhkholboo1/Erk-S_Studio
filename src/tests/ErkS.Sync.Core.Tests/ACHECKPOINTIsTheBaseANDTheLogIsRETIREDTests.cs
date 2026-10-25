namespace ErkS.Sync.Core.Tests;

/// <summary>
/// CGA shared file К5: the owner checkpoints on every save. A machine without the DWG gets the
/// newest checkpoint as its base - a DWG already there is never written over; old checkpoints and
/// changesets every machine has applied are retired.
/// </summary>
public sealed class ACHECKPOINTIsTheBaseANDTheLogIsRETIREDTests
{
    [Fact]
    public void AMachineWithoutTheDwgGetsTheNewestCheckpointAndOneWithItKeepsItsOwn()
    {
        using var folder = new TemporaryFolder();
        using var home = new TemporaryFolder();
        using var scratch = new TemporaryFolder();
        string root = folder.Folder("Erin");
        string dwg = Path.Combine(root, "DWG", "plan.dwg");
        Directory.CreateDirectory(Path.GetDirectoryName(dwg)!);
        File.WriteAllText(dwg, "owner v1");
        SyncCollab.Register(root, dwg, "file-1", "seat-owner", "v1", DateTimeOffset.UtcNow);
        for (int seq = 1; seq <= 3; seq++)
            SyncCollab.Publish(root, "file-1", "owner", Write(scratch, $"m{seq}.json", "m"), Write(scratch, $"e{seq}.dwg", "e"));
        SyncCollab.Checkpoint(root, "file-1", dwg, new Dictionary<string, long> { ["owner"] = 2 });

        // A machine that has its own copy keeps it.
        File.WriteAllText(dwg, "this machine's own");
        SyncCollab.Maintain(root, new SyncHome(home.Path));
        Assert.Equal("this machine's own", File.ReadAllText(dwg));

        // A machine without it gets the base, and what the base holds is not given again.
        File.Delete(dwg);
        SyncCollab.Maintain(root, new SyncHome(home.Path));
        Assert.Equal("owner v1", File.ReadAllText(dwg));
        Assert.Equal([3L], SyncCollab.Pull(root, new SyncHome(home.Path), "file-1", "member").Select(item => item.Seq));
    }

    [Fact]
    public void ACopyNotEditedHereFastForwardsAndOneEditedHereStays()
    {
        using var folder = new TemporaryFolder();
        using var home = new TemporaryFolder();
        string root = folder.Folder("Erin");
        string dwg = Path.Combine(root, "plan.dwg");
        File.WriteAllText(dwg, "pre-share v0");
        SyncCollab.Register(root, dwg, "file-1", "seat-owner", "v1", DateTimeOffset.UtcNow);
        string checkpointSource = Path.Combine(home.Path, "owner.dwg");
        File.WriteAllText(checkpointSource, "checkpoint 1");
        SyncCollab.Checkpoint(root, "file-1", checkpointSource, new Dictionary<string, long> { ["owner"] = 4 });
        string sha = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(dwg)));
        SyncIndexEntry Entry(string writer) => new("plan.dwg", 12, DateTime.UtcNow, SyncFileKind.Dwg, sha, [])
        {
            Version = new SyncFileVersion { Sha256 = sha, Writer = writer, Seq = 1 },
        };

        // Edited on this machine: never written over.
        SyncCollab.Maintain(root, new SyncHome(home.Path), _ => Entry("THIS"), "THIS");
        Assert.Equal("pre-share v0", File.ReadAllText(dwg));

        // A newer checkpoint, and the copy here is one that arrived from the owner: fast-forward.
        File.WriteAllText(checkpointSource, "checkpoint 2");
        SyncCollab.Checkpoint(root, "file-1", checkpointSource, new Dictionary<string, long> { ["owner"] = 6 });
        SyncCollab.Maintain(root, new SyncHome(home.Path), _ => Entry("OWNER"), "THIS");
        Assert.Equal("checkpoint 2", File.ReadAllText(dwg));
        Assert.Single(Directory.EnumerateFiles(Path.Combine(home.Path, "collab", "file-1", "replaced")));

        // A copy a partner also held (its version chain) is a synced copy, whoever stamped it here.
        File.WriteAllText(dwg, "partner had this");
        string partnerSha = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(dwg)));
        File.WriteAllText(checkpointSource, "checkpoint 2b");
        SyncCollab.Checkpoint(root, "file-1", checkpointSource, new Dictionary<string, long> { ["owner"] = 7 });
        SyncCollab.Maintain(root, new SyncHome(home.Path), _ => null, "THIS", _ => new HashSet<string> { partnerSha });
        Assert.Equal("checkpoint 2b", File.ReadAllText(dwg));

        // The copy SYN placed, untouched, moves on to the next one too.
        File.WriteAllText(checkpointSource, "checkpoint 3");
        SyncCollab.Checkpoint(root, "file-1", checkpointSource, new Dictionary<string, long> { ["owner"] = 8 });
        SyncCollab.Maintain(root, new SyncHome(home.Path), _ => null, "THIS");
        Assert.Equal("checkpoint 3", File.ReadAllText(dwg));
        Assert.Empty(SyncCollab.Pull(root, new SyncHome(home.Path), "file-1", "member"));
    }

    [Fact]
    public void OldCheckpointsGoAndChangesetsGoOnlyWhenEveryMachineHasThem()
    {
        using var folder = new TemporaryFolder();
        using var homeMember = new TemporaryFolder();
        using var scratch = new TemporaryFolder();
        string root = folder.Folder("Erin");
        string dwg = Path.Combine(root, "plan.dwg");
        File.WriteAllText(dwg, "v");
        SyncCollab.Register(root, dwg, "file-1", "seat-owner", "v1", DateTimeOffset.UtcNow, writer: "owner");
        SyncCollab.Register(root, dwg, "file-1", "seat-owner", "v1", DateTimeOffset.UtcNow, writer: "member");
        for (int seq = 1; seq <= 4; seq++)
            SyncCollab.Publish(root, "file-1", "owner", Write(scratch, $"m{seq}.json", "m"), Write(scratch, $"e{seq}.dwg", "e"));
        for (int n = 1; n <= 5; n++)
            SyncCollab.Checkpoint(root, "file-1", dwg, new Dictionary<string, long> { ["owner"] = 3 });
        string fileRoot = SyncCollab.FileRoot(root, "file-1");
        File.WriteAllText(dwg, "the member's own, edited here"); // keeps it; the base is not placed

        // The member has not applied anything yet: no changeset may go.
        IReadOnlySet<string> retired = SyncCollab.Maintain(root, new SyncHome(homeMember.Path));
        Assert.Equal(["000003.dwg", "000004.dwg", "000005.dwg"], Directory.EnumerateFiles(Path.Combine(fileRoot, "checkpoints"), "*.dwg").Select(Path.GetFileName).Order());
        Assert.Contains(Path.Combine(".erks-collab", "file-1", "checkpoints", "000001.dwg"), retired);
        Assert.Equal(4, Directory.EnumerateFiles(Path.Combine(fileRoot, "log", "owner")).Count());

        // The member acks 2: changesets 1-2 go, 3 (in the checkpoint, not yet acked) and 4 stay.
        SyncCollab.Pull(root, new SyncHome(homeMember.Path), "file-1", "member", new Dictionary<string, long> { ["owner"] = 2 });
        retired = SyncCollab.Maintain(root, new SyncHome(homeMember.Path));
        Assert.Equal(["00000003.cgacs", "00000004.cgacs"], Directory.EnumerateFiles(Path.Combine(fileRoot, "log", "owner")).Select(Path.GetFileName).Order());
        Assert.Contains(Path.Combine(".erks-collab", "file-1", "log", "owner", "00000001.cgacs"), retired);
    }

    [Fact]
    public void OwnChangesetsAreNotWaitingAndPendingOutFollowsThePartnersAcks()
    {
        using var folder = new TemporaryFolder();
        using var homeDesktop = new TemporaryFolder();
        using var homeLaptop = new TemporaryFolder();
        using var scratch = new TemporaryFolder();
        string root = folder.Folder("Erin");
        string dwg = Path.Combine(root, "plan.dwg");
        File.WriteAllText(dwg, "v");
        const string desktop = "seat-1957_d11188802", laptop = "seat-b4b9_db11eb7bd";
        SyncCollab.Register(root, dwg, "file-1", "seat-1957", "v1", DateTimeOffset.UtcNow, writer: desktop);
        SyncCollab.Register(root, dwg, "file-1", "seat-1957", "v1", DateTimeOffset.UtcNow, writer: laptop);
        SyncCollab.Publish(root, "file-1", desktop, Write(scratch, "m1.json", "m"), Write(scratch, "e1.dwg", "e"));
        SyncCollab.Publish(root, "file-1", desktop, Write(scratch, "m2.json", "m"), Write(scratch, "e2.dwg", "e"));

        SyncCollabStatus before = SyncCollab.Status(root, new SyncHome(homeDesktop.Path), "file-1", desktop);
        Assert.Equal((0, 2), (before.Waiting, before.PendingOut));

        // The laptop applies both and says so; the desktop sees nothing left to send.
        SyncCollab.Pull(root, new SyncHome(homeLaptop.Path), "file-1", laptop, new Dictionary<string, long> { [desktop] = 2 });
        SyncCollabStatus after = SyncCollab.Status(root, new SyncHome(homeDesktop.Path), "file-1", desktop);
        Assert.Equal((0, 0), (after.Waiting, after.PendingOut));
        Assert.Equal((2L, 2L), (after.Writers.Single().LastInLog, after.Writers.Single().Applied));
    }

    [Fact]
    public void ADwgClosedWithoutSavingGetsBackWhatItLostOwnChangesetsToo()
    {
        using var folder = new TemporaryFolder();
        using var home = new TemporaryFolder();
        using var scratch = new TemporaryFolder();
        string root = folder.Folder("Erin");
        string dwg = Path.Combine(root, "plan.dwg");
        File.WriteAllText(dwg, "v");
        SyncCollab.Register(root, dwg, "file-1", "seat-owner", "v1", DateTimeOffset.UtcNow);
        for (int seq = 1; seq <= 3; seq++)
        {
            SyncCollab.Publish(root, "file-1", "me", Write(scratch, $"m{seq}.json", "m"), Write(scratch, $"e{seq}.dwg", "e"));
            SyncCollab.Publish(root, "file-1", "other", Write(scratch, $"o{seq}.json", "o"), Write(scratch, $"f{seq}.dwg", "f"));
        }
        SyncHome here = new(home.Path);
        Assert.Empty(SyncCollab.Pull(root, here, "file-1", "me", new Dictionary<string, long> { ["other"] = 3 }));

        // Closed without saving: the DWG holds other 1 and own 1. The ack sets the cursor lower,
        // and both writers' lost changesets - its own included - come back.
        IReadOnlyList<SyncCollabChangeset> again = SyncCollab.Pull(root, here, "file-1", "me", new Dictionary<string, long> { ["other"] = 1, ["me"] = 1 });
        Assert.Equal([("me", 2L), ("me", 3L), ("other", 2L), ("other", 3L)], again.Select(item => (item.Writer, item.Seq)));
        Assert.False(SyncCollab.NeedsCheckpoint(root, here, "file-1"));

        // Once 2 of «other» is retired behind a checkpoint, only the base can bring the DWG back.
        SyncCollab.Checkpoint(root, "file-1", dwg, new Dictionary<string, long> { ["other"] = 2 });
        File.Delete(Path.Combine(SyncCollab.FileRoot(root, "file-1"), "log", "other", "00000002.cgacs"));
        Assert.True(SyncCollab.NeedsCheckpoint(root, here, "file-1"));
    }

    private static string Write(TemporaryFolder scratch, string name, string text)
    {
        string path = Path.Combine(scratch.Path, name);
        File.WriteAllText(path, text);
        return path;
    }
}
