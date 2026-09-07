namespace ErkS.Studio;

/// <summary>
/// What a local source refresh actually did.
///
/// 🔴 THIS EXISTS BECAUSE THE REFRESH USED TO ANSWER NOBODY. It ran, wrote a
/// sentence into the status bar and returned void, and its real work happened
/// later still - queued onto the dispatcher, finishing after the caller had
/// already moved on. A single action that reads sources and THEN sends them
/// cannot be built on that: it would report a source read that had not happened
/// and start uploading beside it.
///
/// 🔴 THE DEFAULT IS FAILURE-SHAPED ON PURPOSE. <c>default</c> is
/// <c>Ran = false, Succeeded = false</c>, so any path that forgets to build an
/// outcome reports "it did not work" rather than silently claiming success. The
/// opposite default would turn every forgotten branch into a false green - the
/// same mistake the cloud indicator is built to avoid.
/// </summary>
internal readonly record struct SourceRefreshOutcome(
    bool Ran,
    bool Succeeded,
    int CheckedCount,
    int ChangedCount,
    string FailureMn)
{
    /// <summary>The refresh was refused before it began - busy, no project, no permission.</summary>
    public static SourceRefreshOutcome Blocked(string reasonMn) =>
        new(false, false, 0, 0, reasonMn);

    public static SourceRefreshOutcome Failed(string reasonMn) =>
        new(true, false, 0, 0, reasonMn);

    /// <summary>
    /// It ran. <paramref name="checkedCount"/> is how many packages were
    /// examined and <paramref name="changedCount"/> how many actually changed -
    /// both real counts from the scan, not inferred from what the album looks
    /// like afterwards.
    /// </summary>
    public static SourceRefreshOutcome Completed(int checkedCount, int changedCount) =>
        new(true, true, checkedCount, changedCount, "");
}
