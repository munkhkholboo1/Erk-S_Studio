namespace ErkS.Platform.Core;

/// <summary>One configured source, reduced to what this rule needs.</summary>
/// <param name="Name">What to call it in a sentence the owner reads.</param>
/// <param name="InboxFolder">Where its deliveries land.</param>
public readonly record struct WatchedSourceCandidate(string Name, string? InboxFolder);

/// <summary>
/// Which configured sources nothing is actually watching.
///
/// 🔴 EVERY SCAN IN THIS PRODUCT ITERATES THE WATCHER LIST, NOT THE PROJECT. Both
/// `Rescan` and the manual `RescanFolders` walk `watchers.Values`; the manual one merely
/// filters that list further. So a source whose folder was never registered is invisible
/// to BOTH - and every counter comes back zero for it, including the dropped-delivery
/// counts added on 2026-09-17.
///
/// 🔴 WHICH MAKES «ALL ZERO» AMBIGUOUS, AND THAT AMBIGUITY WAS REPORTED AS AN INSTRUMENT.
/// Zero dropped, zero unattributed and zero rejected reads as «nothing was lost». It
/// equally means «nothing was scanned». A reading that cannot tell those apart is worse
/// than no reading, because it is quietly reassuring - so the thing that separates them
/// is computed here and said out loud.
///
/// ⚠ THIS DOES NOT SAY WHY a source is unwatched. Authorisation, a missing folder and a
/// failed watch all land in the same answer, and each has its own report elsewhere. What
/// it establishes is the fact the other counters cannot: this source is dark.
/// </summary>
public static class StudioSourceWatchCoverage
{
    /// <summary>
    /// The configured sources whose inbox is not among the watched folders.
    ///
    /// Compared as full paths, case-insensitively, because that is how the intake keys
    /// its own watcher table - a rule that normalised differently from the thing it is
    /// auditing would report healthy sources as dark, which is the fastest way to make
    /// a new warning ignored.
    /// </summary>
    public static IReadOnlyList<string> UnwatchedSourceNames(
        IEnumerable<WatchedSourceCandidate> configured,
        IEnumerable<string>? watchedFolders)
    {
        ArgumentNullException.ThrowIfNull(configured);

        var watched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string folder in watchedFolders ?? [])
        {
            if (Normalise(folder) is { Length: > 0 } normalised)
                watched.Add(normalised);
        }

        var dark = new List<string>();
        foreach (WatchedSourceCandidate source in configured)
        {
            // ⚠ A SOURCE WITH NO INBOX AT ALL IS NOT REPORTED HERE. It has nothing to
            // watch, so calling it «unwatched» would be true and useless - it is a
            // source that was never given a folder, which is a different sentence and
            // belongs to whoever configures it.
            if (Normalise(source.InboxFolder) is not { Length: > 0 } folder)
                continue;

            if (!watched.Contains(folder))
                dark.Add(string.IsNullOrWhiteSpace(source.Name) ? folder : source.Name.Trim());
        }

        return dark;
    }

    /// <summary>
    /// A comparable spelling of a folder, or empty when it has none.
    ///
    /// ⚠ A PATH THE SYSTEM REFUSES TO EXPAND IS TREATED AS NO PATH. GetFullPath throws
    /// on a malformed or over-long value, and an audit that threw would take down the
    /// scan report it is attached to - turning a diagnostic into an outage.
    /// </summary>
    private static string Normalise(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
            return "";

        try
        {
            return Path.GetFullPath(folder.Trim()).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return "";
        }
    }
}
