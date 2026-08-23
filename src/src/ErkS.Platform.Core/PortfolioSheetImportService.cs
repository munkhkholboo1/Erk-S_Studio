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
        var deliveredKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (SheetPackageEntry entry in manifest.Sheets)
        {
            if (!SheetDestinations.IsPortfolio(entry.Destination) ||
                !loadResult.TryGetVerifiedPdfPath(entry, out string pdfPath))
            {
                continue;
            }

            string key = SheetRecord.MakeKey(manifest.Source, entry, sourceId);
            deliveredKeys.Add(key);
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
            // A page the user took out stays out. Its record is kept up to date
            // so restoring it later brings back the current drawing, but it is
            // not counted as arriving and does not reappear on its own.
            bool userRemovedThisPage = item?.IsRemoved == true;

            string relativePath = ProjectDocumentFileStore.StoreInsideProject(
                projectPath,
                ProjectDocumentCategories.Portfolio,
                pdfPath);
            string sourceTitle = (string.IsNullOrWhiteSpace(entry.Name)
                ? entry.Number
                : entry.Name).Trim();
            string sourceCaption = (entry.SheetDescription ?? "").Trim();
            int sourcePageNumber = manifest.SchemaVersion >= 5
                ? Math.Max(1, entry.PdfPageNumber)
                : 1;
            if (item is null)
            {
                project.Portfolio.Items.Add(new ProjectPortfolioItem
                {
                    Order = project.Portfolio.Items.Count + 1,
                    Kind = ProjectPortfolioItemKinds.CadPage,
                    // The page carries its own 10 mm margin, so Contain would
                    // frame that margin a second time - and FullBleed would cut
                    // drawing off the edges whenever the portfolio page is a
                    // different shape. Fitted to the edge it keeps both.
                    Layout = ProjectPortfolioLayouts.FitPage,
                    Title = sourceTitle,
                    SourceTitle = sourceTitle,
                    // The description the page was authored with becomes the
                    // caption it starts life with.
                    Caption = sourceCaption,
                    SourceCaption = sourceCaption,
                    RelativePath = relativePath,
                    SourcePageNumber = sourcePageNumber,
                    SourceSheetKey = key,
                    SourceExportedAtUtc = manifest.ExportedAtUtc,
                });
                createdItemCount++;
            }
            else
            {
                // Content follows the source; order, caption, layout and focal
                // point are the user's curation and stay untouched. So is the
                // title once the user has changed it - a name they chose is not
                // overwritten by the drawing's own, while a name they left alone
                // keeps following it.
                if (string.Equals(item.Title, item.SourceTitle, StringComparison.Ordinal) ||
                    string.IsNullOrWhiteSpace(item.Title))
                {
                    item.Title = sourceTitle;
                }
                item.SourceTitle = sourceTitle;
                // The caption follows the same rule as the title: a description
                // written at the source reaches a page nobody has captioned -
                // including one imported before descriptions existed - but a
                // caption the user wrote, or deliberately cleared, is theirs.
                if (string.Equals(item.Caption, item.SourceCaption, StringComparison.Ordinal))
                {
                    item.Caption = sourceCaption;
                }
                item.SourceCaption = sourceCaption;
                item.MissingFromSourceSinceUtc = null;
                item.RelativePath = relativePath;
                item.SourcePageNumber = sourcePageNumber;
                item.SourceExportedAtUtc = manifest.ExportedAtUtc;
                if (!userRemovedThisPage)
                    updatedItemCount++;
            }
        }

        // Only a full snapshot is the complete current set for its source, so
        // only a full snapshot can show that a page is no longer offered.
        if (manifest.PackageScope == SheetPackageScope.FullSnapshot)
        {
            MarkPagesTheSourceNoLongerOffers(
                project,
                manifest,
                sourceId,
                deliveredKeys);
        }

        if (createdItemCount > 0 || updatedItemCount > 0)
        {
            project.Portfolio.Normalize();
            // A changed page leaves its previous file behind, unreferenced.
            PortfolioStorageMaintenance.RemoveUnreferencedFiles(project, projectPath);
        }
        return new PortfolioSheetImportResult(createdItemCount, updatedItemCount);
    }

    /// <summary>
    /// Records which imported pages this source stopped offering. Nothing is
    /// deleted: the page simply says it is no longer in the drawing it came
    /// from, so its presence in the portfolio is explained rather than puzzling.
    /// </summary>
    private static void MarkPagesTheSourceNoLongerOffers(
        ProjectWorkspace project,
        SheetPackageManifest manifest,
        string? sourceId,
        IReadOnlySet<string> deliveredKeys)
    {
        foreach (ProjectPortfolioItem item in project.Portfolio.Items)
        {
            if (!item.Kind.Equals(
                    ProjectPortfolioItemKinds.CadPage,
                    StringComparison.OrdinalIgnoreCase) ||
                item.SourceSheetKey.Length == 0 ||
                deliveredKeys.Contains(item.SourceSheetKey) ||
                !SheetRecord.BelongsToSource(item.SourceSheetKey, manifest.Source, sourceId))
            {
                continue;
            }

            item.MissingFromSourceSinceUtc ??= manifest.ExportedAtUtc;
        }
    }
}
