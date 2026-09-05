using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// Six projects on one machine grew an empty twin folder because the open flow
/// decided where a project lives by comparing FOLDER NAMES. The rule now lives
/// away from the controls so it can be stated here.
/// </summary>
public sealed class ProjectFolderPlannerTests
{
    private const string Root = @"C:\Projects";
    private const string Code = "STUDIO-20260722-1906";
    private const string ProjectId = "a1da1b7133b74b13a28765dc2379b761";

    private static string Default => Path.Combine(Root, Code, "project.erksproject");

    private static string Forked => Path.Combine(Root, Code + "-a1da1b71", "project.erksproject");

    private static LocalProjectFolder Folder(
        string name,
        string serverProjectId,
        int sources,
        int day = 1) =>
        new(Path.Combine(Root, name, "project.erksproject"),
            serverProjectId,
            sources,
            new DateTime(2026, 9, day, 0, 0, 0, DateTimeKind.Utc));

    [Fact]
    public void AProjectThatAlreadyLivesHereIsOpenedWhereItLives()
    {
        // The whole defect in one assertion: the folder exists, it is THIS
        // project, so nothing new is created beside it.
        ProjectFolderPlan plan = ProjectFolderPlanner.Plan(
            ProjectId,
            Default,
            Forked,
            [Folder(Code, ProjectId, sources: 3)]);

        Assert.Equal(ProjectFolderDecision.OpenExistingHome, plan.Decision);
        Assert.Equal(Default, plan.ProjectPath);
        Assert.Empty(plan.RivalPaths);
    }

    [Fact]
    public void TheFolderHoldingTheWorkWinsOverTheEmptyTwinThatWasWrittenLater()
    {
        // The twin is always the empty one, and a cloud refresh touches it - so
        // "most recently written" is exactly the wrong signal, and it is the one
        // the project list uses.
        ProjectFolderPlan plan = ProjectFolderPlanner.Plan(
            ProjectId,
            Default,
            Forked,
            [
                Folder(Code + "-a1da1b71", ProjectId, sources: 0, day: 5),
                Folder(Code, ProjectId, sources: 3, day: 4),
            ]);

        Assert.Equal(ProjectFolderDecision.OpenExistingHome, plan.Decision);
        Assert.Equal(Default, plan.ProjectPath);
        // The other one is named, not touched: the caller asks, and nothing is
        // moved, merged or deleted.
        Assert.Equal([Path.Combine(Root, Code + "-a1da1b71", "project.erksproject")], plan.RivalPaths);
    }

    [Fact]
    public void AFreeNameIsUsedAsItIs()
    {
        ProjectFolderPlan plan = ProjectFolderPlanner.Plan(
            ProjectId,
            Default,
            Forked,
            [Folder("SOMETHING-ELSE", "other-project-id", sources: 2)]);

        Assert.Equal(ProjectFolderDecision.CreateAtDefaultPath, plan.Decision);
        Assert.Equal(Default, plan.ProjectPath);
    }

    [Fact]
    public void ADifferentProjectSharingTheCodeIsTheONLYReasonToSuffixAName()
    {
        ProjectFolderPlan plan = ProjectFolderPlanner.Plan(
            ProjectId,
            Default,
            Forked,
            [Folder(Code, "a-completely-different-project", sources: 1)]);

        Assert.Equal(ProjectFolderDecision.CreateBesideDifferentProject, plan.Decision);
        Assert.Equal(Forked, plan.ProjectPath);
    }

    [Fact]
    public void TheIdIsMatchedWithoutCaringAboutCaseOrStraySpace()
    {
        ProjectFolderPlan plan = ProjectFolderPlanner.Plan(
            "  " + ProjectId.ToUpperInvariant() + " ",
            Default,
            Forked,
            [Folder(Code, ProjectId, sources: 3)]);

        Assert.Equal(ProjectFolderDecision.OpenExistingHome, plan.Decision);
    }

    [Fact]
    public void AProjectWithNoCloudIdMatchesNothingRatherThanEverything()
    {
        // An empty id is not a wildcard. Treating it as one would hand the first
        // local folder to whatever project happened to have no id yet.
        ProjectFolderPlan plan = ProjectFolderPlanner.Plan(
            "",
            Default,
            Forked,
            [Folder("OTHER", "", sources: 4)]);

        Assert.Equal(ProjectFolderDecision.CreateAtDefaultPath, plan.Decision);
    }

    [Fact]
    public void EveryRivalIsListed_NotJustTheFirst()
    {
        ProjectFolderPlan plan = ProjectFolderPlanner.Plan(
            ProjectId,
            Default,
            Forked,
            [
                Folder(Code, ProjectId, sources: 3, day: 4),
                Folder(Code + "-a1da1b71", ProjectId, sources: 0, day: 5),
                Folder(Code + "-copy", ProjectId, sources: 0, day: 3),
            ]);

        Assert.Equal(Default, plan.ProjectPath);
        Assert.Equal(2, plan.RivalPaths.Count);
    }

    [Fact]
    public void WhenNothingHoldsSourcesTheCanonicallyNamedFolderIsPreferred()
    {
        // Both empty - which is the real state of two of the twins on this
        // machine. The folder named after the code is where the project belongs.
        ProjectFolderPlan plan = ProjectFolderPlanner.Plan(
            ProjectId,
            Default,
            Forked,
            [
                Folder(Code + "-a1da1b71", ProjectId, sources: 0, day: 5),
                Folder(Code, ProjectId, sources: 0, day: 2),
            ]);

        Assert.Equal(Default, plan.ProjectPath);
    }
}
