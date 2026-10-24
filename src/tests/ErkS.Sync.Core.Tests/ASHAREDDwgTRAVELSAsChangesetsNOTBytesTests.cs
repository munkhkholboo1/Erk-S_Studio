namespace ErkS.Sync.Core.Tests;

/// <summary>
/// CGA shared file, SYN side (К1, _shared/cga-collab-one-file-design-2026-10-24.md). Owner,
/// 2026-10-24: «2 талд нэг файлыг автокад дээрээ нээгээд өөр өөр блокууд дээр ажиллаад generate
/// дарахад нэг нэгнийхээ хийсэн ажлыг хүлээн авч чаддаг болгох». While a DWG is shared its bytes do
/// not travel; each writer's numbered changesets do - in order, none lost, none twice, never past
/// a missing number.
/// </summary>
public sealed class ASHAREDDwgTRAVELSAsChangesetsNOTBytesTests
{
    private const string Canonical = @"D:\Cloud Work\Erin";

    [Fact]
    public async Task ChangesetsReachTheOtherMachineInOrderAndTheDwgStays()
    {
        using var rootA = new TemporaryFolder();
        using var rootB = new TemporaryFolder();
        using var homeA = new TemporaryFolder();
        using var homeB = new TemporaryFolder();
        using var scratch = new TemporaryFolder();
        using var a = new FakeDevice();
        using var b = new FakeDevice();
        var server = new FakeServer(Canonical, [a, b]) { Relay = true };
        string folderA = rootA.Folder("Erin");
        string folderB = rootB.Folder("Erin");
        SyncFolderMarker.Write(folderA, Identity());
        SyncFolderMarker.Write(folderB, Identity());
        File.WriteAllText(Path.Combine(folderA, "plan.dwg"), "base v0");
        await using var engineA = new SyncEngine(new SyncHome(homeA.Path), server.Client(), a, new SyncPathMap(@"D:\Cloud Work=>" + rootA.Path));
        await using var engineB = new SyncEngine(new SyncHome(homeB.Path), server.Client(), b, new SyncPathMap(@"D:\Cloud Work=>" + rootB.Path));

        // The base goes once, as a file.
        await engineA.PassAsync(CancellationToken.None);
        await engineB.PassAsync(CancellationToken.None);
        Assert.Equal("base v0", File.ReadAllText(Path.Combine(folderB, "plan.dwg")));

        // The owner shares it and publishes two changesets.
        SyncCollab.Register(folderA, Path.Combine(folderA, "plan.dwg"), "file-1", "owner-seat", "v0.034", DateTimeOffset.UtcNow);
        Assert.Equal(1, SyncCollab.Publish(folderA, "file-1", "owner-seat", Write(scratch, "m1.json", "manifest 1"), Write(scratch, "e1.dwg", "entities 1"), new SyncHome(homeA.Path), blocks: 12, myBlocks: 3));
        Assert.Equal(2, SyncCollab.Publish(folderA, "file-1", "owner-seat", Write(scratch, "m2.json", "manifest 2"), Write(scratch, "e2.dwg", "entities 2")));
        // Both save the DWG: its bytes must not cross.
        File.WriteAllText(Path.Combine(folderA, "plan.dwg"), "A saved its own");
        File.WriteAllText(Path.Combine(folderB, "plan.dwg"), "B saved its own");

        await engineA.PassAsync(CancellationToken.None);
        await engineB.PassAsync(CancellationToken.None);
        await engineB.PassAsync(CancellationToken.None);

        Assert.Equal("B saved its own", File.ReadAllText(Path.Combine(folderB, "plan.dwg")));
        IReadOnlyList<SyncCollabChangeset> pending = SyncCollab.Pull(folderB, new SyncHome(homeB.Path), "file-1", "member-seat");
        Assert.Equal([1L, 2L], pending.Select(item => item.Seq));
        Assert.Equal("manifest 1", File.ReadAllText(pending[0].ManifestPath));
        Assert.Equal("entities 2", File.ReadAllText(pending[1].EntitiesPath));

        // Applied: not given again; the member's own changeset goes back the same way.
        Assert.Empty(SyncCollab.Pull(folderB, new SyncHome(homeB.Path), "file-1", "member-seat", new Dictionary<string, long> { ["owner-seat"] = 2 }));
        Assert.Equal(1, SyncCollab.Publish(folderB, "file-1", "member-seat", Write(scratch, "mb.json", "member manifest"), Write(scratch, "eb.dwg", "member entities")));
        await engineB.PassAsync(CancellationToken.None);
        await engineA.PassAsync(CancellationToken.None);
        SyncCollabChangeset back = SyncCollab.Pull(folderA, new SyncHome(homeA.Path), "file-1", "owner-seat").Single();
        Assert.Equal(("member-seat", 1L), (back.Writer, back.Seq));
        Assert.Equal("A saved its own", File.ReadAllText(Path.Combine(folderA, "plan.dwg")));
        // Twin's numbers: two writers on the file, one changeset waiting here, no file names.
        SyncStatusCollab counted = SyncStatusReport.Of(engineA.LastState, running: true).Folders.Single().Collab.Single();
        Assert.Equal(("file-1", 2, 12, 3, 1, false), (counted.FileId, counted.Members, counted.Blocks, counted.MyBlocks, counted.PendingInCount, counted.Gap));
    }

    [Fact]
    public void AMissingNumberIsNeverSkipped()
    {
        using var folder = new TemporaryFolder();
        using var home = new TemporaryFolder();
        using var scratch = new TemporaryFolder();
        string root = folder.Folder("Erin");
        SyncCollab.Register(root, Path.Combine(root, "plan.dwg"), "file-1", "owner", "v1", DateTimeOffset.UtcNow);
        for (int seq = 1; seq <= 4; seq++)
            SyncCollab.Publish(root, "file-1", "owner", Write(scratch, $"m{seq}.json", $"m{seq}"), Write(scratch, $"e{seq}.dwg", $"e{seq}"));
        // Changeset 3 has not arrived yet on this machine.
        File.Delete(Path.Combine(SyncCollab.FileRoot(root, "file-1"), "log", "owner", "00000003.cgacs"));

        IReadOnlyList<SyncCollabChangeset> pending = SyncCollab.Pull(root, new SyncHome(home.Path), "file-1", "member");
        SyncCollabStatus status = SyncCollab.Status(root, new SyncHome(home.Path), "file-1");

        Assert.Equal([1L, 2L], pending.Select(item => item.Seq));
        SyncCollabWriter owner = status.Writers.Single();
        Assert.Equal((4L, 2, true), (owner.LastInLog, owner.Waiting, owner.Gap));
    }

    [Fact]
    public void OneDwgIsOneSharedFile()
    {
        using var folder = new TemporaryFolder();
        string root = folder.Folder("Erin");
        SyncCollab.Register(root, Path.Combine(root, "plan.dwg"), "file-1", "owner", "v1", DateTimeOffset.UtcNow);
        Assert.Equal("file-1", SyncCollab.Register(root, Path.Combine(root, "plan.dwg"), "file-1", "owner", "v1", DateTimeOffset.UtcNow).FileId);
        Assert.Throws<InvalidOperationException>(() => SyncCollab.Register(root, Path.Combine(root, "plan.dwg"), "file-2", "owner", "v1", DateTimeOffset.UtcNow));
        Assert.Contains("plan.dwg", SyncCollab.HeldPaths(root));
    }

    private static string Write(TemporaryFolder scratch, string name, string text)
    {
        string path = Path.Combine(scratch.Path, name);
        File.WriteAllText(path, text);
        return path;
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
