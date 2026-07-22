using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

public sealed class SiteContextMapRuntimeAssetTests
{
    [Fact]
    public void EmbeddedMapRuntime_CanBeExtractedWithoutBuildOutputAssets()
    {
        string folder = Path.Combine(
            Path.GetTempPath(),
            "erk-s-map-runtime-" + Guid.NewGuid().ToString("N"));

        try
        {
            string path = SiteContextMapRuntimeAsset.EnsureExtracted(folder);

            Assert.Equal(SiteContextMapRuntimeAsset.FileName, Path.GetFileName(path));
            Assert.True(File.Exists(path));
            string runtime = File.ReadAllText(path);
            Assert.Contains("window.erks", runtime);
            Assert.Contains("fitBounds", runtime);
            Assert.Contains("currentBounds", runtime);
            Assert.Contains("setPlanFeatures", runtime);
            Assert.Contains("renderPlanFeatures", runtime);
            Assert.Contains("roadPaths", runtime);
            Assert.Contains("buildingRings", runtime);
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void RepeatedExtraction_ReusesOneRuntimeFileWithoutTemporaryFiles()
    {
        string folder = Path.Combine(
            Path.GetTempPath(),
            "erk-s-map-runtime-" + Guid.NewGuid().ToString("N"));

        try
        {
            string first = SiteContextMapRuntimeAsset.EnsureExtracted(folder);
            string second = SiteContextMapRuntimeAsset.EnsureExtracted(folder);

            Assert.Equal(first, second);
            Assert.Single(Directory.GetFiles(folder));
            Assert.Empty(Directory.GetFiles(folder, "*.tmp"));
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }
}
