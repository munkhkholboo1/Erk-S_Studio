using ErkS.Sync.Core;

namespace ErkS.Sync.Core.Tests;

/// <summary>
/// 🔴 Owner, 2026-10-24: «синк ямар хурдаар хийгдэж байгаа нь харагддаг болгоорой» - the laptop sat
/// far from the router and nothing told anyone the transfer crawled at 0.2 MB/s. And the folder
/// read «36.9%» with every file that may travel already there: the Revit central model, which by
/// rule never travels, was counted as missing. Both are shown as what they are now.
/// </summary>
public sealed class THESpeedOfASyncISShownTests
{
    [Fact]
    public void TheRateFollowsTheBytesAsTheyArrive()
    {
        var rate = new SyncTransferRate();
        DateTimeOffset start = DateTimeOffset.UnixEpoch;

        rate.Add(0, start);
        rate.Add(10_000_000, start.AddSeconds(1));
        rate.Add(20_000_000, start.AddSeconds(2));

        Assert.InRange(rate.BytesPerSecond, 9_000_000, 11_000_000);
        Assert.InRange(rate.SecondsLeft(120_000_000, 20_000_000)!.Value, 9, 11);
    }

    [Fact]
    public void ABurstInsideASecondDoesNotFakeASpeed()
    {
        var rate = new SyncTransferRate();
        DateTimeOffset start = DateTimeOffset.UnixEpoch;

        rate.Add(0, start);
        rate.Add(4_000_000, start.AddMilliseconds(5));

        Assert.Equal(0, rate.BytesPerSecond);
        Assert.Null(rate.SecondsLeft(10, 0));
    }

    [Fact]
    public void ACentralModelThatNeverTravelsIsNotCountedAsMissing()
    {
        SyncIndex mine = Index(Sized("a.pdf", "1", 100));
        SyncIndex theirs = Index(Sized("a.pdf", "1", 100), Sized("NP.rvt", "c", 900, SyncFileKind.RevitCentral), Sized(@"NP_backup\x.rws", "d", 50, SyncFileKind.RevitCentralData));

        SyncCarriedDifference carried = SyncIndexComparison.CompareCarried(mine, theirs);

        Assert.Equal(100, carried.Difference.SyncedPercent);
        Assert.Equal(2, carried.NotCarriedCount);
        Assert.Equal(950, carried.NotCarriedBytes);
    }

    private static SyncIndex Index(params SyncIndexEntry[] entries) => new() { FolderId = "f-1", Entries = entries };

    private static SyncIndexEntry Sized(string path, string sha, long size, SyncFileKind? kind = null) =>
        new(path, size, DateTime.UtcNow, kind ?? SyncFileKinds.Of(path), sha, [sha]);
}
