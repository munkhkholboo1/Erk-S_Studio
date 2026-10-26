namespace ErkS.Studio.App.Tests;

/// <summary>
/// Erk-S Sync, 2026-10-27: an open Studio kept saying «no update» until restarted. Erk-S Sync tells it
/// on its pipe the moment the server publishes; only a «release» line for Studio counts.
/// </summary>
public sealed class ANEWStudioIsKNOWNWhenPublishedTests
{
    [Fact]
    public void OnlyAStudioRELEASELineCarriesAVersion()
    {
        Assert.Equal("v0.001.122", StudioReleaseListener.StudioVersionIn("{\"event\":\"release\",\"productCode\":\"ErkS.Studio\",\"version\":\"v0.001.122\"}"));
        Assert.Null(StudioReleaseListener.StudioVersionIn("{\"event\":\"release\",\"productCode\":\"ErkS.CityGen\",\"version\":\"v0.060\"}"));
        Assert.Null(StudioReleaseListener.StudioVersionIn("{\"event\":\"collab-arrived\",\"fileId\":\"x\"}"));
        Assert.Null(StudioReleaseListener.StudioVersionIn("not json"));
        Assert.Equal("v0.001.123", StudioReleaseListener.StudioVersionIn("{\"event\":\"release\",\"productCode\":\"ErkS.Studio\",\"channel\":\"\",\"version\":\"v0.001.123\"}"));
        Assert.Null(StudioReleaseListener.StudioVersionIn("{\"event\":\"release\",\"productCode\":\"ErkS.Studio\",\"channel\":\"beta\",\"version\":\"v0.002\"}"));
    }
}
