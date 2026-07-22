using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

public sealed class StudioUpdateCatalogTests
{
    [Fact]
    public void StudioCatalogEntry_AcceptsStudioInstaller()
    {
        var update = new StudioUpdateLatestResponse
        {
            ProductCode = StudioReleaseInfo.ProductCode,
            Version = "v0.001.13",
            DownloadUrl = "/updates/ErkS.Studio/ErkS_Studio_Demo_Update_v0.001.13.exe",
            IsUpdateAvailable = true,
        };

        StudioUpdateService.ValidateCatalogEntry(update);
    }

    [Fact]
    public void LegacyCatalogEntry_AcceptsProductScopedStudioUrl()
    {
        var update = new StudioUpdateLatestResponse
        {
            Version = "v0.001.13",
            DownloadUrl = "https://erk-s.mn/updates/ErkS.Studio/ErkS_Studio_Demo_Update_v0.001.13.exe",
            IsUpdateAvailable = true,
        };

        StudioUpdateService.ValidateCatalogEntry(update);
    }

    [Theory]
    [InlineData("ErkS.Platform.Revit", "/updates/ErkS.Platform.Revit/ErkS_Platform_Update_v0.011.4.zip")]
    [InlineData("", "/updates/ErkS.Platform.Revit/ErkS_Platform_Update_v0.011.4.exe")]
    public void StudioCatalogEntry_RejectsAnotherProduct(string productCode, string downloadUrl)
    {
        var update = new StudioUpdateLatestResponse
        {
            ProductCode = productCode,
            Version = "v0.011.4",
            DownloadUrl = downloadUrl,
            IsUpdateAvailable = true,
        };

        Assert.Throws<InvalidDataException>(() => StudioUpdateService.ValidateCatalogEntry(update));
    }

    [Fact]
    public void AvailableUpdate_RequiresDownloadUrl()
    {
        var update = new StudioUpdateLatestResponse
        {
            ProductCode = StudioReleaseInfo.ProductCode,
            Version = "v0.001.13",
            IsUpdateAvailable = true,
        };

        Assert.Throws<InvalidDataException>(() => StudioUpdateService.ValidateCatalogEntry(update));
    }
}
