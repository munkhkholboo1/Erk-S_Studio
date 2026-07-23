using System.IO;
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
    private readonly object assetWatcherGate = new();
    private readonly List<FileSystemWatcher> assetWatchers = [];
    private HashSet<string> watchedAssetPaths = new(StringComparer.OrdinalIgnoreCase);

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
    public event Action? AssetSourcesChanged;

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

        ClearAssetSourceWatchers();

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
        ClearAssetSourceWatchers();
        LastOpenMigratedLegacyProject = false;
        bool recoveredSiteContextSnapshots = false;
        if (string.Equals(Path.GetExtension(path), ProjectWorkspace.FileExtension, StringComparison.OrdinalIgnoreCase))
        {
            project = ProjectWorkspaceStore.Load(path);
            recoveredSiteContextSnapshots = ProjectWorkspaceStore.RecoverSiteContextSnapshots(project, path);
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

        bool removedUnownedSourcePages = RemoveSourcePagesFromSourceFreeProject() > 0;
        ProjectAssetSourceReconciliationResult assetReconciliation =
            ReconcileProjectAssetSourcesCore();
        bool reconciledAssets = ApplyAssetReconciliation(assetReconciliation);
        CityGenProjectSiteReconciliationResult siteReconciliation =
            ReconcileCityGenProjectSiteCore();
        bool reconciledSite = ApplyCityGenProjectSiteReconciliation(siteReconciliation);
        if (recoveredSiteContextSnapshots)
        {
            ProjectCloudSyncMetadata.MarkAlbumComponentsPending(
                Project,
                [ProjectCloudSyncMetadata.SiteContextComponentCode]);
            InvalidateBuiltAlbum();
        }
        if (EnsureUniqueSourceInboxes() ||
            reconciledAssets ||
            reconciledSite ||
            removedUnownedSourcePages ||
            recoveredSiteContextSnapshots)
        {
            SaveProject();
        }
        ResetRuntimeServices(scanExistingPackages: false);
        ProjectReplaced?.Invoke();
    }

    public void CloseProject()
    {
        ClearWatchers();
        ClearAssetSourceWatchers();
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

        bool preserveBuildingComposition = Project.Cloud.BuildingCompositionPending;
        ProjectCanonicalSyncService.Apply(Project, ToServerSnapshot(cloudProject));
        Project.Cloud.ServerUrl = serverUrl.TrimEnd('/');
        if (!preserveSyncState)
            Project.Cloud.SyncStatus = ProjectSyncStatuses.Linked;
        Project.Cloud.CurrentUserRoles = (summary.CurrentUserRoles ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        Project.Cloud.CurrentUserScopes = (summary.CurrentUserScopes ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        Project.Cloud.SharedSources = StudioCloudSourcePackageReconciliation.ActiveCanonical(
                (cloudProject.DesignPackages ?? [])
                    .OfType<StudioCloudDesignPackage>()
                    .SelectMany(package => (package.SourcePackages ?? [])
                        .OfType<StudioCloudSourcePackage>()))
            .Select(source => new ProjectCloudSourceReference
            {
                SourceId = source.SourceId ?? "",
                SourceKey = source.SourceKey ?? "",
                SourceApplication = source.SourceApplication ?? "",
                SourceDocumentReference = source.SourceDocumentReference ?? "",
                ManifestId = source.ManifestId ?? "",
                ContentHash = source.ContentHash ?? "",
                SheetCount = source.SheetCount,
                Status = source.Status ?? "",
                RegisteredBy = (source.RegisteredBy ?? "").Trim().ToLowerInvariant(),
                CustodianEmail = (source.CustodianEmail ?? "").Trim().ToLowerInvariant(),
                OwnerEmail = (string.IsNullOrWhiteSpace(source.CustodianEmail)
                    ? source.RegisteredBy ?? ""
                    : source.CustodianEmail).Trim().ToLowerInvariant(),
                RegisteredAtUtc = source.RegisteredAtUtc,
            })
            .ToList();
        bool buildingCompositionChanged = StudioBuildingCompositionSync.ApplyCanonical(
            Project,
            Library,
            cloudProject.BuildingComposition,
            preserveBuildingComposition);
        if (buildingCompositionChanged && !preserveBuildingComposition)
        {
            IReadOnlyList<AlbumPageDefinition> orderedPages =
                BuildingArchitectureConceptAlbumSequencer.OrderPages(
                    Album,
                    Album.Pages,
                    Library,
                    Project.Sources,
                    Project.BuildingGroups,
                    Project.SheetBuildingAssignments);
            Album.Pages.Clear();
            Album.Pages.AddRange(orderedPages);
        }
        StudioCloudAlbumRevision? currentAlbumRevision = (cloudProject.Albums ?? [])
            .OfType<StudioCloudAlbum>()
            .Select(album => (album.Revisions ?? [])
                .OfType<StudioCloudAlbumRevision>()
                .FirstOrDefault(revision => string.Equals(
                    revision.RevisionId,
                    album.CurrentRevisionId,
                    StringComparison.OrdinalIgnoreCase)))
            .FirstOrDefault(revision => revision is not null);
        Project.Cloud.SharedAlbumComponents = (currentAlbumRevision?.SectionManifest ?? [])
            .OfType<StudioCloudAlbumSection>()
            .OrderBy(component => component.Order)
            .ThenBy(component => (component.PageNumbers ?? []).FirstOrDefault())
            .Select(component => new ProjectCloudAlbumComponentReference
            {
                Code = component.Code ?? "",
                Label = component.Label ?? "",
                Order = component.Order,
                PageNumbers = (component.PageNumbers ?? []).ToList(),
                Status = component.Status ?? "",
                OwnerEmail = (component.OwnerEmail ?? "").Trim().ToLowerInvariant(),
                SourceKey = component.SourceKey ?? "",
                ComponentKind = component.ComponentKind ?? "",
            })
            .ToList();

        StudioCloudOrganizationRenderProfile? renderProfile = cloudProject.DesignOrganizationProfile;
        string cloudOrganizationId = cloudProject.ConceptAssignment?.OrganizationId ?? "";
        if (string.IsNullOrWhiteSpace(cloudOrganizationId))
            cloudOrganizationId = renderProfile?.OrganizationId ?? "";
        CompanyProfile? cloudCompany = renderProfile is null
            ? null
            : StudioCompanyProfileMapper.FromRenderProfile(renderProfile);
        ProjectCompanyAssignmentService.MergeCloudAssignment(
            Project,
            cloudOrganizationId,
            summary.DesignOrganizationName,
            cloudCompany);

        List<StudioCloudParticipant> activeParticipants = (cloudProject.Participants ?? [])
            .OfType<StudioCloudParticipant>()
            .Where(item => string.Equals(item.Status, "Active", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Project.Foundation.PlanningTask.AuthorityMembers = activeParticipants
            .Where(item => (item.Roles ?? []).Any(IsAuthorityRole))
            .Select(ToProjectMember)
            .ToList();
        Project.Foundation.DesignCompany.Members = activeParticipants
            .Where(item => !(item.Roles ?? []).Any(IsAuthorityRole) &&
                           !(item.Roles ?? []).Any(IsClientRole))
            .Select(ToProjectMember)
            .ToList();
        StudioCloudParticipant? client = activeParticipants.FirstOrDefault(item =>
            (item.Roles ?? []).Any(IsClientRole));
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

    private static ProjectServerSnapshot ToServerSnapshot(StudioCloudProjectDetail cloudProject)
    {
        StudioCloudProjectSummary summary = cloudProject.Project;
        StudioCloudProjectInformation information = cloudProject.ProjectInformation ?? new();
        StudioCloudSiteAndLand siteAndLand = cloudProject.SiteAndLand ?? new();
        StudioCloudProjectFoundation? foundation = cloudProject.Foundation;
        StudioCloudProjectSurface? surface = cloudProject.Surface;
        return new ProjectServerSnapshot
        {
            ProjectId = summary.ProjectId,
            ProjectCode = summary.ProjectCode,
            Name = summary.Name,
            Status = summary.Status,
            CurrentStage = summary.CurrentStage,
            ClientName = summary.ClientName,
            PlanningAuthorityName = summary.PlanningAuthorityName,
            DesignOrganizationName = summary.DesignOrganizationName,
            UpdatedAtUtc = summary.UpdatedAtUtc,
            ConcurrencyToken = summary.ConcurrencyToken,
            Surface = new ProjectServerSurface
            {
                SchemaVersion = surface?.SchemaVersion ?? "",
                ProductName = surface?.ProductName ?? "",
                Sections = (surface?.Sections ?? [])
                    .Select(item => new ProjectServerSurfaceSection
                    {
                        Id = item.Id,
                        Label = item.Label,
                        Icon = item.Icon,
                        Order = item.Order,
                    })
                    .ToList(),
                FoundationSections = (surface?.FoundationSections ?? [])
                    .Select(item => new ProjectServerSurfaceSection
                    {
                        Id = item.Id,
                        Label = item.Label,
                        Icon = item.Icon,
                        Order = item.Order,
                    })
                    .ToList(),
            },
            Information = new ProjectServerInformation
            {
                ProjectId = information.ProjectId,
                ProjectCode = information.ProjectCode,
                Name = information.Name,
                Location = information.Location,
                BuildingPurpose = information.BuildingPurpose,
                Capacity = information.Capacity,
                CapacityUnit = information.CapacityUnit,
                FootprintSquareMeters = information.FootprintSquareMeters,
                GrossFloorAreaSquareMeters = information.GrossFloorAreaSquareMeters,
                HeightMeters = information.HeightMeters,
                FloorsAboveGround = information.FloorsAboveGround,
                FloorsBelowGround = information.FloorsBelowGround,
            },
            Foundation = new ProjectServerFoundation
            {
                IsAvailable = foundation != null,
                Version = Math.Max(1, foundation?.Version ?? 1),
                InitiationBasis = new ProjectServerInitiationBasis
                {
                    SourceType = foundation?.InitiationBasis?.SourceType ?? "",
                    RequestNumber = foundation?.InitiationBasis?.RequestNumber ?? "",
                    RequestedAtUtc = foundation?.InitiationBasis?.RequestedAtUtc,
                    ClientType = foundation?.InitiationBasis?.ClientType ?? "",
                    ClientName = foundation?.InitiationBasis?.ClientName ?? "",
                    ClientEmail = foundation?.InitiationBasis?.ClientEmail ?? "",
                    ClientRepresentativePosition = foundation?.InitiationBasis?.ClientRepresentativePosition ?? "",
                    ClientRepresentativeName = foundation?.InitiationBasis?.ClientRepresentativeName ?? "",
                    ClientLogoUrl = foundation?.InitiationBasis?.ClientLogoUrl ?? "",
                    SiteAddress = foundation?.InitiationBasis?.SiteAddress ?? "",
                    LandReference = foundation?.InitiationBasis?.LandReference ?? "",
                    SourceOrganizationName = foundation?.InitiationBasis?.SourceOrganizationName ?? "",
                    ServerRecordId = foundation?.InitiationBasis?.ServerRecordId ?? "",
                    Summary = foundation?.InitiationBasis?.Summary ?? "",
                },
                PlanningTask = new ProjectServerPlanningTask
                {
                    AtdNumber = foundation?.PlanningTask?.AtdNumber ?? "",
                    IssuedAtUtc = foundation?.PlanningTask?.IssuedAtUtc,
                    IssuingAuthorityName = foundation?.PlanningTask?.IssuingAuthorityName ?? "",
                    Status = foundation?.PlanningTask?.Status ?? "",
                    Summary = foundation?.PlanningTask?.Summary ?? "",
                    Requirements = (foundation?.PlanningTask?.Requirements ?? []).ToList(),
                },
            },
            SiteAndLand = new ProjectServerSiteAndLand
            {
                ParcelNumbers = (siteAndLand.ParcelNumbers ?? []).ToList(),
                Addresses = (siteAndLand.Addresses ?? []).ToList(),
                RestrictionReferences = (siteAndLand.RestrictionReferences ?? []).ToList(),
            },
        };
    }

    private static ProjectMember ToProjectMember(StudioCloudParticipant participant) => new()
    {
        Id = participant.ParticipantId,
        FamilyName = participant.FamilyName,
        GivenName = participant.GivenName,
        FullName = MongolianPersonNameFormatter.ForDisplay(
            participant.FamilyName,
            participant.GivenName,
            string.IsNullOrWhiteSpace(participant.DisplayName)
                ? participant.AccountEmail
                : participant.DisplayName),
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
        RefreshAssetSourceWatchers();
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
        Intake.WatchFolder(
            source.InboxFolder,
            source.UseLegacySheetKeys ? null : source.Id,
            Project.ProjectId);
    }

    public int RemoveDesignSource(ProjectDesignSource source)
    {
        HashSet<string> knownSourceKeys = Library.Snapshot()
            .Where(record => SourceRecordBelongsTo(record, source))
            .Select(record => record.Key)
            .ToHashSet(StringComparer.Ordinal);
        string localSourcePrefix = source.Id.Trim().ToLowerInvariant() + "|";
        knownSourceKeys.UnionWith(Project.SheetBuildingAssignments.Keys.Where(key =>
            key.StartsWith(localSourcePrefix, StringComparison.OrdinalIgnoreCase)));
        bool removedBuildingAssignments =
            StudioBuildingCompositionSync.RemoveSourceAssignments(
                Project,
                source,
                knownSourceKeys);
        Project.Sources.RemoveAll(existing =>
            string.Equals(existing.Id, source.Id, StringComparison.OrdinalIgnoreCase));
        Intake.UnwatchFolder(source.InboxFolder);

        int removedPageCount = Project.Sources.Count == 0
            ? RemoveSourcePagesFromSourceFreeProject()
            : RemoveAlbumPagesForSource(source, knownSourceKeys);
        if (removedBuildingAssignments)
            ProjectCloudSyncMetadata.MarkBuildingCompositionPending(Project);
        InvalidateBuiltAlbum();
        SaveProject();
        ResetRuntimeServices();
        return removedPageCount;
    }

    public void UpdateBuildingComposition(
        IEnumerable<ProjectBuildingGroup> groups,
        IReadOnlyDictionary<string, string> assignments)
    {
        List<ProjectBuildingGroup> normalizedGroups =
            ProjectBuildingComposition.NormalizeGroups(groups);
        Project.BuildingGroups = normalizedGroups;
        Project.SheetBuildingAssignments =
            ProjectBuildingComposition.NormalizeAssignments(
                assignments,
                normalizedGroups);
        ProjectCloudSyncMetadata.MarkBuildingCompositionPending(Project);
        InvalidateBuiltAlbum();
        SaveProject();
    }

    public PackageRecordResult? RecordPackageReceived(SheetPackageLoadResult result)
    {
        ProjectPackageReconciliationResult? reconciled =
            ProjectPackageReconciliationService.Apply(Project, Album, Library, result);
        if (reconciled is null)
        {
            return null;
        }

        if (StudioBuildingCompositionSync.MaterializeSharedAssignments(
                Project,
                Library))
        {
            IReadOnlyList<AlbumPageDefinition> orderedPages =
                BuildingArchitectureConceptAlbumSequencer.OrderPages(
                    Album,
                    Album.Pages,
                    Library,
                    Project.Sources,
                    Project.BuildingGroups,
                    Project.SheetBuildingAssignments);
            Album.Pages.Clear();
            Album.Pages.AddRange(orderedPages);
        }
        SaveProject();
        return new PackageRecordResult(reconciled.SourceId, reconciled.RemovedAlbumPageCount);
    }

    public IReadOnlyList<SheetPackageCheckpoint> CurrentSourcePackageCheckpoints()
    {
        if (!HasOpenProject)
        {
            return [];
        }

        return ProjectCloudSyncMetadata.SourcePackages(Project)
            .Select(candidate => Guid.TryParse(candidate.ManifestId, out Guid packageId)
                ? new SheetPackageCheckpoint(
                    Project.ProjectId,
                    candidate.Source.Id,
                    packageId,
                    candidate.ExportedAtUtc,
                    candidate.ContentHash)
                : null)
            .OfType<SheetPackageCheckpoint>()
            .ToList();
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
        ProjectAssetSourceReconciliationResult assetReconciliation =
            ReconcileProjectAssetSourcesCore();
        CityGenProjectSiteReconciliationResult siteReconciliation =
            ReconcileCityGenProjectSiteCore();
        if (ApplyAssetReconciliation(assetReconciliation) |
            ApplyCityGenProjectSiteReconciliation(siteReconciliation))
            SaveProject();

        var company = Project.Foundation.DesignCompany.OrganizationSnapshot;
        return new AlbumProject
        {
            ProjectId = Project.ProjectId,
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
            ApprovalWorkflow = Project.Foundation.ApprovalWorkflow.Clone(),
            Company = company,
            Participants = Project.Foundation.DesignCompany.Members
                .SelectMany(member => member.Roles.DefaultIfEmpty("").Select(role => new ProjectParticipant
                {
                    FamilyName = member.FamilyName,
                    GivenName = member.GivenName,
                    FullName = member.FullName,
                    Email = member.Email,
                    Role = role,
                }))
                .ToList(),
            DesignSources = Project.Sources,
            BuildingGroups = Project.BuildingGroups
                .Select(group => group.Clone())
                .ToList(),
            SheetBuildingAssignments = new Dictionary<string, string>(
                Project.SheetBuildingAssignments,
                StringComparer.OrdinalIgnoreCase),
            Visualizations = Project.Visualizations.CreateProjectSnapshot(Project.ProjectId),
            SiteContext = Project.SiteContext.CreateProjectSnapshot(Project.ProjectId),
            SourceFolders = Project.Sources.Select(source => source.InboxFolder).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Album = Album,
            OutputFolder = ResolveOutputFolder(),
            ProjectFolder = ResolveProjectFolder(),
        };
    }

    public ProjectAssetSourceReconciliationResult ReconcileProjectAssetSources()
    {
        ProjectAssetSourceReconciliationResult result = ReconcileProjectAssetSourcesCore();
        if (ApplyAssetReconciliation(result))
            SaveProject();
        return result;
    }

    public CityGenProjectSiteReconciliationResult ReconcileCityGenProjectSite()
    {
        CityGenProjectSiteReconciliationResult result = ReconcileCityGenProjectSiteCore();
        if (ApplyCityGenProjectSiteReconciliation(result))
            SaveProject();
        return result;
    }

    public void MarkFoundationContentChanged()
    {
        if (!HasOpenProject)
            return;
        Project.Foundation.Version = Math.Max(1, Project.Foundation.Version) + 1;
        AlbumDocument.FoundationVersion = Project.Foundation.Version;
        ProjectCloudSyncMetadata.MarkAlbumComponentsPending(
            Project,
            [
                ProjectCloudSyncMetadata.CoverComponentCode,
                ProjectCloudSyncMetadata.CompanyRegistrationComponentCode,
                ProjectCloudSyncMetadata.CompanyLicenseComponentCode,
                ProjectCloudSyncMetadata.ApprovedAtdComponentCode,
            ]);
        InvalidateBuiltAlbum();
        SaveProject();
    }

    public void MarkAlbumComponentChanged(string componentCode)
    {
        if (!HasOpenProject || string.IsNullOrWhiteSpace(componentCode))
            return;
        ProjectCloudSyncMetadata.MarkAlbumComponentsPending(Project, [componentCode]);
    }

    public void MarkSiteContextChanged()
    {
        if (!HasOpenProject)
            return;
        ProjectCloudSyncMetadata.MarkAlbumComponentsPending(
            Project,
            [ProjectCloudSyncMetadata.SiteContextComponentCode]);
        InvalidateBuiltAlbum();
        SaveProject();
    }

    public bool RefreshProjectDocumentMetadata()
    {
        ProjectAssetSourceReconciliationResult result = ReconcileProjectAssetSourcesCore();
        CityGenProjectSiteReconciliationResult siteResult = ReconcileCityGenProjectSiteCore();
        return ApplyAssetReconciliation(result) |
               ApplyCityGenProjectSiteReconciliation(siteResult);
    }

    private ProjectAssetSourceReconciliationResult ReconcileProjectAssetSourcesCore()
    {
        if (!HasOpenProject || string.IsNullOrWhiteSpace(ProjectPath))
            return new ProjectAssetSourceReconciliationResult();
        return ProjectAssetSourceReconciler.ReconcileProject(Project, ProjectPath);
    }

    private CityGenProjectSiteReconciliationResult ReconcileCityGenProjectSiteCore()
    {
        if (!HasOpenProject)
            return new CityGenProjectSiteReconciliationResult();
        return CityGenProjectSiteReconciler.Reconcile(Project);
    }

    private bool ApplyAssetReconciliation(ProjectAssetSourceReconciliationResult result)
    {
        if (!result.Changed)
            return false;
        Project.Foundation.Version = Math.Max(1, Project.Foundation.Version) + 1;
        AlbumDocument.FoundationVersion = Project.Foundation.Version;
        InvalidateBuiltAlbum();
        return true;
    }

    private bool ApplyCityGenProjectSiteReconciliation(
        CityGenProjectSiteReconciliationResult result)
    {
        if (!result.Changed)
            return false;
        ProjectCloudSyncMetadata.MarkAlbumComponentsPending(
            Project,
            [ProjectCloudSyncMetadata.SiteContextComponentCode]);
        InvalidateBuiltAlbum();
        return true;
    }

    public void RecordBuiltAlbum(string outputPath, int pageCount, string pageSizeSummary, string createdBy)
    {
        ProjectCloudSyncMetadata.RecordBuiltAlbum(
            Project,
            AlbumDocument,
            ProjectPath!,
            outputPath,
            pageCount,
            pageSizeSummary,
            createdBy);
        Project.PrimaryAlbum.RendererRevision = StudioAlbumRendererMigration.CurrentRevision;
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

    private void ResetRuntimeServices(bool scanExistingPackages = true)
    {
        ClearWatchers();
        RefreshAssetSourceWatchers();
        Library.Clear();
        foreach (var source in Project.Sources)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(source.InboxFolder))
                {
                    Directory.CreateDirectory(source.InboxFolder);
                    Intake.WatchFolder(
                        source.InboxFolder,
                        source.UseLegacySheetKeys ? null : source.Id,
                        Project.ProjectId,
                        scanExisting: scanExistingPackages);
                }
                if (source.Metadata.TryGetValue("LegacyInboxFolder", out var legacyInbox) &&
                    !string.IsNullOrWhiteSpace(legacyInbox) &&
                    ProjectWorkspacePaths.IsInside(ResolveProjectFolder(), legacyInbox))
                {
                    Intake.WatchFolder(
                        legacyInbox,
                        projectId: Project.ProjectId,
                        scanExisting: scanExistingPackages);
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

    public void RefreshAssetSourceWatchers()
    {
        if (!HasOpenProject)
        {
            ClearAssetSourceWatchers();
            return;
        }

        HashSet<string> paths = EnumerateLinkedAssetPaths()
            .Select(TryGetFullPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        lock (assetWatcherGate)
        {
            DisposeAssetWatchersUnsafe();
            watchedAssetPaths = paths;
            foreach (IGrouping<string, string> directoryGroup in paths
                         .Where(path => !string.IsNullOrWhiteSpace(Path.GetDirectoryName(path)))
                         .GroupBy(path => Path.GetDirectoryName(path)!, StringComparer.OrdinalIgnoreCase))
            {
                if (!Directory.Exists(directoryGroup.Key))
                    continue;
                try
                {
                    var watcher = new FileSystemWatcher(directoryGroup.Key)
                    {
                        IncludeSubdirectories = false,
                        NotifyFilter = NotifyFilters.FileName |
                                       NotifyFilters.LastWrite |
                                       NotifyFilters.Size |
                                       NotifyFilters.CreationTime,
                    };
                    watcher.Changed += OnAssetSourceFileChanged;
                    watcher.Created += OnAssetSourceFileChanged;
                    watcher.Deleted += OnAssetSourceFileChanged;
                    watcher.Renamed += OnAssetSourceFileRenamed;
                    watcher.EnableRaisingEvents = true;
                    assetWatchers.Add(watcher);
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    // Manual "check for updates" remains available for folders
                    // that cannot be watched by the current Windows account.
                }
            }
        }
    }

    private IEnumerable<string> EnumerateLinkedAssetPaths()
    {
        IEnumerable<ProjectFileReference> documents =
            Project.Foundation.InitiationBasis.Documents
                .Concat(Project.Foundation.PlanningTask.Documents)
                .Concat(Project.Foundation.DesignCompany.OrganizationSnapshot.RegistrationCertificateDocuments)
                .Concat(Project.Foundation.DesignCompany.OrganizationSnapshot.DesignLicenseDocuments);
        foreach (ProjectFileReference document in documents)
        {
            if (!string.IsNullOrWhiteSpace(document.LinkedSourcePath))
                yield return document.LinkedSourcePath;
        }
        foreach (ProjectVisualizationImage image in Project.Visualizations.ImagesForProject(Project.ProjectId))
        {
            if (!string.IsNullOrWhiteSpace(image.LinkedSourcePath))
                yield return image.LinkedSourcePath;
        }
        foreach (string sidecarPath in CityGenProjectSiteReconciler.EnumerateSidecarPaths(Project.Sources))
            yield return sidecarPath;
    }

    private void OnAssetSourceFileChanged(object sender, FileSystemEventArgs eventArgs) =>
        RaiseAssetSourceChangedIfWatched(eventArgs.FullPath);

    private void OnAssetSourceFileRenamed(object sender, RenamedEventArgs eventArgs)
    {
        RaiseAssetSourceChangedIfWatched(eventArgs.OldFullPath);
        RaiseAssetSourceChangedIfWatched(eventArgs.FullPath);
    }

    private void RaiseAssetSourceChangedIfWatched(string path)
    {
        string fullPath = TryGetFullPath(path);
        bool isWatched;
        lock (assetWatcherGate)
        {
            isWatched = !string.IsNullOrWhiteSpace(fullPath) && watchedAssetPaths.Contains(fullPath);
        }
        if (isWatched)
            AssetSourcesChanged?.Invoke();
    }

    private static string TryGetFullPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "";
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                ArgumentException or NotSupportedException)
        {
            return "";
        }
    }

    private void ClearAssetSourceWatchers()
    {
        lock (assetWatcherGate)
        {
            DisposeAssetWatchersUnsafe();
            watchedAssetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void DisposeAssetWatchersUnsafe()
    {
        foreach (FileSystemWatcher watcher in assetWatchers)
            watcher.Dispose();
        assetWatchers.Clear();
    }

    private int RemoveSourcePagesFromSourceFreeProject()
    {
        if (Project.Sources.Count != 0)
        {
            return 0;
        }

        // A Cloud mirror may intentionally have no native source files on this
        // device. Persisted sheet/page references can belong to collaborators.
        if (Project.Cloud.Origin.Equals(ProjectOrigins.Cloud, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(Project.Cloud.ServerProjectId))
        {
            return 0;
        }

        int removedReferenceCount = Album.Pages.RemoveAll(page =>
            !string.IsNullOrWhiteSpace(page.SheetKey));
        foreach (AlbumSection section in Album.Sections)
        {
            removedReferenceCount += section.SheetKeys.Count;
            section.SheetKeys.Clear();
        }

        if (removedReferenceCount > 0)
        {
            InvalidateBuiltAlbum();
        }
        return removedReferenceCount;
    }

    private int RemoveAlbumPagesForSource(
        ProjectDesignSource source,
        IReadOnlySet<string> knownSourceKeys)
    {
        bool BelongsToRemovedSource(string key) =>
            knownSourceKeys.Contains(key) ||
            (!source.UseLegacySheetKeys &&
             key.StartsWith(source.Id.Trim().ToLowerInvariant() + "|", StringComparison.Ordinal));

        int removedPageCount = Album.Pages.RemoveAll(page =>
            !string.IsNullOrWhiteSpace(page.SheetKey) && BelongsToRemovedSource(page.SheetKey));
        foreach (AlbumSection section in Album.Sections)
        {
            section.SheetKeys.RemoveAll(key => BelongsToRemovedSource(key));
        }
        return removedPageCount;
    }

    private static bool SourceRecordBelongsTo(SheetRecord record, ProjectDesignSource source)
    {
        if (!source.UseLegacySheetKeys)
        {
            return string.Equals(record.SourceId, source.Id, StringComparison.OrdinalIgnoreCase);
        }

        if (!string.IsNullOrWhiteSpace(record.SourceId))
        {
            return false;
        }
        return !string.IsNullOrWhiteSpace(source.InboxFolder) &&
            ProjectWorkspacePaths.IsInside(source.InboxFolder, record.ManifestPath);
    }

    private void InvalidateBuiltAlbum()
    {
        ProjectAlbumRecord album = Project.PrimaryAlbum;
        album.LastPdfPath = "";
        album.LastPdfSha256 = "";
        album.LastPageCount = 0;
        album.LastPageSizeSummary = "";
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
        ClearAssetSourceWatchers();
        Intake.Dispose();
    }
}
