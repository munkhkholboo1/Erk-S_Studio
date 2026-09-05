namespace ErkS.Platform.Core;

/// <summary>One project folder on this disk, as the planner needs to see it.</summary>
/// <param name="ProjectPath">Full path of its project.erksproject.</param>
/// <param name="ServerProjectId">
/// The cloud id the folder records for itself. Used ONLY to recognise which
/// folder belongs to which project - never as an authority for what anyone may
/// do. It is a field the file writes about itself, and a folder that claims the
/// wrong id can at worst be offered as the wrong home, which the person is asked
/// about rather than moved into.
/// </param>
/// <param name="LocalSourceCount">How many design sources the folder actually holds.</param>
/// <param name="LastWriteTimeUtc">When the project file last changed.</param>
public sealed record LocalProjectFolder(
    string ProjectPath,
    string ServerProjectId,
    int LocalSourceCount,
    DateTime LastWriteTimeUtc);

public enum ProjectFolderDecision
{
    /// <summary>This project already lives somewhere on this disk. Open that.</summary>
    OpenExistingHome,

    /// <summary>Nothing here yet; the folder named after the code is free.</summary>
    CreateAtDefaultPath,

    /// <summary>
    /// The folder named after the code belongs to a DIFFERENT project that
    /// happens to share a code. Only then is a suffixed name correct.
    /// </summary>
    CreateBesideDifferentProject,
}

/// <param name="RivalPaths">
/// Other folders claiming the same project, newest-looking first. Empty in the
/// ordinary case. When it is not empty the caller must ASK - never move, merge
/// or delete anything.
/// </param>
public sealed record ProjectFolderPlan(
    ProjectFolderDecision Decision,
    string ProjectPath,
    IReadOnlyList<string> RivalPaths);

/// <summary>
/// Which folder a cloud project should open into.
///
/// THE DEFECT THIS EXISTS FOR. The rule used to live inside the open flow and
/// read: "if a folder with this code already has a project file, add the first
/// eight characters of the project id to the name". It compared the FOLDER NAME,
/// not the project. So opening a cloud project whose mirror already existed
/// created a SECOND, empty folder beside the real one - and because the project
/// list keeps only the most recently written folder per project id, the empty
/// twin then hid the folder holding every source, delivery and native document.
/// Six projects on one machine ended up with a twin that way, all created within
/// three minutes of each other, each with zero sources.
///
/// The rule is here, away from the controls, because the version inside the open
/// flow was wrong for months with nothing able to measure it.
/// </summary>
public static class ProjectFolderPlanner
{
    public static ProjectFolderPlan Plan(
        string serverProjectId,
        string defaultProjectPath,
        string forkedProjectPath,
        IReadOnlyList<LocalProjectFolder> localFolders)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultProjectPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(forkedProjectPath);
        ArgumentNullException.ThrowIfNull(localFolders);

        string wanted = (serverProjectId ?? "").Trim();
        List<LocalProjectFolder> homes = wanted.Length == 0
            ? []
            : [.. localFolders.Where(folder =>
                (folder.ServerProjectId ?? "").Trim().Equals(wanted, StringComparison.OrdinalIgnoreCase))];

        if (homes.Count > 0)
        {
            // Ranked by what the person is actually looking for: the folder that
            // holds their work. A twin is always the empty one, so counting
            // sources separates them without anyone having to guess from names
            // or timestamps - and the newest write is precisely the wrong signal
            // here, because a cloud refresh touches the empty twin.
            List<LocalProjectFolder> ranked = [.. homes
                .OrderByDescending(folder => folder.LocalSourceCount)
                .ThenBy(folder => IsSamePath(folder.ProjectPath, defaultProjectPath) ? 0 : 1)
                .ThenByDescending(folder => folder.LastWriteTimeUtc)
                .ThenBy(folder => folder.ProjectPath, StringComparer.OrdinalIgnoreCase)];

            return new ProjectFolderPlan(
                ProjectFolderDecision.OpenExistingHome,
                ranked[0].ProjectPath,
                [.. ranked.Skip(1).Select(folder => folder.ProjectPath)]);
        }

        // No folder claims this project. The default name is free unless some
        // OTHER project already sits there.
        bool defaultTaken = localFolders.Any(folder =>
            IsSamePath(folder.ProjectPath, defaultProjectPath));

        return defaultTaken
            ? new ProjectFolderPlan(ProjectFolderDecision.CreateBesideDifferentProject, forkedProjectPath, [])
            : new ProjectFolderPlan(ProjectFolderDecision.CreateAtDefaultPath, defaultProjectPath, []);
    }

    private static bool IsSamePath(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left ?? "").TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(right ?? "").TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
}
