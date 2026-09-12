using System.IO;
using ErkS.Platform.Contracts;
using ErkS.Platform.Core;
using ErkS.Platform.Core.ProjectTypes;
using ErkS.Platform.Pdf;

namespace ErkS.Studio;

public sealed record PackageRecordResult(
    string SourceId,
    int RemovedAlbumPageCount,
    int CreatedPortfolioItemCount = 0,
    int UpdatedPortfolioItemCount = 0,

    /// <summary>
    /// Sheets kept out of the album because they duplicate a page Studio
    /// composes. Empty on every well-formed package; not empty is worth saying
    /// out loud, which is what it is carried here for.
    /// </summary>
    IReadOnlyList<string>? StudioComposedPagesSkipped = null)
{
    public bool BroughtPortfolioPages =>
        CreatedPortfolioItemCount > 0 || UpdatedPortfolioItemCount > 0;
}

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
    private StudioRuntimeIdentity runtimeIdentity = StudioRuntimeIdentity.None;

    public bool HasOpenProject => project is not null;

    /// <summary>
    /// What the server last said about each participant, keyed by email.
    /// </summary>
    /// <remarks>
    /// Deliberately not part of the saved project. Presence is true only for
    /// the moment it was fetched; writing it to disk would mean reopening a
    /// project tomorrow and being told who was online yesterday, stated as
    /// though it were now.
    /// </remarks>
    public Dictionary<string, ParticipantPresenceInfo> ParticipantPresence { get; } =
        new(StringComparer.OrdinalIgnoreCase);

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

    /// <summary>
    /// The second delivery channel, watched beside the first. Two watchers
    /// rather than one wider filter: the channels answer to different
    /// contracts, and the sheet intake is the path a user's day runs through.
    /// </summary>
    public VisualIntakeService VisualIntake { get; } = new();

    public AlbumBuilder Builder { get; }

    public event Action? ProjectReplaced;
    public event Action? AssetSourcesChanged;

    /// <summary>
    /// A source whose delivery watch could not be established, with the reason.
    /// Nothing that arrives for that source will be noticed until it is fixed,
    /// so somebody has to be told.
    /// </summary>
    public event Action<ProjectDesignSource, Exception>? SourceWatchFailed;

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
        // The seat survives a sign-in change: that is the whole point of it.
        StudioRuntimeIdentity next = runtimeIdentity with
        {
            SessionEmail = accountEmail,
            DeviceFingerprint = deviceFingerprint,
        };
        if (next == runtimeIdentity)
        {
            return;
        }

        runtimeIdentity = next;
        if (HasOpenProject)
        {
            _ = UpgradeSourceMetadata();
            ResetRuntimeServices(scanExistingPackages: false);
        }
    }

    /// <summary>
    /// Binds this machine to a seat, so what it owns and receives stops
    /// following whoever is signed in. Passing an empty value hands the seat
    /// back to the session, which is the state every machine is in today.
    /// </summary>
    public void ConfigureDeviceSeat(string? seatEmail)
    {
        StudioRuntimeIdentity next =
            runtimeIdentity.WithSeat(seatEmail);
        if (next == runtimeIdentity)
        {
            return;
        }

        runtimeIdentity = next;
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
        bool relocatedSourceInboxes = false;
        bool relinkedNativeDocuments = false;
        if (string.Equals(Path.GetExtension(path), ProjectWorkspace.FileExtension, StringComparison.OrdinalIgnoreCase))
        {
            project = ProjectWorkspaceStore.Load(path);
            recoveredSiteContextSnapshots = ProjectWorkspaceStore.RecoverSiteContextSnapshots(project, path);
            // A project copied from another machine still names that machine's
            // inbox. Runs before the uniqueness pass so two relocated sources
            // landing on one folder are still separated.
            relocatedSourceInboxes =
                ProjectWorkspaceStore.RelocateSourceInboxesInsideProject(project, path);
            relinkedNativeDocuments =
                ProjectWorkspaceStore.RelinkNativeDocumentsInsideProject(project, path);
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
            relocatedSourceInboxes ||
            relinkedNativeDocuments ||
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
                SourceOwnerKind = (source.SourceOwnerKind ?? "").Trim(),
                // Not lowercased: a botId is an identifier, not an email.
                SourceOwnerRef = (source.SourceOwnerRef ?? "").Trim(),
                SourceOwnerDisplayName = (source.SourceOwnerDisplayName ?? "").Trim(),
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
        StudioCloudAlbumRevision? currentAlbumRevision =
            StudioCloudAlbumSelection.CurrentRevision(cloudProject);
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
                            Title = page.Title ?? "",
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
        ParticipantPresence.Clear();
        foreach (StudioCloudParticipant participant in activeParticipants)
        {
            if (string.IsNullOrWhiteSpace(participant.AccountEmail))
                continue;

            ParticipantPresence[participant.AccountEmail.Trim()] = new ParticipantPresenceInfo(
                participant.LastSeenAtUtc,
                participant.ProfileImageUrl,
                participant.Initials);
        }
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
            InformationConcurrencyToken = summary.InformationConcurrencyToken,
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
                runtimeIdentity.OwnerEmail,
                runtimeIdentity.DeviceFingerprint,
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
                VisualIntake.UnwatchFolder(previousInbox);
            }
            source = existingSource;
        }

        CityGenProjectSiteReconciliationResult siteResult =
            ReconcileCityGenProjectSiteCore();
        ApplyCityGenProjectSiteReconciliation(siteResult);

        SaveProject();
        if (IsRuntimeSource(source))
        {
            Intake.WatchFolder(
                source.InboxFolder,
                source.UseLegacySheetKeys ? null : source.Id,
                Project.ProjectId);
            VisualIntake.WatchFolder(source.InboxFolder, source.Id);
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

        CityGenProjectSiteReconciliationResult siteResult =
            ReconcileCityGenProjectSiteCore();
        ApplyCityGenProjectSiteReconciliation(siteResult);

        InvalidateBuiltAlbum();
        SaveProject();
        ResetRuntimeServices();
        return removedPageCount;
    }

    public void UpdateBuildingComposition(
        IEnumerable<ProjectBuildingGroup> groups,
        IReadOnlyDictionary<string, string> assignments)
    {
        // Read before anything is overwritten: the scope of a composition edit
        // is the DIFFERENCE it makes, and after the assignment below there is
        // nothing left to compare against.
        List<ProjectBuildingGroup> previousGroups = (Project.BuildingGroups ?? [])
            .Select(group => new ProjectBuildingGroup
            {
                Id = group.Id,
                Name = group.Name,
                Order = group.Order,
            })
            .ToList();
        Dictionary<string, string> previousAssignments = new(
            Project.SheetBuildingAssignments ?? [],
            StringComparer.OrdinalIgnoreCase);

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

        // 🔴 THE SUB-COVERS ARE NOT THE DRAWINGS. The call above marks the pages
        // that carry a group's title; the pages that carry its drawings live in
        // source components whose code CONTAINS the building, and nothing marked
        // those. So a correction took effect locally and never reached the cloud
        // album - which is the album Studio shows once a canonical one exists.
        // The rule is in its own type, with the reason and the measured case.
        IReadOnlyList<string> resliced =
            StudioBuildingCompositionResliceScope.SourceComponentCodes(
                Project,
                Album,
                previousGroups,
                previousAssignments,
                runtimeIdentity.OwnerEmail);
        if (resliced.Count > 0)
            ProjectCloudSyncMetadata.MarkAlbumComponentsPending(Project, resliced);

        // 🔴 THE STORED ORDER FOLLOWS THE COMPOSITION. Without this the groups
        // and assignments were updated, the built album was invalidated and the
        // project was saved - and Album.Pages kept the sequence the OLD
        // assignments produced. The album that Studio SHOWS reads that stored
        // sequence, so a user who corrected a building type saw their correction
        // ignored; the built PDF was right, because the builder derives its own
        // order, which is why the two disagreed.
        //
        // The same call already existed in two other places - the cloud-sync
        // path and the package-record path - each behind a condition that has
        // nothing to do with somebody editing the composition by hand. This is
        // the third and the only one on the user's own route.
        ReorderStoredAlbumPages();
        InvalidateBuiltAlbum();
        SaveProject();
    }

    /// <summary>
    /// Puts the stored page order back in step with the building composition.
    ///
    /// Every path that changes groups or assignments has to end here, or the
    /// list Studio shows drifts away from the album it builds. The rule itself
    /// lives in the sequencer and is tested there; this is only the connection,
    /// which is the half that keeps going missing.
    /// </summary>
    /// <summary>
    /// Puts a project whose stored order was left behind back in step, and
    /// reports whether it had to.
    ///
    /// 🔴 THE FIX ALONE ONLY PREVENTED. UpdateBuildingComposition now reorders,
    /// so a composition edited from today on stays consistent - but every
    /// project already saved with the old sequence keeps it, and the only cure
    /// would have been asking the user to open the dialog and press OK on
    /// assignments that are already correct. Their data was never wrong; only
    /// the order derived from it was stale.
    ///
    /// So the stored order is checked against the derived one where the album is
    /// shown, and corrected when they differ. Nothing is guessed: the sequencer
    /// is the same one the builder uses, so "corrected" means "made to agree
    /// with the album that would be built".
    ///
    /// It does nothing while the library is empty. The order is derived by
    /// resolving each page's sheet, and resolving against a library that has not
    /// been filled yet would reorder a project on no information at all - the
    /// same class of mistake as saving an empty location over a stored one.
    /// </summary>
    public AlbumOrderHealResult EnsureStoredAlbumOrder()
    {
        if (!HasOpenProject || Library.Snapshot().Count == 0 || Album.Pages.Count == 0)
            return AlbumOrderHealResult.NotRun;

        List<Guid> before = Album.Pages.Select(page => page.Id).ToList();
        ReorderStoredAlbumPages();
        List<Guid> after = Album.Pages.Select(page => page.Id).ToList();

        // Counted rather than compared, because the report says HOW MANY pages
        // moved and "nothing moved" has to arrive carrying its own number - a
        // bare false reads the same as a heal that never examined anything.
        int moved = 0;
        for (int index = 0; index < after.Count; index++)
        {
            if (index >= before.Count || before[index] != after[index])
                moved++;
        }

        if (moved == 0)
            return new AlbumOrderHealResult(true, after.Count, 0);

        // Saved, because the point is that reopening the project shows the
        // corrected order rather than doing this again every time.
        SaveProject();
        return new AlbumOrderHealResult(true, after.Count, moved);
    }

    private void ReorderStoredAlbumPages()
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

    /// <summary>Why the last package was not taken in, when one was not.</summary>
    public string LastPackageRefusal { get; private set; } = "";

    public PackageRecordResult? RecordPackageReceived(SheetPackageLoadResult result)
    {
        PackageAdmission admission = StudioRuntimeSourceScope.Admit(
            Project,
            result,
            runtimeIdentity.OwnerEmail,
            runtimeIdentity.DeviceFingerprint);
        ProjectDesignSource? admittedSource = admission.Source;
        if (admittedSource is null)
        {
            // Nothing quarantines a refused package, so this reason is the only
            // account of it that will ever exist. Losing it makes a delivery
            // that was refused look exactly like one that never arrived.
            LastPackageRefusal = admission.Refusal;
            return null;
        }
        LastPackageRefusal = "";
        // The source proved it belongs to this device; if it did so under the
        // older fingerprint, record the current one now while the project is
        // being saved anyway.
        StudioLocalSourceBindingPolicy.TryMigrateBinding(
            admittedSource,
            runtimeIdentity.OwnerEmail,
            runtimeIdentity.DeviceFingerprint);

        ProjectPackageReconciliationResult? reconciled =
            ProjectPackageReconciliationService.Apply(Project, Album, Library, result);
        if (reconciled is null ||
            !reconciled.SourceId.Equals(
                admittedSource.Id,
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        PortfolioSheetImportResult portfolioImport = PortfolioSheetImportResult.Empty;
        if (!string.IsNullOrWhiteSpace(ProjectPath))
        {
            portfolioImport = PortfolioSheetImportService.Import(
                Project,
                ProjectPath,
                result,
                reconciled.SourceId);
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
        return new PackageRecordResult(
            reconciled.SourceId,
            reconciled.RemovedAlbumPageCount,
            portfolioImport.CreatedItemCount,
            portfolioImport.UpdatedItemCount);
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

                // 🔴 SWEPT ONLY AFTER A RECONCILIATION THAT CHANGED SOMETHING, and
                // only after the save. Before the record is repointed the OLD copy
                // is still the referenced one and the new one does not exist yet -
                // a sweep there would find no orphan and do nothing but read the
                // disk. And a sweep before the save could delete a copy the project
                // on disk still names, if the save then failed.
                VisualizationStoreSweepResult sweep =
                    StudioVisualizationStoreMaintenance.Sweep(Project, ProjectPath);
                LastVisualizationStoreSweep = sweep;

                // 🔴 THE DELETION IS WRITTEN DOWN WHERE IT HAPPENS, NOT AT THE
                // CALLERS. Half a dozen places ask for a build project and any of
                // them may reconcile; recording the sweep at each would let the
                // next one added escape being written down - the same reason the
                // draw decision is recorded at its one exit.
                Project.PrimaryAlbum.LastDraw ??= new AlbumDrawRecord();
                Project.PrimaryAlbum.LastDraw.RecordStoreSweep(
                    sweep.RemovedCount,
                    sweep.RemovedBytes,
                    sweep.RefusalMn,
                    DateTimeOffset.UtcNow);

                // 🔴 SAVED AGAIN, AND ONLY WHEN THERE IS SOMETHING TO SAY. The
                // save above had to come BEFORE the sweep, so that a save which
                // failed could never leave the project on disk naming a file that
                // was already deleted. The cost of that ordering is that the trace
                // of the deletion is not in it - and a trace that does not survive
                // the next restart is not a trace of anything.
                if (sweep.RemovedCount > 0 || sweep.RefusalMn.Length > 0)
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
            GeneralDesignCipher = Project.Identity.GeneralDesignCipher,
            TechnicalDesignCipher = Project.Identity.TechnicalDesignCipher,
            SheetDateUtc = Project.Identity.SheetDateUtc,
            Description = projectDescription,
            ServerProjectId = Project.Cloud.ServerProjectId,
            ServerUrl = Project.Cloud.ServerUrl,
            CloudProjectCode = Project.Cloud.CloudProjectCode,
            ClientName = initiationBasis.ClientName,
            PlanningAuthorityName = planningAuthorityName,
            DesignOrganizationName = designOrganizationName,
            CloudStatus = Project.Cloud.SyncStatus,
            CornerTableStyle = AlbumCornerTableStyles.Normalize(
                Project.AlbumStyle.CornerTable),
            ConceptCoverStyle = AlbumConceptCoverStyles.Normalize(
                Project.AlbumStyle.ConceptCover),
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
                runtimeIdentity.OwnerEmail,
                runtimeIdentity.DeviceFingerprint,
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

    /// <summary>
    /// The visualisation images the album will be drawn from, brought down to the
    /// album's own density.
    ///
    /// 🔴 PREPARED HERE BECAUSE THIS IS WHERE THE SNAPSHOT IS MADE, and the
    /// snapshot is what the fingerprint is taken from. Preparing later - at the
    /// writer, say - would fingerprint the originals and draw the prepared copies,
    /// so the album would be declared current against a project it does not match.
    ///
    /// 🔴 THE SNAPSHOT IS A CLONE, which is what makes this safe: the owner's
    /// own records keep pointing at their 16k renders.
    /// </summary>
    private ProjectVisualizationSource CreateAlbumVisualizationSnapshot()
    {
        ProjectVisualizationSource snapshot =
            StudioAuxiliarySourceLocalityPolicy.CreateLocalVisualizationSnapshot(
                Project,
                runtimeIdentity.OwnerEmail,
                runtimeIdentity.DeviceFingerprint,
                HasVerifiedPayload);

        VisualizationRasterPreparation preparation =
            StudioVisualizationRasterPreparer.PrepareForAlbum(snapshot, ProjectPath);
        LastVisualizationRasterPreparation = preparation;

        // 🔴 RECORDED WHEN THERE IS SOMETHING TO SAY, AND ALSO WHEN THERE IS
        // SOMETHING TO STOP SAYING. A pass that only reuses prepared copies is the
        // ordinary one and writes nothing; but a stored «2 images at source size»
        // that has since been fixed must be cleared, or the line keeps making a
        // statement about an album that no longer exists.
        int wasUnprepared = Project.PrimaryAlbum.LastDraw?.LastUnpreparedImageCount ?? 0;
        if (preparation.PreparedCount > 0 ||
            preparation.FailedCount > 0 ||
            wasUnprepared > 0)
        {
            Project.PrimaryAlbum.LastDraw ??= new AlbumDrawRecord();
            Project.PrimaryAlbum.LastDraw.RecordRasterPreparation(
                preparation.PreparedCount,
                preparation.FailedCount,
                DateTimeOffset.UtcNow);
            SaveProject();
        }

        return snapshot;
    }

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

    /// <summary>
    /// What the last store sweep did, or null if none has run this session.
    ///
    /// Kept so the album line can say it: reclaiming 1.8 GB without telling anybody
    /// is indistinguishable from losing it, and the owner has spent a week on
    /// exactly that distinction.
    /// </summary>
    internal VisualizationStoreSweepResult? LastVisualizationStoreSweep { get; private set; }

    /// <summary>
    /// What the last raster preparation pass did. In memory only - the parts worth
    /// keeping past a restart are on the album's draw record.
    /// </summary>
    internal VisualizationRasterPreparation? LastVisualizationRasterPreparation { get; private set; }

    /// <summary>
    /// Whether any linked render has been overwritten since the project copied it.
    /// READS ONLY - the rebuild decision may not write to the project.
    /// </summary>
    public LinkedSourceSurvey SurveyLinkedVisualizationSources()
    {
        if (!HasOpenProject || string.IsNullOrWhiteSpace(ProjectPath))
            return new LinkedSourceSurvey(0, 0);

        try
        {
            return ProjectAssetSourceReconciler.SurveyLinkedVisualizationSources(
                Project,
                ProjectPath);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                ArgumentException or NotSupportedException)
        {
            // A question that throws is worse than a question with no answer: the
            // album would stop being drawable because a share was offline.
            return new LinkedSourceSurvey(0, 0);
        }
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
                runtimeIdentity.OwnerEmail,
                runtimeIdentity.DeviceFingerprint),
            image => StudioAuxiliarySourceLocalityPolicy.BindingMatches(
                Project,
                image,
                runtimeIdentity.OwnerEmail,
                runtimeIdentity.DeviceFingerprint));
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
                        runtimeIdentity.OwnerEmail,
                        runtimeIdentity.DeviceFingerprint,
                        HasVerifiedPayload(document)));
            MarkAuxiliaryAlbumComponentChanged(
                ProjectCloudSyncMetadata.ApprovedAtdComponentCode,
                runtimeIdentity.OwnerEmail,
                runtimeIdentity.DeviceFingerprint,
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
                            runtimeIdentity.OwnerEmail,
                            runtimeIdentity.DeviceFingerprint,
                            HasVerifiedPayload(image)));
                MarkAuxiliaryAlbumComponentChanged(
                    ProjectCloudSyncMetadata.VisualizationsComponentCode,
                    runtimeIdentity.OwnerEmail,
                    runtimeIdentity.DeviceFingerprint,
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
        // Only on the path that cleared the site context, which is the only one
        // that reports a change it did not import.
        //
        // Keyed on the PATH, not on "no boundary geometry": the two are equal
        // today only because ConvertManifest refuses a ring with fewer than
        // four points, so an import always carries geometry (a short ring is
        // a read error, not a geometry-less import - measured 2026-09-03, and
        // the earlier claim here that it imported cleanly was wrong). Should
        // an import ever legitimately arrive without geometry, a geometry-keyed
        // check would delete the snapshots underneath it; those files are what
        // RecoverSiteContextSnapshots rebuilds a lost record from, so deleting
        // them on the wrong path costs the only way back.
        if (!result.Imported &&
            !Project.SiteContext.Boundary.HasGeometry &&
            !string.IsNullOrWhiteSpace(ProjectPath))
        {
            ProjectWorkspaceStore.DeleteSiteContextSnapshots(ProjectPath);
        }
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
                    // Visual packages land in the same folder under their own
                    // name. Without this they would arrive to silence.
                    VisualIntake.WatchFolder(
                        source.InboxFolder,
                        source.Id,
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
            catch (Exception exception)
            {
                // The source remains visible so the user can repair its link -
                // but it used to remain visible and SILENT, looking connected
                // while nothing it received could ever arrive. A watch that
                // could not be established is reported; zero deliveries is a
                // suspicious value, not an answer.
                SourceWatchFailed?.Invoke(source, exception);
            }
        }
    }

    private void ClearWatchers()
    {
        foreach (var watched in Intake.WatchedFolders)
        {
            Intake.UnwatchFolder(watched);
        }
        foreach (string watched in VisualIntake.WatchedFolders)
        {
            VisualIntake.UnwatchFolder(watched);
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
                    runtimeIdentity.OwnerEmail,
                    runtimeIdentity.DeviceFingerprint,
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
                    runtimeIdentity.OwnerEmail,
                    runtimeIdentity.DeviceFingerprint,
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
                runtimeIdentity.OwnerEmail,
                runtimeIdentity.DeviceFingerprint)
            : [];

    private bool IsRuntimeSource(ProjectDesignSource source) =>
        HasOpenProject &&
        StudioRuntimeSourceScope.IsAuthorizedLocal(
            Project,
            source,
            runtimeIdentity.OwnerEmail,
            runtimeIdentity.DeviceFingerprint);

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

/// <summary>
/// What the server last said about one participant, as of the last sync.
/// </summary>
/// <param name="LastSeenAtUtc">
/// When the server last heard from them, or null when it never has - which is
/// not the same as their being away.
/// </param>
/// <param name="ProfileImageUrl">Empty when they have not set a photograph.</param>
/// <param name="Initials">The server's initials, used when there is no photograph.</param>
public sealed record ParticipantPresenceInfo(
    DateTimeOffset? LastSeenAtUtc,
    string ProfileImageUrl,
    string Initials);
