namespace ErkS.Sync.Core.Tests;

/// <summary>
/// Owner, 2026-10-26: «программаа шинэчлээгүй онлайн болоогүй удсан гишүүн бусад гишүүдийн ажиллагаанд
/// нөлөөлөх учиргүй». A member long offline, or older than the server's minimum, is no source of files.
/// </summary>
public sealed class ALAGGINGMemberSHAPESNoOneElsesFolderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 22, 0, 0, TimeSpan.Zero);

    private static SyncServerDevice Device(DateTimeOffset seen, string version = "") =>
        new("D", "", "member@example.mn", "", Now.AddDays(-30), seen, "", version);

    [Fact]
    public void LongOfflineOrOutdatedIsLagging()
    {
        Assert.False(SyncLagging.IsLagging(Device(Now.AddHours(-2), "0.001.108"), Now, "0.001.108"));
        Assert.True(SyncLagging.IsLagging(Device(Now.AddHours(-30)), Now));
        Assert.True(SyncLagging.IsLagging(Device(Now, "0.001.104"), Now, "0.001.108"));
        // A server without the minimum, or a member that never said its version: only time counts.
        Assert.False(SyncLagging.IsLagging(Device(Now, "0.001.104"), Now));
        Assert.False(SyncLagging.IsLagging(Device(Now), Now, "0.001.108"));
    }

    [Fact]
    public void TheReleaseComesFromTheInstallManifest()
    {
        using var folder = new TemporaryFolder();
        File.WriteAllText(Path.Combine(folder.Path, ".erks-install.json"), "{\"productCode\":\"ErkS.Studio\",\"version\":\"v0.001.108\",\"files\":[]}");
        Assert.Equal("0.001.108", SyncLagging.InstalledRelease(folder.Path));
        Assert.Equal("", SyncLagging.InstalledRelease(Path.Combine(folder.Path, "missing")));
    }
}
