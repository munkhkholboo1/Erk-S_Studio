using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using ErkS.Platform.Contracts;

namespace ErkS.Platform.Core;

public static class ProjectWorkspaceStore
{
    public static ProjectWorkspace Load(string path)
    {
        var json = File.ReadAllText(path, Encoding.UTF8);
        var project = JsonSerializer.Deserialize<ProjectWorkspace>(json, SheetPackageJson.Options)
            ?? throw new InvalidDataException($"Project workspace is empty or invalid: {path}");
        if (project.FormatVersion > ProjectWorkspace.CurrentFormatVersion)
        {
            throw new InvalidDataException(
                $"Project format {project.FormatVersion} is newer than supported {ProjectWorkspace.CurrentFormatVersion}.");
        }

        Normalize(project);
        return project;
    }

    public static void Save(ProjectWorkspace project, string path)
    {
        Normalize(project);
        project.FormatVersion = ProjectWorkspace.CurrentFormatVersion;
        project.UpdatedAtUtc = DateTimeOffset.UtcNow;
        AtomicJsonFile.Write(path, project);
    }

    public static bool RecoverSiteContextSnapshots(ProjectWorkspace project, string projectPath)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (string.IsNullOrWhiteSpace(projectPath))
            throw new ArgumentException("Project path is required.", nameof(projectPath));

