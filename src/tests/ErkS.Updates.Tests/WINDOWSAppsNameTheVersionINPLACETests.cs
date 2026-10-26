namespace ErkS.Updates.Tests;

/// <summary>Master, 2026-10-27: after a delta update Windows «Apps» still named the old version - the applier moves it, in the entry's own form.</summary>
public sealed class WINDOWSAppsNameTheVersionINPLACETests
{
    [Theory]
    [InlineData("Demo V0.001.101", "v0.001.127", true, "Demo V0.001.127")]
    [InlineData("v0.040", "v0.065", true, "v0.065")]
    [InlineData("0.040", "v0.065", true, "0.065")]
    [InlineData("", "v0.065", true, "v0.065")]
    [InlineData("Erk-S CityGen version v0.032", "v0.065", false, "Erk-S CityGen version v0.065")]
    [InlineData("Erk-S Studio", "v0.001.127", false, "Erk-S Studio")]
    public void TheVersionMovesTheRestStays(string current, string version, bool isDisplayVersion, string expected) =>
        Assert.Equal(expected, UninstallEntryText.WithVersion(current, version, isDisplayVersion));
}
