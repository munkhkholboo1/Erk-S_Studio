using System.IO;
using ErkS.Platform.Core;
using ErkS.Platform.Pdf;

namespace ErkS.Studio;

/// <summary>What one board preparation pass did.</summary>
/// <param name="PreparedCount">Copies written by this pass.</param>
/// <param name="ReusedCount">Copies a previous pass had already written.</param>
/// <param name="AlreadyCoarseCount">Drawings already at or below the rule.</param>
/// <param name="FailedCount">
/// Drawings that go on at source size because preparing them failed. Same policy as
/// the album and the portfolio: one unreadable file must not cost the board.
/// </param>
/// <param name="VectorCount">
/// Cards that carry no raster to reduce - a PDF page placed as a form, or a plan drawn
/// from its own classification. Counted so «nothing was prepared» can be told from
/// «nothing needed preparing».
/// </param>
/// <param name="OffGridCount">
/// Cards the grid refuses. The writer warns and draws nothing, so there is no frame to
/// measure and nothing on the sheet that could be soft.
/// </param>
/// <param name="RemovedCount">Superseded prepared copies deleted.</param>
/// <param name="RemovedBytes">How much disk those gave back.</param>
internal sealed record BoardRasterPreparation(
    int PreparedCount,
    int ReusedCount,
    int AlreadyCoarseCount,
    int FailedCount,
    int VectorCount,
    int OffGridCount,
    int RemovedCount,
    long RemovedBytes)
{
    internal static BoardRasterPreparation Nothing { get; } = new(0, 0, 0, 0, 0, 0, 0, 0);
}

/// <summary>
/// Brings a board's cards down to the owner's raster rule before export.
///
/// 🔴 THE THIRD WRITER, AND THE BOARD ALREADY HELD HALF THE RULE. 300 DPI has been in
/// <see cref="BoardCardMeasurements"/> since the boards were built - but only as a
/// FLOOR, asked of the person preparing artwork by hand: «has this render enough
/// pixels». Nothing asked whether it had too many, so a 16k render placed on a card
/// went onto the sheet whole. Same density, opposite direction, one direction missing.
///
/// 🔴 THE CROP IS THIS WRITER'S OWN HAZARD. A card shows a FRACTION of its source, so
/// the whole source is drawn 1/crop times larger than the card. Measuring the card and
/// stopping there would leave every cropped card at a fraction of the density it needs
/// - and softness on a sheet printed a metre across is visible to everyone in front of
/// it. <see cref="BoardRasterBudget"/> carries the crop; this pass hands it over.
///
/// 🔴 ITS OWN FOLDER, AND THAT IS NOT TIDINESS. Each sweep deletes what its own
/// reference set does not name, and there are now three sets: the album's built from
/// its pages, the portfolio's from its items, this one from its cards. A shared folder
/// would make every pass an orphan-killer for the other two, re-encoding on every
/// export for ever - the defect the album's «do not delete a live cache entry» test was
/// written against, now reachable from two sides instead of one.
///
/// ⚠ ONLY CONTENT CARDS. A legend, a north arrow and a scale bar are drawn from what
/// the plan card turned out to be, and the writer never sends them to the raster path;
/// swapping their source would change a card nobody places.
/// </summary>
internal static class StudioBoardRasterPreparer
{
    /// <summary>Where prepared board copies live - beside the other two, not in them.</summary>
    internal static readonly string[] PreparedSegments = ["sources", "boards", "prepared"];

    /// <summary>
    /// Returns the boards to build with, each raster card pointed at a prepared copy
    /// where the rule asks for one, and says what the pass did.
    /// </summary>
    public static (IReadOnlyList<BoardBuildBoard> Boards, BoardRasterPreparation Did) Prepare(
        IReadOnlyList<BoardBuildBoard> boards,
        string? projectPath,
        BoardGrid grid,
        double boardWidthMm,
        double boardHeightMm)
    {
        if (boards is null || boards.Count == 0 ||
            string.IsNullOrWhiteSpace(projectPath) || grid is null)
        {
            return (boards ?? [], BoardRasterPreparation.Nothing);
        }

        string preparedFolder;
        try
        {
            preparedFolder = Path.Combine(
                ProjectWorkspacePaths.GetProjectFolder(projectPath),
                Path.Combine(PreparedSegments));
        }
        catch (Exception exception) when (IsFileTrouble(exception))
        {
            return (boards, BoardRasterPreparation.Nothing);
        }

        var builtBoards = new List<BoardBuildBoard>(boards.Count);
        var inUse = new List<string>();
        var prepared = 0;
        var reused = 0;
        var coarse = 0;
        var failed = 0;
        var vector = 0;
        var offGrid = 0;

        foreach (BoardBuildBoard board in boards)
        {
            var cards = new List<BoardBuildCard>(board.Cards.Count);
            foreach (BoardBuildCard card in board.Cards)
            {
                cards.Add(PrepareCard(card));
            }

            builtBoards.Add(board with { Cards = cards });
        }

        VisualizationStoreSweepResult sweep = prepared > 0
            ? StudioVisualizationStoreMaintenance.SweepFolder(projectPath, preparedFolder, inUse)
            : new VisualizationStoreSweepResult(0, 0, 0, "");

        return (
            builtBoards,
            new BoardRasterPreparation(
                prepared,
                reused,
                coarse,
                failed,
                vector,
                offGrid,
                sweep.RemovedCount,
                sweep.RemovedBytes));

        BoardBuildCard PrepareCard(BoardBuildCard card)
        {
            if (!card.Kind.Equals(BoardBuildCardKinds.Content, StringComparison.Ordinal))
                return card;

            // A plan card is drawn from its classification, not placed as a picture, so
            // there is no raster to reduce and no file to swap.
            if (!string.IsNullOrWhiteSpace(card.PlanPath))
            {
                vector++;
                return card;
            }

            string path = (card.SourcePath ?? "").Trim();
            if (path.Length == 0 || !File.Exists(path))
                return card;

            if (Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                vector++;
                return card;
            }

            // The frame, resolved the way the writer resolves it - the same Core
            // function, given the same span and the same size override.
            BoardRectMm? cell = BoardCardGeometry.Resolve(
                grid,
                boardWidthMm,
                boardHeightMm,
                new BoardElement
                {
                    Column = card.Column,
                    ColumnSpan = card.ColumnSpan,
                    Row = card.Row,
                    RowSpan = card.RowSpan,
                    WidthMm = card.WidthMm,
                    HeightMm = card.HeightMm,
                });
            if (cell is not { } frame)
            {
                offGrid++;
                return card;
            }

            try
            {
                (int sourceWidth, int sourceHeight) =
                    StudioVisualizationRasterPreparer.ReadPixelSizeForSharing(path);
                VisualizationRasterPlan plan = BoardRasterBudget.For(
                    frame,
                    hasCaption: !string.IsNullOrWhiteSpace(card.Caption),
                    card.Layout,
                    card.CropWidth,
                    card.CropHeight,
                    sourceWidth,
                    sourceHeight);
                if (!plan.Resample)
                {
                    coarse++;
                    return card;
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
                if (existed)
                    reused++;
                else
                    prepared++;
                return card with { SourcePath = preparedPath };
            }
            catch (Exception exception) when (IsFileTrouble(exception))
            {
                // The drawing goes on at source size: a heavier board, not a missing
                // card and not a failed export.
                failed++;
                return card;
            }
        }
    }

    private static bool IsFileTrouble(Exception exception) =>
        StudioVisualizationRasterPreparer.IsFileTroubleForSharing(exception);
}
