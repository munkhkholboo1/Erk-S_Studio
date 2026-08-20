using ErkS.Platform.Core;

namespace ErkS.Studio.App.Tests;

public sealed class StudioAlbumPreviewManifestCacheTests
{
    [Fact]
    public void CloudLocalPreview_UsesWorkingManifestWithNewLocalPages()
    {
        string previewPath = Path.Combine(
            Path.GetTempPath(),
            "album-preview-cache-tests",
            "cloud-local",
            "working.pdf");
        var result = new AlbumBuildResult
        {
            OutputPath = previewPath,
            SheetCount = 2,
            PageCount = 14,
        };
        result.Components.Add(new AlbumBuildComponent
        {
            Code = "source:architect:a2-plans",
            Label = "A2 plans",
            Order = 20,
            PageNumbers = [13, 14],
        });
        ProjectCloudAlbumComponentReference[] staleServerManifest =
        [
            new()
            {
                Code = "source:architect:a2-plans",
                PageNumbers = [13],
            },
        ];
        var cache = new StudioAlbumPreviewManifestCache();

        cache.Record(result);
        IReadOnlyList<ProjectCloudAlbumComponentReference> resolved =
            cache.Resolve(previewPath, staleServerManifest);

        Assert.Equal([13, 14], Assert.Single(resolved).PageNumbers);
    }

    [Fact]
    public void DifferentPreview_UsesItsPersistedSharedManifest()
    {
        string workingPath = Path.Combine(
            Path.GetTempPath(),
            "album-preview-cache-tests",
            "cloud-local",
            "working.pdf");
        var result = new AlbumBuildResult
        {
            OutputPath = workingPath,
            SheetCount = 1,
            PageCount = 14,
        };
        result.Components.Add(new AlbumBuildComponent
        {
            Code = "working",
            Label = "Working",
            Order = 1,
            PageNumbers = [14],
        });
        ProjectCloudAlbumComponentReference[] sharedManifest =
        [
            new() { Code = "canonical", PageNumbers = [13] },
        ];
        var cache = new StudioAlbumPreviewManifestCache();

        cache.Record(result);
        IReadOnlyList<ProjectCloudAlbumComponentReference> resolved =
            cache.Resolve(
                Path.Combine(Path.GetDirectoryName(workingPath)!, "canonical.pdf"),
                sharedManifest);

        Assert.Equal("canonical", Assert.Single(resolved).Code);
    }
}
