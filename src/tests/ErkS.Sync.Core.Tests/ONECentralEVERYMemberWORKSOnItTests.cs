namespace ErkS.Sync.Core.Tests;

/// <summary>
/// 🔴 Owner, 2026-10-24: «синк фолдерт central model sync хийгдэж ажиллана. гишүүд local дээрээ
/// ажиллаад синк хийхэд централ модел шинэчлэгдэнэ … нэг нэгнээсээ зөвшөөрөл хүсэх процесс бас
/// яг дүрмийн дагуу ажиллах ёстой». There is one central, on its host; a member's file operations
/// run there on real handles, so the share modes and locks Revit relies on answer exactly as
/// NTFS answers - and nothing outside the central is reachable.
/// </summary>
public sealed class ONECentralEVERYMemberWORKSOnItTests
{
    [Fact]
    public async Task TwoMembersMeetTheSameShareModesAndLocks()
    {
        using var folder = new TemporaryFolder();
        File.WriteAllBytes(Path.Combine(folder.Path, "NP.rvt"), new byte[64]);
        Directory.CreateDirectory(Path.Combine(folder.Path, "NP_backup"));
        using var host = new SyncCentralHost(folder.Path, "NP.rvt");
        var a = new SyncCentralClient(new SyncCentralLocalChannel(host, 1));
        var b = new SyncCentralClient(new SyncCentralLocalChannel(host, 2));

        // A holds the model for writing, sharing reads only: B may read, may not write.
        SyncCentralResponse held = await a.CallAsync(new SyncCentralRequest { Op = SyncCentralOp.Open, Path = "NP.rvt", Access = FileAccess.ReadWrite, Share = FileShare.Read });
        Assert.True(held.Ok);
        SyncCentralResponse refused = await b.CallAsync(new SyncCentralRequest { Op = SyncCentralOp.Open, Path = "NP.rvt", Access = FileAccess.ReadWrite, Share = FileShare.ReadWrite });
        Assert.Equal(SyncCentralErrors.SharingViolation, refused.Error);
        SyncCentralResponse reader = await b.CallAsync(new SyncCentralRequest { Op = SyncCentralOp.Open, Path = "NP.rvt", Access = FileAccess.Read, Share = FileShare.ReadWrite });
        Assert.True(reader.Ok);

        // What A writes, B reads - one file, no copy.
        Assert.True((await a.WriteAsync(held.Handle, 10, new byte[] { 7, 8, 9 })).Ok);
        Assert.True((await a.CallAsync(new SyncCentralRequest { Op = SyncCentralOp.Flush, Handle = held.Handle })).Ok);
        (SyncCentralResponse read, byte[] bytes) = await b.ReadAsync(reader.Handle, 10, 3);
        Assert.True(read.Ok);
        Assert.Equal(new byte[] { 7, 8, 9 }, bytes);

        // A byte range A locks is refused to B.
        Assert.True((await a.CallAsync(new SyncCentralRequest { Op = SyncCentralOp.Lock, Handle = held.Handle, Offset = 0, Length = 8 })).Ok);
        SyncCentralResponse bWrites = await b.CallAsync(new SyncCentralRequest { Op = SyncCentralOp.Open, Path = @"NP_backup\lock.dat", Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None });
        Assert.True(bWrites.Ok);

        // Revit's exclusive lock file: the second creator is refused.
        SyncCentralResponse again = await a.CallAsync(new SyncCentralRequest { Op = SyncCentralOp.Open, Path = @"NP_backup\lock.dat", Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None });
        Assert.Equal(SyncCentralErrors.Exists, again.Error);
    }

    [Fact]
    public async Task AMemberGoneReleasesWhatItHeld()
    {
        using var folder = new TemporaryFolder();
        File.WriteAllBytes(Path.Combine(folder.Path, "NP.rvt"), new byte[8]);
        using var host = new SyncCentralHost(folder.Path, "NP.rvt");
        var a = new SyncCentralClient(new SyncCentralLocalChannel(host, 1));
        var b = new SyncCentralClient(new SyncCentralLocalChannel(host, 2));
        Assert.True((await a.CallAsync(new SyncCentralRequest { Op = SyncCentralOp.Open, Path = "NP.rvt", Access = FileAccess.ReadWrite, Share = FileShare.None })).Ok);
        Assert.Equal(SyncCentralErrors.SharingViolation,
            (await b.CallAsync(new SyncCentralRequest { Op = SyncCentralOp.Open, Path = "NP.rvt", Access = FileAccess.Read, Share = FileShare.ReadWrite })).Error);

        host.EndSession(1); // the connection dropped

        Assert.True((await b.CallAsync(new SyncCentralRequest { Op = SyncCentralOp.Open, Path = "NP.rvt", Access = FileAccess.Read, Share = FileShare.ReadWrite })).Ok);
    }

