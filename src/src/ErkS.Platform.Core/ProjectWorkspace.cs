using System.Text.Json.Serialization;

namespace ErkS.Platform.Core;

/// <summary>
/// Root aggregate of an Erk-S Studio project. Albums and reports are child
/// deliverables; they are never used as the project container.
/// </summary>
public sealed class ProjectWorkspace
{
    public const int CurrentFormatVersion = 2;
    public const string FileExtension = ".erksproject";
    public const string DefaultFileName = "project.erksproject";
    public const string BuildingArchitectureConcept = "BuildingArchitectureConcept";
    public const string ConceptDesignStage = "ConceptDesign";
    public const string ConceptAlbumRelativePath = "albums/building-architecture-concept.erksalbum";
    public const string DefaultOutputRelativePath = "albums";

    public int FormatVersion { get; set; } = CurrentFormatVersion;

    public string ProjectId { get; set; } = Guid.NewGuid().ToString("N");

    public ProjectIdentity Identity { get; set; } = new();

    public ProjectCloudLink Cloud { get; set; } = new();

    /// <summary>
    /// Immutable provenance of the first project record. Cloud is intentionally
    /// separate because a Studio-created project can be linked to a server later.
    /// </summary>
    public ProjectCreationInfo Creation { get; set; } = new();

    public ProjectFoundation Foundation { get; set; } = new();

    /// <summary>
    /// Project-owned working inputs. Native RVT/DWG files remain at source;
    /// only PDF deliveries and manifests are received into the project.
    /// </summary>
    public List<ProjectDesignSource> Sources { get; set; } = [];

    /// <summary>Studio-owned raster source for automatically composed visualization pages.</summary>
    public ProjectVisualizationSource Visualizations { get; set; } = new();

    public ProjectDeliverables Deliverables { get; set; } = new();

    public ProjectArchive Archive { get; set; } = new();

    public ProjectMigrationInfo? Migration { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    [JsonIgnore]
    public string Name => Identity.Name;

    [JsonIgnore]
    public string Code => Identity.Code;

    [JsonIgnore]
    public string DesignOrganizationName
    {
        get
        {
            var snapshot = Foundation.DesignCompany.OrganizationSnapshot;
            return string.IsNullOrWhiteSpace(snapshot.Name)
                ? Foundation.DesignCompany.OrganizationName
                : snapshot.Name;
        }
    }

    [JsonIgnore]
    public string DesignOrganizationDisplayName
    {
        get
        {
            var snapshot = Foundation.DesignCompany.OrganizationSnapshot;
            return string.IsNullOrWhiteSpace(snapshot.DisplayName)
                ? DesignOrganizationName
                : snapshot.DisplayName;
        }
    }

    [JsonIgnore]
    public ProjectAlbumRecord PrimaryAlbum => Deliverables.Albums.First(album => album.IsPrimary);
}

public sealed class ProjectIdentity
{
    public string Name { get; set; } = "Шинэ төсөл";
    public string Code { get; set; } = "";
    public string Description { get; set; } = "";
    public string ProjectType { get; set; } = ProjectWorkspace.BuildingArchitectureConcept;
    public string StageCode { get; set; } = ProjectWorkspace.ConceptDesignStage;
    public string StageName { get; set; } = "Загвар зураг";
}

public sealed class ProjectCloudLink
{
    public string Origin { get; set; } = ProjectOrigins.Local;
    public string ServerProjectId { get; set; } = "";
    public string ServerUrl { get; set; } = "";
    public string CloudProjectCode { get; set; } = "";
    public string SyncStatus { get; set; } = ProjectSyncStatuses.Local;
    public DateTimeOffset? LastSyncedAtUtc { get; set; }
    public DateTimeOffset? LastCloudCheckedAtUtc { get; set; }
    public DateTimeOffset? LastCloudRefreshedAtUtc { get; set; }
    public string LastSyncedAlbumSha256 { get; set; } = "";
    public string LastSyncedRevisionId { get; set; } = "";
    public string LastReceivedAlbumSha256 { get; set; } = "";
    public string LastReceivedAlbumRevisionId { get; set; } = "";
    public int LastReceivedAlbumRevisionNumber { get; set; }
    public string LastReceivedClientLogoKey { get; set; } = "";
    public string LastReceivedDesignOrganizationLogoKey { get; set; } = "";
    public string LastServerConcurrencyToken { get; set; } = "";
    public string LastSyncError { get; set; } = "";
    public string LastSyncNote { get; set; } = "";
    /// <summary>
    /// Rendered album components that still need to be merged into the
    /// canonical Cloud revision. A failed merge remains retryable.
    /// </summary>
    public List<string> PendingAlbumComponentCodes { get; set; } = [];
    public List<string> CurrentUserRoles { get; set; } = [];
    public List<string> CurrentUserScopes { get; set; } = [];
    public ProjectServerSnapshot ServerSnapshot { get; set; } = new();
    public PendingProjectInformationUpdate? PendingProjectInformation { get; set; }

