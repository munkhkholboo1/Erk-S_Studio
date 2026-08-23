using ErkS.Platform.Contracts;

namespace ErkS.Platform.Core;

public sealed record PortfolioSheetImportResult(int CreatedItemCount, int UpdatedItemCount)
{
    public static PortfolioSheetImportResult Empty { get; } = new(0, 0);

    public bool HasChanges => CreatedItemCount > 0 || UpdatedItemCount > 0;
}

/// <summary>
/// Files a verified package's Portfolio entries into the project portfolio.
/// The album pipeline never sees these entries; each becomes (or updates) one
/// portfolio item whose PDF is copied into project-owned storage. A full
/// snapshot that omits a previously imported page does not remove its item:
/// the portfolio is the user's own presentation, and it must not lose content.
/// </summary>
public static class PortfolioSheetImportService
{
    public static PortfolioSheetImportResult Import(
        ProjectWorkspace project,
        string projectPath,
        SheetPackageLoadResult loadResult,
        string? sourceIdOverride = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(loadResult);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        if (!loadResult.IsLossless || loadResult.Manifest is null)
        {
            return PortfolioSheetImportResult.Empty;
        }

        SheetPackageManifest manifest = loadResult.Manifest;
        string? sourceId = string.IsNullOrWhiteSpace(sourceIdOverride)
            ? manifest.Source.SourceId
            : sourceIdOverride;
        int createdItemCount = 0;
        int updatedItemCount = 0;
        foreach (SheetPackageEntry entry in manifest.Sheets)
        {
            if (!SheetDestinations.IsPortfolio(entry.Destination) ||
                !loadResult.TryGetVerifiedPdfPath(entry, out string pdfPath))
            {
                continue;
            }

            string key = SheetRecord.MakeKey(manifest.Source, entry, sourceId);
            ProjectPortfolioItem? item = project.Portfolio.Items.FirstOrDefault(candidate =>
                candidate.Kind.Equals(
                    ProjectPortfolioItemKinds.CadPage,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(candidate.SourceSheetKey, key, StringComparison.Ordinal));
            // An older export re-scanned later must not roll the item back.
            if (item?.SourceExportedAtUtc > manifest.ExportedAtUtc)
            {
                continue;
            }

            string relativePath = ProjectDocumentFileStore.StoreInsideProject(
                projectPath,
                ProjectDocumentCategories.Portfolio,
                pdfPath);
            string title = (string.IsNullOrWhiteSpace(entry.Name)
                ? entry.Number
                : entry.Name).Trim();
            int sourcePageNumber = manifest.SchemaVersion >= 5
                ? Math.Max(1, entry.PdfPageNumber)
                : 1;
            if (item is null)
            {
                project.Portfolio.Items.Add(new ProjectPortfolioItem
                {
                    Order = project.Portfolio.Items.Count + 1,
                    Kind = ProjectPortfolioItemKinds.CadPage,
                    // The page carries its own 10 mm margin; Contain would
                    // frame that margin a second time.
                    Layout = ProjectPortfolioLayouts.FullBleed,
                    Title = title,
                    RelativePath = relativePath,
                    SourcePageNumber = sourcePageNumber,
                    SourceSheetKey = key,
                    SourceExportedAtUtc = manifest.ExportedAtUtc,
                });
                createdItemCount++;
            }
            else
            {
                // Content and naming follow the source; order, caption, layout
                // and focal point are the user's curation and stay untouched.
                item.Title = title;
                item.RelativePath = relativePath;
                item.SourcePageNumber = sourcePageNumber;
                item.SourceExportedAtUtc = manifest.ExportedAtUtc;
                updatedItemCount++;
            }
        }

        if (createdItemCount > 0 || updatedItemCount > 0)
        {
            project.Portfolio.Normalize();
        }
        return new PortfolioSheetImportResult(createdItemCount, updatedItemCount);
    }
}
