namespace ErkS.Platform.Core;

/// <summary>
/// Root document of Erk-S Platform: one album project bundles sheets coming
/// from many design files (AutoCAD, Revit, ...) into a single publishable
/// PDF album. Saved as a JSON ".erksalbum" file.
/// </summary>
public sealed class AlbumProject
{
    public const int CurrentFormatVersion = 2;
    public const string FileExtension = ".erksalbum";

    public int FormatVersion { get; set; } = CurrentFormatVersion;

    /// <summary>
    /// Local workspace identity used to keep project-owned sources isolated.
    /// Empty is retained only for legacy standalone .erksalbum documents.
    /// </summary>
    public string ProjectId { get; set; } = "";

    public string Name { get; set; } = "New project";

    /// <summary>Project code stamped on covers and used for publishing, e.g. "ERKS-2026-014".</summary>
    public string Code { get; set; } = "";

    public string Description { get; set; } = "";

    /// <summary>
    /// Optional Cloud ERA link fields. They are nullable/empty-safe so old
    /// ".erksalbum" files remain valid and keep loading as local-only projects.
    /// </summary>
    public string ServerProjectId { get; set; } = "";

    public string ServerUrl { get; set; } = "";

    public string CloudProjectCode { get; set; } = "";

    public string ClientName { get; set; } = "";

    public string PlanningAuthorityName { get; set; } = "";

    public string DesignOrganizationName { get; set; } = "";

    public string CloudStatus { get; set; } = "";

    public ProjectInitiationBasis InitiationBasis { get; set; } = new();

    public PlanningTaskInformation PlanningTask { get; set; } = new();

    public ProjectApprovalWorkflow ApprovalWorkflow { get; set; } = new();

    /// <summary>
    /// Company information used for covers and (later) corner tables. This is
    /// the single source of truth - the AutoCAD/Revit plugins will stop
    /// drawing their own corner tables.
    /// </summary>
    public CompanyProfile Company { get; set; } = new();

    public List<ProjectParticipant> Participants { get; set; } = [];

    public List<ProjectStage> Stages { get; set; } = [];

    public List<ProjectWorkPackage> WorkPackages { get; set; } = [];

    public List<ProjectDocumentRecord> Documents { get; set; } = [];

    /// <summary>Folders watched for incoming sheet packages.</summary>
    public List<string> SourceFolders { get; set; } = [];

    /// <summary>
    /// Structured design sources. SourceFolders is retained as the version 1
    /// compatibility surface; new projects should use this registry.
    /// </summary>
    public List<ProjectDesignSource> DesignSources { get; set; } = [];

    /// <summary>Project-owned images composed into the concept album's visualization pages.</summary>
    public ProjectVisualizationSource Visualizations { get; set; } = new();

    /// <summary>Studio-owned map extents and compact snapshots for the site context page.</summary>
    public ProjectSiteContextMap SiteContext { get; set; } = new();

    public AlbumDefinition Album { get; set; } = new();

    /// <summary>Where composed album PDFs are written.</summary>
    public string OutputFolder { get; set; } = "";

    /// <summary>
    /// Runtime-only root used to resolve project-relative foundation assets.
    /// It is deliberately excluded from .erksalbum serialization.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string ProjectFolder { get; set; } = "";
}

public sealed class CompanyProfile
{
    /// <summary>Cloud ERA organization identity. Empty for legacy/local profiles.</summary>
    public string OrganizationId { get; set; } = "";
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string ShortName { get; set; } = "";
    public string RegistrationNumber { get; set; } = "";
    public string LegalEntityType { get; set; } = "";
    public string LegalForm { get; set; } = "";
    public List<string> ActivityDirections { get; set; } = [];
    public DateTimeOffset? RegisteredAtUtc { get; set; }
    public string OfficialRepresentativeName { get; set; } = "";
    public string RegistrySource { get; set; } = "SelfDeclared";
    public string RegistrySourceUrl { get; set; } = "https://opendata.burtgel.gov.mn/les";
    public DateTimeOffset? RegistryCheckedAtUtc { get; set; }
    public string OrganizationType { get; set; } = "DesignCompany";
    public string Status { get; set; } = "";
    public string VerificationStatus { get; set; } = "";
    public string RegisteredCity { get; set; } = "";
    public string Address { get; set; } = "";
    public string Phone { get; set; } = "";
    public List<string> PhoneNumbers { get; set; } = [];
    public string Email { get; set; } = "";
    public string WebSite { get; set; } = "";
    public string LicenseScope { get; set; } = "";
    public string LicenseNumber { get; set; } = "";
    public string DirectorTitle { get; set; } = "";
    public string DirectorName { get; set; } = "";
    public string DesignRepresentativeTitle { get; set; } = "";
    public string DesignRepresentativeName { get; set; } = "";
    public string LogoPath { get; set; } = "";
    /// <summary>User-facing source name; LogoPath may be content-addressed.</summary>
    public string LogoOriginalFileName { get; set; } = "";