        project.SiteContext ??= new ProjectSiteContextMap();
        project.SiteContext.Normalize(project.ProjectId);
        if (!string.IsNullOrWhiteSpace(project.SiteContext.OwnerProjectId) &&
            !project.SiteContext.OwnerProjectId.Equals(
                project.ProjectId,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        bool recoveredLocation = RecoverSiteContextSnapshot(
            projectPath,
            project.SiteContext.LocationScheme,
            "assets/site-context/location-scheme.png");
        bool recoveredOverview = RecoverSiteContextSnapshot(
            projectPath,
            project.SiteContext.SurroundingsOverview,
            "assets/site-context/surroundings-overview.png");
        if (!recoveredLocation && !recoveredOverview)
            return false;

        project.SiteContext.UpdatedAtUtc = new[]
            {
                project.SiteContext.LocationScheme.UpdatedAtUtc,
                project.SiteContext.SurroundingsOverview.UpdatedAtUtc,
            }
            .Where(value => value.HasValue)
            .Max();
        return true;
    }

    private static bool RecoverSiteContextSnapshot(
        string projectPath,
        ProjectMapViewport viewport,
        string expectedRelativePath)
    {
        if (viewport.HasSnapshot)
        {
            try
            {
                string currentPath = ProjectWorkspacePaths.ResolveInsideProject(
                    projectPath,
                    viewport.SnapshotRelativePath);
                if (File.Exists(currentPath))
                    return false;
            }
            catch (InvalidDataException)
            {
            }
        }

        string expectedPath = ProjectWorkspacePaths.ResolveInsideProject(
            projectPath,
            expectedRelativePath);
        if (!File.Exists(expectedPath))
            return false;

        (int width, int height) = ReadPngDimensions(expectedPath);
        viewport.SnapshotRelativePath = ProjectWorkspacePaths.ToRelativePath(
            projectPath,
            expectedPath);
        viewport.SnapshotSha256 = ProjectDocumentFileStore.ComputeSha256(expectedPath);
        viewport.SnapshotPixelWidth = width;
        viewport.SnapshotPixelHeight = height;
        viewport.UpdatedAtUtc = File.GetLastWriteTimeUtc(expectedPath);
        return true;
    }

    private static (int Width, int Height) ReadPngDimensions(string path)
    {
        Span<byte> header = stackalloc byte[24];
        using FileStream stream = File.OpenRead(path);
        if (stream.Read(header) != header.Length ||
            !header[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
        {
            return (0, 0);
        }

        int width = BinaryPrimitives.ReadInt32BigEndian(header[16..20]);
        int height = BinaryPrimitives.ReadInt32BigEndian(header[20..24]);
        return (Math.Max(0, width), Math.Max(0, height));
    }

    public static ProjectWorkspace Create(string code, string name)
    {
        return Create(new ProjectCreationRequest
        {
            Code = code,
            Name = name,
        });
    }

    public static ProjectWorkspace Create(ProjectCreationRequest request)
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

        var hasKnownInitiator = string.Equals(
                                    request.InitiatorType,
                                    ProjectInitiatorTypes.DesignOrganization,
                                    StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(
                                    request.InitiatorType,
                                    ProjectInitiatorTypes.GovernmentAuthority,
                                    StringComparison.OrdinalIgnoreCase);
        if (hasKnownInitiator && string.IsNullOrWhiteSpace(request.InitiatorOrganizationName))
        {
            throw new ArgumentException("Initiator organization name is required.", nameof(request));
        }

        var createdAtUtc = DateTimeOffset.UtcNow;
        var designOrganizationCreated = string.Equals(
            request.InitiatorType,
            ProjectInitiatorTypes.DesignOrganization,
            StringComparison.OrdinalIgnoreCase);
        var governmentCreated = string.Equals(
            request.InitiatorType,
            ProjectInitiatorTypes.GovernmentAuthority,
            StringComparison.OrdinalIgnoreCase);
        var organizationName = request.InitiatorOrganizationName.Trim();
        var project = new ProjectWorkspace
        {
            CreatedAtUtc = createdAtUtc,
            Identity = new ProjectIdentity
            {
                Code = request.Code.Trim(),
                Name = request.Name.Trim(),
                Description = request.Description.Trim(),
            },
            Creation = new ProjectCreationInfo
            {
                Channel = string.IsNullOrWhiteSpace(request.Channel)
                    ? ProjectCreationChannels.Studio
                    : request.Channel.Trim(),
                InitiatorType = string.IsNullOrWhiteSpace(request.InitiatorType)
                    ? ProjectInitiatorTypes.Unknown
                    : request.InitiatorType.Trim(),
                InitiatorOrganizationId = request.InitiatorOrganizationId.Trim(),
                InitiatorOrganizationName = organizationName,
                InitiatorUserId = request.InitiatorUserId.Trim(),
                InitiatorDisplayName = request.InitiatorDisplayName.Trim(),
                CreatedAtUtc = createdAtUtc,
            },
            Foundation = new ProjectFoundation
            {
                InitiationBasis = new ProjectInitiationBasis
                {
                    SourceType = governmentCreated
                        ? ProjectInitiationSourceTypes.GovernmentCreated
                        : designOrganizationCreated
                            ? ProjectInitiationSourceTypes.DesignOrganizationCreated
                            : ProjectInitiationSourceTypes.AtdRequest,
                    ClientType = ProjectClientTypes.Normalize(request.ClientType),
                    ClientName = request.ClientName.Trim(),
                    ClientEmail = request.ClientEmail.Trim(),
                    SiteAddress = request.SiteAddress.Trim(),
                    SourceOrganizationName = organizationName,
                    Summary = request.Description.Trim(),
                },
                PlanningTask = new PlanningTaskInformation
                {
                    IssuingAuthorityName = governmentCreated ? organizationName : "",
                },
                DesignCompany = new ProjectCompanyAssignment
                {
                    OrganizationId = designOrganizationCreated
                        ? request.InitiatorOrganizationId.Trim()
                        : "",
                    OrganizationName = designOrganizationCreated ? organizationName : "",
                    AssignmentSource = designOrganizationCreated ? "StudioSelfCreated" : "",
                    AssignedAtUtc = designOrganizationCreated ? createdAtUtc : null,
                    OrganizationSnapshot = new CompanyProfile
                    {
                        OrganizationId = designOrganizationCreated
                            ? request.InitiatorOrganizationId.Trim()
                            : "",
                        Name = designOrganizationCreated ? organizationName : "",
                    },
                },
            },
        };
        Normalize(project);
        return project;
    }

    internal static void Normalize(ProjectWorkspace project)
    {
        if (string.IsNullOrWhiteSpace(project.ProjectId))
        {
            project.ProjectId = Guid.NewGuid().ToString("N");
        }
        project.Identity ??= new ProjectIdentity();
        project.Cloud ??= new ProjectCloudLink();
        project.Cloud.PendingAlbumComponentCodes = (project.Cloud.PendingAlbumComponentCodes ?? [])
            .OfType<string>()
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        project.Cloud.LastReceivedAlbumPdfPath =
            project.Cloud.LastReceivedAlbumPdfPath?.Trim() ?? "";
        project.Cloud.SharedSources = (project.Cloud.SharedSources ?? [])
            .OfType<ProjectCloudSourceReference>()
            .ToList();
        foreach (ProjectCloudSourceReference source in project.Cloud.SharedSources)
        {
            source.SourceId ??= "";
            source.SourceKey ??= "";
            source.SourceApplication ??= "";
            source.SourceDocumentReference ??= "";
            source.ManifestId ??= "";
            source.ContentHash ??= "";
            source.Status ??= "";
            source.OwnerEmail ??= "";
        }
        project.Cloud.SharedAlbumComponents = (project.Cloud.SharedAlbumComponents ?? [])
            .OfType<ProjectCloudAlbumComponentReference>()
            .ToList();
        foreach (ProjectCloudAlbumComponentReference component in project.Cloud.SharedAlbumComponents)
        {
            component.Code ??= "";
            component.Label ??= "";
            component.PageNumbers ??= [];
            component.Status ??= "";
            component.OwnerEmail ??= "";
            component.SourceKey ??= "";
            component.ComponentKind ??= "";
        }
        project.Cloud.CurrentUserRoles = (project.Cloud.CurrentUserRoles ?? [])
            .OfType<string>()
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        project.Cloud.CurrentUserScopes = (project.Cloud.CurrentUserScopes ?? [])
            .OfType<string>()
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        project.Cloud.ServerSnapshot ??= new ProjectServerSnapshot();
        project.Cloud.ServerSnapshot.Surface ??= new ProjectServerSurface();
        project.Cloud.ServerSnapshot.Surface.Sections ??= [];
        project.Cloud.ServerSnapshot.Surface.FoundationSections ??= [];
        project.Cloud.ServerSnapshot.Information ??= new ProjectServerInformation();
        project.Cloud.ServerSnapshot.Foundation ??= new ProjectServerFoundation();
        project.Cloud.ServerSnapshot.Foundation.InitiationBasis ??= new ProjectServerInitiationBasis();
        project.Cloud.ServerSnapshot.Foundation.PlanningTask ??= new ProjectServerPlanningTask();
        project.Cloud.ServerSnapshot.Foundation.PlanningTask.Requirements ??= [];
        project.Cloud.ServerSnapshot.SiteAndLand ??= new ProjectServerSiteAndLand();
        project.Cloud.ServerSnapshot.SiteAndLand.ParcelNumbers ??= [];
        project.Cloud.ServerSnapshot.SiteAndLand.Addresses ??= [];
        project.Cloud.ServerSnapshot.SiteAndLand.RestrictionReferences ??= [];
        if (project.Cloud.PendingProjectInformation is { } pendingInformation)
            pendingInformation.Foundation ??= new ProjectServerFoundationUpdate();
        project.Creation ??= new ProjectCreationInfo();
        project.Foundation ??= new ProjectFoundation();
        project.Foundation.InitiationBasis ??= new ProjectInitiationBasis();
        project.Foundation.InitiationBasis.Documents ??= [];
        project.Foundation.InitiationBasis.ClientOrganizationSnapshot ??= new CompanyProfile();
        project.Foundation.InitiationBasis.ClientOrganizationSnapshot.Normalize();
        project.Foundation.InitiationBasis.ClientType = ProjectClientTypes.ResolveStoredType(
            project.Foundation.InitiationBasis.ClientType,
            project.Foundation.InitiationBasis.ClientOrganizationSnapshot);
        if (ProjectClientTypes.UsesLogo(project.Foundation.InitiationBasis.ClientType) &&
            string.IsNullOrWhiteSpace(project.Foundation.InitiationBasis.ClientOrganizationSnapshot.Name))
        {
            project.Foundation.InitiationBasis.ClientOrganizationSnapshot.Name =
                project.Foundation.InitiationBasis.ClientName.Trim();
        }
        project.Foundation.PlanningTask ??= new PlanningTaskInformation();
        project.Foundation.PlanningTask.Requirements ??= [];
        project.Foundation.PlanningTask.Documents ??= [];
        project.Foundation.PlanningTask.AuthorityMembers ??= [];
        foreach (var member in project.Foundation.PlanningTask.AuthorityMembers)
        {
            member.Roles ??= [];
            NormalizeRegisteredMemberName(member);
        }
        project.Foundation.ApprovalWorkflow ??= new ProjectApprovalWorkflow();
        project.Foundation.ApprovalWorkflow.Normalize();
        project.Foundation.DesignCompany ??= new ProjectCompanyAssignment();
        project.Foundation.DesignCompany.OrganizationSnapshot ??= new CompanyProfile();
        project.Foundation.DesignCompany.OrganizationSnapshot.Normalize();
        project.Foundation.DesignCompany.OrganizationSnapshot.Signers ??= [];
        project.Foundation.DesignCompany.Members ??= [];
        project.Foundation.DesignCompany.History ??= [];
        ProjectCompanyAssignment assignment = project.Foundation.DesignCompany;
        CompanyProfile companySnapshot = assignment.OrganizationSnapshot;
        if (string.IsNullOrWhiteSpace(assignment.OrganizationId) &&
            !string.IsNullOrWhiteSpace(companySnapshot.OrganizationId))
        {
            assignment.OrganizationId = companySnapshot.OrganizationId.Trim();
        }
        if (string.IsNullOrWhiteSpace(assignment.OrganizationId) &&
            project.Creation.InitiatorType.Equals(ProjectInitiatorTypes.DesignOrganization, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(project.Creation.InitiatorOrganizationId) &&
            (assignment.AssignmentSource.Equals("StudioSelfCreated", StringComparison.OrdinalIgnoreCase) ||
             assignment.OrganizationName.Equals(project.Creation.InitiatorOrganizationName, StringComparison.OrdinalIgnoreCase)))
        {
            assignment.OrganizationId = project.Creation.InitiatorOrganizationId.Trim();
        }
        if (string.IsNullOrWhiteSpace(companySnapshot.OrganizationId) &&
            !string.IsNullOrWhiteSpace(assignment.OrganizationId))
        {
            companySnapshot.OrganizationId = assignment.OrganizationId;
        }
        foreach (var member in project.Foundation.DesignCompany.Members)
        {
            member.Roles ??= [];
            NormalizeRegisteredMemberName(member);
        }

        project.Sources ??= [];
        foreach (var source in project.Sources)
        {
            source.Metadata ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(source.Id))
            {
                source.Id = Guid.NewGuid().ToString("N");
            }
            source.Metadata ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        project.Visualizations ??= new ProjectVisualizationSource();
        project.Visualizations.Normalize(project.ProjectId);
        project.SiteContext ??= new ProjectSiteContextMap();
        project.SiteContext.Normalize(project.ProjectId);

        project.Deliverables ??= new ProjectDeliverables();
        project.Deliverables.Albums ??= [];
        project.Deliverables.Reports ??= [];
        if (project.Deliverables.Albums.Count == 0)
        {
            project.Deliverables.Albums.Add(new ProjectAlbumRecord());
        }
        if (!project.Deliverables.Albums.Any(album => album.IsPrimary))
        {
            project.Deliverables.Albums[0].IsPrimary = true;
        }
        foreach (ProjectAlbumRecord album in project.Deliverables.Albums)
        {
            album.RendererRevision = Math.Max(0, album.RendererRevision);
        }

        project.Archive ??= new ProjectArchive();
        project.Archive.Items ??= [];
        if (project.CreatedAtUtc == default)
        {
            project.CreatedAtUtc = DateTimeOffset.UtcNow;
        }
        if (project.Creation.CreatedAtUtc == default)
        {
            project.Creation.CreatedAtUtc = project.CreatedAtUtc;
        }
        if (string.IsNullOrWhiteSpace(project.Creation.Channel))
        {
            project.Creation.Channel = string.Equals(
                project.Cloud.Origin,
                ProjectOrigins.Cloud,
                StringComparison.OrdinalIgnoreCase)
                ? ProjectCreationChannels.Server
                : project.Migration is null
                    ? ProjectCreationChannels.Studio
                    : ProjectCreationChannels.Imported;
        }
        if (string.IsNullOrWhiteSpace(project.Creation.InitiatorType) ||
            string.Equals(project.Creation.InitiatorType, ProjectInitiatorTypes.Unknown, StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(
                    project.Foundation.InitiationBasis.SourceType,
                    ProjectInitiationSourceTypes.AtdRequest,
                    StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(project.Foundation.PlanningTask.IssuingAuthorityName))
            {
                project.Creation.InitiatorType = ProjectInitiatorTypes.GovernmentAuthority;
            }
            else if (!string.IsNullOrWhiteSpace(project.DesignOrganizationName))
            {
                project.Creation.InitiatorType = ProjectInitiatorTypes.DesignOrganization;
            }
        }
        if (string.IsNullOrWhiteSpace(project.Creation.InitiatorOrganizationName))
        {
            project.Creation.InitiatorOrganizationName = string.Equals(
                project.Creation.InitiatorType,
                ProjectInitiatorTypes.GovernmentAuthority,
                StringComparison.OrdinalIgnoreCase)
                ? project.Foundation.PlanningTask.IssuingAuthorityName
                : project.DesignOrganizationName;
        }
    }

    private static void NormalizeRegisteredMemberName(ProjectMember member)
    {
        member.FamilyName ??= "";
        member.GivenName ??= "";
        member.FullName ??= "";
        member.Email ??= "";
        if (!string.IsNullOrWhiteSpace(member.FamilyName) ||
            !string.IsNullOrWhiteSpace(member.GivenName))
        {
            member.FullName = MongolianPersonNameFormatter.ForDisplay(
                member.FamilyName,
                member.GivenName,
                member.FullName);
        }
    }
}

public static class StudioAlbumDocumentStore
{
    public static bool IsAlbumDocument(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var json = JsonDocument.Parse(stream);
            return json.RootElement.TryGetProperty("documentType", out var type) &&
                   string.Equals(type.GetString(), "ErkSAlbum", StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    public static StudioAlbumDocument Load(string path)
    {
        var json = File.ReadAllText(path, Encoding.UTF8);
        var album = JsonSerializer.Deserialize<StudioAlbumDocument>(json, SheetPackageJson.Options)
            ?? throw new InvalidDataException($"Album document is empty or invalid: {path}");
        if (!string.Equals(album.DocumentType, "ErkSAlbum", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"The selected file is not a project-owned album document: {path}");
        }
        if (album.FormatVersion > StudioAlbumDocument.CurrentFormatVersion)
        {
            throw new InvalidDataException(
                $"Album format {album.FormatVersion} is newer than supported {StudioAlbumDocument.CurrentFormatVersion}.");
        }

        Normalize(album);
        return album;
    }

    public static void Save(StudioAlbumDocument album, string path)
    {
        Normalize(album);
        album.FormatVersion = StudioAlbumDocument.CurrentFormatVersion;
        AtomicJsonFile.Write(path, album);
    }

    internal static void Normalize(StudioAlbumDocument album)
    {
        album.Definition ??= new AlbumDefinition();
        album.Definition.Sections ??= [];
        album.Definition.Pages ??= [];
        album.Definition.Composition ??= [];
        foreach (var item in album.Definition.Composition)
        {
            item.MatchContentKinds ??= [];
            item.MatchNameTerms ??= [];
        }
        album.Revisions ??= [];
        int nextRevisionNumber = 1;
        foreach (DeliverableRevisionRecord revision in album.Revisions.OrderBy(item => item.RevisionNumber > 0 ? item.RevisionNumber : item.Version))
        {
            if (string.IsNullOrWhiteSpace(revision.RevisionId))
                revision.RevisionId = Guid.NewGuid().ToString("N");
            revision.RevisionNumber = revision.RevisionNumber > 0
                ? revision.RevisionNumber
                : revision.Version > 0 ? revision.Version : nextRevisionNumber;
            revision.Version = revision.RevisionNumber;
            nextRevisionNumber = Math.Max(nextRevisionNumber, revision.RevisionNumber + 1);
            revision.Status = string.IsNullOrWhiteSpace(revision.Status)
                ? DeliverableRevisionStatuses.Draft
                : revision.Status.Trim();
            revision.ReviewStatus = string.IsNullOrWhiteSpace(revision.ReviewStatus)
                ? revision.Status
                : revision.ReviewStatus.Trim();
            revision.SourcePackageIds ??= [];
        }
        if (string.IsNullOrWhiteSpace(album.AlbumId))
        {
            album.AlbumId = "building-architecture-concept";
        }
        BuildingArchitectureConceptAlbumTemplate.Ensure(album);
    }
}

public static class ProjectWorkspacePaths
{
    public static string DefaultRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "Erk-S Platform",
        "Studio Projects");

    public static string GetProjectFolder(string projectPath) =>
        Path.GetDirectoryName(Path.GetFullPath(projectPath))
        ?? throw new InvalidDataException($"Project path has no directory: {projectPath}");

    public static string ResolveInsideProject(string projectPath, string relativeOrAbsolutePath)
    {
        var root = GetProjectFolder(projectPath);
        var candidate = Path.IsPathRooted(relativeOrAbsolutePath)
            ? Path.GetFullPath(relativeOrAbsolutePath)
            : Path.GetFullPath(Path.Combine(root, relativeOrAbsolutePath));
        if (!IsInside(root, candidate))
        {
            throw new InvalidDataException($"Project content must remain inside the project folder: {candidate}");
        }
        return candidate;
    }

    public static bool IsInside(string rootDirectory, string candidatePath)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootDirectory));
        var candidate = Path.GetFullPath(candidatePath);
        return string.Equals(root, candidate, StringComparison.OrdinalIgnoreCase) ||
               candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    public static string ToRelativePath(string projectPath, string candidatePath)
    {
        var root = GetProjectFolder(projectPath);
        if (!IsInside(root, candidatePath))
        {
            throw new InvalidDataException($"Path is outside project: {candidatePath}");
        }
        return Path.GetRelativePath(root, Path.GetFullPath(candidatePath)).Replace('\\', '/');
    }
}

public sealed class ProjectWorkspaceMigrationResult
{
    public required ProjectWorkspace Project { get; init; }
    public required StudioAlbumDocument Album { get; init; }
    public required string ProjectPath { get; init; }
    public required string AlbumPath { get; init; }
    public bool CreatedFiles { get; init; }
}

public static class LegacyAlbumProjectImporter
{
    public static ProjectWorkspaceMigrationResult Import(string legacyPath, bool persist)
    {
        legacyPath = Path.GetFullPath(legacyPath);
        if (StudioAlbumDocumentStore.IsAlbumDocument(legacyPath))
        {
            throw new InvalidDataException("An album document must be opened through its project workspace.");
        }
        var projectFolder = Path.GetDirectoryName(legacyPath)
            ?? throw new InvalidDataException($"Legacy project path has no directory: {legacyPath}");
        var projectPath = Path.Combine(projectFolder, ProjectWorkspace.DefaultFileName);

        if (File.Exists(projectPath))
        {
            var existing = ProjectWorkspaceStore.Load(projectPath);
            var existingAlbumPath = ProjectWorkspacePaths.ResolveInsideProject(
                projectPath,
                existing.PrimaryAlbum.DocumentPath);
            var existingAlbum = StudioAlbumDocumentStore.Load(existingAlbumPath);
            return new ProjectWorkspaceMigrationResult
            {
                Project = existing,
                Album = existingAlbum,
                ProjectPath = projectPath,
                AlbumPath = existingAlbumPath,
                CreatedFiles = false,
            };
        }

        var legacy = AlbumProjectStore.Load(legacyPath);
        RepairLegacyText(legacy);
        var legacyClient = legacy.Participants.FirstOrDefault(participant =>
            participant.Role.Contains("Client", StringComparison.OrdinalIgnoreCase));
        var projectId = string.IsNullOrWhiteSpace(legacy.ServerProjectId)
            ? Guid.NewGuid().ToString("N")
            : legacy.ServerProjectId;
        var company = legacy.Company ?? new CompanyProfile();
        if (string.IsNullOrWhiteSpace(company.Name))
        {
            company.Name = legacy.DesignOrganizationName;
        }

        var workspace = new ProjectWorkspace
        {
            ProjectId = projectId,
            Identity = new ProjectIdentity
            {
                Name = legacy.Name,
                Code = legacy.Code,
                Description = legacy.Description,
            },
            Cloud = new ProjectCloudLink
            {
                Origin = string.IsNullOrWhiteSpace(legacy.ServerProjectId) ? ProjectOrigins.Local : ProjectOrigins.Cloud,
                ServerProjectId = legacy.ServerProjectId,
                ServerUrl = legacy.ServerUrl,
                CloudProjectCode = legacy.CloudProjectCode,
                SyncStatus = string.IsNullOrWhiteSpace(legacy.CloudStatus)
                    ? (string.IsNullOrWhiteSpace(legacy.ServerProjectId) ? ProjectSyncStatuses.Local : ProjectSyncStatuses.Linked)
                    : legacy.CloudStatus,
            },
            Foundation = new ProjectFoundation
            {
                InitiationBasis = new ProjectInitiationBasis
                {
                    SourceType = string.IsNullOrWhiteSpace(legacy.ServerProjectId) ? "LegacyStudioProject" : "ATDRequest",
                    ClientName = !string.IsNullOrWhiteSpace(legacyClient?.FullName)
                        ? legacyClient.FullName
                        : legacy.ClientName,
                    ClientEmail = !string.IsNullOrWhiteSpace(legacyClient?.Email)
                        ? legacyClient.Email
                        : (legacy.ClientName.Contains('@') ? legacy.ClientName : ""),
                    ServerRecordId = legacy.ServerProjectId,
                    Summary = legacy.Description,
                },
                PlanningTask = new PlanningTaskInformation
                {
                    IssuingAuthorityName = legacy.PlanningAuthorityName,
                    Status = string.IsNullOrWhiteSpace(legacy.PlanningAuthorityName) ? "" : "Issued",
                    AuthorityMembers = MergeMembers(legacy.Participants.Where(IsAuthorityParticipant)),
                },
                DesignCompany = new ProjectCompanyAssignment
                {
                    OrganizationName = string.IsNullOrWhiteSpace(legacy.DesignOrganizationName)
                        ? company.Name
                        : legacy.DesignOrganizationName,
                    AssignmentSource = string.IsNullOrWhiteSpace(legacy.ServerProjectId) ? "LegacyProject" : "CloudERA",
                    OrganizationSnapshot = company,
                    Members = MergeMembers(legacy.Participants.Where(participant =>
                        !IsAuthorityParticipant(participant) &&
                        !participant.Role.Contains("Client", StringComparison.OrdinalIgnoreCase))),
                },
            },
            Deliverables = new ProjectDeliverables
            {
                Albums =
                [
                    new ProjectAlbumRecord
                    {
                        Title = legacy.Album.Title,
                        OutputFolder = ResolveOutputRelativePath(projectFolder, legacy.OutputFolder),
                    },
                ],
                Reports = legacy.Documents
                    .Where(document => !string.Equals(document.Type, "Album", StringComparison.OrdinalIgnoreCase))
                    .Select(document => new ProjectReportRecord
                    {
                        Id = document.Id.ToString("N"),
                        Type = string.IsNullOrWhiteSpace(document.Type) ? "Report" : document.Type,
                        Title = document.Title,
                        Status = document.Status,
                        DocumentPath = ToSafeRelativePath(projectFolder, document.LocalPath),
                        Version = document.Version,
                    })
                    .ToList(),
            },
            Migration = new ProjectMigrationInfo
            {
                SourceFormat = AlbumProject.FileExtension,
                SourceFormatVersion = legacy.FormatVersion,
                SourcePath = legacyPath,
            },
        };

        workspace.Sources = legacy.DesignSources.Select(source => CloneSourceInsideProject(source, projectFolder)).ToList();
        ProjectWorkspaceStore.Normalize(workspace);

        var album = new StudioAlbumDocument
        {
            ProjectId = workspace.ProjectId,
            AlbumId = workspace.PrimaryAlbum.Id,
            Status = workspace.PrimaryAlbum.Status,
            FoundationVersion = workspace.Foundation.Version,
            Definition = legacy.Album,
        };
        StudioAlbumDocumentStore.Normalize(album);
        var albumPath = ProjectWorkspacePaths.ResolveInsideProject(projectPath, workspace.PrimaryAlbum.DocumentPath);

        if (persist)
        {
            StudioAlbumDocumentStore.Save(album, albumPath);
            ProjectWorkspaceStore.Save(workspace, projectPath);
        }

        return new ProjectWorkspaceMigrationResult
        {
            Project = workspace,
            Album = album,
            ProjectPath = projectPath,
            AlbumPath = albumPath,
            CreatedFiles = persist,
        };
    }

    private static List<ProjectMember> MergeMembers(IEnumerable<ProjectParticipant> participants)
    {
        return participants
            .GroupBy(
                participant => string.IsNullOrWhiteSpace(participant.Email)
                    ? participant.FullName
                    : participant.Email,
                StringComparer.OrdinalIgnoreCase)
            .Select(group => new ProjectMember
            {
                FamilyName = group.Select(item => item.FamilyName).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "",
                GivenName = group.Select(item => item.GivenName).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "",
                FullName = MongolianPersonNameFormatter.ForDisplay(
                    group.Select(item => item.FamilyName).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)),
                    group.Select(item => item.GivenName).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)),
                    group.Select(item => item.FullName).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))),
                Email = group.Select(item => item.Email).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "",
                Roles = group.Select(item => item.Role)
                    .Where(role => !string.IsNullOrWhiteSpace(role))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
            })
            .ToList();
    }

    private static bool IsAuthorityParticipant(ProjectParticipant participant)
    {
        return participant.Role.Contains("Authority", StringComparison.OrdinalIgnoreCase) ||
               participant.Role.Contains("Chief Architect", StringComparison.OrdinalIgnoreCase);
    }

    private static void RepairLegacyText(AlbumProject legacy)
    {
        legacy.Name = RepairMojibake(legacy.Name);
        legacy.Description = RepairMojibake(legacy.Description);
        legacy.ClientName = RepairMojibake(legacy.ClientName);
        legacy.PlanningAuthorityName = RepairMojibake(legacy.PlanningAuthorityName);
        legacy.DesignOrganizationName = RepairMojibake(legacy.DesignOrganizationName);
        legacy.Company.Name = RepairMojibake(legacy.Company.Name);
        legacy.Company.ShortName = RepairMojibake(legacy.Company.ShortName);
        legacy.Company.Address = RepairMojibake(legacy.Company.Address);
        foreach (var signer in legacy.Company.Signers)
        {
            signer.Role = RepairMojibake(signer.Role);
            signer.FullName = RepairMojibake(signer.FullName);
        }
        foreach (var participant in legacy.Participants)
        {
            participant.FamilyName = RepairMojibake(participant.FamilyName);
            participant.GivenName = RepairMojibake(participant.GivenName);
            participant.FullName = RepairMojibake(participant.FullName);
            participant.Role = RepairMojibake(participant.Role);
        }
        foreach (var source in legacy.DesignSources)
        {
            source.Name = RepairMojibake(source.Name);
            source.NativeDocumentTitle = RepairMojibake(source.NativeDocumentTitle);
            source.OwnerOrganizationName = RepairMojibake(source.OwnerOrganizationName);
            foreach (var key in source.Metadata.Keys.ToList())
            {
                source.Metadata[key] = RepairMojibake(source.Metadata[key]);
            }
        }
        foreach (var document in legacy.Documents)
        {
            document.Title = RepairMojibake(document.Title);
            document.OwnerOrganizationName = RepairMojibake(document.OwnerOrganizationName);
        }
        legacy.Album.Title = RepairMojibake(legacy.Album.Title);
        foreach (var section in legacy.Album.Sections)
        {
            section.Title = RepairMojibake(section.Title);
        }
        foreach (var page in legacy.Album.Pages)
        {
            page.TitleOverride = RepairMojibake(page.TitleOverride);
        }
    }

    private static string RepairMojibake(string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            (!value.Contains("Р", StringComparison.Ordinal) || !value.Contains("С", StringComparison.Ordinal)))
        {
            return value;
        }

        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var codePage = Encoding.GetEncoding(
                1251,
                EncoderFallback.ExceptionFallback,
                DecoderFallback.ExceptionFallback);
            var strictUtf8 = new UTF8Encoding(false, true);
            var repaired = strictUtf8.GetString(codePage.GetBytes(value));
            var oldMarkers = value.Count(character => character is 'Р' or 'С');
            var newMarkers = repaired.Count(character => character is 'Р' or 'С');
            return newMarkers < oldMarkers ? repaired : value;
        }
        catch
        {
            return value;
        }
    }

    private static ProjectDesignSource CloneSourceInsideProject(ProjectDesignSource source, string projectFolder)
    {
        var clone = new ProjectDesignSource
        {
            Id = source.Id,
            Kind = source.Kind,
            Name = source.Name,
            ApplicationVersion = source.ApplicationVersion,
            NativeDocumentTitle = source.NativeDocumentTitle,
            NativeDocumentPath = source.NativeDocumentPath,
            InboxFolder = source.InboxFolder,
            StageId = source.StageId,
            WorkPackageId = source.WorkPackageId,
            OwnerOrganizationName = source.OwnerOrganizationName,
            Status = source.Status,
            CreatedAtUtc = source.CreatedAtUtc,
            LastPackageAtUtc = source.LastPackageAtUtc,
            UseLegacySheetKeys = source.UseLegacySheetKeys,
            Metadata = new Dictionary<string, string>(source.Metadata, StringComparer.OrdinalIgnoreCase),
        };

        if (string.IsNullOrWhiteSpace(clone.InboxFolder) || !ProjectWorkspacePaths.IsInside(projectFolder, clone.InboxFolder))
        {
            if (!string.IsNullOrWhiteSpace(clone.InboxFolder))
            {
                clone.Metadata["legacyExternalInbox"] = clone.InboxFolder;
            }
            clone.InboxFolder = Path.Combine(projectFolder, "sources", SafePathSegment(clone.DisplayName), "deliveries");
        }
        return clone;
    }

    private static string ResolveOutputRelativePath(string projectFolder, string outputFolder)
    {
        if (!string.IsNullOrWhiteSpace(outputFolder) && ProjectWorkspacePaths.IsInside(projectFolder, outputFolder))
        {
            return Path.GetRelativePath(projectFolder, Path.GetFullPath(outputFolder)).Replace('\\', '/');
        }
        return ProjectWorkspace.DefaultOutputRelativePath;
    }

    private static string ToSafeRelativePath(string projectFolder, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "";
        }
        return ProjectWorkspacePaths.IsInside(projectFolder, path)
            ? Path.GetRelativePath(projectFolder, Path.GetFullPath(path)).Replace('\\', '/')
            : "";
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
}

internal static class AtomicJsonFile
{
    public static void Write<T>(string path, T value)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        var json = JsonSerializer.Serialize(value, SheetPackageJson.Options);
        var tempPath = fullPath + ".tmp";
        File.WriteAllText(tempPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(tempPath, fullPath, overwrite: true);
    }
}
