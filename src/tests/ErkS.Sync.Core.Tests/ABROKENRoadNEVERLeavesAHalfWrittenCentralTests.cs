namespace ErkS.Sync.Core.Tests;

/// <summary>
/// 🔴 Owner, 2026-10-24: «тогтворгүй сүлжээтэй гишүүн болон бүрэн офлайн гишүүний зохицуулалтыг
/// сайтар бодож хийгээрэй». A member whose road breaks in the middle of a synchronize must not
/// leave half its writes in the central: the burst is undone whole. A burst that finishes stays.
/// A call resent after a break is answered, never run twice.
/// </summary>
public sealed class ABROKENRoadNEVERLeavesAHalfWrittenCentralTests
{
    [Fact]
    public async Task ABurstCutMidwayIsUndoneWhole()
    {
        using var folder = new TemporaryFolder();
        string central = Path.Combine(folder.Path, "NP.rvt");
        File.WriteAllText(central, "model v1");
        Directory.CreateDirectory(Path.Combine(folder.Path, "NP_backup"));
        File.WriteAllText(Path.Combine(folder.Path, "NP_backup", "permissions.dat"), "A owns nothing");
        using var host = new SyncCentralHost(folder.Path, "NP.rvt");
        var member = new SyncCentralClient(new SyncCentralLocalChannel(host, 1));

        // Mid-synchronize: the permissions changed and closed, a new rws written, the model open and half written...
        SyncCentralResponse permissions = await member.CallAsync(new SyncCentralRequest { Op = SyncCentralOp.Open, Path = @"NP_backup\permissions.dat", Mode = FileMode.Create, Access = FileAccess.Write, Share = FileShare.None });
        SyncCentralResponse model = await member.CallAsync(new SyncCentralRequest { Op = SyncCentralOp.Open, Path = "NP.rvt", Access = FileAccess.ReadWrite, Share = FileShare.Read });
        await member.WriteAsync(permissions.Handle, 0, "A owns walls"u8.ToArray());
        await member.CallAsync(new SyncCentralRequest { Op = SyncCentralOp.Close, Handle = permissions.Handle });
        SyncCentralResponse rws = await member.CallAsync(new SyncCentralRequest { Op = SyncCentralOp.Open, Path = @"NP_backup\7.rws", Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None });
        await member.WriteAsync(rws.Handle, 0, "episode 7"u8.ToArray());
        await member.CallAsync(new SyncCentralRequest { Op = SyncCentralOp.Close, Handle = rws.Handle });
        await member.WriteAsync(model.Handle, 0, "MODEL"u8.ToArray());

        // ...and the road breaks.
        host.EndSession(1);

        Assert.Equal("model v1", File.ReadAllText(central));
        Assert.Equal("A owns nothing", File.ReadAllText(Path.Combine(folder.Path, "NP_backup", "permissions.dat")));
        Assert.False(File.Exists(Path.Combine(folder.Path, "NP_backup", "7.rws")));
        Assert.True(host.RolledBack >= 1);
    }

    [Fact]
    public async Task ABurstThatFinishesStays()
    {
        using var folder = new TemporaryFolder();
        string central = Path.Combine(folder.Path, "NP.rvt");
        File.WriteAllText(central, "model v1");
        using var host = new SyncCentralHost(folder.Path, "NP.rvt");
        var member = new SyncCentralClient(new SyncCentralLocalChannel(host, 1));

        SyncCentralResponse model = await member.CallAsync(new SyncCentralRequest { Op = SyncCentralOp.Open, Path = "NP.rvt", Mode = FileMode.Create, Access = FileAccess.Write, Share = FileShare.None });
        await member.WriteAsync(model.Handle, 0, "model v2"u8.ToArray());
        await member.CallAsync(new SyncCentralRequest { Op = SyncCentralOp.Close, Handle = model.Handle });
        host.EndSession(1);

        Assert.Equal("model v2", File.ReadAllText(central));
    }

    [Fact]
    public async Task PfrHoldsTheCommitUntilTheSynchronizeEnds()
    {
        using var folder = new TemporaryFolder();
        string central = Path.Combine(folder.Path, "NP.rvt");
        File.WriteAllText(central, "model v1");
        Directory.CreateDirectory(Path.Combine(folder.Path, "NP_backup"));
        using var host = new SyncCentralHost(folder.Path, "NP.rvt");
        var member = new SyncCentralClient(new SyncCentralLocalChannel(host, 1));

        await member.CallAsync(new SyncCentralRequest { Op = SyncCentralOp.TxBegin });
        SyncCentralResponse model = await member.CallAsync(new SyncCentralRequest { Op = SyncCentralOp.Open, Path = "NP.rvt", Mode = FileMode.Create, Access = FileAccess.Write, Share = FileShare.None });
        await member.WriteAsync(model.Handle, 0, "model v2"u8.ToArray());
        await member.CallAsync(new SyncCentralRequest { Op = SyncCentralOp.Close, Handle = model.Handle });
        // Revit closed the model but has more to write: the road breaks before PFR's end.
        host.EndSession(1);
        Assert.Equal("model v1", File.ReadAllText(central));

        var again = new SyncCentralClient(new SyncCentralLocalChannel(host, 2));
        await again.CallAsync(new SyncCentralRequest { Op = SyncCentralOp.TxBegin });
        SyncCentralResponse second = await again.CallAsync(new SyncCentralRequest { Op = SyncCentralOp.Open, Path = "NP.rvt", Mode = FileMode.Create, Access = FileAccess.Write, Share = FileShare.None });
        await again.WriteAsync(second.Handle, 0, "model v2"u8.ToArray());
        await again.CallAsync(new SyncCentralRequest { Op = SyncCentralOp.Close, Handle = second.Handle });
        await again.CallAsync(new SyncCentralRequest { Op = SyncCentralOp.TxEnd });
        host.EndSession(2);
        Assert.Equal("model v2", File.ReadAllText(central));
    }

    [Fact]
    public void ACallResentAfterABreakIsNotRunTwice()
    {
        using var folder = new TemporaryFolder();
        File.WriteAllText(Path.Combine(folder.Path, "NP.rvt"), "x");
        using var host = new SyncCentralHost(folder.Path, "NP.rvt");

        SyncCentralResponse first = host.Handle(1, new SyncCentralRequest { Op = SyncCentralOp.Open, Seq = 1, Path = "NP.rvt.lock", Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None }, ReadOnlyMemory<byte>.Empty, out _);
        // The answer was lost on the road; the member resends call 1.
        SyncCentralResponse resent = host.Handle(1, new SyncCentralRequest { Op = SyncCentralOp.Open, Seq = 1, Path = "NP.rvt.lock", Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None }, ReadOnlyMemory<byte>.Empty, out _);

        Assert.True(first.Ok);
        Assert.True(resent.Ok); // not «exists»: the same call, answered again
        Assert.Equal(first.Handle, resent.Handle);
    }
}
