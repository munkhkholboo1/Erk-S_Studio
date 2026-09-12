using System.IO;
using ErkS.Platform.Core;

namespace ErkS.Studio;

/// <summary>What a sweep of the project's visualisation store actually did.</summary>
/// <param name="RemovedCount">Copies deleted.</param>
/// <param name="RemovedBytes">How much came back.</param>
/// <param name="KeptCount">Copies a record still points at.</param>
/// <param name="RefusalMn">Why nothing was removed; empty when the sweep ran.</param>
/// <param name="ConsideredCount">
/// How many files the sweep LOOKED AT.
///
/// 🔴 THE SCOPE HAD TO BECOME OBSERVABLE. Two guards protect the outcome here -
/// «enumerate only the store» and «check IsInside before deleting» - and sabotage
/// showed each one masking the removal of the other: widen the walk to the whole
/// project folder and the inside-check still saves the day, so nothing goes red.
/// Defence in depth is right for deletion code, but it left the SCOPE unpinned.
/// Counting what was examined is what makes a widened walk visible.
/// </param>
internal sealed record VisualizationStoreSweepResult(
    int RemovedCount,
    long RemovedBytes,
    int KeptCount,
    string RefusalMn,
    int ConsideredCount = 0);

/// <summary>
/// Removes visualisation copies no record points at any more.
///
/// 🔴 THE NEED WAS CREATED BY THE FIX BESIDE IT. The store names each copy by the
/// SHA-256 of its content, so overwriting a render writes a NEW file and leaves the
/// old one - and the owner overwrites renders as a matter of course: «тэр
/// хангалтгүй хэмжээнд байгаа зурагнуудаа сайжруулсаар байх болно». Until the album
/// began noticing those overwrites the growth never happened, because the new
/// copies were never made. Making their iteration work is what made this
/// necessary, so the two belong in the same breath.
///
/// 🔴 THE DISCIPLINE IS BORROWED FROM <see cref="CloudAlbumCacheMaintenance"/>,
/// which keeps only what the current build points at: every path is checked to be
/// INSIDE the folder being swept before it is touched, and every filesystem
/// failure is an answer rather than an exception. What is NOT borrowed is its
/// question - that one keeps a single current file, this one keeps a set.
///
/// 🔴 AND IT DECIDES NOTHING ITSELF. Which copies are orphans is
/// <see cref="VisualizationStoreCleanup"/>'s rule, in Core, with its own tests -
/// including the refusal that makes an empty reference set remove nothing. A
/// deletion whose rule lives inside the code that deletes is a deletion nobody can
/// check.
/// </summary>
internal static class StudioVisualizationStoreMaintenance
{
    private static readonly string[] StoreSegments = ["sources", "visualizations", "images"];

    /// <summary>
    /// Sweeps the store. Runs AFTER reconciliation, because until the record has
    /// been repointed the old copy is still the referenced one - swept before, the
    /// sweep would find no orphan and the new copy would not exist yet.
    /// </summary>
    public static VisualizationStoreSweepResult Sweep(
        ProjectWorkspace project,
        string projectPath)
    {
        ArgumentNullException.ThrowIfNull(project);

        string storeFolder;
        try
        {
            storeFolder = Path.Combine(
                ProjectWorkspacePaths.GetProjectFolder(projectPath),
                Path.Combine(StoreSegments));
        }
        catch (Exception exception) when (IsFileTrouble(exception))
        {
            // A tidy-up that throws would take the album build down with it. The
            // store simply goes unswept, which costs disk and nothing else.
            return new VisualizationStoreSweepResult(0, 0, 0, "");
        }

        // 🔴 EVERY RECORD'S PATH, NOT THE PROJECT-FILTERED VIEW.
        // ImagesForProject returns an EMPTY list when the source carries another
        // project's id - an ordinary mismatch, not an error - and on that answer a
        // sweep keyed to it would orphan every copy the owner has. The store folder
        // is per project, so every record in this source belongs to this store.
        List<string> referenced = project.Visualizations.Images
            .Where(image => !string.IsNullOrWhiteSpace(image.RelativePath))
            .Select(image => image.RelativePath)
            .ToList();

        return SweepFolder(projectPath, storeFolder, referenced);
    }

    /// <summary>
    /// Deletes everything in <paramref name="folder"/> that
    /// <paramref name="referenced"/> does not name.
    ///
    /// 🔴 ONE EXECUTOR, TWO REFERENCE SETS - THE OPERATION IS SHARED, THE
    /// QUESTION IS NOT. The payload store is kept against the project's records; the
    /// prepared cache is kept against what the current album composition actually
    /// needs. Copying this loop for the second caller is how one copy keeps the
    /// IsInside re-check and the other quietly loses it - and both of them delete.
    ///
    /// 🔴 THE RULE STILL LIVES IN CORE. Which files are orphans, and the refusal
    /// on an empty reference set, are <see cref="VisualizationStoreCleanup"/>'s and
    /// have their own tests. This is only the part that touches the disk.
    /// </summary>
    internal static VisualizationStoreSweepResult SweepFolder(
        string projectPath,
        string folder,
        IReadOnlyList<string> referenced)
    {
        List<string> present;
        try
        {
            if (!Directory.Exists(folder))
                return new VisualizationStoreSweepResult(0, 0, 0, "");

            present = Directory
                .EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly)
                .Select(path => ProjectWorkspacePaths.ToRelativePath(projectPath, path))
                .ToList();
        }
        catch (Exception exception) when (IsFileTrouble(exception))
        {
            return new VisualizationStoreSweepResult(0, 0, 0, "");
        }

        VisualizationStoreSweep plan = VisualizationStoreCleanup.Plan(present, referenced);
        if (!plan.WillRemoveAnything)
        {
            return new VisualizationStoreSweepResult(
                0,
                0,
                plan.KeptCount,
                plan.RefusalMn,
                present.Count);
        }

        var removed = 0;
        long bytes = 0;
        foreach (string relative in plan.OrphanRelativePaths)
        {
            try
            {
                string fullPath = Path.GetFullPath(
                    Path.Combine(ProjectWorkspacePaths.GetProjectFolder(projectPath), relative));

                // 🔴 CHECKED AGAIN AT THE MOMENT OF DELETION. The plan works on
                // relative strings; this is the only place that knows they resolve
                // where they should, and a record holding «..\..\something» must not
                // be able to reach out of the folder being swept.
                if (!ProjectWorkspacePaths.IsInside(folder, fullPath) ||
                    !File.Exists(fullPath))
                {
                    continue;
                }

                long size = new FileInfo(fullPath).Length;
                File.Delete(fullPath);
                removed++;
                bytes += size;
            }
            catch (Exception exception) when (IsFileTrouble(exception))
            {
                // One file that will not go is not a reason to stop: the rest are
                // just as orphaned, and a locked file comes back next time.
            }
        }

        return new VisualizationStoreSweepResult(
            removed,
            bytes,
            plan.KeptCount,
            "",
            present.Count);
    }

    /// <summary>
    /// What counts as «the filesystem would not cooperate» rather than a defect.
    /// One list, so the folder lookup and the per-file deletion cannot drift apart.
    /// </summary>
    private static bool IsFileTrouble(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or ArgumentException or
            NotSupportedException or InvalidDataException;
}
