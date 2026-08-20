using System.IO;
using ErkS.Platform.Contracts;
using ErkS.Platform.Core;
using ErkS.Platform.Core.ProjectTypes;
using ErkS.Platform.Pdf;

namespace ErkS.Studio;

public sealed record PackageRecordResult(string SourceId, int RemovedAlbumPageCount);

/// <summary>
/// Runtime state of one explicitly opened project workspace. There is no
/// synthetic project while Studio is showing the project catalog.
/// </summary>
public sealed class AppState : IDisposable
{
    private long workspaceEpoch;
    private ProjectWorkspace? project;
    private StudioAlbumDocument? albumDocument;
    private readonly object assetWatcherGate = new();
    private readonly List<FileSystemWatcher> assetWatchers = [];
    private HashSet<string> watchedAssetPaths = new(StringComparer.OrdinalIgnoreCase);
    private string runtimeAccountEmail = "";
    private string runtimeDeviceFingerprint = "";

    public bool HasOpenProject => project is not null;

    public ProjectWorkspace Project => project
        ?? throw new InvalidOperationException("No project workspace is open.");

    public StudioAlbumDocument AlbumDocument => albumDocument
        ?? throw new InvalidOperationException("No project album is open.");

    public AlbumDefinition Album => AlbumDocument.Definition;

    public string? ProjectPath { get; private set; }

    public long WorkspaceEpoch => Interlocked.Read(ref workspaceEpoch);

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

    public void ConfigureSourceRuntimeContext(
        string? currentAccountEmail,
        string? currentDeviceFingerprint)
    {
        string accountEmail =
            (currentAccountEmail ?? "").Trim().ToLowerInvariant();
        string deviceFingerprint =
            (currentDeviceFingerprint ?? "").Trim().ToLowerInvariant();
        if (accountEmail.Equals(
                runtimeAccountEmail,
                StringComparison.OrdinalIgnoreCase) &&
            deviceFingerprint.Equals(
                runtimeDeviceFingerprint,
                StringComparison.Ordinal))
        {
            return;
        }

        runtimeAccountEmail = accountEmail;
        runtimeDeviceFingerprint = deviceFingerprint;
        if (HasOpenProject)
        {
            _ = UpgradeSourceMetadata();
            ResetRuntimeServices(scanExistingPackages: false);
        }
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
        Interlocked.Increment(ref workspaceEpoch);
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
                ProjectAlbumTemplateResolver.Apply(project, albumDocument);
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

        StudioSourceMetadataUpgradeReport sourceMetadataUpgrade =
            UpgradeSourceMetadata(persistChanges: false);
        bool removedUnownedSourcePages = RemoveSourcePagesFromSourceFreeProject() > 0;
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
        if (EnsureUniqueSourceInboxes(RuntimeSources()) ||
            reconciledSite ||
            removedUnownedSourcePages ||
            recoveredSiteContextSnapshots ||
            sourceMetadataUpgrade.ChangedCount > 0)
        {
            SaveProject();
        }
        ResetRuntimeServices(scanExistingPackages: false);
        Interlocked.Increment(ref workspaceEpoch);
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
        Interlocked.Increment(ref workspaceEpoch);
        ProjectReplaced?.Invoke();
    }

