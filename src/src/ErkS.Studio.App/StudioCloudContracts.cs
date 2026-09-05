namespace ErkS.Studio;

internal sealed class StudioProjectChatResponse
{
    public bool IsValid { get; set; }
    public string Message { get; set; } = "";
    public string ProjectId { get; set; } = "";
    public string ProjectCode { get; set; } = "";
    public string ProjectName { get; set; } = "";
    public string CurrentUserEmail { get; set; } = "";
    public string SelectedPeerEmail { get; set; } = "";
    public string ConversationKind { get; set; } = "";
    public int UnreadTotal { get; set; }
    public string[] ReactionChoices { get; set; } = [];
    public List<StudioProjectChatParticipant> Participants { get; set; } = [];
    public List<StudioProjectChatConversation> Conversations { get; set; } = [];
    public StudioProjectChatParticipant? SelectedPeer { get; set; }
    public List<StudioProjectChatMessage> Messages { get; set; } = [];
    public DateTimeOffset ServerTimeUtc { get; set; }
}

internal sealed class StudioProjectChatParticipant
{
    public string Email { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Initials { get; set; } = "";
    public string RoleLabel { get; set; } = "";
    public string ProfileImageUrl { get; set; } = "";
}

internal sealed class StudioProjectChatConversation
{
    public string PeerEmail { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Initials { get; set; } = "";
    public string RoleLabel { get; set; } = "";
    public string ProfileImageUrl { get; set; } = "";
    public string LastMessagePreview { get; set; } = "";
    public DateTimeOffset LastMessageAtUtc { get; set; }
    public string LastMessageTime { get; set; } = "";
    public bool LastMessageIsMine { get; set; }
    public int UnreadCount { get; set; }
}

internal sealed class StudioProjectChatMessage
{
    public string MessageId { get; set; } = "";
    public string AuthorEmail { get; set; } = "";
    public string AuthorDisplayName { get; set; } = "";
    public string AuthorInitials { get; set; } = "";
    public string AuthorRoleLabel { get; set; } = "";
    public string AuthorProfileImageUrl { get; set; } = "";
    public bool IsMine { get; set; }
    public bool ReadByPeer { get; set; }
    public string ReadLabel { get; set; } = "";
    public string Body { get; set; } = "";
    public DateTimeOffset CreatedAtUtc { get; set; }
    public string DisplayTime { get; set; } = "";
    public string AttachmentId { get; set; } = "";
    public string AttachmentFileName { get; set; } = "";
    public string AttachmentUrl { get; set; } = "";
    public string AttachmentContentType { get; set; } = "";
    public long AttachmentSizeBytes { get; set; }
    public DateTimeOffset AttachmentExpiresAtUtc { get; set; }
    public bool AttachmentIsImage { get; set; }
    public bool AttachmentExpired { get; set; }
    public List<StudioProjectChatReaction> Reactions { get; set; } = [];
}

internal sealed class StudioProjectChatReaction
{
    public string Reaction { get; set; } = "";
    public int Count { get; set; }
    public bool ReactedByMe { get; set; }
}

internal sealed class StudioProjectChatReactionRequest
{
    public string Reaction { get; set; } = "";
    public string PeerEmail { get; set; } = "";
}

internal abstract class StudioDeviceBoundRequest
{
    public string DeviceFingerprint { get; set; } = "";
    public string LegacyDeviceFingerprint { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public string AppVersion { get; set; } = "";

    /// <summary>
    /// Which program is asking, and the version it reports for itself.
    /// </summary>
    /// <remarks>
    /// Defaulted here rather than set at each call site, and the difference
    /// from the relationship-boundary header - which must not be attached
    /// automatically - is what these two things are. That header is a claim
    /// that a person consented, so it may only follow a person's action. This
    /// is a fact about the running program, true on every request whether or
    /// not anyone is watching, so a default in the type is the honest place
    /// for it and a new call site cannot forget it.
    ///
    /// The version is sent exactly as Studio reports it, under SRV's
    /// read-do-not-compute rule of 2026-08-30: it can be "Demo V0.001.55",
    /// "0.001.56-dev", or "CI Smoke", each with the commit appended. Those are
    /// not three malformed versions - they are what this build is called, and
    /// normalising them would replace what Studio knows with a guess. The
    /// server stores the string and does not parse it.
    /// </remarks>
    public string HostApplication { get; set; } = StudioHost.Application;

    public string HostVersion { get; set; } = StudioHost.Version;
}

/// <summary>What this program calls itself when a server asks.</summary>
internal static class StudioHost
{
    public const string Application = "Studio";

    /// <summary>
    /// The assembly's informational version - a build label rather than a
    /// number, and load-bearing as one: its "-dev" suffix is how Studio decides
    /// it is a development build and does not enforce its companion licence.
    /// See docs/VERSIONING.md.
    /// </summary>
    public static string Version { get; } =
        System.Reflection.CustomAttributeExtensions
            .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(
                typeof(StudioHost).Assembly)
            ?.InformationalVersion
        ?? typeof(StudioHost).Assembly.GetName().Version?.ToString()
        ?? "dev";
}

internal sealed class StudioLicenseActivateRequest : StudioDeviceBoundRequest
{
    public string ProductCode { get; set; } = "";
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
}

internal sealed class StudioLicenseValidateRequest : StudioDeviceBoundRequest
{
    public string ProductCode { get; set; } = "";
    public string Email { get; set; } = "";
    public string LicenseId { get; set; } = "";
    public string ActivationId { get; set; } = "";
}

internal sealed class StudioSessionRequest : StudioDeviceBoundRequest
{
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
    public string ClientName { get; set; } = "Erk-S Studio";
    public string ProductCode { get; set; } = "";
    public string LicenseId { get; set; } = "";
    public string ActivationId { get; set; } = "";
}

internal sealed class StudioSessionRefreshRequest : StudioDeviceBoundRequest
{
    public string Email { get; set; } = "";
    public string ProductCode { get; set; } = "";
    public string LicenseId { get; set; } = "";
    public string ActivationId { get; set; } = "";
}

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

