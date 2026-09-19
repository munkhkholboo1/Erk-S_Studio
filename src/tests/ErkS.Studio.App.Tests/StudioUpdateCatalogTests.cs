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

    /// <summary>
    /// 🔴 THIS GUARD IS LOAD-BEARING, AND THE REASON LIVES ON THE OTHER SIDE OF A BOUNDARY.
    /// SRV measured the live server on 2026-09-19, with positive controls:
    ///
    ///     productCode = "ErkS.Studio"            -> ErkS.Studio v0.001.63          ✅
    ///     productCode = "ErkS.Platform.Studio"   -> ErkS.Platform.Revit v0.011.12   ← a typo
    ///     productCode = "TOTALLY.BOGUS.NOTHING"  -> ErkS.Platform.Revit v0.011.12
    ///     productCode = ""                       -> ErkS.Platform.Revit
    ///
    /// A MISSING code falling back to Revit is deliberate - it serves clients older than
    /// the parameter. An UNRECOGNISED one doing the same is that fallback reaching further
    /// than it was meant to, and the server answers `isUpdateAvailable: true` with a real
    /// download url. So Studio is offered another product's installer by a server that is
    /// behaving exactly as designed.
    ///
    /// What makes it safe is a pair: the server names the product honestly in its reply,
    /// and Studio compares. Either half alone is not a guard. Whoever finds this check
    /// one day and thinks «this never fires, tidy it away» would be removing the half that
    /// lives on this side.
    ///
    /// ⚠ The typo case is the realistic one - nobody types TOTALLY.BOGUS, and a product
    /// code is a string written by hand in a manifest.
    /// </summary>
    [Theory]
    [InlineData("ErkS.Platform.Revit", "/updates/ErkS.Platform.Revit/ErkS_Platform_Update_v0.011.4.zip")]
    [InlineData("ErkS.Platform.Studio", "/updates/ErkS.Platform.Revit/ErkS_Platform_Update_v0.011.12.exe")]
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

    [Fact]
    public void InstallerHandoff_UsesUpdateModeAndProcessId()
    {
        string arguments = StudioUpdateService.CreateInstallerArguments(4242);

        Assert.Equal("/update /waitforpid=4242", arguments);
    }

    [Fact]
    public void InstallerHandoff_RejectsInvalidProcessId()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => StudioUpdateService.CreateInstallerArguments(0));
    }

    [Fact]
    public void ANOTHERProductIsRejectedEvenWhenItsUrlLooksLikeOurs()
    {
        // 🔴 THE THEORY ABOVE DOES NOT ACTUALLY TEST THE PRODUCT-CODE GUARD. A sabotage
        // pass deleted that guard outright and every case stayed green: each of their
        // download urls points at another product's folder, so the URL check refuses them
        // first. The guard SRV had just shown to be load-bearing was held by nothing at
        // all - two checks in one method, and only one of them under test.
        //
        // This case removes the coincidence: the product code is another product's, the
        // url is an ordinary Studio one. Only the product-code comparison can refuse it.
        //
        // ⚠ WHY IT MATTERS, measured by SRV on the live server: an unrecognised product
        // code makes the server answer with the REVIT package, `isUpdateAvailable: true`
        // and a real download url. The server is behaving as designed; what stops Studio
        // installing another product is this comparison, here.
        var update = new StudioUpdateLatestResponse
        {
            ProductCode = "ErkS.Platform.Revit",
            Version = "v0.011.12",
            DownloadUrl = "/updates/ErkS.Studio/ErkS_Studio_Demo_Update_v0.001.13.exe",
            IsUpdateAvailable = true,
        };

        InvalidDataException refused = Assert.Throws<InvalidDataException>(
            () => StudioUpdateService.ValidateCatalogEntry(update));

        // And it refuses for the RIGHT reason - naming the product it was offered.
        Assert.Contains("ErkS.Platform.Revit", refused.Message, StringComparison.Ordinal);
    }
}
