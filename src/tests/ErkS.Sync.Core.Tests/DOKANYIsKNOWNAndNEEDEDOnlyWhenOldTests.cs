namespace ErkS.Sync.Core.Tests;

/// <summary>Erk-S Sync / owner, 2026-10-27: Studio installs Dokany itself when a central needs it - and only then.</summary>
public sealed class DOKANYIsKNOWNAndNEEDEDOnlyWhenOldTests
{
    [Theory]
    [InlineData("", true)]
    [InlineData("1.5.1.1000", true)]
    [InlineData("2.2.0.1000", true)]
    [InlineData("2.3.0.1000", false)]
    [InlineData("2.3.1.1000", false)]
    [InlineData("2.4.0.1000", false)]
    public void ItIsNEEDEDWhenMissingOrOlderThan23(string installed, bool needed) =>
        Assert.Equal(needed, SyncDokany.NeedsInstall(installed));

    [Fact]
    public void ThePINNEDReleaseIsTheOfficialMsi()
    {
        Assert.StartsWith("https://github.com/dokan-dev/dokany/releases/download/v" + SyncDokany.PinnedVersion + "/", SyncDokany.MsiUrl);
        Assert.Equal(64, SyncDokany.MsiSha256.Length);
    }
}