    /// <summary>See <see cref="StudioSessionResponse.Entitlements"/>.</summary>
    public StudioCloudEntitlements? Entitlements { get; set; }
}

internal sealed class StudioSessionResponse
{
    public string AccessToken { get; set; } = "";
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public string AccountEmail { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string FamilyName { get; set; } = "";
    public string GivenName { get; set; } = "";
    public string ProfileImageUrl { get; set; } = "";
    public string ProductCode { get; set; } = "";
    public string LicenseId { get; set; } = "";
    public string ActivationId { get; set; } = "";
    public string LicenseType { get; set; } = "";
    public DateTimeOffset LicenseExpiresAtUtc { get; set; }

    /// <summary>
    /// What this account is entitled to across the product family. Absent from
    /// servers that predate companion entitlements, which MUST read as "this
    /// server does not know", never as "no licence".
    /// </summary>
    public StudioCloudEntitlements? Entitlements { get; set; }
}

internal sealed class StudioCloudEntitlements
{
    public string PlatformTier { get; set; } = "";

    public string CityGenTier { get; set; } = "";

    /// <summary>True when an active Platform or CityGen licence opens Studio.</summary>
    public bool StudioCompanion { get; set; }

    /// <summary>
    /// When the licence granting the companion expires. It is not the Studio
    /// product licence's own expiry, and a server that does not state it leaves
    /// the offline grace window as the only limit.
    /// </summary>
    public DateTimeOffset? CompanionExpiresAtUtc { get; set; }

    public Dictionary<string, bool>? Features { get; set; }
}

internal sealed class StudioCloudProjectListResponse
{
    public List<StudioCloudProjectSummary> Projects { get; set; } = [];
}

internal sealed class StudioCloudOrganizationListResponse
{
    public bool OrganizationRegistryImportConfigured { get; set; }
    public string OrganizationRegistryImportMessage { get; set; } = "";
    public List<StudioCloudOrganization> Organizations { get; set; } = [];
}

internal sealed class StudioCloudOrganization
{
    public string OrganizationId { get; set; } = "";
    public string ConcurrencyToken { get; set; } = "";

    /// <summary>
    /// The organisation's registration certificate as the server holds it.
    /// Read here mainly for the fingerprints: a scan already up there must not
    /// be sent again, and the website can put one there too.
    /// </summary>
    public List<StudioCloudOrganizationDocument> RegistrationCertificateDocuments { get; set; } = [];

    /// <summary>The design licence, on the same footing.</summary>
    public List<StudioCloudOrganizationDocument> DesignLicenseDocuments { get; set; } = [];

    public string LegalName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string ShortName { get; set; } = "";
    public string RegistrationNumber { get; set; } = "";
    public string LegalEntityType { get; set; } = "";
    public string LegalForm { get; set; } = "";
    public string[] ActivityDirections { get; set; } = [];
    public DateTimeOffset? RegisteredAtUtc { get; set; }
    public string OfficialRepresentativeName { get; set; } = "";
    public string RegistrySource { get; set; } = "SelfDeclared";
    public string RegistrySourceUrl { get; set; } = "https://opendata.burtgel.gov.mn/les";
    public DateTimeOffset? RegistryCheckedAtUtc { get; set; }
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
    public string DesignRepresentativeTitle { get; set; } = "";
    public string DesignRepresentativeName { get; set; } = "";
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
    public string BaseConcurrencyToken { get; set; } = "";
    public bool RegistryFieldsIncluded { get; set; }
    public string LegalName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string ShortName { get; set; } = "";
    public string RegistrationNumber { get; set; } = "";
    public string LegalEntityType { get; set; } = "";
    public string LegalForm { get; set; } = "";
    public string[] ActivityDirections { get; set; } = [];
    public DateTimeOffset? RegisteredAtUtc { get; set; }
    public string OfficialRepresentativeName { get; set; } = "";
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
    public string DesignRepresentativeTitle { get; set; } = "";
    public string DesignRepresentativeName { get; set; } = "";

    /// <summary>
    /// Tells the server this request means the two representative fields
    /// literally, so an empty architect clears the stored one.
    /// </summary>
    /// <remarks>
    /// Off, the server ignores the architect half and edits only the director
    /// - which is what a client should ask for when it does not know who the
    /// architect is. See <c>CompanyProfile.DesignRepresentativeKnown</c>.
    /// </remarks>
    public bool SupportsSeparateRepresentatives { get; set; }

    public double LogoScale { get; set; } = 1d;
    public double LogoOffsetX { get; set; }
    public double LogoOffsetY { get; set; }
}

internal static class StudioOrganizationRegistryImportStatuses
{
    public const string PendingAuthorization = "PendingAuthorization";
    public const string Processing = "Processing";
    public const string Completed = "Completed";
    public const string Failed = "Failed";
}

internal sealed class StudioOrganizationRegistryImportRequest
{
    public string RegistrationNumber { get; set; } = "";
    public string BaseConcurrencyToken { get; set; } = "";
}

internal sealed class StudioOrganizationRegistryImportResponse
{
    public string ImportId { get; set; } = "";
    public string Status { get; set; } = StudioOrganizationRegistryImportStatuses.PendingAuthorization;
    public string AuthorizationUrl { get; set; } = "";
    public string Message { get; set; } = "";
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public bool ProviderConfigured { get; set; }
    public StudioCloudOrganization? Organization { get; set; }
}

internal sealed record StudioDownloadedImage(byte[] Bytes, string ContentType);

internal sealed class StudioCloudProjectSummary
{
    public string ProjectId { get; set; } = "";
    public string ProjectCode { get; set; } = "";
    public string Name { get; set; } = "";
    public string ProjectDomain { get; set; } = "";
    public string StageType { get; set; } = "";
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
    public bool CurrentUserIsCreator { get; set; }
    public string ConcurrencyToken { get; set; } = "";

