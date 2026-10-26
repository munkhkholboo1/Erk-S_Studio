namespace ErkS.Sync.Core.Tests;

/// <summary>
/// Owner, 2026-10-26: «программаа шинэчлээгүй онлайн болоогүй удсан гишүүн бусад гишүүдийн ажиллагаанд
/// нөлөөлөх учиргүй». A member long offline, or older than the server's minimum, is no source of files.
/// </summary>
public sealed class ALAGGINGMemberSHAPESNoOneElsesFolderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 22, 0, 0, TimeSpan.Zero);

    private static SyncServerDevice Device(DateTimeOffset seen, string version = "", int protocol = 0) =>
        new("D", "", "member@example.mn", "", Now.AddDays(-30), seen, "", version, protocol);

    [Fact]
    public void LongOfflineOrAnOlderSyncProtocolIsLagging()
    {
        Assert.True(SyncLagging.IsLagging(Device(Now.AddHours(-30), protocol: 1), Now, 1));
        Assert.True(SyncLagging.IsLagging(Device(Now, "0.001.105", protocol: 0), Now, 1)); // said no protocol
        // An older Studio speaking the same sync protocol is not lagging (owner, 2026-10-26: «вершний
        // зөрүү хэт их биш бол синк асуудалгүй явдаг болгоорой»).
        Assert.False(SyncLagging.IsLagging(Device(Now.AddHours(-2), "0.001.113", 1), Now, 1));
        Assert.False(SyncLagging.IsLagging(Device(Now, "0.001.090", 1), Now, 1));
        // A server without a minimum: only time counts.
        Assert.False(SyncLagging.IsLagging(Device(Now, "0.001.104"), Now));
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
