namespace ErkS.Platform.Core;

public sealed class ProjectCatalogItem
{
    public required string ProjectPath { get; init; }
    public required string ProjectId { get; init; }
    public required string ProjectCode { get; init; }
    public required string DisplayName { get; init; }
    public required string StageName { get; init; }
    public required string DesignOrganization { get; init; }
    public required string CreationChannel { get; init; }
    public required string InitiatorType { get; init; }
    public required string InitiatorOrganization { get; init; }
    public required string Origin { get; init; }
    public required string SyncStatus { get; init; }
    public required DateTime LastWriteTimeUtc { get; init; }
    public bool IsLegacyProject { get; init; }
}

public interface IProjectCatalog
{
    IReadOnlyList<ProjectCatalogItem> ListProjects();
}

/// <summary>
/// Unified local catalog. Cloud/local are status attributes of a project, not
/// separate project collections in the Studio UI.
/// </summary>
public sealed class LocalProjectCatalog : IProjectCatalog
{
    public static string DefaultRoot => ProjectWorkspacePaths.DefaultRoot;

    private readonly string rootDirectory;

    public LocalProjectCatalog()
        : this(DefaultRoot)
    {
    }

    public LocalProjectCatalog(string rootDirectory)
    {
        this.rootDirectory = rootDirectory;
    }

    public IReadOnlyList<ProjectCatalogItem> ListProjects()
    {
        if (!Directory.Exists(rootDirectory))
        {
            return [];
        }

        var projects = new List<ProjectCatalogItem>();
        var workspaceFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFiles(
                     rootDirectory,
                     "*" + ProjectWorkspace.FileExtension,
                     SearchOption.AllDirectories)
                 .Where(path => !IsBackupMirrorPath(path)))
        {
            try
            {
                var project = ProjectWorkspaceStore.Load(path);
                workspaceFolders.Add(ProjectWorkspacePaths.GetProjectFolder(path));
                projects.Add(FromWorkspace(path, project));
            }
            catch
            {
                // One invalid project must not hide the rest of the catalog.
            }
        }

        foreach (var path in Directory.EnumerateFiles(
                     rootDirectory,
                     "*" + AlbumProject.FileExtension,
                     SearchOption.AllDirectories)
                 .Where(path => !IsBackupMirrorPath(path)))
        {
            try
            {
                var folder = Path.GetDirectoryName(Path.GetFullPath(path))!;
                if (IsBelowWorkspace(folder, workspaceFolders) || IsAlbumDocument(path))
                {
                    continue;
                }

                var legacy = AlbumProjectStore.Load(path);
                projects.Add(FromLegacy(path, legacy));
            }
            catch
            {
            }
        }

        return projects
            .GroupBy(
                item => string.IsNullOrWhiteSpace(item.ProjectId)
                    ? $"{item.ProjectCode}|{Path.GetDirectoryName(item.ProjectPath)}"
                    : item.ProjectId,
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(item => item.IsLegacyProject).ThenByDescending(item => item.LastWriteTimeUtc).First())
            .OrderByDescending(project => project.LastWriteTimeUtc)
            .ThenBy(project => project.ProjectCode, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static ProjectCatalogItem FromWorkspace(string path, ProjectWorkspace project) => new()
    {
        ProjectPath = path,
        ProjectId = string.IsNullOrWhiteSpace(project.Cloud.ServerProjectId)
            ? project.ProjectId
            : project.Cloud.ServerProjectId,
        ProjectCode = string.IsNullOrWhiteSpace(project.Cloud.CloudProjectCode)
            ? project.Code
            : project.Cloud.CloudProjectCode,
        DisplayName = project.Name,
        StageName = project.Identity.StageName,
        DesignOrganization = project.DesignOrganizationDisplayName,
        CreationChannel = project.Creation.Channel,
        InitiatorType = project.Creation.InitiatorType,
        InitiatorOrganization = project.Creation.InitiatorOrganizationName,
        Origin = project.Cloud.Origin,
        SyncStatus = project.Cloud.SyncStatus,
        LastWriteTimeUtc = File.GetLastWriteTimeUtc(path),
        IsLegacyProject = false,
    };

    private static ProjectCatalogItem FromLegacy(string path, AlbumProject project) => new()
    {
        ProjectPath = path,
        ProjectId = project.ServerProjectId,
        ProjectCode = string.IsNullOrWhiteSpace(project.CloudProjectCode) ? project.Code : project.CloudProjectCode,
        DisplayName = project.Name,
        StageName = "Загвар зураг",
        DesignOrganization = string.IsNullOrWhiteSpace(project.DesignOrganizationName)
            ? project.Company.Name
            : project.DesignOrganizationName,
        CreationChannel = string.IsNullOrWhiteSpace(project.ServerProjectId)
            ? ProjectCreationChannels.Imported
            : ProjectCreationChannels.Server,
        InitiatorType = !string.IsNullOrWhiteSpace(project.PlanningAuthorityName)
            ? ProjectInitiatorTypes.GovernmentAuthority
            : ProjectInitiatorTypes.DesignOrganization,
        InitiatorOrganization = !string.IsNullOrWhiteSpace(project.PlanningAuthorityName)
            ? project.PlanningAuthorityName
            : string.IsNullOrWhiteSpace(project.DesignOrganizationName)
                ? project.Company.Name
                : project.DesignOrganizationName,
        Origin = string.IsNullOrWhiteSpace(project.ServerProjectId) ? ProjectOrigins.Local : ProjectOrigins.Cloud,
        SyncStatus = string.IsNullOrWhiteSpace(project.CloudStatus)
            ? (string.IsNullOrWhiteSpace(project.ServerProjectId) ? ProjectSyncStatuses.Local : ProjectSyncStatuses.Linked)
            : project.CloudStatus,
        LastWriteTimeUtc = File.GetLastWriteTimeUtc(path),
        IsLegacyProject = true,
    };

    private static bool IsBelowWorkspace(string folder, HashSet<string> workspaceFolders)
    {
        return workspaceFolders.Any(root => ProjectWorkspacePaths.IsInside(root, folder));
    }

    private static bool IsAlbumDocument(string path)
    {
        return StudioAlbumDocumentStore.IsAlbumDocument(path);
    }

    private bool IsBackupMirrorPath(string path)
    {
        string relativePath = Path.GetRelativePath(rootDirectory, path);
        string? folder = Path.GetDirectoryName(relativePath);
        if (string.IsNullOrWhiteSpace(folder))
            return false;

        return folder
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => segment.EndsWith(".old", StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>Future server boundary after Studio account sign-in.</summary>
public sealed class ServerProjectCatalogPlaceholder : IProjectCatalog
{
    public IReadOnlyList<ProjectCatalogItem> ListProjects() => [];
}
