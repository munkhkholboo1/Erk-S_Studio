using System.IO;
using System.Security.Cryptography;
using ErkS.Platform.Contracts;
using ErkS.Platform.Core;
using ErkS.Platform.Pdf;

namespace ErkS.Studio;

public sealed record PackageRecordResult(string SourceId, int RemovedAlbumPageCount);

/// <summary>
/// Runtime state of one explicitly opened project workspace. There is no
/// synthetic project while Studio is showing the project catalog.
/// </summary>
public sealed class AppState : IDisposable
{
    private ProjectWorkspace? project;
    private StudioAlbumDocument? albumDocument;

    public bool HasOpenProject => project is not null;

    public ProjectWorkspace Project => project
        ?? throw new InvalidOperationException("No project workspace is open.");

    public StudioAlbumDocument AlbumDocument => albumDocument
        ?? throw new InvalidOperationException("No project album is open.");

    public AlbumDefinition Album => AlbumDocument.Definition;

    public string? ProjectPath { get; private set; }

    public string? AlbumPath { get; private set; }

    public bool LastOpenMigratedLegacyProject { get; private set; }

    public SheetLibrary Library { get; } = new();

    public SheetIntakeService Intake { get; }

    public AlbumBuilder Builder { get; }

    public event Action? ProjectReplaced;

    public AppState()
    {
        Intake = new SheetIntakeService(Library);
        Builder = new AlbumBuilder(new PdfSharpAlbumWriter());
    }

    public void NewProject(string code, string name)
    {
        NewProject(new ProjectCreationRequest
        {
            Code = code,
            Name = name,
        });
    }

