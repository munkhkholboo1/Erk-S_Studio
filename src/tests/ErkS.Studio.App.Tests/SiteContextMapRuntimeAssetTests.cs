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
            Assert.Contains("prepareCapture", runtime);
            Assert.Contains("waitForLeafletTiles", runtime);
            Assert.Contains("currentLeafletTilesReady", runtime);
            Assert.Contains("fitBounds(bounds, true)", runtime);
            Assert.Contains("map.options.zoomSnap = 0", runtime);
            Assert.Contains("maxNativeZoom: nativeMaxZoom", runtime);
            Assert.Contains("map.setMaxZoom(leafletCaptureMaxZoom)", runtime);
            Assert.Contains("map.setMaxZoom(leafletInteractiveMaxZoom)", runtime);
            Assert.Contains("renderAnnotations", runtime);
            Assert.Contains("locationIconPath", runtime);
            Assert.Contains("landmarks", runtime);
            Assert.Contains("distanceMeasures", runtime);
            Assert.Contains("radiusMeasures", runtime);
            Assert.Contains("haversineDistance", runtime);
            Assert.Contains("niceDistanceStep", runtime);
            Assert.Contains("boundaryAreaSquareMeters", runtime);
            Assert.Contains("setTool, finishDrawing", runtime);
            Assert.Contains("updateSelectedAnnotation", runtime);
            Assert.Contains("selectedDistanceVertexIndex", runtime);
            Assert.Contains("selectDistanceVertex", runtime);
            Assert.Contains("return Math.max(.72, host.clientWidth / 750);", runtime);
            Assert.DoesNotContain("Math.min(2.4, host.clientWidth / 750)", runtime);
            Assert.Contains("radiiMeters", runtime);
            Assert.Contains("ringColors", runtime);
            Assert.Contains("normalizeRadiusRings", runtime);
            Assert.Contains("applyRadiusRings", runtime);
            Assert.Contains("nextAvailableRadius", runtime);
            Assert.Contains("addRadius", runtime);
            Assert.Contains("removeSelectedRadius", runtime);
            Assert.Contains("Энэ радиустай цагираг аль хэдийн байна", runtime);
            Assert.Contains("editing && !capturing", runtime);
            Assert.Contains("annotationOverlay.classList.toggle('editing', editing && !capturing)", runtime);
            Assert.DoesNotContain("primaryRadiusMeters", runtime);
            Assert.Contains("tool-landmark", runtime);
            Assert.Contains("tool-distance", runtime);
            Assert.Contains("annotationStatusMessage", runtime);
            Assert.Contains("annotationToolName", runtime);
            Assert.Contains("toolOptions.name = '';", runtime);
            Assert.Contains("Нэр: ${landmark.name || 'Нэргүй'}", runtime);
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

    [Theory]
    [InlineData(0, 1500, 1809)]
    [InlineData(1, 3000, 3618)]
    [InlineData(2, 3000, 3618)]
    public void CaptureMetrics_RenderAtTheFinalPixelSize(
        double detailDelta,
        int expectedWidth,
        int expectedHeight)
    {
        (int width, int height, double deviceScaleFactor) =
            SiteContextMapEditorControl.CalculateCaptureMetrics(detailDelta);

        Assert.Equal(expectedWidth, width);
        Assert.Equal(expectedHeight, height);
        Assert.Equal(1d, deviceScaleFactor);
    }
}