    /// <summary>Logo zoom relative to a centered contain fit.</summary>
    public double LogoScale { get; set; } = 1d;

    /// <summary>Horizontal logo offset normalized to the target frame (-1..1).</summary>
    public double LogoOffsetX { get; set; }

    /// <summary>Vertical logo offset normalized to the target frame (-1..1).</summary>
    public double LogoOffsetY { get; set; }

    /// <summary>Organization registration certificate scans/PDFs.</summary>
    public List<ProjectFileReference> RegistrationCertificateDocuments { get; set; } = [];

    /// <summary>Design-service license scans/PDFs.</summary>
    public List<ProjectFileReference> DesignLicenseDocuments { get; set; } = [];

    public DateTimeOffset? UpdatedAtUtc { get; set; }
    public List<CompanySigner> Signers { get; set; } = [];

    public void Normalize()
    {
        PhoneNumbers ??= [];
        ActivityDirections ??= [];
        Signers ??= [];
        RegistrationCertificateDocuments ??= [];
        DesignLicenseDocuments ??= [];
        PhoneNumbers = PhoneNumbers
            .Select(value => (value ?? "").Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (PhoneNumbers.Count == 0 && !string.IsNullOrWhiteSpace(Phone))
        {
            PhoneNumbers.Add(Phone.Trim());
        }
        Phone = PhoneNumbers.FirstOrDefault() ?? Phone.Trim();
        ActivityDirections = ActivityDirections
            .Select(value => (value ?? "").Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (string.IsNullOrWhiteSpace(DesignRepresentativeTitle))
            DesignRepresentativeTitle = DirectorTitle;
        if (string.IsNullOrWhiteSpace(DesignRepresentativeName))
            DesignRepresentativeName = DirectorName;
        DirectorTitle = DesignRepresentativeTitle;
        DirectorName = DesignRepresentativeName;
        RegistrySource = string.IsNullOrWhiteSpace(RegistrySource) ? "SelfDeclared" : RegistrySource.Trim();
        RegistrySourceUrl = string.IsNullOrWhiteSpace(RegistrySourceUrl)
            ? "https://opendata.burtgel.gov.mn/les"
            : RegistrySourceUrl.Trim();
        LogoScale = double.IsFinite(LogoScale) ? Math.Clamp(LogoScale, 0.25d, 4d) : 1d;
        LogoOffsetX = double.IsFinite(LogoOffsetX) ? Math.Clamp(LogoOffsetX, -1d, 1d) : 0d;
        LogoOffsetY = double.IsFinite(LogoOffsetY) ? Math.Clamp(LogoOffsetY, -1d, 1d) : 0d;
        LogoOriginalFileName = LogoOriginalFileName?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(DisplayName))
        {
            DisplayName = string.IsNullOrWhiteSpace(ShortName) ? Name : ShortName;
        }
    }

    public CompanyProfile Clone()
    {
        Normalize();
        return new CompanyProfile
        {
            OrganizationId = OrganizationId,
            Name = Name,
            DisplayName = DisplayName,
            ShortName = ShortName,
            RegistrationNumber = RegistrationNumber,
            LegalEntityType = LegalEntityType,
            LegalForm = LegalForm,
            ActivityDirections = [.. (ActivityDirections ?? [])],
            RegisteredAtUtc = RegisteredAtUtc,
            OfficialRepresentativeName = OfficialRepresentativeName,
            RegistrySource = RegistrySource,
            RegistrySourceUrl = RegistrySourceUrl,
            RegistryCheckedAtUtc = RegistryCheckedAtUtc,
            OrganizationType = OrganizationType,
            Status = Status,
            VerificationStatus = VerificationStatus,
            RegisteredCity = RegisteredCity,
            Address = Address,
            Phone = Phone,
            PhoneNumbers = [.. PhoneNumbers],
            Email = Email,
            WebSite = WebSite,
            LicenseScope = LicenseScope,
            LicenseNumber = LicenseNumber,
            DirectorTitle = DirectorTitle,
            DirectorName = DirectorName,
            DesignRepresentativeTitle = DesignRepresentativeTitle,
            DesignRepresentativeName = DesignRepresentativeName,
            LogoPath = LogoPath,
            LogoOriginalFileName = LogoOriginalFileName,
            LogoScale = LogoScale,
            LogoOffsetX = LogoOffsetX,
            LogoOffsetY = LogoOffsetY,
            RegistrationCertificateDocuments = RegistrationCertificateDocuments
                .Select(document => document.Clone())
                .ToList(),
            DesignLicenseDocuments = DesignLicenseDocuments
                .Select(document => document.Clone())
                .ToList(),
            UpdatedAtUtc = UpdatedAtUtc,
            Signers = Signers.Select(item => new CompanySigner { Role = item.Role, FullName = item.FullName }).ToList(),
        };
    }
}

public sealed class CompanySigner
{
    /// <summary>Role label, e.g. "Захирал", "ТЗ", "Гүйцэтгэсэн".</summary>
    public string Role { get; set; } = "";
    public string FullName { get; set; } = "";
}

public sealed class ProjectParticipant
{
    /// <summary>Registered profile surname/ovog. Optional for legacy album files.</summary>
    public string FamilyName { get; set; } = "";

