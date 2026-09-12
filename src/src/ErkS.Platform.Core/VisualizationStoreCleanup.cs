namespace ErkS.Platform.Core;

/// <summary>
/// What a sweep of the visualisation store would remove, and why it might refuse.
/// </summary>
/// <param name="OrphanRelativePaths">Files no record points at any more.</param>
/// <param name="KeptCount">Files a record still points at.</param>
/// <param name="RefusalMn">
/// Why nothing will be removed, in the reader's language; empty when the sweep is
/// safe to run.
/// </param>
public sealed record VisualizationStoreSweep(
    IReadOnlyList<string> OrphanRelativePaths,
    int KeptCount,
    string RefusalMn)
{
    public bool WillRemoveAnything => RefusalMn.Length == 0 && OrphanRelativePaths.Count > 0;
}

/// <summary>
/// Which copies in the visualisation store are no longer referenced.
///
/// 🔴 KEYED BY REFERENCE, NOT BY AGE. The store names each file by the SHA-256 of
/// its content, so overwriting a render produces a NEW file and leaves the old one
/// behind - and the owner's way of working is to overwrite renders repeatedly:
/// «тэр хангалтгүй хэмжээнд байгаа зурагнуудаа сайжруулсаар байх болно». At 26
/// images of about 68 MB each, every round adds roughly 1.8 GB. An age rule would
/// answer wrongly in both directions: it would delete a copy still on the sheet
/// because it was made last month, and keep this morning's orphan.
///
/// 🔴 AND THIS EXISTS BECAUSE THE OTHER FIX CREATED THE NEED. Until the album
/// started noticing overwritten renders, that growth was slow - the new files were
/// never even made. The fix that made the owner's iteration work is exactly what
/// makes this necessary, which is why they belong together.
///
/// 🔴 IT DELETES NOTHING ON AN EMPTY REFERENCE SET. «No record points at anything»
/// is far more often a record that failed to load than a project that genuinely
/// orphaned every file - and one wrong answer here is the owner's renders gone.
/// The refusal has a sentence rather than being a silent no-op.
/// </summary>
public static class VisualizationStoreCleanup
{
    /// <summary>
    /// The refusal, in the owner's language. Public because it is shown to them -
    /// a refusal nobody can read is a silent no-op with extra steps.
    /// </summary>
    public const string NothingReferencedMn =
        "Төслийн бүртгэлд ямар ч зураг заагдаагүй тул цэвэрлэгээ хийгдсэнгүй — " +
        "бүртгэл уншигдаагүй байж магадгүй.";

    /// <summary>
    /// Which of <paramref name="presentRelativePaths"/> nothing refers to.
    /// </summary>
    /// <param name="presentRelativePaths">What the store folder holds.</param>
    /// <param name="referencedRelativePaths">
    /// Every path any record points at - INCLUDING images excluded from the
    /// pages. «Excluded» keeps the file and only drops it from the layout, so an
    /// exclusion that deleted the copy would destroy work the owner deliberately
    /// set aside.
    /// </param>
    public static VisualizationStoreSweep Plan(
        IReadOnlyList<string>? presentRelativePaths,
        IReadOnlyList<string>? referencedRelativePaths)
    {
        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in referencedRelativePaths ?? [])
        {
            string normalised = Normalise(path);
            if (normalised.Length > 0)
                referenced.Add(normalised);
        }

        var present = new List<string>();
        foreach (string path in presentRelativePaths ?? [])
        {
            string normalised = Normalise(path);
            if (normalised.Length > 0)
                present.Add(normalised);
        }

        if (present.Count == 0)
            return new VisualizationStoreSweep([], 0, "");

        // 🔴 THE REFUSAL, AND IT COMES BEFORE ANY ARITHMETIC. A project whose
        // visualisation source belongs to another project id answers with an EMPTY
        // image list rather than an error - so «nothing referenced» is reachable
        // from a perfectly ordinary mismatch, and on that answer a sweep would
        // remove every render the owner has.
        if (referenced.Count == 0)
            return new VisualizationStoreSweep([], present.Count, NothingReferencedMn);

        var orphans = new List<string>();
        var kept = 0;
        foreach (string path in present)
        {
            if (referenced.Contains(path))
                kept++;
            else
                orphans.Add(path);
        }

        return new VisualizationStoreSweep(orphans, kept, "");
    }

    /// <summary>
    /// One spelling for one file. The store writes forward slashes into the record
    /// and Windows hands back backslashes from the folder, so comparing them raw
    /// would call every present file an orphan - and delete the lot.
    /// </summary>
    private static string Normalise(string? relativePath) =>
        (relativePath ?? "")
            .Trim()
            .Replace('\\', '/')
            .TrimStart('/');
}