    public bool HasScope(string scope) => CurrentUserScopes.Any(value =>
        value.Equals(scope, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Canonical project information saved in Studio while an older server runtime
/// does not yet expose the project-information update endpoint.
/// </summary>
public sealed class PendingProjectInformationUpdate
{
    public string Name { get; set; } = "";
    public string ClientName { get; set; } = "";
    public string PlanningAuthorityName { get; set; } = "";
    public string DesignOrganizationName { get; set; } = "";
    public string Location { get; set; } = "";
    public string BuildingPurpose { get; set; } = "";
    public string CapacityUnit { get; set; } = "";
    public ProjectServerFoundationUpdate Foundation { get; set; } = new();
    public DateTimeOffset QueuedAtUtc { get; set; }
}

/// <summary>
/// Last canonical project record received from Erk-S Server. Studio keeps this
/// as a local mirror only; the server remains the source of truth.
/// </summary>
public sealed class ProjectServerSnapshot
{
    public string ProjectId { get; set; } = "";
    public string ProjectCode { get; set; } = "";
    public string Name { get; set; } = "";
    public string Status { get; set; } = "";
    public string CurrentStage { get; set; } = "";
    public string ClientName { get; set; } = "";
    public string PlanningAuthorityName { get; set; } = "";
    public string DesignOrganizationName { get; set; } = "";
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public string ConcurrencyToken { get; set; } = "";
    public ProjectServerSurface Surface { get; set; } = new();
    public ProjectServerInformation Information { get; set; } = new();
    public ProjectServerFoundation Foundation { get; set; } = new();
    public ProjectServerSiteAndLand SiteAndLand { get; set; } = new();
}

public sealed class ProjectServerSurface
{
    public string SchemaVersion { get; set; } = "";
    public string ProductName { get; set; } = "";
    public List<ProjectServerSurfaceSection> Sections { get; set; } = [];
    public List<ProjectServerSurfaceSection> FoundationSections { get; set; } = [];
}

public sealed class ProjectServerSurfaceSection
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string Icon { get; set; } = "";
    public int Order { get; set; }
}

public sealed class ProjectServerFoundation
{
    public bool IsAvailable { get; set; }
    public int Version { get; set; } = 1;
    public ProjectServerInitiationBasis InitiationBasis { get; set; } = new();
    public ProjectServerPlanningTask PlanningTask { get; set; } = new();
}

public sealed class ProjectServerInitiationBasis
{
    public string SourceType { get; set; } = "";
    public string RequestNumber { get; set; } = "";
    public DateTimeOffset? RequestedAtUtc { get; set; }
    public string ClientType { get; set; } = "";
    public string ClientName { get; set; } = "";
    public string ClientEmail { get; set; } = "";
    public string ClientRepresentativePosition { get; set; } = "";
    public string ClientRepresentativeName { get; set; } = "";
    public string ClientLogoUrl { get; set; } = "";
    public string SiteAddress { get; set; } = "";
    public string LandReference { get; set; } = "";
    public string SourceOrganizationName { get; set; } = "";
    public string ServerRecordId { get; set; } = "";
    public string Summary { get; set; } = "";
}

public sealed class ProjectServerPlanningTask
{
    public string AtdNumber { get; set; } = "";
    public DateTimeOffset? IssuedAtUtc { get; set; }
    public string IssuingAuthorityName { get; set; } = "";
    public string Status { get; set; } = "";
    public string Summary { get; set; } = "";
    public List<string> Requirements { get; set; } = [];
}

public sealed class ProjectServerFoundationUpdate
{
    public bool IsAvailable { get; set; }
    public string SourceType { get; set; } = "";
    public string RequestNumber { get; set; } = "";
    public string ClientType { get; set; } = "";
    public string ClientEmail { get; set; } = "";
    public string ClientRepresentativePosition { get; set; } = "";
    public string ClientRepresentativeName { get; set; } = "";
    public string SiteAddress { get; set; } = "";
    public string LandReference { get; set; } = "";
    public string SourceOrganizationName { get; set; } = "";
    public string BasisSummary { get; set; } = "";
    public string AtdNumber { get; set; } = "";
    public string AtdAuthorityName { get; set; } = "";
    public string AtdStatus { get; set; } = "";
    public string AtdSummary { get; set; } = "";
}

public sealed class ProjectServerInformation
{
    public string ProjectId { get; set; } = "";
    public string ProjectCode { get; set; } = "";
    public string Name { get; set; } = "";
    public string Location { get; set; } = "";
    public string BuildingPurpose { get; set; } = "";
    public decimal? Capacity { get; set; }
    public string CapacityUnit { get; set; } = "";
    public decimal? FootprintSquareMeters { get; set; }
    public decimal? GrossFloorAreaSquareMeters { get; set; }
    public decimal? HeightMeters { get; set; }
    public int? FloorsAboveGround { get; set; }
    public int? FloorsBelowGround { get; set; }
}

public sealed class ProjectServerSiteAndLand
{
    public List<string> ParcelNumbers { get; set; } = [];
    public List<string> Addresses { get; set; } = [];
    public List<string> RestrictionReferences { get; set; } = [];
}

public sealed class ProjectCreationInfo
{
    public string Channel { get; set; } = "";
    public string InitiatorType { get; set; } = ProjectInitiatorTypes.Unknown;
    public string InitiatorOrganizationId { get; set; } = "";
    public string InitiatorOrganizationName { get; set; } = "";
    public string InitiatorUserId { get; set; } = "";
    public string InitiatorDisplayName { get; set; } = "";
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class ProjectCreationRequest
{
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string Channel { get; init; } = ProjectCreationChannels.Studio;
    public string InitiatorType { get; init; } = ProjectInitiatorTypes.Unknown;
    public string InitiatorOrganizationId { get; init; } = "";
    public string InitiatorOrganizationName { get; init; } = "";
    public string InitiatorUserId { get; init; } = "";
    public string InitiatorDisplayName { get; init; } = "";
    public string ClientType { get; init; } = ProjectClientTypes.Citizen;
    public string ClientName { get; init; } = "";
    public string ClientEmail { get; init; } = "";
    public string SiteAddress { get; init; } = "";
}

public static class ProjectCreationChannels
{
    public const string Studio = "Studio";
    public const string Server = "Server";
    public const string Imported = "Imported";
}

public static class ProjectInitiatorTypes
{
    public const string Unknown = "Unknown";
    public const string GovernmentAuthority = "GovernmentAuthority";
    public const string DesignOrganization = "DesignOrganization";
}

public static class ProjectInitiationSourceTypes
{
    public const string AtdRequest = "ATDRequest";
    public const string GovernmentCreated = "GovernmentCreated";
    public const string DesignOrganizationCreated = "DesignOrganizationCreated";
}

public static class ProjectClientTypes
{
    public const string Citizen = "Citizen";
    public const string Organization = "Organization";
    public const string GovernmentAuthority = "GovernmentAuthority";

    public static string Normalize(string? value)
    {
        if (string.Equals(value, Organization, StringComparison.OrdinalIgnoreCase))
            return Organization;
        if (string.Equals(value, GovernmentAuthority, StringComparison.OrdinalIgnoreCase))
            return GovernmentAuthority;
        return Citizen;
    }

    public static string DisplayName(string? value) => Normalize(value) switch
    {
        Organization => "Байгууллага",
        GovernmentAuthority => "Төрийн байгууллага",
        _ => "Иргэн",
    };

    public static bool UsesLogo(string? value) =>
        !Normalize(value).Equals(Citizen, StringComparison.Ordinal);

    public static bool ShowsDirectClientName(string? value) =>
        Normalize(value).Equals(Citizen, StringComparison.Ordinal);

    public static string ClientNameFieldLabel(string? value) => Normalize(value) switch
    {
        Organization => "Захиалагч байгууллагын нэр",
        GovernmentAuthority => "Төрийн байгууллагын нэр",
        _ => "Захиалагчийн нэр",
    };

    /// <summary>
    /// Resolves the text printed in the client position cell. For organizations,
    /// the legal/display name is part of the cover identity and the client type
    /// is only a classification.
    /// </summary>
    public static string ResolveCoverRole(
        string? clientType,
        string? clientName,
        string? representativePosition)
    {
        string normalized = Normalize(clientType);
        if (normalized.Equals(Citizen, StringComparison.Ordinal))
            return DisplayName(normalized);

        string organizationName = clientName?.Trim() ?? "";
        string position = representativePosition?.Trim() ?? "";
        if (!string.IsNullOrWhiteSpace(organizationName) && !string.IsNullOrWhiteSpace(position))
            return $"{organizationName} {position}";
        if (!string.IsNullOrWhiteSpace(organizationName))
            return organizationName;
        if (!string.IsNullOrWhiteSpace(position))
            return position;
        return DisplayName(normalized);
    }

    /// <summary>
    /// Resolves the person printed in the client section of generated pages.
    /// Organization names and legacy client values must never replace the
    /// explicitly selected representative for non-citizen clients.
    /// </summary>
    public static string ResolveCoverPersonName(
        string? clientType,
        string? clientName,
        string? representativeName,
        string? citizenFallback = null)
    {
        if (!ShowsDirectClientName(clientType))
            return representativeName?.Trim() ?? "";

        if (!string.IsNullOrWhiteSpace(clientName))
            return clientName.Trim();
        return citizenFallback?.Trim() ?? "";
    }
}

public static class ProjectOrigins
{
    public const string Local = "Local";
    public const string Cloud = "Cloud";
}

public static class ProjectSyncStatuses
{
    public const string Local = "Local";
    public const string Linked = "Linked";
    public const string Pending = "Pending";
    public const string Syncing = "Syncing";
    public const string Synced = "Synced";
    public const string Conflict = "Conflict";
    public const string Error = "Error";
}

/// <summary>
/// Information that exists before design production starts. Deliverable
/// revisions pin this version so later foundation updates do not rewrite an
/// approved album or report.
/// </summary>
public sealed class ProjectFoundation
{
    public int Version { get; set; } = 1;
    public ProjectInitiationBasis InitiationBasis { get; set; } = new();
    public PlanningTaskInformation PlanningTask { get; set; } = new();
    public ProjectApprovalWorkflow ApprovalWorkflow { get; set; } = new();
    public ProjectCompanyAssignment DesignCompany { get; set; } = new();
}

public sealed class ProjectInitiationBasis
{
    public string SourceType { get; set; } = ProjectInitiationSourceTypes.AtdRequest;
    public string RequestNumber { get; set; } = "";
    public DateTimeOffset? RequestedAtUtc { get; set; }
    public string ClientType { get; set; } = ProjectClientTypes.Citizen;
    public string ClientName { get; set; } = "";
    public string ClientEmail { get; set; } = "";
    /// <summary>Non-citizen client's representative position printed on the cover.</summary>
    public string ClientRepresentativePosition { get; set; } = "";
    /// <summary>Non-citizen client's representative name printed on the cover.</summary>
    public string ClientRepresentativeName { get; set; } = "";
    public CompanyProfile ClientOrganizationSnapshot { get; set; } = new();
    public string SiteAddress { get; set; } = "";
    public string LandReference { get; set; } = "";
    public string SourceOrganizationName { get; set; } = "";
    public string ServerRecordId { get; set; } = "";
    public string Summary { get; set; } = "";
    public List<ProjectFileReference> Documents { get; set; } = [];
}

public sealed class PlanningTaskInformation
{
    public string AtdNumber { get; set; } = "";
    public DateTimeOffset? IssuedAtUtc { get; set; }
    public string IssuingAuthorityName { get; set; } = "";
    public string Status { get; set; } = "";
    public string Summary { get; set; } = "";
    public List<string> Requirements { get; set; } = [];
    public List<ProjectFileReference> Documents { get; set; } = [];
    public string ServerDocumentId { get; set; } = "";
    public int ServerDocumentVersion { get; set; }
    public string DocumentCloudSyncStatus { get; set; } = ProjectDocumentCloudSyncStatuses.Local;
    public List<ProjectMember> AuthorityMembers { get; set; } = [];
}

/// <summary>
/// The design-company assignment is stage-scoped. Reassignments retain their
/// previous snapshots so project documents remain auditable.
/// </summary>
public sealed class ProjectCompanyAssignment
{
    public string StageCode { get; set; } = ProjectWorkspace.ConceptDesignStage;
    public string StageName { get; set; } = "Загвар зураг";
    public string OrganizationId { get; set; } = "";
    public string OrganizationName { get; set; } = "";
    public string AssignmentSource { get; set; } = "";
    public DateTimeOffset? AssignedAtUtc { get; set; }
    public CompanyProfile OrganizationSnapshot { get; set; } = new();
    public List<ProjectMember> Members { get; set; } = [];
    public List<ProjectCompanyAssignmentHistoryEntry> History { get; set; } = [];
}

public sealed class ProjectCompanyAssignmentHistoryEntry
{
    public string OrganizationId { get; set; } = "";
    public string OrganizationName { get; set; } = "";
    public string AssignmentSource { get; set; } = "";
    public DateTimeOffset? AssignedAtUtc { get; set; }
    public DateTimeOffset ReplacedAtUtc { get; set; }
    public CompanyProfile OrganizationSnapshot { get; set; } = new();
}

public sealed class ProjectMember
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    /// <summary>Registered profile surname/ovog. Empty only for legacy or offline members.</summary>
    public string FamilyName { get; set; } = "";
    /// <summary>Registered profile given name/ner. Empty only for legacy or offline members.</summary>
    public string GivenName { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public List<string> Roles { get; set; } = [];
}

public sealed class ProjectFileReference
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Category { get; set; } = "";
    public string Title { get; set; } = "";
    public string RelativePath { get; set; } = "";
    public string OriginalFileName { get; set; } = "";
    /// <summary>
    /// Local file selected by the user. Studio keeps an owned copy for album
    /// generation and uses this link only to detect later source revisions.
    /// </summary>
    public string LinkedSourcePath { get; set; } = "";
    public DateTimeOffset? LinkedSourceLastWriteTimeUtc { get; set; }
    /// <summary>
    /// Missing linked files remain registered but are excluded from generated
    /// album pages. This lets a temporarily unavailable source be restored.
    /// </summary>
    public bool IsAvailable { get; set; } = true;
    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }
    public int PageCount { get; set; } = 1;
    public string ServerDocumentId { get; set; } = "";
    public string ServerFileId { get; set; } = "";
    public string ServerFileRevisionId { get; set; } = "";
    public int ServerDocumentVersion { get; set; }
    public string CloudSyncStatus { get; set; } = ProjectDocumentCloudSyncStatuses.Local;
    public string Sha256 { get; set; } = "";
    public int Version { get; set; } = 1;
    public DateTimeOffset AddedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public ProjectFileReference Clone() => new()
    {
        Id = Id,
        Category = Category,
        Title = Title,
        RelativePath = RelativePath,
        OriginalFileName = OriginalFileName,
        LinkedSourcePath = LinkedSourcePath,
        LinkedSourceLastWriteTimeUtc = LinkedSourceLastWriteTimeUtc,
        IsAvailable = IsAvailable,
        ContentType = ContentType,
        SizeBytes = SizeBytes,
        PageCount = PageCount,
        ServerDocumentId = ServerDocumentId,
        ServerFileId = ServerFileId,
        ServerFileRevisionId = ServerFileRevisionId,
        ServerDocumentVersion = ServerDocumentVersion,
        CloudSyncStatus = CloudSyncStatus,
        Sha256 = Sha256,
        Version = Version,
        AddedAtUtc = AddedAtUtc,
    };
}

public static class ProjectDocumentCloudSyncStatuses
{
    public const string Local = "Local";
    public const string PendingUpload = "PendingUpload";
    public const string Synced = "Synced";
    public const string Conflict = "Conflict";
}

public sealed class ProjectDeliverables
{
    public List<ProjectAlbumRecord> Albums { get; set; } = [];
    public List<ProjectReportRecord> Reports { get; set; } = [];
}

public sealed class ProjectAlbumRecord
{
    public string Id { get; set; } = "building-architecture-concept";
    public string Type { get; set; } = ProjectWorkspace.BuildingArchitectureConcept;
    public string Title { get; set; } = "Барилга архитектурын загвар зургийн альбум";
    public string Status { get; set; } = "Draft";
    public bool IsPrimary { get; set; } = true;
    public string DocumentPath { get; set; } = ProjectWorkspace.ConceptAlbumRelativePath;
    public string OutputFolder { get; set; } = ProjectWorkspace.DefaultOutputRelativePath;
    public string LastPdfPath { get; set; } = "";
    public string LastPdfSha256 { get; set; } = "";
    public int LastPageCount { get; set; }
    public string LastPageSizeSummary { get; set; } = "";
    public int Version { get; set; } = 1;
}

public sealed class ProjectReportRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Type { get; set; } = "Report";
    public string Title { get; set; } = "";
    public string Status { get; set; } = "Draft";
    public string DocumentPath { get; set; } = "";
    public int Version { get; set; } = 1;
}

public sealed class ProjectArchive
{
    public List<ProjectArchiveRecord> Items { get; set; } = [];
}

public sealed class ProjectArchiveRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string DeliverableId { get; set; } = "";
    public string Type { get; set; } = "";
    public string Title { get; set; } = "";
    public string Status { get; set; } = "Archived";
    public string RevisionId { get; set; } = "";
    public int RevisionNumber { get; set; }
    public int FoundationVersion { get; set; } = 1;
    public string CompanySnapshotId { get; set; } = "";
    public int PageCount { get; set; }
    public string PageSizeSummary { get; set; } = "";
    public string CreatedBy { get; set; } = "";
    public string AuditNote { get; set; } = "";
    public string RelativePath { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public DateTimeOffset ArchivedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ProjectMigrationInfo
{
    public string SourceFormat { get; set; } = "";
    public int SourceFormatVersion { get; set; }
    public string SourcePath { get; set; } = "";
    public DateTimeOffset ImportedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>One album document stored below a project workspace.</summary>
public sealed class StudioAlbumDocument
{
    public const int CurrentFormatVersion = 2;

    public int FormatVersion { get; set; } = CurrentFormatVersion;
    public string DocumentType { get; set; } = "ErkSAlbum";
    public string AlbumId { get; set; } = "building-architecture-concept";
    public string ProjectId { get; set; } = "";
    public string PackageType { get; set; } = ProjectWorkspace.BuildingArchitectureConcept;
    public string StageCode { get; set; } = ProjectWorkspace.ConceptDesignStage;
    public string Status { get; set; } = "Draft";
    public int FoundationVersion { get; set; } = 1;
    public AlbumDefinition Definition { get; set; } = new()
    {
        Title = "Барилга архитектурын загвар зургийн альбум",
    };
    public List<DeliverableRevisionRecord> Revisions { get; set; } = [];
}

public sealed class DeliverableRevisionRecord
{
    public string RevisionId { get; set; } = Guid.NewGuid().ToString("N");
    public int RevisionNumber { get; set; } = 1;
    public int Version { get; set; } = 1;
    public string ParentRevisionId { get; set; } = "";
    public string Status { get; set; } = "Draft";
    public int FoundationVersion { get; set; } = 1;
    public string CompanySnapshotId { get; set; } = "";
    public List<string> SourcePackageIds { get; set; } = [];
    public string PdfPath { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public int PageCount { get; set; }
    public string PageSizeSummary { get; set; } = "";
    public string CreatedBy { get; set; } = "";
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public string ReviewStatus { get; set; } = "Draft";
    public DeliverableApprovalRecord? ApprovalRecord { get; set; }
    public DateTimeOffset? ReleasedAtUtc { get; set; }
    public string SupersededByRevisionId { get; set; } = "";
    public string AuditNote { get; set; } = "";
}

public sealed class DeliverableApprovalRecord
{
    public string Status { get; set; } = "Approved";
    public string ApprovedBy { get; set; } = "";
    public DateTimeOffset ApprovedAtUtc { get; set; }
    public string Note { get; set; } = "";
}
