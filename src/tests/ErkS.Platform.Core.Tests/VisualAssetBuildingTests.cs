using System.Text.Json;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// Which building a rendered view is of.
///
/// Revit had been declaring this on every asset for some time and this reader
/// was dropping it, because the field existed in the exporter's contract and
/// not in ours. Nothing failed: packages loaded, assets drew, and a board
/// simply could not group visuals by building - the information had been sent
/// and thrown away.
///
/// The shape below is copied from a package Revit actually produced, so this
/// pins the contract rather than a guess about it.
/// </summary>
public sealed class VisualAssetBuildingTests
{
    private const string RealAsset = """
        {
          "assetId": "9903dfb4-e4a2-4c91-95ed-44640dfaa59f-001ad879",
          "viewName": "ErkS_Sample_Perspective_Realistic",
          "kind": "render",
          "mediaType": "image/png",
          "fileName": "002_ErkS_Sample_Perspective_Realistic.png",
          "widthPx": 4725,
          "heightPx": 2837,
          "dpi": 300,
          "isPerspective": true,
          "buildingId": "b745c5ebe5b343c6af3f9a4d0ebf1f74",
          "buildingName": "Орон сууц"
        }
        """;

    private static readonly JsonSerializerOptions Options =
        new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };

    [Fact]
    public void TheBuildingOnAnAssetIsRead()
    {
        VisualAsset asset = Read();

        Assert.Equal("b745c5ebe5b343c6af3f9a4d0ebf1f74", asset.BuildingId);
        Assert.Equal("Орон сууц", asset.BuildingName);
    }

    [Fact]
    public void AnAssetFromASourceWithNoBuildingIsStillPerfectlyValid()
    {
        // A model that belongs to no building is ordinary. Reading the absence
        // as a problem would turn every general-plan render into a complaint.
        const string json = """
            { "assetId": "a1", "kind": "render", "mediaType": "image/png", "widthPx": 10, "heightPx": 10 }
            """;

        VisualAsset? asset = JsonSerializer.Deserialize<VisualAsset>(json, Options);

        Assert.NotNull(asset);
        Assert.Equal("", asset!.BuildingId);
        Assert.Equal("", asset.BuildingName);
    }

    [Fact]
    public void APerspectiveSaysSoAndKeepsItsResolution()
    {
        // A perspective has no scale, so the exporter sizes it from the board
        // rather than the paper. The pixel count is the only thing that says
        // whether it can be printed large.
        VisualAsset asset = Read();

        Assert.True(asset.IsPerspective);
        Assert.Equal(4725, asset.WidthPx);
        Assert.Equal(300, asset.Dpi);
    }

    [Fact]
    public void TheKindIsNotInferredFromTheMediaTypeEitherWay()
    {
        // Both directions are independent, confirmed on real packages: one
        // kind arrived as PNG and as PDF, and two different kinds arrived as
        // PNG. Neither field may be guessed from the other.
        VisualAsset asset = Read();

        Assert.Equal("render", asset.Kind);
        Assert.Equal("image/png", asset.MediaType);
        Assert.False(asset.IsVector);
    }

    private static VisualAsset Read() =>
        JsonSerializer.Deserialize<VisualAsset>(RealAsset, Options)!;
}
