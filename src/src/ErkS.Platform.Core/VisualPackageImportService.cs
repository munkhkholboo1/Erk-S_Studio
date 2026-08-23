namespace ErkS.Platform.Core;

public sealed record VisualPackageImportResult(
    string SourceId,
    int CreatedItemCount,
    int UpdatedItemCount,
    int SkippedAssetCount,
    IReadOnlyList<string> Issues)
{
    public bool BroughtAnything => CreatedItemCount > 0 || UpdatedItemCount > 0;
}

/// <summary>
/// Brings a Revit visual package into the project's pool of material.
///
/// It is the same intake the sheet package already has, and deliberately so: an
/// asset is keyed by the source and the view's own identity, copied into the
/// project, and refreshed in place when the same view is exported again. A
/// render the user has captioned and placed on three boards keeps all of that
/// when the model changes.
/// </summary>
public static class VisualPackageImportService
{
    public static VisualPackageImportResult Import(
        ProjectWorkspace project,
        string projectPath,
        VisualPackageLoadResult loaded,
        string packageFolder,
        string? sourceId = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ArgumentNullException.ThrowIfNull(loaded);
        project.Portfolio ??= new ProjectPortfolio();

        if (!loaded.IsLoaded)
            return new VisualPackageImportResult("", 0, 0, 0, loaded.Issues);

        VisualPackageManifest manifest = loaded.Manifest!;
        string resolvedSourceId = string.IsNullOrWhiteSpace(sourceId)
            ? manifest.Source.SourceId
            : sourceId.Trim();

        var issues = new List<string>(loaded.SkippedAssets);
        int created = 0;
        int updated = 0;

        foreach (VisualAsset asset in loaded.Accepted)
        {
            string key = MakeKey(resolvedSourceId, asset.AssetId);
            ProjectPortfolioItem? item = project.Portfolio.Items.FirstOrDefault(candidate =>
                string.Equals(candidate.SourceSheetKey, key, StringComparison.Ordinal));

            // An older export re-scanned later must not roll the item back.
            if (item?.SourceExportedAtUtc > manifest.ExportedAtUtc)
                continue;

            string filePath = Path.Combine(packageFolder, asset.FileName);
            string relativePath;
            try
            {
                relativePath = ProjectDocumentFileStore.StoreInsideProject(
                    projectPath,
                    ProjectDocumentCategories.Portfolio,
                    filePath);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                issues.Add($"{Describe(asset)}: хадгалж чадсангүй - {exception.Message}");
                continue;
            }

            string sourceTitle = string.IsNullOrWhiteSpace(asset.ViewName)
                ? asset.FileName
                : asset.ViewName.Trim();
            (double cropX, double cropY, double cropWidth, double cropHeight) = Crop(asset);

            if (item is null)
            {
                project.Portfolio.Items.Add(new ProjectPortfolioItem
                {
                    Order = project.Portfolio.Items.Count + 1,
                    Kind = ProjectPortfolioItemKinds.Visual,
                    // A view already carries whatever framing it was given, so
                    // fitting it to the edge keeps every millimetre of it
                    // without adding a second margin around the first.
                    Layout = ProjectPortfolioLayouts.FitPage,
                    Title = sourceTitle,
                    SourceTitle = sourceTitle,
                    RelativePath = relativePath,
                    SourceSheetKey = key,
                    SourceExportedAtUtc = manifest.ExportedAtUtc,
                    SourceCropX = cropX,
                    SourceCropY = cropY,
                    SourceCropWidth = cropWidth,
                    SourceCropHeight = cropHeight,
                    SourceWidthPixels = asset.WidthPx,
                    SourceHeightPixels = asset.HeightPx,
                    SourceSeriesId = asset.SeriesId,
                    SourceSeriesOrder = asset.SeriesOrder,
                });
                created++;
                continue;
            }

            item.RelativePath = relativePath;
            item.Kind = ProjectPortfolioItemKinds.Visual;
            // The user's own wording is theirs. Only a title they never changed
            // follows the source.
            if (string.Equals(item.Title, item.SourceTitle, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(item.Title))
            {
                item.Title = sourceTitle;
            }
            item.SourceTitle = sourceTitle;
            item.SourceExportedAtUtc = manifest.ExportedAtUtc;
            item.SourceCropX = cropX;
            item.SourceCropY = cropY;
            item.SourceCropWidth = cropWidth;
            item.SourceCropHeight = cropHeight;
            item.SourceWidthPixels = asset.WidthPx;
            item.SourceHeightPixels = asset.HeightPx;
            item.SourceSeriesId = asset.SeriesId;
            item.SourceSeriesOrder = asset.SeriesOrder;
            item.MissingFromSourceSinceUtc = null;
            updated++;
        }

        project.Portfolio.Normalize();
        return new VisualPackageImportResult(
            resolvedSourceId,
            created,
            updated,
            loaded.SkippedAssets.Count,
            issues);
    }

    /// <summary>
    /// The key an asset is remembered by: the source it came from and the
    /// view's own identity in the model. The same shape the sheet package uses,
    /// so one mechanism recognises a page and a view alike.
    /// </summary>
    public static string MakeKey(string sourceId, string assetId) =>
        $"{(sourceId ?? "").Trim()}|{(assetId ?? "").Trim()}";

    /// <summary>
    /// The part of the delivered file that is the drawing. A vector view says
    /// so through its page rectangle; anything else is all drawing.
    /// </summary>
    private static (double X, double Y, double Width, double Height) Crop(VisualAsset asset) =>
        asset.IsVector && asset.Page?.AsNormalizedCrop() is { } crop
            ? crop
            : (0, 0, 1, 1);

    private static string Describe(VisualAsset asset) =>
        string.IsNullOrWhiteSpace(asset.ViewName) ? asset.FileName : asset.ViewName;
}