    /// <summary>
    /// If-Match token for the information endpoint alone. It does not move when
    /// an album is uploaded or a member is added, so a queued information edit
    /// survives the user's own unrelated activity - which is what invalidated
    /// it before. Empty against a server that predates it; callers fall back to
    /// <see cref="ConcurrencyToken"/> there.
    /// </summary>
    public string InformationConcurrencyToken { get; set; } = "";
}

internal sealed class StudioCloudProjectDeleteRequest
{
    public string ConfirmProjectCode { get; set; } = "";
    public string Reason { get; set; } = "";
}

internal sealed class StudioCloudProjectDetail
{
    public StudioCloudProjectSurface? Surface { get; set; }
    public StudioCloudProjectSummary Project { get; set; } = new();
    public StudioCloudProjectInformation ProjectInformation { get; set; } = new();
    public StudioCloudProjectFoundation? Foundation { get; set; }
    public StudioCloudSiteAndLand SiteAndLand { get; set; } = new();
    public StudioCloudBuildingComposition? BuildingComposition { get; set; }
    public StudioCloudOrganizationAssignment? ConceptAssignment { get; set; }
    public StudioCloudOrganizationRenderProfile? DesignOrganizationProfile { get; set; }
    public List<StudioCloudStageInstance> Stages { get; set; } = [];
    public List<StudioCloudOrganizationAssignment> OrganizationAssignments { get; set; } = [];
    public List<StudioCloudParticipant> Participants { get; set; } = [];
    public List<StudioCloudDesignPackage> DesignPackages { get; set; } = [];
    public List<StudioCloudAlbum> Albums { get; set; } = [];
}

internal sealed record StudioCloudProjectRefreshResult(
    bool IsModified,
    StudioCloudProjectDetail? Project);

internal sealed class StudioCloudProjectSurface
{
    public string SchemaVersion { get; set; } = "";
    public string ProductName { get; set; } = "";
    public List<StudioCloudProjectSurfaceSection> Sections { get; set; } = [];
    public List<StudioCloudProjectSurfaceSection> FoundationSections { get; set; } = [];
    public StudioCloudProjectSurfaceTheme Theme { get; set; } = new();
}

internal sealed class StudioCloudProjectSurfaceSection
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string Icon { get; set; } = "";
    public int Order { get; set; }
}

internal sealed class StudioCloudProjectSurfaceTheme
{
    public string WindowBackground { get; set; } = "";
    public string Panel { get; set; } = "";
    public string PanelAlt { get; set; } = "";
    public string Input { get; set; } = "";
    public string Border { get; set; } = "";
    public string BorderHover { get; set; } = "";
    public string Text { get; set; } = "";
    public string MutedText { get; set; } = "";
    public string FaintText { get; set; } = "";
    public string Accent { get; set; } = "";
    public string AccentSoft { get; set; } = "";
    public string Button { get; set; } = "";
    public string Success { get; set; } = "";
    public string Warning { get; set; } = "";
    public string Danger { get; set; } = "";
    public int RailWidth { get; set; }
    public int CornerRadius { get; set; }
}

internal sealed class StudioCloudProjectFoundation
{
    public int Version { get; set; } = 1;
    public StudioCloudProjectInitiationBasis InitiationBasis { get; set; } = new();
    public StudioCloudPlanningTask PlanningTask { get; set; } = new();
}

internal sealed class StudioCloudProjectInitiationBasis
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

internal sealed class StudioCloudPlanningTask
{
    public string AtdNumber { get; set; } = "";
    public DateTimeOffset? IssuedAtUtc { get; set; }
    public string IssuingAuthorityName { get; set; } = "";
    public string Status { get; set; } = "";
    public string Summary { get; set; } = "";
    public string[] Requirements { get; set; } = [];
}

internal sealed class StudioCloudOrganizationRenderProfile
{
    public string OrganizationId { get; set; } = "";
    public string LegalName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string ShortName { get; set; } = "";
    public string RegistrationNumber { get; set; } = "";
    public string LegalEntityType { get; set; } = "";
    public string LegalForm { get; set; } = "";
    public string[] ActivityDirections { get; set; } = [];
    public DateTimeOffset? RegisteredAtUtc { get; set; }
    public string OfficialRepresentativeName { get; set; } = "";
    public string RegistrySource { get; set; } = "SelfDeclared";
    public string RegistrySourceUrl { get; set; } = "https://opendata.burtgel.gov.mn/les";
    public DateTimeOffset? RegistryCheckedAtUtc { get; set; }
    public string RegisteredCity { get; set; } = "";
    public string Address { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Email { get; set; } = "";
    public string Website { get; set; } = "";
    public string LicenseScope { get; set; } = "";
    public string LicenseNumber { get; set; } = "";
    public string DirectorTitle { get; set; } = "";
    public string DirectorName { get; set; } = "";
    public string DesignRepresentativeTitle { get; set; } = "";
    public string DesignRepresentativeName { get; set; } = "";
    public string LogoUrl { get; set; } = "";
    public double LogoScale { get; set; } = 1d;
    public double LogoOffsetX { get; set; }
    public double LogoOffsetY { get; set; }
    public bool IsProjectSnapshot { get; set; } = true;

    /// <summary>
    /// The organisation's registration certificate, as scans the server holds.
    ///
    /// Somebody uploads these into their own organisation once and every
    /// project that organisation is on should carry them. Until this arrived
    /// they only existed on the machine of whoever added them, so a colleague
    /// opening the same project found the certificate page empty and was told,
    /// in effect, that they had not uploaded it.
    ///
    /// Empty against a server that predates the field, which is every server
    /// until the next deploy - the album keeps its placeholder page and says
    /// why.
    /// </summary>
    public List<StudioCloudOrganizationDocument> RegistrationCertificateDocuments { get; set; } = [];

    /// <summary>The organisation's design licence, on the same footing.</summary>
    public List<StudioCloudOrganizationDocument> DesignLicenseDocuments { get; set; } = [];
}

/// <summary>
/// One scan the server holds for an organisation.
/// </summary>
internal sealed class StudioCloudOrganizationDocument
{
    public string DocumentId { get; set; } = "";
    public string Category { get; set; } = "";
    public string Title { get; set; } = "";
    public string OriginalFileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }

    /// <summary>
    /// How many faces the scan has, or <c>0</c> for "not counted". Zero is not
    /// "no pages": the server counts only what it can open, and a document it
    /// could not measure still has to be drawn.
    /// </summary>
    public int PageCount { get; set; }

    public string Sha256 { get; set; } = "";

    /// <summary>
    /// Where to fetch the file, relative to the server. The path goes through
    /// the project rather than the organisation, so being a member of the
    /// project is enough - which is the situation of most people who need to
    /// print the album.
    /// </summary>
    public string ContentUrl { get; set; } = "";

    public DateTimeOffset? UpdatedAtUtc { get; set; }
}

internal sealed class StudioCloudProjectInformation
{
    public string ProjectId { get; set; } = "";
    public string ProjectCode { get; set; } = "";
    public string Name { get; set; } = "";
    public string ProjectDomain { get; set; } = "";
    public string StageType { get; set; } = "";
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

internal sealed class StudioCloudProjectInformationUpdateRequest
{
    public string ProjectCode { get; set; } = "";
    public string Name { get; set; } = "";
    public string ProjectType { get; set; } = "";
    public string StageType { get; set; } = "";
    public string ClientName { get; set; } = "";
    public string PlanningAuthorityName { get; set; } = "";
    public string DesignOrganizationName { get; set; } = "";
    public string Location { get; set; } = "";
    public string BuildingPurpose { get; set; } = "";
    public string CapacityUnit { get; set; } = "";
    public StudioCloudProjectFoundationUpdate Foundation { get; set; } = new();
}

internal sealed class StudioCloudBuildingComposition
{
    public int Version { get; set; } = 1;
    public List<StudioCloudBuildingGroup> Groups { get; set; } = [];
    public List<StudioCloudBuildingSheetAssignment> SheetAssignments { get; set; } = [];
}

internal sealed class StudioCloudBuildingCompositionUpdateRequest
{
    public List<StudioCloudBuildingGroup> Groups { get; set; } = [];
    public List<StudioCloudBuildingSheetAssignment> SheetAssignments { get; set; } = [];
}

internal sealed class StudioCloudBuildingGroup
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int Order { get; set; }
}

internal sealed class StudioCloudBuildingSheetAssignment
{
    public string SourceOwnerEmail { get; set; } = "";
    public string SourceKey { get; set; } = "";
    public string SheetId { get; set; } = "";
    public string BuildingGroupId { get; set; } = "";
}

internal sealed class StudioCloudProjectFoundationUpdate
{
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

internal sealed class StudioCloudSiteAndLand
{
    public string[] ParcelNumbers { get; set; } = [];
    public string[] Addresses { get; set; } = [];
    public string[] RestrictionReferences { get; set; } = [];
}

internal sealed class StudioCloudOrganizationAssignment
{
    public string AssignmentId { get; set; } = "";
    public string StageInstanceId { get; set; } = "";
    public string OrganizationId { get; set; } = "";
    public string OrganizationSnapshotId { get; set; } = "";
    public string Scope { get; set; } = "";
    public string Status { get; set; } = "";
    public string Role { get; set; } = "";
    public DateTimeOffset? AcceptedAtUtc { get; set; }
    public DateTimeOffset? EndedAtUtc { get; set; }
    public StudioCloudOrganizationRenderProfile? OrganizationProfile { get; set; }
}

internal sealed class StudioCloudStageInstance
{
    public string StageInstanceId { get; set; } = "";
    public string StageType { get; set; } = "";
    public int Sequence { get; set; }
    public string PreviousStageInstanceId { get; set; } = "";
    public string BasisAlbumRevisionId { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
}

internal sealed class StudioCloudParticipant
{
    public string ParticipantId { get; set; } = "";
    public string AccountEmail { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string FamilyName { get; set; } = "";
    public string GivenName { get; set; } = "";
    public string OrganizationId { get; set; } = "";
    public string[] Roles { get; set; } = [];
    public string Status { get; set; } = "";

    /// <summary>
    /// When the server last heard from this person, or null when it never has.
    /// </summary>
    /// <remarks>
    /// A timestamp rather than an "online" flag: a flag decided when the
    /// response was built is stale by the time it is read, and "3 цагийн өмнө"
    /// cannot be recovered from the word "Offline". Null is not offline - it is
    /// nobody having heard from them.
    /// </remarks>
    public DateTimeOffset? LastSeenAtUtc { get; set; }

    public string ProfileImageUrl { get; set; } = "";
    public string Initials { get; set; } = "";
}

/// <summary>
/// One rule the server hands out so it can be changed without updating anyone's
/// Studio.
/// </summary>
internal sealed class StudioServerRule
{
    public string Id { get; set; } = "";
    public int Version { get; set; }
    public Dictionary<string, long> Values { get; set; } = [];
}

internal sealed class StudioServerRulesResponse
{
    public List<StudioServerRule> Rules { get; set; } = [];
}

internal sealed class StudioConceptArchitectAssignmentRequest
{
    public string ParticipantId { get; set; } = "";
}

internal sealed class StudioParticipantRoleUpdateRequest
{
    public string[] Roles { get; set; } = [];
}

internal sealed class StudioCloudAccountLookupResponse
{
    public bool Found { get; set; }
    public string Email { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string FamilyName { get; set; } = "";
    public string GivenName { get; set; } = "";
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

internal sealed class StudioProjectMembershipExitRequestCreateRequest
{
    public string Reason { get; set; } = "";
}

internal sealed class StudioProjectMembershipExitRequestListResponse
{
    public DateTimeOffset ServerTimeUtc { get; set; }
    public List<StudioProjectMembershipExitRequest> Requested { get; set; } = [];
    public List<StudioProjectMembershipExitRequest> AwaitingApproval { get; set; } = [];
}

internal sealed class StudioProjectMembershipExitRequest
{
    public string RequestId { get; set; } = "";
    public string ProjectId { get; set; } = "";
    public string ProjectCode { get; set; } = "";
    public string ProjectName { get; set; } = "";
    public string ParticipantId { get; set; } = "";
    public string ParticipantEmail { get; set; } = "";
    public string ParticipantDisplayName { get; set; } = "";
    public string ApprovalOrganizationId { get; set; } = "";
    public string ApprovalOrganizationName { get; set; } = "";
    public string Reason { get; set; } = "";
    public string[] AffectedSourceKeys { get; set; } = [];
    public DateTimeOffset RequestedAtUtc { get; set; }
    public string Status { get; set; } = "";
    public string DecidedByEmail { get; set; } = "";
    public DateTimeOffset? DecidedAtUtc { get; set; }
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
    public string TraceId { get; set; } = "";
    public string CurrentSourceId { get; set; } = "";
    public string CurrentRevisionId { get; set; } = "";

    /// <summary>
    /// The organization's canonical concurrency token at the moment a write
    /// was refused with 412, so the caller can retry without a full re-list.
    /// </summary>
    public string CurrentOrganizationConcurrencyToken { get; set; } = "";

    public Dictionary<string, string[]>? FieldErrors { get; set; }
}

internal sealed class StudioCloudProjectCreateRequest
{
    public string ProjectCode { get; set; } = "";
    public string Name { get; set; } = "";
    public string Location { get; set; } = "";
    public string Description { get; set; } = "";
    public string TemplateId { get; set; } = "";
    public string ProjectType { get; set; } = "";
    public string InitialStageType { get; set; } = "";
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

internal sealed class StudioCloudStageAdvanceRequest
{
    public string CurrentStageInstanceId { get; set; } = "";
    public string NextStageType { get; set; } = "";
    public string BasisAlbumRevisionId { get; set; } = "";
    public string TargetOrganizationEmail { get; set; } = "";
}

internal sealed class StudioCloudStageAdvanceResponse
{
    public StudioCloudProjectDetail Project { get; set; } = new();
    public string InvitationId { get; set; } = "";
    public string InvitationCode { get; set; } = "";
    public DateTimeOffset? InvitationExpiresAtUtc { get; set; }
}

internal static class StudioCloudTemplateIds
{
    public const string BuildingArchitectureConcept = "MN-BLD-ARCH-CONCEPT";
}

internal sealed class StudioCloudControlledDocument
{
    public string DocumentId { get; set; } = "";
    public string[] RequirementKeys { get; set; } = [];
    public string Category { get; set; } = "";
    public string DocumentType { get; set; } = "";
    public string Title { get; set; } = "";
    public string DocumentNumber { get; set; } = "";
    public int Version { get; set; }
    public string Status { get; set; } = "";
    public string Visibility { get; set; } = "";
    public List<StudioCloudFile> FileRevisions { get; set; } = [];
    public string[] CurrentFileRevisionIds { get; set; } = [];
    public List<StudioCloudFile> CurrentFiles { get; set; } = [];
    public string UpdatedBy { get; set; } = "";
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

internal sealed class StudioCloudFile
{
    public string FileRevisionId { get; set; } = "";
    public string FileId { get; set; } = "";
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = "";
    public string ScanStatus { get; set; } = "";
    public string UploadedBy { get; set; } = "";
    public DateTimeOffset UploadedAtUtc { get; set; }
}

internal sealed class StudioCloudAlbum
{
    public string AlbumId { get; set; } = "";
    public string DesignPackageId { get; set; } = "";
    public string AlbumType { get; set; } = "";
    public string Title { get; set; } = "";
    public string CurrentRevisionId { get; set; } = "";
    public int RequiredBuildingCompositionVersion { get; set; }
    public bool CanonicalRebuildPending { get; set; }
    public bool CanonicalReflowRequired { get; set; }
    public List<string> PendingComponentTombstoneCodes { get; set; } = [];
    public List<StudioCloudAlbumRevision> Revisions { get; set; } = [];
}

internal sealed class StudioCloudAlbumRevision
{
    public string RevisionId { get; set; } = "";
    public int RevisionNumber { get; set; }
    public string PdfFileId { get; set; } = "";
    public string PdfSha256 { get; set; } = "";
    public string SourceUploadSha256 { get; set; } = "";
    public int PageCount { get; set; }
    public string PageSizeSummary { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTimeOffset CreatedAtUtc { get; set; }
    public int BuildingCompositionVersion { get; set; }
    public List<StudioCloudAlbumSection> SectionManifest { get; set; } = [];
}

internal sealed class StudioCloudAlbumSection
{
    public string Code { get; set; } = "";
    public string Label { get; set; } = "";
    public int Order { get; set; }
    public int[] PageNumbers { get; set; } = [];
    public string Status { get; set; } = "";
    public string OwnerEmail { get; set; } = "";
    public string SourceKey { get; set; } = "";
    public string ComponentKind { get; set; } = "";
    public string SectionKey { get; set; } = "";
    public string SequenceKey { get; set; } = "";
    public List<StudioCloudAlbumComponentPage> Pages { get; set; } = [];
}

internal sealed class StudioCloudAlbumComponentPage
{
    public int PageNumber { get; set; }
    public string PageKey { get; set; } = "";
    public string Title { get; set; } = "";
    public string SortKey { get; set; } = "";
    public string SectionKey { get; set; } = "";
    public string SequenceKey { get; set; } = "";
}

internal sealed class StudioCloudAlbumComponentManifestUpdateRequest
{
    public string ProjectConcurrencyToken { get; set; } = "";
    public string ExpectedBaseRevisionId { get; set; } = "";
    public List<StudioCloudAlbumSection> Components { get; set; } = [];
}

internal sealed class StudioCloudAlbumComponentUploadDescriptor
{
    public string FieldName { get; set; } = "";
    public string Code { get; set; } = "";
    public string Label { get; set; } = "";
    public int Order { get; set; }
    public bool Remove { get; set; }
    public string SourceKey { get; set; } = "";
    public string ComponentKind { get; set; } = "";
    public string SectionKey { get; set; } = "";
    public string SequenceKey { get; set; } = "";
    public List<StudioCloudAlbumComponentPage> Pages { get; set; } = [];
}

internal sealed record StudioAlbumComponentUpload(
    string Code,
    string Label,
    int Order,
    string PdfPath,
    bool Remove = false,
    string SourceKey = "",
    string ComponentKind = "",
    string SectionKey = "",
    string SequenceKey = "",
    IReadOnlyList<StudioCloudAlbumComponentPage>? Pages = null);

internal sealed class StudioCloudAlbumUploadStartRequest
{
    public string FileName { get; set; } = "";
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = "";
    public int PageCount { get; set; }
    public string PageSizeSummary { get; set; } = "";
    public int ChunkSizeBytes { get; set; }
    public string ProjectConcurrencyToken { get; set; } = "";
    public string? ExpectedBaseRevisionId { get; set; }
    public bool InheritComponentManifest { get; set; }
    public List<StudioCloudAlbumSection>? ComponentManifest { get; set; }
}

internal sealed class StudioCloudAlbumUploadSession
{
    public string UploadId { get; set; } = "";
    public int ChunkSizeBytes { get; set; }
    public int TotalChunks { get; set; }
    public int[] ReceivedChunks { get; set; } = [];
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public string CompletedRevisionId { get; set; } = "";
}

internal sealed class StudioCloudSourcePackageCreateRequest
{
    public string ExpectedBaseSourceId { get; set; } = "";
    public string SourceKey { get; set; } = "";
    public string SourceApplication { get; set; } = "";
    public string SourcePurpose { get; set; } = "";
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
    public string SourcePurpose { get; set; } = "";
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
    public string CustodianParticipantId { get; set; } = "";
    public string CustodianEmail { get; set; } = "";
    public string CustodyStatus { get; set; } = "";
}

internal sealed class StudioCloudSourceCustodianAssignRequest
{
    public string ParticipantId { get; set; } = "";
    public string ProjectConcurrencyToken { get; set; } = "";
    public string ExpectedSourceId { get; set; } = "";
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

internal sealed class StudioSheetCommentReply
{
    public string ReplyId { get; set; } = "";
    public string AuthorEmail { get; set; } = "";
    public string AuthorDisplayName { get; set; } = "";
    public string AuthorInitials { get; set; } = "";
    public string Body { get; set; } = "";
    public DateTimeOffset CreatedAtUtc { get; set; }
}

internal sealed class StudioSheetComment
{
    public string CommentId { get; set; } = "";
    public string PageIdentity { get; set; } = "";
    public string PageLabel { get; set; } = "";
    public int PageNumber { get; set; }
    public double AnchorX { get; set; }
    public double AnchorY { get; set; }
    public string Shape { get; set; } = "";
    public List<StudioSheetCommentPoint> ShapePoints { get; set; } = [];
    public string Kind { get; set; } = "";
    public string Status { get; set; } = "";
    public string Body { get; set; } = "";
    public string AuthorEmail { get; set; } = "";
    public string AuthorDisplayName { get; set; } = "";
    public string AuthorInitials { get; set; } = "";
    public string AuthorRoleLabel { get; set; } = "";
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public string ResolvedByEmail { get; set; } = "";
    public string ResolvedByDisplayName { get; set; } = "";
    public DateTimeOffset? ResolvedAtUtc { get; set; }
    public bool CanManage { get; set; }
    public List<StudioSheetCommentReply> Replies { get; set; } = [];
}

internal sealed class StudioSheetCommentList
{
    public string ProjectId { get; set; } = "";
    public string CurrentUserEmail { get; set; } = "";
    public bool CanComment { get; set; }
    public int OpenCount { get; set; }
    public int ChangeRequiredCount { get; set; }
    public List<StudioSheetComment> Comments { get; set; } = [];
}

internal sealed class StudioSheetCommentCreateRequest
{
    public string PageIdentity { get; set; } = "";
    public string PageLabel { get; set; } = "";
    public int PageNumber { get; set; }
    public double AnchorX { get; set; }
    public double AnchorY { get; set; }
    public string Shape { get; set; } = "";
    public List<StudioSheetCommentPoint> ShapePoints { get; set; } = [];
    public string Kind { get; set; } = "";
    public string Body { get; set; } = "";
}

internal sealed class StudioSheetCommentReplyRequest
{
    public string Body { get; set; } = "";
}

internal sealed class StudioSheetCommentStatusRequest
{
    public string Status { get; set; } = "";
}

/// <summary>One point of a drawn mark, as a fraction of the page.</summary>
internal sealed class StudioSheetCommentPoint
{
    public double X { get; set; }
    public double Y { get; set; }
}

// ---------------------------------------------------------------------------
// Bot seats, bot state, PIN and link invitations.
//
// Shapes mirror the server's OpenAPI snapshot exactly
// (Erk-S-Server/src/ErkS.LicenseServer/openapi/ErkS.LicenseServer.json).
// Both device fingerprint forms travel on every request that names a device:
// one machine has two valid values, and a request carrying only one proves
// nothing about a record stored under the other.
// ---------------------------------------------------------------------------

internal sealed class StudioCloudBotSeat
{
    public string BotId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string InternalEmail { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTimeOffset CreatedAtUtc { get; set; }
    public string CreatedByEmail { get; set; } = "";

    /// <summary>Whether a machine is sitting on this seat right now.</summary>
    public bool DeviceSeated { get; set; }

    /// <summary>When that machine entered bot state; null when none is on the seat.</summary>
    public DateTimeOffset? DeviceSeatedAtUtc { get; set; }

    /// <summary>
    /// Who put the MACHINE into bot state - an act, at one moment. Not the same
    /// fact as MemberEmail, and reading one for the other shows the wrong name
    /// the first time an owner seats a machine on somebody else's behalf.
    /// </summary>
    public string DeviceSeatedByEmail { get; set; } = "";

    /// <summary>
    /// Who is staffed on the SEAT - a relationship, over an interval. Empty
    /// when nobody is, which is a normal state for a seat that exists and has
    /// not been filled.
    /// </summary>
    public string MemberEmail { get; set; } = "";

    public DateTimeOffset? MemberSinceUtc { get; set; }
}

/// <summary>
/// One project a seat is assigned to, with the roles it holds there.
///
/// The assignment belongs to the SEAT, not to whoever is staffed on it: a
/// member leaving closes their interval and leaves the assignment standing.
/// </summary>
internal sealed class StudioCloudBotAssignment
{
    public string AssignmentId { get; set; } = "";
    public string ProjectId { get; set; } = "";

    /// <summary>
    /// Resolved by the server. A screen showing only srv_prj_4d81e2a7 is a
    /// screen nobody can use, and the client cannot look up a project it is not
    /// a member of.
    /// </summary>
    public string ProjectName { get; set; } = "";

    public List<string> Roles { get; set; } = [];
    public DateTimeOffset AssignedAtUtc { get; set; }
    public string AssignedByEmail { get; set; } = "";
}

internal sealed class StudioCloudBotAssignmentListResponse
{
    public List<StudioCloudBotAssignment> Assignments { get; set; } = [];
    public DateTimeOffset ServerTimeUtc { get; set; }
}

internal sealed class StudioCloudBotAssignmentWriteRequest
{
    public string ProjectId { get; set; } = "";
    public List<string> Roles { get; set; } = [];
}

internal sealed class StudioCloudBotSeatListResponse
{
    public List<StudioCloudBotSeat> Items { get; set; } = [];
    public int OccupiedSeats { get; set; }
    public int DeviceRights { get; set; }
    /// <summary>
    /// Whether the licence sets no device limit at all. READ THIS, NOT THE
    /// NUMBER: when it is true DeviceRights carries int.MaxValue, which renders
    /// as "2147483647" and reads as a bug.
    ///
    /// A server that predates the flag sends nothing, so this is false and the
    /// number is shown as it arrived. That is the honest answer - the server did
    /// not say "unlimited" - and it is deliberately not patched up by guessing
    /// at the sentinel again: the platform already carries three conventions for
    /// this same idea, and each client inferring its own is how that happened.
    /// </summary>
    public bool DeviceRightsUnlimited { get; set; }
    public bool LicenceActive { get; set; }
    public DateTimeOffset ServerTimeUtc { get; set; }
}

internal sealed class StudioCloudBotSeatCreateRequest
{
    public string DisplayName { get; set; } = "";
    public string InternalEmail { get; set; } = "";
}

internal sealed class StudioCloudBotPinSetRequest
{
    public string Pin { get; set; } = "";
}

internal sealed class StudioCloudBotPinSetResponse
{
    public string BotId { get; set; } = "";
    public DateTimeOffset SetAtUtc { get; set; }

    /// <summary>
    /// The seated device must register again before the new PIN works. Carried
    /// with the change, in one value, so the message can say both at once -
    /// otherwise the employee types the new PIN, nothing happens, and the
    /// reason appears nowhere.
    /// </summary>
    public bool DeviceMustReRegister { get; set; }

    public DateTimeOffset ServerTimeUtc { get; set; }
}

internal sealed class StudioCloudBotPinReveal
{
    public string BotId { get; set; } = "";
    public string Pin { get; set; } = "";
    public bool Locked { get; set; }
    public DateTimeOffset SetAtUtc { get; set; }
    public DateTimeOffset ServerTimeUtc { get; set; }
}

internal sealed class StudioCloudBotStateEnterRequest
{
    public string DeviceFingerprint { get; set; } = "";
    public string LegacyDeviceFingerprint { get; set; } = "";
    public string DeviceName { get; set; } = "";
}

internal sealed class StudioCloudOwnerCredentialRevocation
{
    public bool Revoked { get; set; }
    public DateTimeOffset RevokedAtUtc { get; set; }
}

internal sealed class StudioCloudBotStateEnterResponse
{
    public string BotId { get; set; } = "";
    public string OrganizationId { get; set; } = "";
    public string TokenId { get; set; } = "";
    public DateTimeOffset EnteredAtUtc { get; set; }

    /// <summary>
    /// What the server did to the owner's session. Its half of the invariant;
    /// the other half - erasing the credential on this machine - only Studio
    /// can do, and a failure there must abort the transition.
    /// </summary>
    public StudioCloudOwnerCredentialRevocation? OwnerCredentialsRevoked { get; set; }

    public string TokenType { get; set; } = "Bearer";

    /// <summary>The seat's own credential, issued as the device is seated.</summary>
    public string AccessToken { get; set; } = "";

    public DateTimeOffset AccessTokenExpiresAtUtc { get; set; }

    public DateTimeOffset ServerTimeUtc { get; set; }
}

internal sealed class StudioCloudBotStateResumeRequest
{
    public string DeviceFingerprint { get; set; } = "";
    public string LegacyDeviceFingerprint { get; set; } = "";
}

internal sealed class StudioCloudBotStateResume
{
    public string BotId { get; set; } = "";
    public string OrganizationId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string SeatStatus { get; set; } = "";
    public bool PinLocked { get; set; }

    /// <summary>
    /// Who is appointed to this seat, or null when nobody is yet. Null is a
    /// NORMAL state - a seat created, a machine seated, no invitation accepted
    /// yet - and is not an error.
    /// </summary>
    public StudioCloudBotSeatMember? Member { get; set; }

    /// <summary>What this seat may open. The whole of it - a project absent here is refused.</summary>
    public List<StudioCloudBotAssignedProject> AssignedProjects { get; set; } = [];

    public DateTimeOffset ServerTimeUtc { get; set; }
}

internal sealed class StudioCloudBotPinLockoutRequest
{
    public string DeviceFingerprint { get; set; } = "";
    public string LegacyDeviceFingerprint { get; set; } = "";
}

internal sealed class StudioCloudBotInvitation
{
    public string InvitationId { get; set; } = "";
    public string BotId { get; set; } = "";
    public string BotDisplayName { get; set; } = "";
    public string OrganizationId { get; set; } = "";
    public string ProjectId { get; set; } = "";
    public List<string> Roles { get; set; } = [];
    public string TargetEmail { get; set; } = "";
    public string InvitedByEmail { get; set; } = "";
    public DateTimeOffset InvitedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }

    /// <summary>Sent / Accepted / Declined / Cancelled. Four, not five: expiry is read from ExpiresAtUtc against the server's clock, never stored.</summary>
    public string State { get; set; } = "";
}

internal sealed class StudioCloudBotInvitationListResponse
{
    public List<StudioCloudBotInvitation> Items { get; set; } = [];
    public DateTimeOffset ServerTimeUtc { get; set; }
}

internal sealed class StudioCloudBotInvitationCreateRequest
{
    /// <summary>
    /// The invitation says WHO fills the seat, not what the seat works on.
    /// Project and roles were removed on 2026-09-05: the server refuses an
    /// invitation that still carries them, because carrying them is what made
    /// assigning an empty seat impossible.
    /// </summary>
    public string TargetEmail { get; set; } = "";
}

internal sealed class StudioCloudBotInvitationAccepted
{
    public string BotId { get; set; } = "";
    public string ProjectId { get; set; } = "";
    public List<string> Roles { get; set; } = [];
    public DateTimeOffset LinkedAtUtc { get; set; }

    /// <summary>
    /// True when this acceptance opened a new career interval - the person
    /// joined. False when they were already linked and only gained a project.
    /// Two different sentences to the owner; one message for both would have
    /// them counting a working day twice.
    /// </summary>
    public bool OpenedNewInterval { get; set; }

    public DateTimeOffset ServerTimeUtc { get; set; }
}

internal sealed class StudioCloudBotAssignedProject
{
    public string ProjectId { get; set; } = "";

    /// <summary>
    /// For showing, never for deciding. What the seat MAY do is Scopes below -
    /// resolved by the server. Reading a role list and concluding "an admin may
    /// invite" is the same defect as a client-side HasFeature, which is how
    /// PFR's auto-dimension drifted.
    /// </summary>
    public List<string> Roles { get; set; } = [];

    /// <summary>
    /// What this seat may do in this project, in the server's own scope words -
    /// the same vocabulary a signed-in person's currentUserScopes uses, so no
    /// translation table exists on either side.
    ///
    /// project.delete and project.leave never appear here: a machine does not
    /// leave a project on somebody's behalf, and deleting one is the owner's.
    /// </summary>
    public List<string> Scopes { get; set; } = [];

    public DateTimeOffset AssignedAtUtc { get; set; }
}

/// <summary>
/// The person appointed to a seat - NOT whoever put the machine into bot state.
/// Usually the same, but they part company the first time an owner seats a
/// machine for somebody else, and only one of them is a relationship.
/// </summary>
internal sealed class StudioCloudBotSeatMember
{
    public string AccountEmail { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public DateTimeOffset MemberSinceUtc { get; set; }
}

/// <summary>
/// The bot-scoped, device-bound credential a seated machine works with. It is
/// never the owner's token and cannot become one: returning to owner state is
/// a fresh owner sign-in and nothing else.
/// </summary>
internal sealed class StudioCloudBotStateToken
{
    public string TokenType { get; set; } = "Bearer";
    public string AccessToken { get; set; } = "";
    public DateTimeOffset AccessTokenExpiresAtUtc { get; set; }
    public string BotId { get; set; } = "";
    public string OrganizationId { get; set; } = "";
    public DateTimeOffset ServerTimeUtc { get; set; }
}

internal sealed class StudioCloudBotSeatDeleted
{
    public string BotId { get; set; } = "";
    public string Status { get; set; } = "";

    /// <summary>
    /// Whether a device was sitting in the seat and has been released by the
    /// deletion. The server does not hesitate; the person should still be told
    /// what just happened to a machine somebody may be working at.
    /// </summary>
    public bool DeviceReleased { get; set; }

    /// <summary>
    /// Recounted by the server rather than decremented here: a client that
    /// subtracts one from 7/10 disagrees with the server the day the counting
    /// rule changes.
    /// </summary>
    public int OccupiedSeats { get; set; }

    public int DeviceRights { get; set; }

    /// <summary>
    /// Whether the licence sets no device limit at all. READ THIS, NOT THE
    /// NUMBER: when it is true DeviceRights carries int.MaxValue, which renders
    /// as "2147483647" and reads as a bug.
    ///
    /// A server that predates the flag sends nothing, so this is false and the
    /// number is shown as it arrived. That is the honest answer - the server did
    /// not say "unlimited" - and it is deliberately not patched up by guessing
    /// at the sentinel again: the platform already carries three conventions for
    /// this same idea, and each client inferring its own is how that happened.
    /// </summary>
    public bool DeviceRightsUnlimited { get; set; }
    public DateTimeOffset ServerTimeUtc { get; set; }
}

// ---------------------------------------------------------------------------
// Device key registration. The device fingerprint stops being a claim the
// client makes about itself and becomes the hash of a key only that machine
// can sign with. Shape on the wire is unchanged: 64 uppercase hex.
// ---------------------------------------------------------------------------

internal sealed class StudioCloudDeviceKeyChallenge
{
    /// <summary>Base64url, single use, short lived. Bound to the account in the token, not to any fingerprint.</summary>
    public string Nonce { get; set; } = "";
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset ServerTimeUtc { get; set; }
}

internal sealed class StudioCloudDeviceKeyRegisterRequest
{
    /// <summary>SubjectPublicKeyInfo DER, base64. P-256 only.</summary>
    public string PublicKey { get; set; } = "";

    public string Nonce { get; set; } = "";

    /// <summary>IEEE P1363 (r||s, 64 bytes), base64 - not DER.</summary>
    public string Signature { get; set; } = "";

    /// <summary>Optional. Sent anyway so a client that computes it wrongly is refused rather than quietly corrected.</summary>
    public string DeviceFingerprint { get; set; } = "";
}

internal sealed class StudioCloudDeviceKeyRegistration
{
    public string DeviceFingerprint { get; set; } = "";
    public DateTimeOffset RegisteredAtUtc { get; set; }
    public DateTimeOffset ServerTimeUtc { get; set; }
}
