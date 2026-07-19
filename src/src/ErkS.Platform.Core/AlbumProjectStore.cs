using System.Text.Json;
using ErkS.Platform.Contracts;

namespace ErkS.Platform.Core;

/// <summary>Loads and saves ".erksalbum" project files (JSON).</summary>
public static class AlbumProjectStore
{
    public static AlbumProject Load(string path)
    {
        var json = File.ReadAllText(path);
        var project = JsonSerializer.Deserialize<AlbumProject>(json, SheetPackageJson.Options)
            ?? throw new InvalidDataException($"Project file is empty or invalid: {path}");

        if (project.FormatVersion > AlbumProject.CurrentFormatVersion)
        {
            throw new InvalidDataException(
                $"Project format {project.FormatVersion} is newer than supported {AlbumProject.CurrentFormatVersion}. Update Erk-S Platform.");
        }

        Normalize(project);
        return project;
    }

    public static void Save(AlbumProject project, string path)
    {
        Normalize(project);
        project.FormatVersion = AlbumProject.CurrentFormatVersion;
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(project, SheetPackageJson.Options);
        // Write-then-replace so a crash never leaves a corrupt project file.
        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, path, overwrite: true);
    }

    private static void Normalize(AlbumProject project)
    {
        project.ProjectId = project.ProjectId?.Trim() ?? "";
        project.ServerProjectId = project.ServerProjectId?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(project.ProjectId) &&
            !string.IsNullOrWhiteSpace(project.ServerProjectId))
        {
            project.ProjectId = project.ServerProjectId;
        }
        project.InitiationBasis ??= new ProjectInitiationBasis();
        project.InitiationBasis.Documents ??= [];
        project.InitiationBasis.ClientType = ProjectClientTypes.Normalize(
            project.InitiationBasis.ClientType);
        project.InitiationBasis.ClientOrganizationSnapshot ??= new CompanyProfile();
        project.InitiationBasis.ClientOrganizationSnapshot.Normalize();
        project.PlanningTask ??= new PlanningTaskInformation();
        project.PlanningTask.Requirements ??= [];
        project.PlanningTask.Documents ??= [];
        project.PlanningTask.AuthorityMembers ??= [];
        project.ApprovalWorkflow ??= new ProjectApprovalWorkflow();
        project.ApprovalWorkflow.Normalize();
        project.Company ??= new CompanyProfile();
        project.Company.Normalize();
        project.Participants ??= [];
        foreach (ProjectParticipant participant in project.Participants)
        {
            participant.FamilyName ??= "";
            participant.GivenName ??= "";
            participant.FullName ??= "";
            participant.Role ??= "";
            participant.Email ??= "";
            if (!string.IsNullOrWhiteSpace(participant.FamilyName) ||
                !string.IsNullOrWhiteSpace(participant.GivenName))
            {
                participant.FullName = MongolianPersonNameFormatter.ForDisplay(
                    participant.FamilyName,
                    participant.GivenName,
                    participant.FullName);
            }
        }
        project.Stages ??= [];
        project.WorkPackages ??= [];
        project.Documents ??= [];
        project.SourceFolders ??= [];
        project.DesignSources ??= [];
        project.Visualizations ??= new ProjectVisualizationSource();
        project.Visualizations.Normalize(string.IsNullOrWhiteSpace(project.ProjectId)
            ? null
            : project.ProjectId);
        project.Album ??= new AlbumDefinition();
        project.Album.Sections ??= [];
        project.Album.Pages ??= [];

        // Version 1 projects only knew watched folders. Surface them as
        // structured sources while retaining legacy sheet keys and fields.
        if (project.DesignSources.Count == 0)
        {
            for (var index = 0; index < project.SourceFolders.Count; index++)
            {
                var folder = project.SourceFolders[index];
                project.DesignSources.Add(new ProjectDesignSource
                {
                    Id = $"legacy-folder-{index + 1}",
                    Kind = DesignSourceKind.Folder,
                    Name = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                    InboxFolder = folder,
                    Status = DesignSourceStatuses.Connected,
                    UseLegacySheetKeys = true,
                });
            }
        }

        foreach (var source in project.DesignSources)
        {
            if (string.IsNullOrWhiteSpace(source.Id))
            {
                source.Id = Guid.NewGuid().ToString("N");
            }

            source.Metadata ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
