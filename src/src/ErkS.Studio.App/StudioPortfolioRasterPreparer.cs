using System.IO;
using ErkS.Platform.Core;
using ErkS.Platform.Pdf;

namespace ErkS.Studio;

/// <summary>What one portfolio preparation pass did.</summary>
/// <param name="PreparedCount">Copies written by this pass.</param>
/// <param name="ReusedCount">Copies a previous pass had already written.</param>
/// <param name="AlreadyCoarseCount">Drawings already at or below the rule.</param>
/// <param name="FailedCount">
/// Drawings that go in at source size because preparing them failed. Same policy as
/// the album: one unreadable file must not cost the export.
/// </param>
/// <param name="VectorCount">
/// PDF pages, which are placed as forms and stay vector. Counted so «nothing was
/// prepared» can be told from «nothing needed preparing».
/// </param>
/// <param name="RemovedCount">Superseded prepared copies deleted.</param>
/// <param name="RemovedBytes">How much disk those gave back.</param>
internal sealed record PortfolioRasterPreparation(
    int PreparedCount,
    int ReusedCount,
    int AlreadyCoarseCount,
    int FailedCount,
    int VectorCount,
    int RemovedCount,
    long RemovedBytes)
{
    internal static PortfolioRasterPreparation Nothing { get; } = new(0, 0, 0, 0, 0, 0, 0);
}

/// <summary>
/// Brings the portfolio's drawings down to the album's raster rule before export.
///
/// 🔴 THIS IS THE HALF THE FIRST FIX MISSED. The visualisation pages were capped and
/// the portfolio was not - and the portfolio draws THE SAME FILES, because its intake
/// copies image.RelativePath straight off the owner's visualisation records. So the
/// 16k renders came back the moment they exported a portfolio, and «the raster is
/// fixed» was true of one writer out of three.
///
/// 🔴 THE RULE IS THE OWNER'S, NOT THE ALBUM'S: «яг зөв растерийн дүрэм 300dpi» is a
/// rule about the product. Reading it as «raster that goes into the album» was a
/// narrowing nobody asked for, so this is the rule's reach and not an extension.
///
/// 🔴 ITS OWN FOLDER, AND THAT IS NOT TIDINESS. Each sweep deletes what its own
/// reference set does not name, and the album's set is built from the album's pages.
/// Portfolio copies living in the album's folder would be swept as orphans on the next
/// reconciling build and re-encoded on the next export, for ever - the exact defect
/// that «delete a live cache entry» test was written against, arriving from the other
/// side. Two questions, two folders, each set complete.
///
/// ⚠ PDF PAGES ARE LEFT ALONE. The writer places them as forms so they stay vector;
/// reaching the raster inside one means re-rasterising the page and turning the vector
/// around it into pixels. Counted, not touched.
/// </summary>
internal static class StudioPortfolioRasterPreparer
{
    /// <summary>Where prepared portfolio copies live - beside the album's, not in it.</summary>
    internal static readonly string[] PreparedSegments = ["sources", "portfolio", "prepared"];

    /// <summary>
    /// Returns the items to build with, each pointed at a prepared copy where the rule
    /// asks for one, and says what the pass did.
    /// </summary>
    public static (IReadOnlyList<PortfolioBuildItem> Items, PortfolioRasterPreparation Did) Prepare(
        IReadOnlyList<PortfolioBuildItem> items,
        string? projectPath,
        double pageWidthMm,
        double pageHeightMm,
        bool useSourcePageSize)
    {
        if (items is null || items.Count == 0 || string.IsNullOrWhiteSpace(projectPath))
            return (items ?? [], PortfolioRasterPreparation.Nothing);

        // ⚠ A page sized to its own drawing needs every pixel the drawing has, so there
        // is nothing to reduce. No caller turns this on today; answering «no work»
        // rather than silently capping is what keeps that true if one ever does.
        if (useSourcePageSize)
            return (items, PortfolioRasterPreparation.Nothing);

        string preparedFolder;
        try
        {
            preparedFolder = Path.Combine(
                ProjectWorkspacePaths.GetProjectFolder(projectPath),
                Path.Combine(PreparedSegments));
        }
        catch (Exception exception) when (IsFileTrouble(exception))
        {
            return (items, PortfolioRasterPreparation.Nothing);
        }

        var built = new List<PortfolioBuildItem>(items.Count);
        var inUse = new List<string>();
        var prepared = 0;
        var reused = 0;
        var coarse = 0;
        var failed = 0;
        var vector = 0;

        foreach (PortfolioBuildItem item in items)
        {
            string path = (item.SourcePath ?? "").Trim();
            if (path.Length == 0 || !File.Exists(path))
            {
                built.Add(item);
                continue;
            }

            if (Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                vector++;
                built.Add(item);
                continue;
            }

            try
            {
                (int sourceWidth, int sourceHeight) = ReadPixelSize(path);
                VisualizationRasterPlan plan = PortfolioRasterBudget.For(
                    pageWidthMm,
                    pageHeightMm,
                    item.Layout,
                    hasCaption: !string.IsNullOrWhiteSpace(item.Caption),
                    sourceWidth,
                    sourceHeight);
                if (!plan.Resample)
                {
                    coarse++;
                    built.Add(item);
                    continue;
                }

                string preparedPath = Path.Combine(
                    preparedFolder,
                    StudioVisualizationRasterPreparer.PreparedFileName(null, path, plan));
                bool existed = File.Exists(preparedPath);
                if (!existed)
                {
                    Directory.CreateDirectory(preparedFolder);
                    StudioVisualizationRasterPreparer.WritePrepared(path, preparedPath, plan);
                }

                inUse.Add(ProjectWorkspacePaths.ToRelativePath(projectPath, preparedPath));
                built.Add(item with { SourcePath = preparedPath });
                if (existed)
                    reused++;
                else
                    prepared++;
            }
            catch (Exception exception) when (IsFileTrouble(exception))
            {
                // The drawing goes in at source size: a heavier portfolio, not a
                // missing page and not a failed export.
                failed++;
                built.Add(item);
            }
        }

        VisualizationStoreSweepResult sweep = prepared > 0
            ? StudioVisualizationStoreMaintenance.SweepFolder(projectPath, preparedFolder, inUse)
            : new VisualizationStoreSweepResult(0, 0, 0, "");

        return (
            built,
            new PortfolioRasterPreparation(
                prepared,
                reused,
                coarse,
                failed,
                vector,
                sweep.RemovedCount,
                sweep.RemovedBytes));
    }

    /// <summary>
    /// The drawing's own pixel count, read from its header.
    ///
    /// 🔴 READ FROM THE FILE, NOT FROM A RECORD. The album's pass has records that
    /// declare PixelWidth; a portfolio item is a path and a layout, and an album-page
    /// item is a rendered preview nobody has inspected. Asking the file is the only
    /// answer available to all of them - and it is the answer the writer itself uses.
    /// </summary>
    private static (int Width, int Height) ReadPixelSize(string path)
    {
        using FileStream input = File.OpenRead(path);
        System.Windows.Media.Imaging.BitmapFrame frame =
            System.Windows.Media.Imaging.BitmapDecoder.Create(
                input,
                System.Windows.Media.Imaging.BitmapCreateOptions.DelayCreation,
                System.Windows.Media.Imaging.BitmapCacheOption.None).Frames[0];
        return (frame.PixelWidth, frame.PixelHeight);
    }

    private static bool IsFileTrouble(Exception exception) =>
        StudioVisualizationRasterPreparer.IsFileTroubleForSharing(exception);
}
