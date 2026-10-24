namespace ErkS.Platform.Core;

/// <summary>
/// Where a source's native file went when its recorded path is gone - a folder renamed or
/// reorganised around it (2026-10-24: «D:\Cloud Work\TananTsamkhag\Nairamdal\*.rvt» moved to
/// «…\NairamdalApartment\RVT\» while the project kept pointing at the old place). Moving
/// folders becomes common as projects move to Erk-S Sync.
///
/// It only FINDS. A file of the same name under the nearest folder that still exists (and under
/// the project's sync folder) is a candidate; exactly one candidate is the answer, more than one
/// is a question for a person. Nothing on disk is touched - only Studio's record of the path.
/// </summary>
public static class ProjectSourceRelocation
{
    /// <summary>How many directory entries one search may look at - a guard, not a target.</summary>
    public const int MaxEntries = 200_000;

    private static readonly string[] SkippedDirectories = [".erks-sync", ".sync", "$RECYCLE.BIN", "System Volume Information", "Revit_temp"];

    /// <summary>
    /// The files that could be <paramref name="missingPath"/> now: same file name, under the nearest
    /// existing folder above it or under <paramref name="extraRoots"/>. Empty when the file is still
    /// there, nothing matches, or the only folder left to search is a drive root.
    /// </summary>
    public static IReadOnlyList<string> FindCandidates(string missingPath, IEnumerable<string>? extraRoots = null)
    {
        if (string.IsNullOrWhiteSpace(missingPath))
            return [];
        string full;
        try
        {
            full = Path.GetFullPath(missingPath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return [];
        }
        if (File.Exists(full))
            return [];
        string name = Path.GetFileName(full);
        if (name.Length == 0)
            return [];

        var roots = new List<string>();
        string? nearest = Path.GetDirectoryName(full);
        while (nearest is not null && !Directory.Exists(nearest))
            nearest = Path.GetDirectoryName(nearest);
        // A whole disk is not «near»: a same-named file anywhere on it says nothing.
        if (nearest is not null && Path.GetDirectoryName(nearest) is not null)
            roots.Add(nearest);
        foreach (string root in extraRoots ?? [])
        {
            if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root) && Path.GetDirectoryName(Path.GetFullPath(root)) is not null)
                roots.Add(Path.GetFullPath(root));
        }

        var found = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        int budget = MaxEntries;
        foreach (string root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
            Search(root, name, found, ref budget);
        return found.ToList();
    }

    private static void Search(string root, string name, SortedSet<string> found, ref int budget)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0 && budget > 0)
        {
            string directory = pending.Pop();
            IEnumerable<string> entries;
            try
            {
                entries = Directory.EnumerateFileSystemEntries(directory).ToList();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            foreach (string entry in entries)
            {
                if (--budget <= 0)
                    return;
                string entryName = Path.GetFileName(entry);
                FileAttributes attributes;
                try
                {
                    attributes = File.GetAttributes(entry);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    continue;
                }
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    continue;
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    // A central's «_backup» holds Revit's own copies, never the file a person works in.
                    if (!SkippedDirectories.Contains(entryName, StringComparer.OrdinalIgnoreCase) &&
                        !entryName.EndsWith("_backup", StringComparison.OrdinalIgnoreCase))
                    {
                        pending.Push(entry);
                    }
                }
                else if (entryName.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    found.Add(entry);
                }
            }
        }
    }
}