    internal void LinkCurrentProjectToCloud(
        StudioCloudProjectDetail cloudProject,
        string serverUrl,
        string permissionSnapshotAccountEmail,
        ProjectCreationRequest? creationRequest = null,
        bool preserveCreation = false,
        bool preserveSyncState = false)
    {
        ArgumentNullException.ThrowIfNull(cloudProject);
        StudioProjectCloudIsolation.ValidateEnvelope(cloudProject);
        StudioCloudProjectSummary summary = cloudProject.Project;
        if (string.IsNullOrWhiteSpace(summary.ProjectId))
        {
            throw new InvalidDataException("Cloud project ID is empty.");
        }

        bool preserveBuildingComposition = Project.Cloud.BuildingCompositionPending;
        string localProjectType = Project.Identity.ProjectType;
        string localStageCode = Project.Identity.StageCode;
        string localStageName = Project.Identity.StageName;
        ProjectCanonicalSyncService.Apply(Project, ToServerSnapshot(cloudProject));
        StudioCloudProjectInformation cloudInformation = cloudProject.ProjectInformation ?? new();
        // During creation the user's explicit classification is authoritative.
        // Never let a stale/default Cloud template response silently turn an
        // urban-planning project into a building concept project.
        if (creationRequest is not null)
        {
            Project.Identity.ProjectType = creationRequest.ProjectType.Trim();
            Project.Identity.StageCode = creationRequest.InitialStageType.Trim();
            Project.Identity.StageName = creationRequest.InitialStageName.Trim();
        }
        else if (preserveCreation)
        {
            // A local project edit or creation request remains authoritative until
            // Cloud has accepted that classification. Several refresh paths use
            // preserveCreation while reconciling the returned canonical snapshot;
            // without restoring these values an older/default Cloud stage changes
            // working drawings back to model design on every refresh.
            Project.Identity.ProjectType = localProjectType;
            Project.Identity.StageCode = localStageCode;
            Project.Identity.StageName = localStageName;
        }
        else
        {
            string cloudProjectType = StudioProjectCreationClassification.ResolveCloudProjectType(cloudProject);
            string cloudStageType = StudioProjectCreationClassification.ResolveCloudStageType(cloudProject);
            if (!string.IsNullOrWhiteSpace(cloudProjectType))
                Project.Identity.ProjectType = cloudProjectType;
            if (!string.IsNullOrWhiteSpace(cloudStageType))
            {
                Project.Identity.StageCode = cloudStageType;
                Project.Identity.StageName = StudioProjectCreationClassification.ResolveStageName(
                    cloudProjectType,
                    cloudStageType);
            }
        }
        ProjectAlbumTemplateResolver.Apply(Project, AlbumDocument);
        Project.Cloud.ServerUrl = serverUrl.TrimEnd('/');
        if (!preserveSyncState)
            Project.Cloud.SyncStatus = ProjectSyncStatuses.Linked;
        Project.Cloud.PermissionSnapshotAccountEmail =
            (permissionSnapshotAccountEmail ?? "")
                .Trim()
                .ToLowerInvariant();
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
                SourcePurpose = StudioSourcePurpose.Normalize(
                    source.SourcePurpose),
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
        bool usesBuildingComposition = Project.Identity.ProjectType.Equals(
            BuildingDesignProjectType.TypeId,
            StringComparison.OrdinalIgnoreCase);
        StudioBuildingCompositionApplyResult buildingCompositionApply = usesBuildingComposition
            ? StudioBuildingCompositionSync.ApplyCanonicalWithResult(
                Project,
                Library,
                cloudProject.BuildingComposition,
                preserveBuildingComposition)
            : new StudioBuildingCompositionApplyResult(false, false);
        if (!usesBuildingComposition)
        {
            Project.BuildingGroups.Clear();
            Project.SheetBuildingAssignments.Clear();
        }
        if (buildingCompositionApply.LocalCompositionChanged &&
            !preserveBuildingComposition)
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
                SectionKey = component.SectionKey ?? "",
                SequenceKey = component.SequenceKey ?? "",
                Pages = (component.Pages ?? [])
                    .Select(page =>
                        new ProjectCloudAlbumComponentPageReference
                        {
                            PageNumber = page.PageNumber,
                            PageKey = page.PageKey ?? "",
                            SortKey = page.SortKey ?? "",
                            SectionKey = page.SectionKey ?? "",
                            SequenceKey = page.SequenceKey ?? "",
                        })
                    .ToList(),
            })
            .ToList();
        _ = StudioCanonicalAlbumRebuildPolicy.Apply(Project, cloudProject);

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

        Dictionary<string, ProjectStageOrganizationAssignment> existingAssignments =
            (Project.StageAssignments ?? [])
            .Where(item => !string.IsNullOrWhiteSpace(item.AssignmentId))
            .ToDictionary(item => item.AssignmentId, StringComparer.OrdinalIgnoreCase);
        IStudioProjectTypeDefinition projectType = StudioProjectTypeRegistry.Resolve(Project.Identity.ProjectType);
        Project.Stages = (cloudProject.Stages ?? [])
            .OrderBy(item => item.Sequence)
            .Select(item => new ProjectStageInstance
            {
                StageInstanceId = item.StageInstanceId,
                StageType = item.StageType,
                StageName = projectType.Stages.FirstOrDefault(stage =>
                    stage.Id.Equals(item.StageType, StringComparison.OrdinalIgnoreCase))?.Label ?? item.StageType,
                Sequence = item.Sequence,
                PreviousStageInstanceId = item.PreviousStageInstanceId,
                BasisAlbumRevisionId = item.BasisAlbumRevisionId,
                Status = item.Status,
                CreatedAtUtc = item.CreatedAtUtc,
                CompletedAtUtc = item.CompletedAtUtc,
            })
            .ToList();
        Project.StageAssignments = (cloudProject.OrganizationAssignments ?? [])
            .Select(item =>
            {
                CompanyProfile snapshot = item.OrganizationProfile is not null
                    ? StudioCompanyProfileMapper.FromRenderProfile(item.OrganizationProfile)
                    : existingAssignments.TryGetValue(item.AssignmentId, out ProjectStageOrganizationAssignment? existing)
                        ? existing.OrganizationSnapshot.Clone()
                    : item.OrganizationId.Equals(cloudOrganizationId, StringComparison.OrdinalIgnoreCase) && cloudCompany is not null
                        ? cloudCompany.Clone()
                        : new CompanyProfile { OrganizationId = item.OrganizationId };
                return new ProjectStageOrganizationAssignment
                {
                    AssignmentId = item.AssignmentId,
                    StageInstanceId = item.StageInstanceId,
                    OrganizationId = item.OrganizationId,
                    OrganizationSnapshot = snapshot,
                    Role = item.Role,
                    Status = item.Status,
                    AcceptedAtUtc = item.AcceptedAtUtc,
                    EndedAtUtc = item.EndedAtUtc,
                };
            })
            .ToList();
        if (Project.Stages.Count == 0)
            ProjectStageLifecycle.EnsureLegacyStage(Project);

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
        if (client is not null && cloudProject.Foundation is null)
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
        RefreshSourceRuntimeWatchers();
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

    public bool RefreshAlbumTemplateForProjectClassification() =>
        ProjectAlbumTemplateResolver.Apply(Project, AlbumDocument);

    internal StudioSourceMetadataUpgradeReport UpgradeSourceMetadata(
        bool persistChanges = true)
    {
        StudioSourceMetadataUpgradeReport report =
            StudioSourceMetadataUpgradePolicy.Apply(
                Project,
                runtimeAccountEmail,
                runtimeDeviceFingerprint,
                source =>
                    StudioLocalSourceBindingPolicy
                        .HasVerifiedLegacyUpgradePayload(Project, source));
        if (persistChanges && report.ChangedCount > 0)
            SaveProject();
        return report;
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
        // Native paths are workstation-local and may remain on disk after a
        // different account signs in. Never use path/name equality to adopt a
        // Cloud mirror owned by another account or bound to another device.
        // Reusing an existing row is safe only when that row is already an
        // authorized, verified local source for the current runtime identity.
        var existingSource = Project.Sources.FirstOrDefault(existing =>
            IsRuntimeSource(existing) &&
            (string.Equals(existing.Id, source.Id, StringComparison.OrdinalIgnoreCase) ||
             (existing.Kind == source.Kind &&
              PathsEqual(existing.NativeDocumentPath, source.NativeDocumentPath)) ||
             (existing.Kind == source.Kind &&
              string.IsNullOrWhiteSpace(existing.NativeDocumentPath) &&
              string.Equals(existing.Name, source.Name, StringComparison.OrdinalIgnoreCase))));
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
            existingSource.Metadata ??=
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach ((string key, string value) in source.Metadata ??
                     new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))
            {
                existingSource.Metadata[key] = value;
            }
            foreach (string inactiveSheetId in source.InactiveSheetIds ?? [])
            {
                existingSource.SetSheetActive(inactiveSheetId, active: false);
            }
            foreach (ProjectInactiveSourceSheetState inactiveState in source.InactiveSheetStates ?? [])
            {
                existingSource.StoreInactiveSheetState(inactiveState);
            }
            existingSource.NormalizeSheetActivityState();
            if (!string.Equals(previousInbox, existingSource.InboxFolder, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(previousInbox))
            {
                Intake.UnwatchFolder(previousInbox);
            }
            source = existingSource;
        }
        SaveProject();
        if (IsRuntimeSource(source))
        {
            Intake.WatchFolder(
                source.InboxFolder,
                source.UseLegacySheetKeys ? null : source.Id,
                Project.ProjectId);
        }
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

        int removedPageCount =
            RemoveAlbumPagesForSource(source, knownSourceKeys);
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
        StudioBuildingCompositionSync.RecordLocalGroupSet(
            Project,
            normalizedGroups);
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
        ProjectDesignSource? admittedSource =
            StudioRuntimeSourceScope.ResolvePackageSource(
                Project,
                result,
                runtimeAccountEmail,
                runtimeDeviceFingerprint);
        if (admittedSource is null)
            return null;

        ProjectPackageReconciliationResult? reconciled =
            ProjectPackageReconciliationService.Apply(Project, Album, Library, result);
        if (reconciled is null ||
            !reconciled.SourceId.Equals(
                admittedSource.Id,
                StringComparison.OrdinalIgnoreCase))
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

    public void SetSourceSheetActivity(
        ProjectDesignSource source,
        IEnumerable<string> sheetIds,
        bool active)
    {
        ProjectPackageReconciliationService.SetSheetActivity(
            Project,
            Album,
            Library,
            source,
            sheetIds,
            active);
        InvalidateBuiltAlbum();
        SaveProject();
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

    public AlbumProject CreateAlbumBuildProject(
        bool reconcileLinkedProjectAssets = true)
    {
        if (reconcileLinkedProjectAssets)
        {
            ProjectAssetSourceReconciliationResult assetReconciliation =
                ReconcileProjectAssetSourcesCore();
            CityGenProjectSiteReconciliationResult siteReconciliation =
                ReconcileCityGenProjectSiteCore();
            if (ApplyAssetReconciliation(assetReconciliation) |
                ApplyCityGenProjectSiteReconciliation(siteReconciliation))
            {
                SaveProject();
            }
        }

        CompanyProfile company = Project.Foundation.DesignCompany.OrganizationSnapshot;
        ProjectServerSnapshot server = Project.Cloud.ServerSnapshot ?? new ProjectServerSnapshot();
        ProjectServerInitiationBasis serverBasis =
            server.Foundation?.InitiationBasis ?? new ProjectServerInitiationBasis();
        ProjectInitiationBasis initiationBasis = CloneInitiationBasis(
            Project.Foundation.InitiationBasis);
        initiationBasis.ClientName = FirstAlbumValue(
            initiationBasis.ClientName,
            serverBasis.ClientName,
            server.ClientName);
        initiationBasis.SiteAddress = FirstAlbumValue(
            initiationBasis.SiteAddress,
            serverBasis.SiteAddress,
            server.Information?.Location,
            server.SiteAndLand?.Addresses?.FirstOrDefault());
        initiationBasis.LandReference = FirstAlbumValue(
            initiationBasis.LandReference,
            serverBasis.LandReference,
            server.SiteAndLand?.ParcelNumbers is { Count: > 0 } parcelNumbers
                ? string.Join(", ", parcelNumbers)
                : "");
        initiationBasis.Summary = FirstAlbumValue(
            initiationBasis.Summary,
            serverBasis.Summary,
            server.Information?.BuildingPurpose);
        string projectName = FirstAlbumValue(
            Project.Name,
            server.Name,
            server.Information?.Name);
        string projectDescription = FirstAlbumValue(
            Project.Identity.Description,
            serverBasis.Summary,
            server.Information?.BuildingPurpose);
        string planningAuthorityName = FirstAlbumValue(
            Project.Foundation.PlanningTask.IssuingAuthorityName,
            server.Foundation?.PlanningTask?.IssuingAuthorityName,
            server.PlanningAuthorityName);
        string designOrganizationName = FirstAlbumValue(
            Project.DesignOrganizationName,
            server.DesignOrganizationName,
            company.Name);
        return new AlbumProject
        {
            ProjectId = Project.ProjectId,
            Name = projectName,
            Code = Project.Code,
            Description = projectDescription,
            ServerProjectId = Project.Cloud.ServerProjectId,
            ServerUrl = Project.Cloud.ServerUrl,
            CloudProjectCode = Project.Cloud.CloudProjectCode,
            ClientName = initiationBasis.ClientName,
            PlanningAuthorityName = planningAuthorityName,
            DesignOrganizationName = designOrganizationName,
            CloudStatus = Project.Cloud.SyncStatus,
            InitiationBasis = initiationBasis,
            PlanningTask = CreateAlbumPlanningTaskSnapshot(),
            ApprovalWorkflow = Project.Foundation.ApprovalWorkflow.Clone(),
            Company = company,
            Participants = Project.Foundation.DesignCompany.Members
                .SelectMany(member => member.Roles.DefaultIfEmpty("").Select(role => new ProjectParticipant
                {
                    ParticipantId = member.Id,
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
            Visualizations = CreateAlbumVisualizationSnapshot(),
            SiteContext = Project.SiteContext.CreateProjectSnapshot(Project.ProjectId),
            SourceFolders = Project.Sources.Select(source => source.InboxFolder).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Album = Album,
            OutputFolder = ResolveOutputFolder(),
            ProjectFolder = ResolveProjectFolder(),
        };
    }

    private static ProjectInitiationBasis CloneInitiationBasis(ProjectInitiationBasis source) => new()
    {
        SourceType = source.SourceType,
        RequestNumber = source.RequestNumber,
        RequestedAtUtc = source.RequestedAtUtc,
        ClientType = source.ClientType,
        ClientName = source.ClientName,
        ClientEmail = source.ClientEmail,
        ClientRepresentativePosition = source.ClientRepresentativePosition,
        ClientRepresentativeName = source.ClientRepresentativeName,
        ClientOrganizationSnapshot = source.ClientOrganizationSnapshot.Clone(),
        SiteAddress = source.SiteAddress,
        LandReference = source.LandReference,
        SourceOrganizationName = source.SourceOrganizationName,
        ServerRecordId = source.ServerRecordId,
        Summary = source.Summary,
        Documents = source.Documents.Select(document => document.Clone()).ToList(),
    };

    private PlanningTaskInformation CreateAlbumPlanningTaskSnapshot()
    {
        PlanningTaskInformation source = Project.Foundation.PlanningTask;
        IReadOnlyList<ProjectFileReference> documents =
            StudioAuxiliarySourceLocalityPolicy.LocalDocuments(
                Project,
                source.Documents,
                runtimeAccountEmail,
                runtimeDeviceFingerprint,
                HasVerifiedPayload);
        return new PlanningTaskInformation
        {
            AtdNumber = source.AtdNumber,
            IssuedAtUtc = source.IssuedAtUtc,
            IssuingAuthorityName = source.IssuingAuthorityName,
            Status = source.Status,
            Summary = source.Summary,
            Requirements = source.Requirements.ToList(),
            Documents = documents.Select(document => document.Clone()).ToList(),
            ServerDocumentId = source.ServerDocumentId,
            ServerDocumentVersion = source.ServerDocumentVersion,
            DocumentCloudSyncStatus = source.DocumentCloudSyncStatus,
            AuthorityMembers = source.AuthorityMembers.ToList(),
        };
    }

    private ProjectVisualizationSource CreateAlbumVisualizationSnapshot() =>
        StudioAuxiliarySourceLocalityPolicy.CreateLocalVisualizationSnapshot(
            Project,
            runtimeAccountEmail,
            runtimeDeviceFingerprint,
            HasVerifiedPayload);

    private static string FirstAlbumValue(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";

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

    public CityGenProjectSiteReconciliationResult ReconcileCityGenProjectSite(
        IEnumerable<ProjectDesignSource> sources)
    {
        HashSet<string> requestedSourceIds = (sources ?? [])
            .Where(source => source is not null)
            .Select(source => source.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<ProjectDesignSource> authorizedSources =
            RuntimeSources()
                .Where(source => requestedSourceIds.Contains(source.Id))
                .ToList();
        CityGenProjectSiteReconciliationResult result = HasOpenProject
            ? CityGenProjectSiteReconciler.Reconcile(
                Project,
                authorizedSources)
            : new CityGenProjectSiteReconciliationResult();
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
            ]);
        ProjectCloudSyncMetadata.MarkCanonicalTitleBlockPending(Project);
        InvalidateBuiltAlbum();
        SaveProject();
    }

    public void MarkAlbumComponentChanged(string componentCode)
    {
        if (!HasOpenProject || string.IsNullOrWhiteSpace(componentCode))
            return;
        ProjectCloudSyncMetadata.MarkAlbumComponentsPending(Project, [componentCode]);
    }

    public void MarkAuxiliaryAlbumComponentChanged(
        string componentCode,
        string? ownerEmail,
        string? deviceFingerprint,
        bool isRemoval)
    {
        if (!HasOpenProject || string.IsNullOrWhiteSpace(componentCode))
            return;
        if (!StudioAuxiliarySourceLocalityPolicy.IsCloudLinked(Project))
        {
            ProjectCloudSyncMetadata.MarkAlbumComponentsPending(
                Project,
                [componentCode]);
            return;
        }

        ProjectCloudSyncMetadata.MarkAlbumComponentPendingForBinding(
            Project,
            componentCode,
            ownerEmail ?? "",
            deviceFingerprint ?? "",
            isRemoval);
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
        return ProjectAssetSourceReconciler.ReconcileProject(
            Project,
            ProjectPath,
            document => StudioAuxiliarySourceLocalityPolicy.BindingMatches(
                Project,
                document,
                runtimeAccountEmail,
                runtimeDeviceFingerprint),
            image => StudioAuxiliarySourceLocalityPolicy.BindingMatches(
                Project,
                image,
                runtimeAccountEmail,
                runtimeDeviceFingerprint));
    }

    private CityGenProjectSiteReconciliationResult ReconcileCityGenProjectSiteCore()
    {
        if (!HasOpenProject)
            return new CityGenProjectSiteReconciliationResult();
        return CityGenProjectSiteReconciler.Reconcile(
            Project,
            RuntimeSources());
    }

    private bool ApplyAssetReconciliation(ProjectAssetSourceReconciliationResult result)
    {
        if (!result.Changed)
            return false;
        if (result.ChangedDocumentCategories.Contains(
                ProjectDocumentCategories.ApprovedPlanningTask,
                StringComparer.OrdinalIgnoreCase))
        {
            bool hasLocalAtdPayload =
                Project.Foundation.PlanningTask.Documents.Any(document =>
                    document.Category.Equals(
                        ProjectDocumentCategories.ApprovedPlanningTask,
                        StringComparison.OrdinalIgnoreCase) &&
                    document.IsAvailable &&
                    StudioAuxiliarySourceLocalityPolicy.IsLocalDocument(
                        Project,
                        document,
                        runtimeAccountEmail,
                        runtimeDeviceFingerprint,
                        HasVerifiedPayload(document)));
            MarkAuxiliaryAlbumComponentChanged(
                ProjectCloudSyncMetadata.ApprovedAtdComponentCode,
                runtimeAccountEmail,
                runtimeDeviceFingerprint,
                isRemoval: !hasLocalAtdPayload);
        }

        if (result.ChangedVisualizationIds.Count > 0)
        {
            HashSet<string> changedImageIds = result.ChangedVisualizationIds
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            bool includedImageChanged = Project.Visualizations
                .ImagesForProject(Project.ProjectId)
                .Any(image =>
                    changedImageIds.Contains(image.Id) &&
                    image.IsIncludedInAlbum);
            if (includedImageChanged)
            {
                bool hasIncludedLocalPayload = Project.Visualizations
                    .ImagesForProject(Project.ProjectId)
                    .Any(image =>
                        image.IsIncludedInAlbum &&
                        image.IsAvailable &&
                        StudioAuxiliarySourceLocalityPolicy.IsLocalVisualizationImage(
                            Project,
                            image,
                            runtimeAccountEmail,
                            runtimeDeviceFingerprint,
                            HasVerifiedPayload(image)));
                MarkAuxiliaryAlbumComponentChanged(
                    ProjectCloudSyncMetadata.VisualizationsComponentCode,
                    runtimeAccountEmail,
                    runtimeDeviceFingerprint,
                    isRemoval: !hasIncludedLocalPayload);
            }
        }

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
        var album = new StudioAlbumDocument
        {
            ProjectId = workspace.ProjectId,
            AlbumId = workspace.PrimaryAlbum.Id,
            PackageType = workspace.PrimaryAlbum.Type,
            Status = workspace.PrimaryAlbum.Status,
            FoundationVersion = workspace.Foundation.Version,
            StageCode = workspace.Identity.StageCode,
            Definition = ProjectAlbumTemplateResolver.CreateDefinition(workspace),
        };
        ProjectAlbumTemplateResolver.Apply(workspace, album);
        return album;
    }

    private void ResetRuntimeServices(bool scanExistingPackages = true)
    {
        Library.Clear();
        RefreshSourceRuntimeWatchers(scanExistingPackages);
    }

    public void RefreshSourceRuntimeWatchers(
        bool scanExistingPackages = false)
    {
        ClearWatchers();
        RefreshAssetSourceWatchers();
        foreach (ProjectDesignSource source in RuntimeSources())
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
            if (!string.IsNullOrWhiteSpace(document.LinkedSourcePath) &&
                StudioAuxiliarySourceLocalityPolicy.IsLocalDocument(
                    Project,
                    document,
                    runtimeAccountEmail,
                    runtimeDeviceFingerprint,
                    HasVerifiedPayload(document)))
            {
                yield return document.LinkedSourcePath;
            }
        }
        foreach (ProjectVisualizationImage image in Project.Visualizations.ImagesForProject(Project.ProjectId))
        {
            if (!string.IsNullOrWhiteSpace(image.LinkedSourcePath) &&
                StudioAuxiliarySourceLocalityPolicy.IsLocalVisualizationImage(
                    Project,
                    image,
                    runtimeAccountEmail,
                    runtimeDeviceFingerprint,
                    HasVerifiedPayload(image)))
            {
                yield return image.LinkedSourcePath;
            }
        }
        foreach (string sidecarPath in
                 CityGenProjectSiteReconciler.EnumerateSidecarPaths(
                     RuntimeSources()))
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

    private bool EnsureUniqueSourceInboxes(
        IEnumerable<ProjectDesignSource> sources)
    {
        var usedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var changed = false;
        foreach (ProjectDesignSource source in sources)
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

    internal IReadOnlySet<string> WatchedAssetPathsSnapshot()
    {
        lock (assetWatcherGate)
        {
            return watchedAssetPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
    }

    private bool HasVerifiedPayload(ProjectFileReference document) =>
        !string.IsNullOrWhiteSpace(ProjectPath) &&
        StudioAuxiliarySourceLocalityPolicy.HasVerifiedPayload(
            ProjectPath,
            document);

    private bool HasVerifiedPayload(ProjectVisualizationImage image) =>
        !string.IsNullOrWhiteSpace(ProjectPath) &&
        StudioAuxiliarySourceLocalityPolicy.HasVerifiedPayload(
            ProjectPath,
            image);

    private IReadOnlyList<ProjectDesignSource> RuntimeSources() =>
        HasOpenProject
            ? StudioRuntimeSourceScope.AuthorizedSources(
                Project,
                runtimeAccountEmail,
                runtimeDeviceFingerprint)
            : [];

    private bool IsRuntimeSource(ProjectDesignSource source) =>
        HasOpenProject &&
        StudioRuntimeSourceScope.IsAuthorizedLocal(
            Project,
            source,
            runtimeAccountEmail,
            runtimeDeviceFingerprint);

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