    /// <summary>Registered profile given name/ner. Optional for legacy album files.</summary>
    public string GivenName { get; set; } = "";

    public string FullName { get; set; } = "";
    public string Role { get; set; } = "";
    public string Email { get; set; } = "";
}

public sealed class ProjectStage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Status { get; set; } = "";
    public string AssignedOrganizationName { get; set; } = "";
}

public sealed class ProjectWorkPackage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? StageId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Status { get; set; } = "";
    public string AssignedOrganizationName { get; set; } = "";
}

public sealed class ProjectDocumentRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? StageId { get; set; }
    public Guid? WorkPackageId { get; set; }
    public string Type { get; set; } = "";
    public string Title { get; set; } = "";
    public string Status { get; set; } = "";
    public string OwnerOrganizationName { get; set; } = "";
    public string LocalPath { get; set; } = "";
    public string ServerDocumentId { get; set; } = "";
    public int Version { get; set; } = 1;
}

public static class ProjectDocumentCategories
{
    public const string CompanyRegistrationCertificate = "CompanyRegistrationCertificate";
    public const string CompanyDesignLicense = "CompanyDesignLicense";
    public const string ApprovedPlanningTask = "ApprovedPlanningTask";
}

public sealed class AlbumDefinition
{
    public string Title { get; set; } = "Project album";

    /// <summary>
    /// Optional project-owned composition template. Empty keeps the legacy
    /// cover/contents/source-pages behavior unchanged.
    /// </summary>
    public string TemplateId { get; set; } = "";

    public bool IncludeCover { get; set; } = true;

    public bool IncludeTableOfContents { get; set; } = true;

    public List<AlbumCompositionItem> Composition { get; set; } = [];

    public List<AlbumSection> Sections { get; set; } = [];

    /// <summary>
    /// Ordered page instances. When empty, version 1 behavior is preserved and
    /// every received sheet is merged as-is.
    /// </summary>
    public List<AlbumPageDefinition> Pages { get; set; } = [];
}

public sealed class AlbumCompositionItem
{
    public string Id { get; set; } = "";
    public int Order { get; set; }
    public string Number { get; set; } = "";
    public string Title { get; set; } = "";
    public string SectionTitle { get; set; } = "";
    public AlbumCompositionKind Kind { get; set; }
    public AlbumGeneratedPageKind GeneratedPageKind { get; set; }
    public bool Required { get; set; } = true;
    public bool AllowMultiple { get; set; }
    public List<string> MatchContentKinds { get; set; } = [];
    public List<string> MatchNameTerms { get; set; } = [];
}

public enum AlbumCompositionKind
{
    Generated,
    SourceSlot,
}

public enum AlbumGeneratedPageKind
{
    None,
    Cover,
    DesignOrganization,
    PlanningTask,
    SiteContext,
}

public sealed class AlbumSection
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Section title, e.g. "Архитектур", "Ерөнхий төлөвлөгөө".</summary>
    public string Title { get; set; } = "";

    /// <summary>
    /// Ordered sheet keys (<see cref="SheetRecord.Key"/>). The album is built
    /// section by section in this order.
    /// </summary>
    public List<string> SheetKeys { get; set; } = [];
}

public sealed class AlbumPageDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string SheetKey { get; set; } = "";

    /// <summary>Project-template slot filled by this source sheet.</summary>
    public string TemplateSlotId { get; set; } = "";

    public Guid? SectionId { get; set; }

    public string PageFormatId { get; set; } = PageFormatCatalog.SourceAsIsId;

    /// <summary>
    /// Immutable inline format geometry captured from the source package.
    /// Optional so every existing album document remains readable.
    /// </summary>
    public PageFormatDefinition? PageFormatSnapshot { get; set; }

    /// <summary>
    /// Null means a legacy page that may adopt its first source format; true
    /// follows later source revisions; false preserves a user's manual choice.
    /// </summary>
    public bool? FollowSourceFormat { get; set; }

    public PagePlacementMode PlacementMode { get; set; } = PagePlacementMode.FitDrawingArea;

    public string NumberOverride { get; set; } = "";

    public string TitleOverride { get; set; } = "";

    /// <summary>
    /// Optional Studio-owned facade narrative. Null inherits the source
    /// sheet's description; an explicit value remains project-local.
    /// </summary>
    public string? ElevationDescriptionOverride { get; set; }
}

public enum PagePlacementMode
{
    FitDrawingArea,
    FullPage,
    FillCrop,
    PreserveDrawingSpace,
}
