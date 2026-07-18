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
    public string LastSyncedAlbumSha256 { get; set; } = "";
    public string LastSyncedRevisionId { get; set; } = "";
    public string LastServerConcurrencyToken { get; set; } = "";
    public string LastSyncError { get; set; } = "";
    public string LastSyncNote { get; set; } = "";
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
    public ProjectServerInformation Information { get; set; } = new();
    public ProjectServerSiteAndLand SiteAndLand { get; set; } = new();
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
    public ProjectCompanyAssignment DesignCompany { get; set; } = new();
}

public sealed class ProjectInitiationBasis
{
    public string SourceType { get; set; } = ProjectInitiationSourceTypes.AtdRequest;
    public string RequestNumber { get; set; } = "";
    public DateTimeOffset? RequestedAtUtc { get; set; }
    public string ClientName { get; set; } = "";
    public string ClientEmail { get; set; } = "";
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
    public string ServerDocumentId { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public int Version { get; set; } = 1;
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
    public const int CurrentFormatVersion = 1;

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
    public int Version { get; set; } = 1;
    public string Status { get; set; } = "Draft";
    public int FoundationVersion { get; set; } = 1;
    public string PdfPath { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
