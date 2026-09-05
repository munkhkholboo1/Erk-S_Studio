using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// The catalogue and the open flow must agree about which folder IS a project.
/// They did not, and the disagreement is what made the folder holding every
/// source disappear from the project list behind an empty twin.
/// </summary>
public sealed class ProjectFolderRankTests
{
    private const string Root = @"C:\Projects";

    private static LocalProjectFolder Folder(string name, int sources, int day) =>
        new(Path.Combine(Root, name, "project.erksproject"),
            "a1da1b7133b74b13a28765dc2379b761",
            sources,
            new DateTime(2026, 9, day, 0, 0, 0, DateTimeKind.Utc));

    [Fact]
    public void TheFolderHoldingTheWorkComesFirstEvenWhenItIsTheOlderOne()
    {
        // The exact shape on the user's machine: the twin is empty and was
        // written a day later, because opening the cloud project touched it.
        IReadOnlyList<LocalProjectFolder> ranked = ProjectFolderPlanner.Rank(
        [
            Folder("STUDIO-20260722-1906-a1da1b71", sources: 0, day: 5),
            Folder("STUDIO-20260722-1906", sources: 3, day: 4),
        ]);

        Assert.EndsWith(@"STUDIO-20260722-1906\project.erksproject", ranked[0].ProjectPath);
    }

    [Fact]
    public void AmongEqualsTheMostRecentlyWrittenStillWins()
    {
        // Where neither holds more work, the old rule is still the sensible one.
        IReadOnlyList<LocalProjectFolder> ranked = ProjectFolderPlanner.Rank(
        [
            Folder("older", sources: 2, day: 1),
            Folder("newer", sources: 2, day: 9),
        ]);

        Assert.EndsWith(@"newer\project.erksproject", ranked[0].ProjectPath);
    }

    [Fact]
    public void ThePreferredPathOnlyBreaksTiesAndNeverBeatsRealWork()
    {
        // The canonical name is a tie-break, not an override: a folder named
        // after the code but holding nothing must not win over the one holding
        // the sources.
        string canonical = Path.Combine(Root, "canonical", "project.erksproject");
        IReadOnlyList<LocalProjectFolder> ranked = ProjectFolderPlanner.Rank(
            [Folder("canonical", sources: 0, day: 9), Folder("other", sources: 4, day: 1)],
            canonical);

        Assert.EndsWith(@"other\project.erksproject", ranked[0].ProjectPath);
    }

    [Fact]
    public void RankingIsStableForASingleFolder()
    {
        IReadOnlyList<LocalProjectFolder> ranked =
            ProjectFolderPlanner.Rank([Folder("only", sources: 0, day: 1)]);

        Assert.Single(ranked);
    }
}