    [Fact]
    public async Task TheCentralIsCopiedBeforeAMemberFirstWritesAndTenAreKept()
    {
        using var folder = new TemporaryFolder();
        using var snapshots = new TemporaryFolder();
        File.WriteAllText(Path.Combine(folder.Path, "NP.rvt"), "before");
        Directory.CreateDirectory(Path.Combine(folder.Path, "NP_backup"));
        File.WriteAllText(Path.Combine(folder.Path, "NP_backup", "1.rws"), "data");
        using var host = new SyncCentralHost(folder.Path, "NP.rvt", snapshots.Path, keepSnapshots: 2);
        var member = new SyncCentralClient(new SyncCentralLocalChannel(host, 1));

        // Reading takes no snapshot.
        Assert.True((await member.CallAsync(new SyncCentralRequest { Op = SyncCentralOp.Stat, Path = "NP.rvt" })).Ok);
        Assert.Empty(Directory.GetDirectories(snapshots.Path));

        SyncCentralResponse open = await member.CallAsync(new SyncCentralRequest { Op = SyncCentralOp.Open, Path = "NP.rvt", Access = FileAccess.ReadWrite, Share = FileShare.Read });
        await member.WriteAsync(open.Handle, 0, "AFTER!"u8.ToArray());

        string taken = Directory.GetDirectories(snapshots.Path).Single();
        Assert.Equal("before", File.ReadAllText(Path.Combine(taken, "NP.rvt")));
        Assert.Equal("data", File.ReadAllText(Path.Combine(taken, "NP_backup", "1.rws")));
        Assert.NotNull(host.LastSnapshotUtc);

        // Later sessions, minutes apart: only the last two are kept.
        DateTimeOffset now = DateTimeOffset.UtcNow;
        for (int session = 2; session <= 4; session++)
            host.Handle(session, new SyncCentralRequest { Op = SyncCentralOp.SetInfo, Path = "NP.rvt" }, ReadOnlyMemory<byte>.Empty, now.AddMinutes(session * 2), out _);
        Assert.Equal(2, Directory.GetDirectories(snapshots.Path).Length);
    }

    [Fact]
    public async Task ASilentMemberIsReleasedAfterItsLease()
    {
        using var folder = new TemporaryFolder();
        File.WriteAllBytes(Path.Combine(folder.Path, "NP.rvt"), new byte[8]);
        using var host = new SyncCentralHost(folder.Path, "NP.rvt");
        DateTimeOffset start = DateTimeOffset.UtcNow;
        Assert.True(host.Handle(1, new SyncCentralRequest { Op = SyncCentralOp.Open, Path = "NP.rvt", Access = FileAccess.ReadWrite, Share = FileShare.None }, ReadOnlyMemory<byte>.Empty, start, out _).Ok);
        Assert.Equal((1, 1), (host.Counts.Sessions, host.Counts.Handles));

        Assert.Equal(0, host.Sweep(start.AddSeconds(30), TimeSpan.FromMinutes(2)));
        Assert.Equal(1, host.Sweep(start.AddMinutes(3), TimeSpan.FromMinutes(2)));

        var other = new SyncCentralClient(new SyncCentralLocalChannel(host, 2));
        Assert.True((await other.CallAsync(new SyncCentralRequest { Op = SyncCentralOp.Open, Path = "NP.rvt", Access = FileAccess.ReadWrite, Share = FileShare.None })).Ok);
    }

    [Theory]
    [InlineData("NP.rvt", true)]
    [InlineData(@"NP_backup\EditableElements.0001.dat", true)]
    [InlineData("NP.rvt.tmp", true)]
    [InlineData("Sheets.rvt", false)]
    [InlineData(@"..\Other\NP.rvt", false)]
    [InlineData(@"Client\logo.png", false)]
    public void OnlyTheCentralIsReachable(string path, bool belongs) =>
        Assert.Equal(belongs, SyncCentralPaths.Belongs("NP.rvt", path));

    [Fact]
    public async Task AFileBesideTheCentralIsRefused()
    {
        using var folder = new TemporaryFolder();
        File.WriteAllText(Path.Combine(folder.Path, "NP.rvt"), "c");
        File.WriteAllText(Path.Combine(folder.Path, "Sheets.rvt"), "s");
        using var host = new SyncCentralHost(folder.Path, "NP.rvt");
        var member = new SyncCentralClient(new SyncCentralLocalChannel(host, 1));

        Assert.Equal(SyncCentralErrors.NotCentral,
            (await member.CallAsync(new SyncCentralRequest { Op = SyncCentralOp.Open, Path = "Sheets.rvt" })).Error);
        SyncCentralResponse listed = await member.CallAsync(new SyncCentralRequest { Op = SyncCentralOp.List, Path = "" });
        Assert.Equal(["NP.rvt"], listed.Entries!.Select(entry => entry.Name));
    }
}