    public void NewProject(ProjectCreationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Code))
        {
            throw new ArgumentException("Project code is required.", nameof(request));
        }
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ArgumentException("Project name is required.", nameof(request));
        }

        var projectFolder = Path.Combine(ProjectWorkspacePaths.DefaultRoot, SafePathSegment(request.Code));
        var projectPath = Path.Combine(projectFolder, ProjectWorkspace.DefaultFileName);
        if (File.Exists(projectPath))
        {
            throw new InvalidOperationException($"Project already exists: {projectPath}");
        }

        Directory.CreateDirectory(projectFolder);
        Directory.CreateDirectory(Path.Combine(projectFolder, "sources"));
        Directory.CreateDirectory(Path.Combine(projectFolder, "albums"));
        Directory.CreateDirectory(Path.Combine(projectFolder, "reports"));
        Directory.CreateDirectory(Path.Combine(projectFolder, "archive"));

        project = ProjectWorkspaceStore.Create(request);
        albumDocument = CreateDefaultAlbum(project);
        ProjectPath = projectPath;
        AlbumPath = ProjectWorkspacePaths.ResolveInsideProject(projectPath, project.PrimaryAlbum.DocumentPath);
        LastOpenMigratedLegacyProject = false;
        SaveProject();
        ResetRuntimeServices();
        ProjectReplaced?.Invoke();
    }

    public void OpenProject(string path)
    {
        path = Path.GetFullPath(path);
        LastOpenMigratedLegacyProject = false;
        if (string.Equals(Path.GetExtension(path), ProjectWorkspace.FileExtension, StringComparison.OrdinalIgnoreCase))
        {
            project = ProjectWorkspaceStore.Load(path);
            ProjectPath = path;
            AlbumPath = ProjectWorkspacePaths.ResolveInsideProject(path, project.PrimaryAlbum.DocumentPath);
            if (File.Exists(AlbumPath))
            {
                albumDocument = StudioAlbumDocumentStore.Load(AlbumPath);
            }
            else
            {
                albumDocument = CreateDefaultAlbum(project);
                StudioAlbumDocumentStore.Save(albumDocument, AlbumPath);
            }
        }
        else if (string.Equals(Path.GetExtension(path), AlbumProject.FileExtension, StringComparison.OrdinalIgnoreCase))
        {
            if (StudioAlbumDocumentStore.IsAlbumDocument(path))
            {
                throw new InvalidDataException("Альбумыг дангаар нь биш, харьяалах төслөөс нь нээнэ.");
            }
            var imported = LegacyAlbumProjectImporter.Import(path, persist: true);
            project = imported.Project;
            albumDocument = imported.Album;
            ProjectPath = imported.ProjectPath;
            AlbumPath = imported.AlbumPath;
            LastOpenMigratedLegacyProject = imported.CreatedFiles;
        }
        else
        {
            throw new InvalidDataException($"Unsupported Erk-S Studio project file: {path}");
        }

        if (!string.Equals(albumDocument.ProjectId, project.ProjectId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Album document belongs to a different project workspace.");
        }

        if (EnsureUniqueSourceInboxes())
        {
            SaveProject();
        }
        ResetRuntimeServices();
        ProjectReplaced?.Invoke();
    }

    public void CloseProject()
    {
        ClearWatchers();
        Library.Clear();
        project = null;
        albumDocument = null;
        ProjectPath = null;
        AlbumPath = null;
        LastOpenMigratedLegacyProject = false;
        ProjectReplaced?.Invoke();
    }

    internal void LinkCurrentProjectToCloud(
        StudioCloudProjectDetail cloudProject,
        string serverUrl,
        ProjectCreationRequest? creationRequest = null,
        bool preserveCreation = false,
        bool preserveSyncState = false)
    {
        ArgumentNullException.ThrowIfNull(cloudProject);
        StudioCloudProjectSummary summary = cloudProject.Project;
        if (string.IsNullOrWhiteSpace(summary.ProjectId))
        {
            throw new InvalidDataException("Cloud project ID is empty.");
        }

        Project.ProjectId = summary.ProjectId;
        Project.Identity.Code = summary.ProjectCode;
        Project.Identity.Name = summary.Name;
        Project.Identity.StageName = string.IsNullOrWhiteSpace(summary.CurrentStage)
            ? Project.Identity.StageName
            : summary.CurrentStage;
        Project.Cloud.Origin = ProjectOrigins.Cloud;
        Project.Cloud.ServerProjectId = summary.ProjectId;
        Project.Cloud.ServerUrl = serverUrl.TrimEnd('/');
        Project.Cloud.CloudProjectCode = summary.ProjectCode;
        if (!preserveSyncState)
            Project.Cloud.SyncStatus = ProjectSyncStatuses.Linked;
        Project.Cloud.CurrentUserRoles = summary.CurrentUserRoles
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        Project.Cloud.CurrentUserScopes = summary.CurrentUserScopes
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Project.Foundation.InitiationBasis.ClientName = summary.ClientName;
        Project.Foundation.InitiationBasis.SiteAddress = string.IsNullOrWhiteSpace(cloudProject.ProjectInformation.Location)
            ? cloudProject.SiteAndLand.Addresses.FirstOrDefault() ?? ""
            : cloudProject.ProjectInformation.Location;
        Project.Foundation.InitiationBasis.LandReference = string.Join(", ", cloudProject.SiteAndLand.ParcelNumbers);
        Project.Foundation.InitiationBasis.ServerRecordId = summary.ProjectId;
        Project.Foundation.PlanningTask.IssuingAuthorityName = summary.PlanningAuthorityName;
        ProjectCompanyAssignment companyAssignment = Project.Foundation.DesignCompany;
        string cloudOrganizationId = cloudProject.ConceptAssignment?.OrganizationId ?? "";
        bool sameCompany = !string.IsNullOrWhiteSpace(cloudOrganizationId) &&
            companyAssignment.OrganizationId.Equals(cloudOrganizationId, StringComparison.OrdinalIgnoreCase);
        string preservedLogoPath = sameCompany ? companyAssignment.OrganizationSnapshot.LogoPath : "";
        companyAssignment.OrganizationId = cloudOrganizationId;
        StudioCloudOrganizationRenderProfile? renderProfile = cloudProject.DesignOrganizationProfile;
        companyAssignment.OrganizationName = renderProfile?.LegalName ?? summary.DesignOrganizationName;
        if (renderProfile is not null)
        {
            companyAssignment.OrganizationSnapshot = new CompanyProfile
            {
                OrganizationId = renderProfile.OrganizationId,
                Name = renderProfile.LegalName,
                DisplayName = renderProfile.DisplayName,
                ShortName = renderProfile.ShortName,
                RegistrationNumber = renderProfile.RegistrationNumber,
                Address = renderProfile.Address,
                Phone = renderProfile.Phone,
                PhoneNumbers = string.IsNullOrWhiteSpace(renderProfile.Phone) ? [] : [renderProfile.Phone],
                Email = renderProfile.Email,
                WebSite = renderProfile.Website,
                LicenseScope = renderProfile.LicenseScope,
                LicenseNumber = renderProfile.LicenseNumber,
                DirectorTitle = renderProfile.DirectorTitle,
                DirectorName = renderProfile.DirectorName,
                LogoPath = preservedLogoPath,
                LogoScale = renderProfile.LogoScale,
                LogoOffsetX = renderProfile.LogoOffsetX,
                LogoOffsetY = renderProfile.LogoOffsetY,
                Signers = string.IsNullOrWhiteSpace(renderProfile.DirectorName)
                    ? []
                    : [new CompanySigner
                    {
                        Role = renderProfile.DirectorTitle,
                        FullName = renderProfile.DirectorName,
                    }],
            };
        }
        else if (!sameCompany)
        {
            companyAssignment.OrganizationSnapshot = new CompanyProfile
            {
                OrganizationId = cloudOrganizationId,
                Name = summary.DesignOrganizationName,
                DisplayName = summary.DesignOrganizationName,
            };
        }
        else
        {
            companyAssignment.OrganizationSnapshot.OrganizationId = cloudOrganizationId;
            if (string.IsNullOrWhiteSpace(companyAssignment.OrganizationSnapshot.Name))
                companyAssignment.OrganizationSnapshot.Name = summary.DesignOrganizationName;
            if (string.IsNullOrWhiteSpace(companyAssignment.OrganizationSnapshot.DisplayName))
                companyAssignment.OrganizationSnapshot.DisplayName = summary.DesignOrganizationName;
        }

        List<StudioCloudParticipant> activeParticipants = cloudProject.Participants
            .Where(item => item.Status.Equals("Active", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Project.Foundation.PlanningTask.AuthorityMembers = activeParticipants
            .Where(item => item.Roles.Any(IsAuthorityRole))
            .Select(ToProjectMember)
            .ToList();
        Project.Foundation.DesignCompany.Members = activeParticipants
            .Where(item => !item.Roles.Any(IsAuthorityRole) && !item.Roles.Any(IsClientRole))
            .Select(ToProjectMember)
            .ToList();
        StudioCloudParticipant? client = activeParticipants.FirstOrDefault(item => item.Roles.Any(IsClientRole));
        if (client is not null)
        {
            Project.Foundation.InitiationBasis.ClientEmail = client.AccountEmail;
            if (string.IsNullOrWhiteSpace(Project.Foundation.InitiationBasis.ClientName))
            {
                Project.Foundation.InitiationBasis.ClientName = client.DisplayName;
            }
        }

        if (!preserveCreation && creationRequest is not null)
        {
            Project.Creation.Channel = ProjectCreationChannels.Studio;
            Project.Creation.InitiatorType = creationRequest.InitiatorType;
            Project.Creation.InitiatorOrganizationId = creationRequest.InitiatorOrganizationId;
            Project.Creation.InitiatorOrganizationName = creationRequest.InitiatorOrganizationName;
            Project.Creation.InitiatorUserId = creationRequest.InitiatorUserId;
            Project.Creation.InitiatorDisplayName = creationRequest.InitiatorDisplayName;
        }
        else if (!preserveCreation)
        {
            Project.Creation.Channel = ProjectCreationChannels.Server;
            Project.Creation.InitiatorType = string.IsNullOrWhiteSpace(summary.PlanningAuthorityName)
                ? ProjectInitiatorTypes.DesignOrganization
                : ProjectInitiatorTypes.GovernmentAuthority;
            Project.Creation.InitiatorOrganizationName = string.IsNullOrWhiteSpace(summary.PlanningAuthorityName)
                ? summary.DesignOrganizationName
                : summary.PlanningAuthorityName;
        }

        SaveProject();
        ProjectReplaced?.Invoke();
    }

    private static ProjectMember ToProjectMember(StudioCloudParticipant participant) => new()
    {
        Id = participant.ParticipantId,
        FullName = string.IsNullOrWhiteSpace(participant.DisplayName) ? participant.AccountEmail : participant.DisplayName,
        Email = participant.AccountEmail,
        Roles = participant.Roles.ToList(),
    };

    private static bool IsAuthorityRole(string role) => role is
        "AuthoritySpecialist" or "AuthorityDepartmentHead" or "ChiefArchitect";

    private static bool IsClientRole(string role) => role is "Client" or "Applicant";

    public void SaveProject()
    {
        if (ProjectPath is null || AlbumPath is null)
        {
            throw new InvalidOperationException("Project workspace has no storage path.");
        }

        Project.PrimaryAlbum.Title = Album.Title;
        Project.PrimaryAlbum.Status = AlbumDocument.Status;
        AlbumDocument.ProjectId = Project.ProjectId;
        AlbumDocument.AlbumId = Project.PrimaryAlbum.Id;
        AlbumDocument.FoundationVersion = Project.Foundation.Version;
        StudioAlbumDocumentStore.Save(AlbumDocument, AlbumPath);
        ProjectWorkspaceStore.Save(Project, ProjectPath);
    }

    public void AddDesignSource(ProjectDesignSource source)
    {
        if (string.IsNullOrWhiteSpace(source.Id))
        {
            source.Id = Guid.NewGuid().ToString("N");
        }
        if (string.IsNullOrWhiteSpace(source.InboxFolder))
        {
            source.InboxFolder = ResolveDefaultSourceFolder(source.DisplayName);
        }

        source.InboxFolder = Path.GetFullPath(source.InboxFolder);
        var projectFolder = ResolveProjectFolder();
        if (!ProjectWorkspacePaths.IsInside(projectFolder, source.InboxFolder))
        {
            throw new InvalidDataException("Эх үүсвэрийн PDF/manifest хавтас төслийн дотор байх ёстой.");
        }

        Directory.CreateDirectory(source.InboxFolder);
        var existingSource = Project.Sources.FirstOrDefault(existing =>
            string.Equals(existing.Id, source.Id, StringComparison.OrdinalIgnoreCase) ||
            (existing.Kind == source.Kind && PathsEqual(existing.NativeDocumentPath, source.NativeDocumentPath)) ||
            (existing.Kind == source.Kind &&
             string.IsNullOrWhiteSpace(existing.NativeDocumentPath) &&
             string.Equals(existing.Name, source.Name, StringComparison.OrdinalIgnoreCase)));
        if (existingSource is null && Project.Sources.Any(existing =>
            PathsEqual(existing.InboxFolder, source.InboxFolder)))
        {
            source.InboxFolder = ResolveUniqueSourceFolder(source, Project.Sources.Select(item => item.InboxFolder));
            Directory.CreateDirectory(source.InboxFolder);
        }
        if (existingSource is null)
        {
            Project.Sources.Add(source);
        }
        else
        {
            var previousInbox = existingSource.InboxFolder;
            source.Id = existingSource.Id;
            existingSource.Kind = source.Kind;
            existingSource.Name = source.Name;
            existingSource.ApplicationVersion = source.ApplicationVersion;
            existingSource.NativeDocumentTitle = source.NativeDocumentTitle;
            existingSource.NativeDocumentPath = source.NativeDocumentPath;
            existingSource.InboxFolder = source.InboxFolder;
            existingSource.OwnerOrganizationName = string.IsNullOrWhiteSpace(source.OwnerOrganizationName)
                ? existingSource.OwnerOrganizationName
                : source.OwnerOrganizationName;
            existingSource.Status = source.Status;
            if (!string.Equals(previousInbox, existingSource.InboxFolder, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(previousInbox))
            {
                Intake.UnwatchFolder(previousInbox);
            }
            source = existingSource;
        }
        SaveProject();
        Intake.WatchFolder(source.InboxFolder, source.UseLegacySheetKeys ? null : source.Id);
    }

    public void RemoveDesignSource(ProjectDesignSource source)
    {
        Project.Sources.RemoveAll(existing =>
            string.Equals(existing.Id, source.Id, StringComparison.OrdinalIgnoreCase));
        Intake.UnwatchFolder(source.InboxFolder);
        SaveProject();
    }

    public PackageRecordResult? RecordPackageReceived(SheetPackageLoadResult result)
    {
        if (!result.IsLossless || result.Manifest is null)
        {
            return null;
        }

        var packageSource = result.Manifest.Source;
        var source = Project.Sources.FirstOrDefault(existing =>
            string.Equals(existing.Id, packageSource.SourceId, StringComparison.OrdinalIgnoreCase));
        if (source is null)
        {
            return null;
        }

        source.Status = DesignSourceStatuses.Connected;
        source.LastPackageAtUtc = result.Manifest.ExportedAtUtc;
        source.ApplicationVersion = packageSource.ApplicationVersion;
        if (string.IsNullOrWhiteSpace(source.NativeDocumentTitle))
        {
            source.NativeDocumentTitle = packageSource.DocumentTitle;
        }
        if (string.IsNullOrWhiteSpace(source.NativeDocumentPath))
        {
            source.NativeDocumentPath = packageSource.DocumentPath;
        }
        using (FileStream stream = File.OpenRead(result.ManifestPath))
        {
            string manifestSha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            ProjectCloudSyncMetadata.RecordPackage(Project, source, result.Manifest, manifestSha256);
        }

        var usesConceptTemplate = string.Equals(
            Album.TemplateId,
            BuildingArchitectureConceptAlbumTemplate.TemplateId,
            StringComparison.OrdinalIgnoreCase);
        var removedAlbumPageCount = 0;
        if (result.Manifest.PackageScope == SheetPackageScope.FullSnapshot &&
            Library.IsCurrentAuthoritativeSnapshot(result.Manifest, source.Id))
        {
            var authoritativeKeys = result.Manifest.Sheets
                .Select(entry => SheetRecord.MakeKey(packageSource, entry, source.Id))
                .ToHashSet(StringComparer.Ordinal);
            removedAlbumPageCount = Album.Pages.RemoveAll(page =>
                SheetRecord.BelongsToSource(page.SheetKey, packageSource, source.Id) &&
                !authoritativeKeys.Contains(page.SheetKey));
            foreach (var section in Album.Sections)
            {
                section.SheetKeys.RemoveAll(key =>
                    SheetRecord.BelongsToSource(key, packageSource, source.Id) &&
                    !authoritativeKeys.Contains(key));
            }
        }
        foreach (var entry in result.Manifest.Sheets)
        {
            var key = SheetRecord.MakeKey(packageSource, entry, source.Id);
            var currentRecord = Library.Find(key);
            if (currentRecord?.PackageId != result.Manifest.PackageId)
            {
                continue;
            }

            var pages = Album.Pages.Where(page =>
                    string.Equals(page.SheetKey, key, StringComparison.Ordinal))
                .ToList();
            if (usesConceptTemplate && pages.Count == 0)
            {
                var newPage = new AlbumPageDefinition { SheetKey = key };
                Album.Pages.Add(newPage);
                pages.Add(newPage);
            }

            var slot = usesConceptTemplate
                ? BuildingArchitectureConceptAlbumTemplate.FindSourceSlot(Album, entry)
                : null;
            foreach (var page in pages)
            {
                if (usesConceptTemplate)
                {
                    page.TemplateSlotId = slot?.Id ?? "";
                    page.SectionId = BuildingArchitectureConceptAlbumTemplate.ResolveSectionId(Album, slot);
                }
                PageFormatResolver.ApplySourceFormat(page, entry);
            }
        }

        if (usesConceptTemplate)
        {
            var orderedPages = BuildingArchitectureConceptAlbumSequencer.OrderPages(
                Album,
                Album.Pages,
                Library,
                Project.Sources);
            Album.Pages.Clear();
            Album.Pages.AddRange(orderedPages);
        }

        SaveProject();
        return new PackageRecordResult(source.Id, removedAlbumPageCount);
    }

    public string ResolveOutputFolder()
    {
        return ProjectWorkspacePaths.ResolveInsideProject(ProjectPath!, Project.PrimaryAlbum.OutputFolder);
    }

    public string ResolveProjectFolder()
    {
        if (ProjectPath is null)
        {
            throw new InvalidOperationException("No project workspace is open.");
        }
        return ProjectWorkspacePaths.GetProjectFolder(ProjectPath);
    }

    public string ResolveDefaultSourceFolder(string sourceName)
    {
        return Path.Combine(ResolveProjectFolder(), "sources", SafePathSegment(sourceName), "deliveries");
    }

    public AlbumProject CreateAlbumBuildProject()
    {
        var company = Project.Foundation.DesignCompany.OrganizationSnapshot;
        return new AlbumProject
        {
            Name = Project.Name,
            Code = Project.Code,
            Description = Project.Identity.Description,
            ServerProjectId = Project.Cloud.ServerProjectId,
            ServerUrl = Project.Cloud.ServerUrl,
            CloudProjectCode = Project.Cloud.CloudProjectCode,
            ClientName = Project.Foundation.InitiationBasis.ClientName,
            PlanningAuthorityName = Project.Foundation.PlanningTask.IssuingAuthorityName,
            DesignOrganizationName = Project.DesignOrganizationName,
            CloudStatus = Project.Cloud.SyncStatus,
            InitiationBasis = Project.Foundation.InitiationBasis,
            PlanningTask = Project.Foundation.PlanningTask,
            Company = company,
            Participants = Project.Foundation.DesignCompany.Members
                .SelectMany(member => member.Roles.DefaultIfEmpty("").Select(role => new ProjectParticipant
                {
                    FullName = member.FullName,
                    Email = member.Email,
                    Role = role,
                }))
                .ToList(),
            DesignSources = Project.Sources,
            SourceFolders = Project.Sources.Select(source => source.InboxFolder).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Album = Album,
            OutputFolder = ResolveOutputFolder(),
        };
    }

    public void RecordBuiltAlbum(string outputPath, int pageCount, string pageSizeSummary)
    {
        ProjectCloudSyncMetadata.RecordBuiltAlbum(Project, ProjectPath!, outputPath, pageCount, pageSizeSummary);
    }

    private static StudioAlbumDocument CreateDefaultAlbum(ProjectWorkspace workspace)
    {
        return new StudioAlbumDocument
        {
            ProjectId = workspace.ProjectId,
            AlbumId = workspace.PrimaryAlbum.Id,
            PackageType = workspace.PrimaryAlbum.Type,
            Status = workspace.PrimaryAlbum.Status,
            FoundationVersion = workspace.Foundation.Version,
            Definition = BuildingArchitectureConceptAlbumTemplate.CreateDefinition(workspace.PrimaryAlbum.Title),
        };
    }

    private void ResetRuntimeServices()
    {
        ClearWatchers();
        Library.Clear();
        foreach (var source in Project.Sources)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(source.InboxFolder))
                {
                    Directory.CreateDirectory(source.InboxFolder);
                    Intake.WatchFolder(source.InboxFolder, source.UseLegacySheetKeys ? null : source.Id);
                }
                if (source.Metadata.TryGetValue("LegacyInboxFolder", out var legacyInbox) &&
                    !string.IsNullOrWhiteSpace(legacyInbox) &&
                    ProjectWorkspacePaths.IsInside(ResolveProjectFolder(), legacyInbox))
                {
                    Intake.WatchFolder(legacyInbox);
                }
            }
            catch
            {
                // The source remains visible so the user can repair its link.
            }
        }
    }

    private void ClearWatchers()
    {
        foreach (var watched in Intake.WatchedFolders)
        {
            Intake.UnwatchFolder(watched);
        }
    }

    private bool EnsureUniqueSourceInboxes()
    {
        var usedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var changed = false;
        foreach (var source in Project.Sources)
        {
            if (string.IsNullOrWhiteSpace(source.InboxFolder))
            {
                source.InboxFolder = ResolveUniqueSourceFolder(source, usedFolders);
                Directory.CreateDirectory(source.InboxFolder);
                usedFolders.Add(Path.GetFullPath(source.InboxFolder));
                changed = true;
                continue;
            }

            var fullPath = Path.GetFullPath(source.InboxFolder);
            source.InboxFolder = fullPath;
            if (usedFolders.Add(fullPath))
            {
                continue;
            }

            source.Metadata["LegacyInboxFolder"] = fullPath;
            source.InboxFolder = ResolveUniqueSourceFolder(source, usedFolders);
            Directory.CreateDirectory(source.InboxFolder);
            usedFolders.Add(Path.GetFullPath(source.InboxFolder));
            changed = true;
        }
        return changed;
    }

    private string ResolveUniqueSourceFolder(ProjectDesignSource source, IEnumerable<string> usedFolders)
    {
        var used = usedFolders
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var shortId = string.IsNullOrWhiteSpace(source.Id)
            ? Guid.NewGuid().ToString("N")[..8]
            : source.Id[..Math.Min(8, source.Id.Length)];
        var folderName = SafePathSegment($"{source.DisplayName}-{shortId}");
        var root = Path.Combine(ResolveProjectFolder(), "sources");
        var candidate = Path.Combine(root, folderName, "deliveries");
        var suffix = 2;
        while (used.Contains(Path.GetFullPath(candidate)))
        {
            candidate = Path.Combine(root, $"{folderName}-{suffix++}", "deliveries");
        }
        return Path.GetFullPath(candidate);
    }

    private static string SafePathSegment(string value)
    {
        var result = string.IsNullOrWhiteSpace(value) ? "source" : value.Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            result = result.Replace(invalid, '_');
        }
        return result;
    }

    private static bool PathsEqual(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        try
        {
            return string.Equals(
                Path.GetFullPath(left),
                Path.GetFullPath(right),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        Intake.Dispose();
    }
}
