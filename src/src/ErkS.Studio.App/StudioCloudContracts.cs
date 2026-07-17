namespace ErkS.Studio;

internal sealed class StudioLicenseResponse
{
    public bool IsValid { get; set; }
    public string Message { get; set; } = "";
    public string LicenseId { get; set; } = "";
    public string ActivationId { get; set; } = "";
    public string ProductCode { get; set; } = "";
    public string Email { get; set; } = "";
    public string LicenseType { get; set; } = "";
    public DateTimeOffset ExpiresAtUtc { get; set; }
}

internal sealed class StudioSessionResponse
{
    public string AccessToken { get; set; } = "";
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public string AccountEmail { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string ProfileImageUrl { get; set; } = "";
    public string ProductCode { get; set; } = "";
    public string LicenseId { get; set; } = "";
    public string ActivationId { get; set; } = "";
    public string LicenseType { get; set; } = "";
    public DateTimeOffset LicenseExpiresAtUtc { get; set; }
}

internal sealed class StudioCloudProjectListResponse
{
    public List<StudioCloudProjectSummary> Projects { get; set; } = [];
}

internal sealed class StudioCloudOrganizationListResponse
{
    public List<StudioCloudOrganization> Organizations { get; set; } = [];
}

internal sealed class StudioCloudOrganization
{
    public string OrganizationId { get; set; } = "";
    public string LegalName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string ShortName { get; set; } = "";
    public string RegistrationNumber { get; set; } = "";
    public string OrganizationType { get; set; } = "";
    public string Status { get; set; } = "";
    public string VerificationStatus { get; set; } = "";
    public string RegisteredCity { get; set; } = "";
    public string Address { get; set; } = "";
    public string[] PhoneNumbers { get; set; } = [];
    public string Email { get; set; } = "";
    public string Website { get; set; } = "";
    public string LicenseScope { get; set; } = "";
    public string LicenseNumber { get; set; } = "";
    public string DirectorTitle { get; set; } = "";
    public string DirectorName { get; set; } = "";
    public string LogoUrl { get; set; } = "";
    public double LogoScale { get; set; } = 1d;
    public double LogoOffsetX { get; set; }
    public double LogoOffsetY { get; set; }
    public bool CanManage { get; set; }
    public string CurrentUserRole { get; set; } = "";
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

internal sealed class StudioCloudOrganizationUpsertRequest
{
    public string LegalName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string ShortName { get; set; } = "";
    public string RegistrationNumber { get; set; } = "";
    public string OrganizationType { get; set; } = "DesignCompany";
    public string RegisteredCity { get; set; } = "";
    public string Address { get; set; } = "";
    public string[] PhoneNumbers { get; set; } = [];
    public string Email { get; set; } = "";
    public string Website { get; set; } = "";
    public string LicenseScope { get; set; } = "";
    public string LicenseNumber { get; set; } = "";
    public string DirectorTitle { get; set; } = "";
    public string DirectorName { get; set; } = "";
    public double LogoScale { get; set; } = 1d;
    public double LogoOffsetX { get; set; }
    public double LogoOffsetY { get; set; }
}

internal sealed record StudioDownloadedImage(byte[] Bytes, string ContentType);

internal sealed class StudioCloudProjectSummary
{
    public string ProjectId { get; set; } = "";
    public string ProjectCode { get; set; } = "";
    public string Name { get; set; } = "";
    public string Status { get; set; } = "";
    public string CurrentStage { get; set; } = "";
    public string TemplateId { get; set; } = "";
    public string TemplateVersion { get; set; } = "";
    public string ClientName { get; set; } = "";
    public string PlanningAuthorityName { get; set; } = "";
    public string DesignOrganizationName { get; set; } = "";
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public string[] CurrentUserRoles { get; set; } = [];
    public string[] CurrentUserScopes { get; set; } = [];
    public string ConcurrencyToken { get; set; } = "";
}

internal sealed class StudioCloudProjectDetail
{
    public StudioCloudProjectSummary Project { get; set; } = new();
    public StudioCloudProjectInformation ProjectInformation { get; set; } = new();
    public StudioCloudSiteAndLand SiteAndLand { get; set; } = new();
    public StudioCloudOrganizationAssignment? ConceptAssignment { get; set; }
    public StudioCloudOrganizationRenderProfile? DesignOrganizationProfile { get; set; }
    public List<StudioCloudParticipant> Participants { get; set; } = [];
}

internal sealed class StudioCloudOrganizationRenderProfile
{
    public string OrganizationId { get; set; } = "";
    public string LegalName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string ShortName { get; set; } = "";
    public string RegistrationNumber { get; set; } = "";
    public string Address { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Email { get; set; } = "";
    public string Website { get; set; } = "";
    public string LicenseScope { get; set; } = "";
    public string LicenseNumber { get; set; } = "";
    public string DirectorTitle { get; set; } = "";
    public string DirectorName { get; set; } = "";
    public string LogoUrl { get; set; } = "";
    public double LogoScale { get; set; } = 1d;
    public double LogoOffsetX { get; set; }
    public double LogoOffsetY { get; set; }
    public bool IsProjectSnapshot { get; set; } = true;
}

internal sealed class StudioCloudProjectInformation
{
    public string ProjectId { get; set; } = "";
    public string ProjectCode { get; set; } = "";
    public string Name { get; set; } = "";
    public string Location { get; set; } = "";
    public string BuildingPurpose { get; set; } = "";
}

internal sealed class StudioCloudSiteAndLand
{
    public string[] ParcelNumbers { get; set; } = [];
    public string[] Addresses { get; set; } = [];
}

internal sealed class StudioCloudOrganizationAssignment
{
    public string OrganizationId { get; set; } = "";
    public string Scope { get; set; } = "";
    public string Status { get; set; } = "";
}

internal sealed class StudioCloudParticipant
{
    public string ParticipantId { get; set; } = "";
    public string AccountEmail { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string OrganizationId { get; set; } = "";
    public string[] Roles { get; set; } = [];
    public string Status { get; set; } = "";
}

internal sealed class StudioCloudAccountLookupResponse
{
    public bool Found { get; set; }
    public string Email { get; set; } = "";
    public string DisplayName { get; set; } = "";
}

internal sealed class StudioProjectMembershipInvitationCreateRequest
{
    public string TargetEmail { get; set; } = "";
    public string[] Roles { get; set; } = [];
    public int ExpiresInDays { get; set; } = 14;
}

internal sealed class StudioProjectMembershipInvitationListResponse
{
    public DateTimeOffset ServerTimeUtc { get; set; }
    public List<StudioProjectMembershipInvitation> Received { get; set; } = [];
    public List<StudioProjectMembershipInvitation> Issued { get; set; } = [];
}

internal sealed class StudioProjectMembershipInvitation
{
    public string InvitationId { get; set; } = "";
    public string ProjectId { get; set; } = "";
    public string ProjectCode { get; set; } = "";
    public string ProjectName { get; set; } = "";
    public string TargetEmail { get; set; } = "";
    public string TargetDisplayName { get; set; } = "";
    public string[] Roles { get; set; } = [];
    public string InvitedByEmail { get; set; } = "";
    public DateTimeOffset InvitedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public string Status { get; set; } = "";
}

internal sealed class StudioProjectRoleListResponse
{
    public List<StudioProjectRole> Roles { get; set; } = [];
}

internal sealed class StudioProjectRole
{
    public string Code { get; set; } = "";
    public string Label { get; set; } = "";
    public bool CanManageTeam { get; set; }
    public bool CanEditContent { get; set; }
    public bool CanSubmitAlbum { get; set; }
}

internal sealed class StudioProjectCreationGrantCreateRequest
{
    public string TargetEmail { get; set; } = "";
    public int ExpiresInDays { get; set; } = 30;
}

internal sealed class StudioProjectCreationGrantListResponse
{
    public DateTimeOffset ServerTimeUtc { get; set; }
    public List<StudioProjectCreationGrant> Received { get; set; } = [];
    public List<StudioProjectCreationGrant> Issued { get; set; } = [];
}

internal sealed class StudioProjectCreationGrant
{
    public string GrantId { get; set; } = "";
    public string OrganizationId { get; set; } = "";
    public string OrganizationName { get; set; } = "";
    public string OrganizationType { get; set; } = "";
    public string TargetEmail { get; set; } = "";
    public string IssuedByEmail { get; set; } = "";
    public DateTimeOffset IssuedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public string Status { get; set; } = "";
    public string ProjectId { get; set; } = "";
}

internal sealed class StudioCloudApiError
{
    public string Code { get; set; } = "";
    public string Message { get; set; } = "";
}

internal sealed class StudioCloudProjectCreateRequest
{
    public string ProjectCode { get; set; } = "";
    public string Name { get; set; } = "";
    public string Location { get; set; } = "";
    public string Description { get; set; } = "";
    public string TemplateId { get; set; } = StudioCloudTemplateIds.BuildingArchitectureConcept;
    public string ClientName { get; set; } = "";
    public string ClientEmail { get; set; } = "";
    public string InitiatorType { get; set; } = "";
    public string InitiatorOrganizationId { get; set; } = "";
    public string InitiatorOrganizationName { get; set; } = "";
}

internal sealed class StudioCloudDesignOrganizationAssignmentRequest
{
    public string OrganizationId { get; set; } = "";
}

internal static class StudioCloudTemplateIds
{
    public const string BuildingArchitectureConcept = "MN-BLD-ARCH-CONCEPT";
}

internal sealed class StudioCloudAlbum
{
    public string AlbumId { get; set; } = "";
    public string DesignPackageId { get; set; } = "";
    public string AlbumType { get; set; } = "";
    public string Title { get; set; } = "";
    public string CurrentRevisionId { get; set; } = "";
    public List<StudioCloudAlbumRevision> Revisions { get; set; } = [];
}

internal sealed class StudioCloudAlbumRevision
{
    public string RevisionId { get; set; } = "";
    public int RevisionNumber { get; set; }
    public string PdfFileId { get; set; } = "";
    public string PdfSha256 { get; set; } = "";
    public int PageCount { get; set; }
    public string PageSizeSummary { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTimeOffset CreatedAtUtc { get; set; }
}

internal sealed class StudioCloudSourcePackageCreateRequest
{
    public string SourceKey { get; set; } = "";
    public string SourceApplication { get; set; } = "";
    public string SourceDocumentReference { get; set; } = "";
    public string ManifestId { get; set; } = "";
    public string ManifestSchemaVersion { get; set; } = "1";
    public DateTimeOffset ExportedAtUtc { get; set; }
    public string WorkPackageId { get; set; } = "";
    public int SheetCount { get; set; }
    public string ContentHash { get; set; } = "";
}

internal sealed class StudioCloudSourcePackage
{
    public string SourceId { get; set; } = "";
    public string SourceKey { get; set; } = "";
    public string SourceApplication { get; set; } = "";
    public string SourceDocumentReference { get; set; } = "";
    public string ManifestId { get; set; } = "";
    public string ManifestSchemaVersion { get; set; } = "";
    public DateTimeOffset ExportedAtUtc { get; set; }
    public string StageId { get; set; } = "";
    public string WorkPackageId { get; set; } = "";
    public int SheetCount { get; set; }
    public string ContentHash { get; set; } = "";
    public string Status { get; set; } = "";
    public string OwnerOrganizationSnapshotId { get; set; } = "";
    public string RegisteredBy { get; set; } = "";
    public DateTimeOffset RegisteredAtUtc { get; set; }
}

internal sealed class StudioCloudDesignPackage
{
    public string DesignPackageId { get; set; } = "";
    public string DesignPackageType { get; set; } = "";
    public string TemplateId { get; set; } = "";
    public string TemplateVersion { get; set; } = "";
    public string Status { get; set; } = "";
    public string AlbumId { get; set; } = "";
    public List<StudioCloudSourcePackage> SourcePackages { get; set; } = [];
}
