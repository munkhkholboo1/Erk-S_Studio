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
    public void TheCentralFollowsCheckpointsTakenFromALocalAndIsNeverOverwrittenWhenSavedDirectly()
    {
        using var folder = new TemporaryFolder();
        using var home = new TemporaryFolder();
        string root = folder.Folder("Erin");
        string central = Path.Combine(root, "plan.dwg");
        File.WriteAllText(central, "pre-share v0");
        string sha0 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(central)));
        SyncCollab.Register(root, central, "file-1", "", "v1", DateTimeOffset.UtcNow);

        // Requirement 7: a checkpoint comes only from a local beside the central.
        string stray = Path.Combine(home.Path, "plan_d0a55efd7_LOCAL.dwg");
        File.WriteAllText(stray, "elsewhere");
        Assert.Throws<InvalidOperationException>(() => SyncCollab.CheckpointFromLocal(root, "file-1", central, stray, new Dictionary<string, long> { ["a"] = 1 }));
        string local = Path.Combine(root, "plan_d0a55efd7_LOCAL.dwg");
        File.WriteAllText(local, "local after Generate 1");
        Assert.True(SyncExclusions.IsExcludedFile(Path.GetFileName(local))); // a local never travels
        Assert.Equal(1, SyncCollab.CheckpointFromLocal(root, "file-1", central, local, new Dictionary<string, long> { ["a"] = 1 }));

        // The central, a synced copy nobody edited, follows the checkpoint; the old one is kept aside.
        SyncCollab.Maintain(root, new SyncHome(home.Path), _ => null, "THIS", _ => new HashSet<string> { sha0 });
        Assert.Equal("local after Generate 1", File.ReadAllText(central));

        // Someone saved the central itself: it is never written over, it is told and kept.
        File.WriteAllText(central, "saved straight into the central");
        File.WriteAllText(local, "local after Generate 2");
        SyncCollab.CheckpointFromLocal(root, "file-1", central, local, new Dictionary<string, long> { ["a"] = 2 });
        SyncCollab.Maintain(root, new SyncHome(home.Path), _ => null, "THIS");
        Assert.Equal("saved straight into the central", File.ReadAllText(central));
        Assert.Contains("шууд засагдсан", SyncCollab.Status(root, new SyncHome(home.Path), "file-1").LastError);
        Assert.Equal(2, Directory.EnumerateFiles(Path.Combine(home.Path, "collab", "file-1", "replaced")).Count());
        Assert.True(SyncCollab.Status(root, new SyncHome(home.Path), "file-1").CentralEdited);

        // collab-central-reset (after CGA made a local from it and published): the newest checkpoint goes in.
        Assert.Equal(2, SyncCollab.ResetCentral(root, new SyncHome(home.Path), "file-1"));
        Assert.Equal("local after Generate 2", File.ReadAllText(central));
        Assert.False(SyncCollab.Status(root, new SyncHome(home.Path), "file-1").CentralEdited);

        // A new local with no applied record is told to start from the newest checkpoint.
        Assert.True(SyncCollab.NeedsCheckpoint(root, new SyncHome(home.Path), "file-1"));
        Assert.False(SyncCollab.NeedsCheckpoint(root, new SyncHome(home.Path), "file-1", new Dictionary<string, long> { ["a"] = 2 }));
        Assert.False(SyncExclusions.IsExcludedFile("plan.dwg"));
    }

    [Fact]
    public void ChangesetsAreNeverRetiredAndCheckpointsNeverGoBackwards()
    {
        using var folder = new TemporaryFolder();
        using var homeMember = new TemporaryFolder();
        using var scratch = new TemporaryFolder();
        string root = folder.Folder("Erin");
        string dwg = Path.Combine(root, "plan.dwg");
        File.WriteAllText(dwg, "v");
        SyncCollab.Register(root, dwg, "file-1", "", "v1", DateTimeOffset.UtcNow, writer: "a");
        SyncCollab.Register(root, dwg, "file-1", "", "v1", DateTimeOffset.UtcNow, writer: "b");
        for (int seq = 1; seq <= 4; seq++)
            SyncCollab.Publish(root, "file-1", "a", Write(scratch, $"m{seq}.json", "m"), Write(scratch, $"e{seq}.dwg", "e"));
        for (int n = 1; n <= 5; n++)
            SyncCollab.Checkpoint(root, "file-1", dwg, new Dictionary<string, long> { ["a"] = 3, ["b"] = n });
        string fileRoot = SyncCollab.FileRoot(root, "file-1");
        SyncCollab.Pull(root, new SyncHome(homeMember.Path), "file-1", "b", new Dictionary<string, long> { ["a"] = 4 });

        IReadOnlySet<string> retired = SyncCollab.Maintain(root, new SyncHome(homeMember.Path));
        Assert.Equal(["000003.dwg", "000004.dwg", "000005.dwg"], Directory.EnumerateFiles(Path.Combine(fileRoot, "checkpoints"), "*.dwg").Select(Path.GetFileName).Order());
        Assert.Equal(4, Directory.EnumerateFiles(Path.Combine(fileRoot, "log", "a")).Count());
        Assert.DoesNotContain(retired, path => path.Contains("log", StringComparison.Ordinal));

        // Anyone may checkpoint, never with less of a writer than the newest holds.
        Assert.Equal(5, SyncCollab.Checkpoint(root, "file-1", dwg, new Dictionary<string, long> { ["a"] = 2, ["b"] = 9 }));
        Assert.Equal(6, SyncCollab.Checkpoint(root, "file-1", dwg, new Dictionary<string, long> { ["a"] = 4, ["b"] = 5 }));
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
