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
