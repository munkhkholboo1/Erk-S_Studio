using System.Text.Json;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// The channel Revit's board visuals arrive through.
///
/// The shapes here are confirmed rather than assumed: a package their exporter
/// actually produced - five assets out of a real model, hashes and all - reads
/// clean through this reader. Asking for that package before writing a line of
/// it was worth doing on its own, because it caught their first output writing
/// a single pixel figure where the contract promised a width and a height. A
/// reader built to the promise would have waited for fields that never came.
/// </summary>
public sealed class VisualPackageReaderTests : IDisposable
{
    private readonly string workDirectory =
        Path.Combine(Path.GetTempPath(), "erks-platform-tests", Guid.NewGuid().ToString("N"));

    public VisualPackageReaderTests() => Directory.CreateDirectory(workDirectory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(workDirectory, recursive: true);
        }
        catch
        {
        }
    }

    [Fact]
    public void APackageOfRendersAndLineViewsIsAccepted()
    {
        VisualAsset render = Raster("render-1", "Хойд перспектив", VisualAssetKinds.Render);
        VisualAsset lineView = Vector("line-1", "Аксонометр");
        VisualPackageManifest manifest = Manifest(render, lineView);
        WriteFile(render, "raster bytes");
        WriteFile(lineView, "%PDF-1.7 vector bytes");

        VisualPackageLoadResult result = VisualPackageReader.Verify(manifest, workDirectory);

        Assert.True(result.IsLoaded, string.Join("; ", result.Issues));
        Assert.Empty(result.SkippedAssets);
        Assert.Equal(2, result.Accepted.Count);
    }

    [Fact]
    public void TheHashIsCheckedRatherThanTrusted()
    {
        // A truncated render still opens, still draws, and is simply wrong -
        // and it would be wrong on a printed board.
        VisualAsset render = Raster("render-1", "Рендер", VisualAssetKinds.Render);
        VisualPackageManifest manifest = Manifest(render);
        WriteFile(render, "the bytes the manifest describes");
        File.WriteAllText(Path.Combine(workDirectory, render.FileName), "different bytes");

        VisualPackageLoadResult result = VisualPackageReader.Verify(manifest, workDirectory);

        Assert.True(result.IsLoaded);
        Assert.Contains(result.SkippedAssets, issue => issue.Contains("хэш"));
        Assert.Empty(result.Accepted);
    }

    [Fact]
    public void ARasterWithoutItsPixelSizeIsRefused()
    {
        // Without it a card cannot say whether the render holds up at the size
        // it is placed, which is the guard against finding out at print time.
        VisualAsset render = Raster("render-1", "Рендер", VisualAssetKinds.Render);
        render.WidthPx = 0;
        WriteFile(render, "bytes");

        VisualPackageLoadResult result = VisualPackageReader.Verify(Manifest(render), workDirectory);

        Assert.Contains(result.SkippedAssets, issue => issue.Contains("пиксел"));
        Assert.Empty(result.Accepted);
    }

    [Fact]
    public void AVectorViewWithoutItsPageRectangleIsRefused()
    {
        // The rectangle is the only authority on where the drawing sits: Revit
        // keeps the geometry outside the clip in the file as well, so measuring
        // the ink finds a far larger area than the view.
        VisualAsset lineView = Vector("line-1", "Аксонометр");
        lineView.Page = null;
        WriteFile(lineView, "bytes");

        VisualPackageLoadResult result = VisualPackageReader.Verify(Manifest(lineView), workDirectory);

        Assert.Contains(result.SkippedAssets, issue => issue.Contains("байрлал"));
    }

    [Fact]
    public void APageRectangleReachingOffThePaperIsRefused()
    {
        VisualAsset lineView = Vector("line-1", "Аксонометр");
        lineView.Page!.ViewWidthMm = 900;
        WriteFile(lineView, "bytes");

        VisualPackageLoadResult result = VisualPackageReader.Verify(Manifest(lineView), workDirectory);

        Assert.Contains(result.SkippedAssets, issue => issue.Contains("байрлал"));
    }

    [Fact]
    public void ThePageRectangleBecomesTheCropACardHolds()
    {
        // The numbers a real Revit export produced: a 300 x 200 mm view centred
        // on ISO B4 landscape.
        var page = new VisualAssetPage
        {
            PaperWidthMm = 353,
            PaperHeightMm = 250,
            ViewXMm = 26.5,
            ViewYMm = 25,
            ViewWidthMm = 300,
            ViewHeightMm = 200,
        };

        (double x, double y, double width, double height) = Require(page.AsNormalizedCrop());

        Assert.Equal(26.5d / 353d, x, precision: 9);
        Assert.Equal(0.1, y, precision: 9);
        Assert.Equal(300d / 353d, width, precision: 9);
        Assert.Equal(0.8, height, precision: 9);
    }

    [Fact]
    public void WhetherSomethingIsVectorComesFromItsMediaTypeNotItsKind()
    {
        // The two are stated separately on purpose: a shaded diagram may arrive
        // either way, and inferring from the kind would be wrong for it.
        var vectorDiagram = new VisualAsset
        {
            Kind = VisualAssetKinds.ShadedDiagram,
            MediaType = VisualMediaTypes.Pdf,
        };
        var rasterDiagram = new VisualAsset
        {
            Kind = VisualAssetKinds.ShadedDiagram,
            MediaType = VisualMediaTypes.Png,
        };

        Assert.True(vectorDiagram.IsVector);
        Assert.False(rasterDiagram.IsVector);
    }

    [Fact]
    public void AnUnknownMediaTypeIsRefused()
    {
        VisualAsset asset = Raster("a", "Юу ч", VisualAssetKinds.Render);
        asset.MediaType = "image/tiff";
        WriteFile(asset, "bytes");

        VisualPackageLoadResult result = VisualPackageReader.Verify(Manifest(asset), workDirectory);

        Assert.Contains(result.SkippedAssets, issue => issue.Contains("дэмжигдэхгүй"));
    }

    [Fact]
    public void AnAssetWithoutAnIdentityIsRefused()
    {
        // A re-export could only append a duplicate, and whatever framing the
        // user had given it would be orphaned.
        VisualAsset asset = Raster("", "Рендер", VisualAssetKinds.Render);
        WriteFile(asset, "bytes");

        VisualPackageLoadResult result = VisualPackageReader.Verify(Manifest(asset), workDirectory);

        Assert.Contains(result.SkippedAssets, issue => issue.Contains("танигчгүй"));
    }

    [Fact]
    public void DuplicateIdentitiesAreRefusedRatherThanSilentlyMerged()
    {
        // Two genuinely different files claiming one identity, which is what a
        // producer with an unstable key actually sends.
        VisualAsset first = Raster("same", "Нэг", VisualAssetKinds.Render);
        VisualAsset second = Raster("same", "Хоёр", VisualAssetKinds.Render);
        first.FileName = "first.png";
        second.FileName = "second.png";
        WriteFile(first, "one");
        WriteFile(second, "two");

        VisualPackageLoadResult result = VisualPackageReader.Verify(
            Manifest(first, second),
            workDirectory);

        Assert.Single(result.Accepted);
        Assert.Contains(result.SkippedAssets, issue => issue.Contains("давхардсан"));
    }

    [Fact]
    public void OneUnusableAssetDoesNotCostThePackageTheRest()
    {
        VisualAsset good = Raster("good", "Сайн", VisualAssetKinds.Render);
        VisualAsset bad = Raster("bad", "Муу", VisualAssetKinds.Render);
        bad.HeightPx = 0;
        WriteFile(good, "bytes");
        WriteFile(bad, "bytes");

        VisualPackageLoadResult result = VisualPackageReader.Verify(
            Manifest(good, bad),
            workDirectory);

        Assert.True(result.IsLoaded);
        Assert.Equal("good", Assert.Single(result.Accepted).AssetId);
        Assert.Single(result.SkippedAssets);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void APackageOfAnotherSchemaVersionIsRefusedWhole(int schemaVersion)
    {
        VisualPackageManifest manifest = Manifest();
        manifest.SchemaVersion = schemaVersion;

        VisualPackageLoadResult result = VisualPackageReader.Verify(manifest, workDirectory);

        Assert.False(result.IsLoaded);
        Assert.Null(result.Manifest);
    }

    [Fact]
    public void APackageThatNamesNoSourceIsRefused()
    {
        VisualPackageManifest manifest = Manifest();
        manifest.Source.SourceId = "";

        Assert.False(VisualPackageReader.Verify(manifest, workDirectory).IsLoaded);
    }

    [Fact]
    public void TheManifestIsReadFromDiskInTheShapeTheContractDescribes()
    {
        VisualAsset render = Raster("3f2a-render", "Шөнийн рендер", VisualAssetKinds.Render);
        VisualAsset lineView = Vector("3f2a-axo", "Аксонометр");
        lineView.SeriesId = "diagram-strip";
        lineView.SeriesOrder = 2;
        WriteFile(render, "raster");
        WriteFile(lineView, "vector");

        string path = Path.Combine(workDirectory, "P-001-Revit-20260824-041500.erks-visuals.json");
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(
                Manifest(render, lineView),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        VisualPackageLoadResult result = VisualPackageReader.Load(path);

        Assert.True(result.IsLoaded, string.Join("; ", result.Issues));
        Assert.Empty(result.SkippedAssets);
        VisualAsset readBack = result.Accepted.Single(asset => asset.AssetId == "3f2a-axo");
        Assert.Equal("diagram-strip", readBack.SeriesId);
        Assert.Equal(2, readBack.SeriesOrder);
        Assert.True(readBack.IsVector);
        Assert.NotNull(readBack.Page);
    }

    [Fact]
    public void AFileThatIsNotJsonIsRefusedRatherThanThrown()
    {
        string path = Path.Combine(workDirectory, "broken.erks-visuals.json");
        File.WriteAllText(path, "{ not json");

        Assert.False(VisualPackageReader.Load(path).IsLoaded);
    }

    private VisualPackageManifest Manifest(params VisualAsset[] assets) => new()
    {
        SchemaVersion = VisualPackageContract.CurrentSchemaVersion,
        PackageId = Guid.NewGuid().ToString("N"),
        ProjectId = "P-JOINT-001",
        ExportedAtUtc = new DateTimeOffset(2026, 8, 24, 4, 15, 0, TimeSpan.Zero),
        Source = new VisualPackageSource
        {
            SourceId = "revit-1",
            Application = VisualPackageContract.ApplicationRevit,
            ApplicationVersion = "2026",
            DocumentTitle = "Competition model",
        },
        Assets = [.. assets],
    };

    private static VisualAsset Raster(string assetId, string viewName, string kind) => new()
    {
        AssetId = assetId,
        ViewName = viewName,
        ViewType = "ThreeD",
        Kind = kind,
        MediaType = VisualMediaTypes.Png,
        FileName = $"{(assetId.Length == 0 ? "anon" : assetId)}.png",
        WidthPx = 4489,
        HeightPx = 2835,
        Dpi = 300,
    };

    private static VisualAsset Vector(string assetId, string viewName) => new()
    {
        AssetId = assetId,
        ViewName = viewName,
        ViewType = "ThreeD",
        Kind = VisualAssetKinds.LineView,
        MediaType = VisualMediaTypes.Pdf,
        FileName = $"{assetId}.pdf",
        // The contract's own measured example: 300 x 200 mm centred on A3.
        // The values a real Revit export produced: a 300 x 200 mm view centred
        // on ISO B4 landscape.
        Page = new VisualAssetPage
        {
            PaperWidthMm = 353,
            PaperHeightMm = 250,
            ViewXMm = 26.5,
            ViewYMm = 25,
            ViewWidthMm = 300,
            ViewHeightMm = 200,
        },
    };

    private void WriteFile(VisualAsset asset, string content)
    {
        string path = Path.Combine(workDirectory, asset.FileName);
        File.WriteAllText(path, content);
        asset.Sha256 = ProjectDocumentFileStore.ComputeSha256(path);
    }

    private static (double X, double Y, double Width, double Height) Require(
        (double X, double Y, double Width, double Height)? crop)
    {
        Assert.NotNull(crop);
        return crop!.Value;
    }
}
