using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// A source nothing is watching is NAMED, instead of reading as «nothing was lost».
///
/// 🔴 THE HOLE IN THE INSTRUMENT SHIPPED THE DAY BEFORE. Dropped, unattributed and
/// rejected counts were wired to the owner's status line so a silent loss would say so.
/// Every one of them is produced by a scan, and every scan in this product iterates the
/// WATCHER table - `Rescan` walks `watchers.Values`, and the manual `RescanFolders` only
/// filters that same list. A source whose folder was never registered is therefore
/// invisible to both, and all three counters come back zero for it.
///
/// So «zero, zero, zero» reads as «nothing was lost» and equally means «nothing was
/// looked at». A reading that cannot separate those is worse than none, because it
/// reassures. This is the reading that separates them.
///
/// ⚠ IT DOES NOT SAY WHY. Authorisation, a missing folder and a failed watch all arrive
/// as the same answer. What it establishes is the fact the counters cannot: this source
/// is dark.
/// </summary>
public sealed class ADARKSourceIsNamedNotCountedAsZeroTests
{
    private static string Folder(string name) =>
        Path.Combine(Path.GetTempPath(), "erks-watch-coverage", name, "deliveries");

    [Fact]
    public void ASOURCEWhoseInboxIsNotWatchedIsNamed()
    {
        IReadOnlyList<string> dark = StudioSourceWatchCoverage.UnwatchedSourceNames(
            [
                new WatchedSourceCandidate("AutoCAD - Layout", Folder("autocad")),
                new WatchedSourceCandidate("Revit - Архитектур", Folder("revit")),
            ],
            [Folder("revit")]);

        Assert.Equal(["AutoCAD - Layout"], dark);
    }

    [Fact]
    public void EVERYSourceWatchedMeansNothingToSay()
    {
        // The positive control for a rule whose ordinary answer is an empty list: it
        // must be capable of returning one, and capable of not returning one.
        Assert.Empty(StudioSourceWatchCoverage.UnwatchedSourceNames(
            [new WatchedSourceCandidate("AutoCAD - Layout", Folder("autocad"))],
            [Folder("autocad")]));
    }

    [Fact]
    public void THEComparisonMatchesHowTheINTAKEKeysItsOwnTable()
    {
        // 🔴 A RULE THAT NORMALISED DIFFERENTLY FROM THE THING IT AUDITS WOULD REPORT
        // HEALTHY SOURCES AS DARK, which is the fastest way to teach the owner to ignore
        // a new warning. The intake keys watchers OrdinalIgnoreCase on the folder, so
        // case and a trailing separator must not make a watched source look unwatched.
        string folder = Folder("autocad");

        Assert.Empty(StudioSourceWatchCoverage.UnwatchedSourceNames(
            [new WatchedSourceCandidate("AutoCAD - Layout", folder.ToUpperInvariant())],
            [folder]));

        Assert.Empty(StudioSourceWatchCoverage.UnwatchedSourceNames(
            [new WatchedSourceCandidate("AutoCAD - Layout", folder + Path.DirectorySeparatorChar)],
            [folder]));

        // And a relative spelling of the same place is the same place.
        Assert.Empty(StudioSourceWatchCoverage.UnwatchedSourceNames(
            [new WatchedSourceCandidate("AutoCAD - Layout", Path.Combine(folder, "..", "deliveries"))],
            [folder]));
    }

    [Fact]
    public void ASOURCEWithNoInboxAtAllIsNotCalledUnwatched()
    {
        // ⚠ TRUE AND USELESS IS STILL USELESS. A source that was never given a folder has
        // nothing to watch; reporting it here would put a permanent line on the screen
        // about a different problem, owned by whoever configures sources.
        Assert.Empty(StudioSourceWatchCoverage.UnwatchedSourceNames(
            [
                new WatchedSourceCandidate("Not configured", null),
                new WatchedSourceCandidate("Blank", "   "),
            ],
            []));
    }

    [Fact]
    public void AMALFORMEDPathIsTreatedAsNoPathRatherThanThrowing()
    {
        // ⚠ THE DIAGNOSTIC MUST NOT BECOME THE OUTAGE. This runs inside the scan report;
        // an exception here would take down the very message it exists to add.
        //
        // 🔴 THE FIRST FIXTURE HERE WAS WRONG AND THE TEST CAUGHT IT. It used a
        // 500-character path, assuming that throws - on this runtime it does not, long
        // paths expand fine, so the source was correctly reported as dark and the
        // assertion failed. My fixture was wrong, not the rule. A path holding a NUL is
        // what actually throws ArgumentException, and that is measured in this repository
        // rather than assumed.
        IReadOnlyList<string> dark = StudioSourceWatchCoverage.UnwatchedSourceNames(
            [new WatchedSourceCandidate("Broken", "C:" + Path.DirectorySeparatorChar + "a" + (char)0 + "b")],
            []);

        Assert.Empty(dark);

        // And the positive control the first version accidentally discovered: a LONG but
        // legal path is not malformed, so it is named like any other unwatched source.
        Assert.Single(StudioSourceWatchCoverage.UnwatchedSourceNames(
            [new WatchedSourceCandidate("Long but legal", Path.Combine(Path.GetTempPath(), new string('x', 200)))],
            []));
    }

    [Fact]
    public void NOWatchedFoldersAtAllMakesEVERYConfiguredSourceDark()
    {
        // 🔴 THE CASE THIS WAS BUILT FOR. When an identity change resets the watchers and
        // nothing re-registers them, the project still lists its sources and the scan
        // still reports zero of everything. This is the sentence that distinguishes that
        // state from a healthy one.
        IReadOnlyList<string> dark = StudioSourceWatchCoverage.UnwatchedSourceNames(
            [
                new WatchedSourceCandidate("AutoCAD - Layout", Folder("autocad")),
                new WatchedSourceCandidate("Revit - Архитектур", Folder("revit")),
            ],
            null);

        Assert.Equal(2, dark.Count);
        Assert.Contains("AutoCAD - Layout", dark);
        Assert.Contains("Revit - Архитектур", dark);
    }

    [Fact]
    public void ANUNNAMEDSourceFallsBackToItsFolderSoTheLineStaysActionable()
    {
        // «1 source is not being watched» with no way to tell which one sends the reader
        // looking through every source they have.
        IReadOnlyList<string> dark = StudioSourceWatchCoverage.UnwatchedSourceNames(
            [new WatchedSourceCandidate("  ", Folder("autocad"))],
            []);

        Assert.Single(dark);
        Assert.Contains("autocad", dark[0], StringComparison.OrdinalIgnoreCase);
    }
}
