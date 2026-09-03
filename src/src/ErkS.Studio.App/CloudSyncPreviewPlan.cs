using ErkS.Platform.Core;

namespace ErkS.Studio;

internal enum CloudSyncChangeDirection
{
    Upload,
    Download,
    Blocked,
}

internal sealed record CloudSyncChangeItem(
    CloudSyncChangeDirection Direction,
    string Code,
    string Title,
    string Detail);

internal sealed class CloudSyncPreviewPlan
{
    private readonly HashSet<string> authorizedSourceIdentities;
    private readonly HashSet<string> authorizedComponentCodes;
    private readonly Dictionary<string, ProjectAlbumComponentClaimAcknowledgement>
        authorizedComponentClaims;

    public CloudSyncPreviewPlan(
        string projectCode,
        string deviceLabel,
        IEnumerable<CloudSyncChangeItem> uploads,
        IEnumerable<CloudSyncChangeItem> downloads,
        IEnumerable<CloudSyncChangeItem> blocked,
        bool authorizeProjectInformation,
        bool authorizeCompanyAssignment,
        bool authorizeBuildingComposition,
        bool authorizeCanonicalTitleBlock,
        IEnumerable<string> authorizedSourceIdentities,
        IEnumerable<string> authorizedComponentCodes,
        IEnumerable<ProjectAlbumComponentClaimAcknowledgement> authorizedComponentClaims)
    {
        ProjectCode = projectCode;
        DeviceLabel = deviceLabel;
        Uploads = uploads.ToList();
        Downloads = downloads.ToList();
        Blocked = blocked.ToList();
        AuthorizeProjectInformation = authorizeProjectInformation;
        AuthorizeCompanyAssignment = authorizeCompanyAssignment;
        AuthorizeBuildingComposition = authorizeBuildingComposition;
        AuthorizeCanonicalTitleBlock = authorizeCanonicalTitleBlock;
        this.authorizedSourceIdentities = authorizedSourceIdentities
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        this.authorizedComponentCodes = authorizedComponentCodes
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        this.authorizedComponentClaims = authorizedComponentClaims
            .GroupBy(claim => claim.ComponentCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Last(),
                StringComparer.OrdinalIgnoreCase);
    }

    public string ProjectCode { get; }

    public string DeviceLabel { get; }

    public IReadOnlyList<CloudSyncChangeItem> Uploads { get; }

    public IReadOnlyList<CloudSyncChangeItem> Downloads { get; }

    public IReadOnlyList<CloudSyncChangeItem> Blocked { get; }

    public bool AuthorizeProjectInformation { get; }

    public bool AuthorizeCompanyAssignment { get; }

    public bool AuthorizeBuildingComposition { get; }

    public bool AuthorizeCanonicalTitleBlock { get; }

    public bool HasUploads => Uploads.Count > 0;

    public bool HasDownloads => Downloads.Count > 0;

    public bool HasBlockedPendingChanges => Blocked.Count > 0;

    public bool AllPendingAuthorized => !HasBlockedPendingChanges;

    public bool IsSourceAuthorized(ProjectSourceSyncCandidate candidate) =>
        authorizedSourceIdentities.Contains(SourceIdentity(candidate));

    public bool IsComponentAuthorized(string code) =>
        authorizedComponentCodes.Contains((code ?? "").Trim());

    public bool HasCompatibleComponentClaim(
        string code,
        CloudSyncPreviewPlan current)
    {
        ArgumentNullException.ThrowIfNull(current);
        string normalized = (code ?? "").Trim();
        bool acceptedHasClaim =
            authorizedComponentClaims.TryGetValue(
                normalized,
                out ProjectAlbumComponentClaimAcknowledgement? accepted);
        bool currentHasClaim =
            current.authorizedComponentClaims.TryGetValue(
                normalized,
                out ProjectAlbumComponentClaimAcknowledgement? latest);
        return acceptedHasClaim == currentHasClaim &&
            (!acceptedHasClaim ||
             accepted!.OwnerEmail.Equals(
                 latest!.OwnerEmail,
                 StringComparison.OrdinalIgnoreCase) &&
             accepted.DeviceFingerprint.Equals(
                 latest.DeviceFingerprint,
                 StringComparison.OrdinalIgnoreCase) &&
             accepted.ClaimToken.Equals(
                 latest.ClaimToken,
                 StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<ProjectAlbumComponentClaimAcknowledgement>
        ComponentClaimAcknowledgements(IEnumerable<string> componentCodes)
    {
        HashSet<string> requested = (componentCodes ?? [])
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return authorizedComponentClaims
            .Where(pair => requested.Contains(pair.Key))
            .Select(pair => pair.Value)
            .ToList();
    }

    public static string SourceIdentity(ProjectSourceSyncCandidate candidate) =>
        string.Join(
            "|",
            candidate.Source.Id.Trim(),
            candidate.SourceKey.Trim(),
            candidate.ManifestId.Trim(),
            candidate.ContentHash.Trim());
}

internal static class CloudSyncPreviewPlanner
{
    public static CloudSyncPreviewPlan Build(
        ProjectWorkspace project,
        string? currentUserEmail,
        string deviceLabel,
        StudioCloudProjectRefreshResult remote,
        string? currentDeviceFingerprint = null,
        Func<ProjectDesignSource, bool>? hasVerifiedPayload = null,
        Func<ProjectFileReference, bool>? hasVerifiedDocumentPayload = null,
        Func<ProjectVisualizationImage, bool>? hasVerifiedVisualizationPayload = null)
    {
        ArgumentNullException.ThrowIfNull(project);

        string currentEmail = NormalizeEmail(currentUserEmail);
        hasVerifiedPayload ??=
            StudioLocalSourceBindingPolicy.HasVerifiedPayload;
        hasVerifiedDocumentPayload ??= static _ => false;
        hasVerifiedVisualizationPayload ??= static _ => false;
        bool canManageCanonical =
            ProjectCloudSyncAuthority.CanManageCanonicalMetadata(
                project.Cloud,
                currentEmail);
        bool canEditBuildingComposition =
            ProjectCloudSyncAuthority.CanEditBuildingComposition(
                project.Cloud,
                currentEmail);
        bool canUploadProjectInformation =
            canManageCanonical &&
            StudioRefreshSyncOperationPolicy.CanUploadPersistedPayload(
                StudioCloudSyncPayload.ProjectInformation);
        bool canUploadCompanyAssignment =
            canManageCanonical &&
            StudioRefreshSyncOperationPolicy.CanUploadPersistedPayload(
                StudioCloudSyncPayload.OrganizationAssignment);
        bool pendingProjectInformation =
            project.Cloud.PendingProjectInformation is not null;
        bool canPublishCanonicalTitleBlock =
            canManageCanonical && !pendingProjectInformation;
        StudioCanonicalAlbumRebuildResolution remoteAlbumRebuild =
            StudioCanonicalAlbumRebuildPolicy.Resolve(project, remote.Project);
        var uploads = new List<CloudSyncChangeItem>();
        var downloads = new List<CloudSyncChangeItem>();
        var blocked = new List<CloudSyncChangeItem>();
        var authorizedSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var authorizedComponents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var authorizedComponentClaims =
            new List<ProjectAlbumComponentClaimAcknowledgement>();

        AddCanonicalChange(
            pendingProjectInformation,
            canUploadProjectInformation,
            "project-information",
            "Төслийн мэдээлэл",
            "Төслийн нэр, хаяг, захиалагч болон суурь мэдээлэл",
            uploads,
            blocked);

        bool pendingCompany = project.Foundation.DesignCompany.AssignmentSource.Equals(
            "StudioCloudPending",
            StringComparison.OrdinalIgnoreCase);
        AddCanonicalChange(
            pendingCompany,
            canUploadCompanyAssignment,
            "company-assignment",
            "Зураг төслийн байгууллага",
            "Сонгосон байгууллага болон төслийн company snapshot",
            uploads,
            blocked);

        AddCanonicalChange(
            project.Cloud.BuildingCompositionPending,
            canEditBuildingComposition,
            "building-composition",
            "Барилгын бүлэг ба дараалал",
            "Барилгын бүлэг, хуудасны харьяалал болон дэд нүүр",
            uploads,
            blocked);

        AddCanonicalChange(
            project.Cloud.CanonicalTitleBlockPending,
            canPublishCanonicalTitleBlock,
            "canonical-title-block",
            "Булангийн хүснэгтийн мэдээлэл",
            "Төсөл, байгууллага, оролцогчдын каноник мэдээллийг бүх хуудсанд шинэчилнэ",
            uploads,
            blocked);

        IReadOnlyList<ProjectSourceSyncCandidate> pendingSources =
            ProjectCloudSyncMetadata.PendingSourcePackages(project);
        var sourceAuthorities = new Dictionary<string, ProjectSourceEditAuthority>(
            StringComparer.OrdinalIgnoreCase);
        foreach (ProjectSourceSyncCandidate source in pendingSources)
        {
            // A durable retirement request supersedes ordinary package upload.
            // The sync runner must retire the exact registry row and remove the
            // local mirror before the album tombstone is merged.
            if (StudioSourceRemovalOutbox.IsSourceStaged(
                    project,
                    source.Source,
                    currentEmail,
                    currentDeviceFingerprint))
            {
                continue;
            }

            ProjectSourceEditAuthority authority =
                ProjectCloudSyncAuthority.ResolveSource(project, source.Source, currentEmail);
            bool hasLocalPayload =
                StudioRuntimeSourceScope.IsAuthorizedLocal(
                    project,
                    source.Source,
                    currentEmail,
                    currentDeviceFingerprint,
                    hasVerifiedPayload);
            ProjectSourceEditAuthority effectiveAuthority =
                authority.CanEdit && !hasLocalPayload
                    ? new ProjectSourceEditAuthority(
                        false,
                        authority.SourceKey,
                        authority.OwnerEmail,
                        "Энэ source Cloud төлөвтэй: баталгаатай payload нь одоогийн бүртгэл/төхөөрөмжтэй холбогдоогүй. Эх файлыг зориуд дахин холбоно уу.")
                    : authority;
            sourceAuthorities[SourceComponentIdentity(source.Source)] =
                effectiveAuthority;
            var item = new CloudSyncChangeItem(
                effectiveAuthority.CanEdit
                    ? CloudSyncChangeDirection.Upload
                    : CloudSyncChangeDirection.Blocked,
                "source:" + source.SourceKey,
                $"Эх үүсвэр: {SourceLabel(source)}",
                effectiveAuthority.CanEdit
                    ? $"{OwnerLabel(effectiveAuthority.OwnerEmail)} · {source.SheetCount} хуудас · " +
                      $"{source.SourceApplication} · SourceKey {source.SourceKey}; native файл илгээхгүй"
                    : effectiveAuthority.Message);
            if (effectiveAuthority.CanEdit)
            {
                authorizedSources.Add(CloudSyncPreviewPlan.SourceIdentity(source));
                uploads.Add(item);
            }
            else
            {
                blocked.Add(item);
            }
        }

        IEnumerable<string> pendingComponentCodes =
            ProjectCloudSyncMetadata.PendingAlbumComponents(project)
                .Concat(remoteAlbumRebuild.PendingComponentCodes)
                .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (string componentCode in pendingComponentCodes)
        {
            ComponentAuthority authority = ResolveComponentAuthority(
                project,
                componentCode,
                currentEmail,
                canManageCanonical,
                sourceAuthorities,
                currentDeviceFingerprint,
                hasVerifiedPayload,
                hasVerifiedDocumentPayload,
                hasVerifiedVisualizationPayload);
            var item = new CloudSyncChangeItem(
                authority.CanEdit ? CloudSyncChangeDirection.Upload : CloudSyncChangeDirection.Blocked,
                componentCode,
                authority.Title,
                authority.Message);
            if (authority.CanEdit)
            {
                string code = componentCode.Trim();
                authorizedComponents.Add(code);
                ProjectLocalAlbumComponentClaim? claim =
                    ProjectCloudSyncMetadata.PendingAlbumComponentClaim(
                        project,
                        code,
                        currentEmail,
                        currentDeviceFingerprint);
                if (claim is not null &&
                    !string.IsNullOrWhiteSpace(claim.ClaimToken))
                {
                    authorizedComponentClaims.Add(
                        new ProjectAlbumComponentClaimAcknowledgement(
                            code,
                            claim.OwnerEmail,
                            claim.DeviceFingerprint,
                            claim.ClaimToken));
                }
                uploads.Add(item);
            }
            else
            {
                blocked.Add(item);
            }
        }

        if (remote.IsModified)
        {
            downloads.Add(new CloudSyncChangeItem(
                CloudSyncChangeDirection.Download,
                "remote-project",
                "Cloud ERA төслийн өөрчлөлт",
                "Төслийн мэдээлэл, байгууллагын snapshot болон багийн эрхийг шинэчилнэ"));

            StudioCloudProjectDetail? detail = remote.Project;
            StudioCloudAlbumRevision? revision = detail?.Albums
                .SelectMany(album => album.Revisions.Where(revision =>
                    revision.RevisionId.Equals(
                        album.CurrentRevisionId,
                        StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(item => item.RevisionNumber)
                .FirstOrDefault();
            if (revision is not null &&
                !revision.RevisionId.Equals(
                    project.Cloud.LastReceivedAlbumRevisionId,
                    StringComparison.OrdinalIgnoreCase))
            {
                downloads.Add(new CloudSyncChangeItem(
                    CloudSyncChangeDirection.Download,
                    "remote-album:" + revision.RevisionId,
                    $"Cloud album R{revision.RevisionNumber}",
                    $"{revision.PageCount} хуудас · зөвхөн өөрчлөгдсөн current PDF cache татагдана"));
            }

            if (detail is not null)
            {
                AddRemoteSourceChanges(project, detail, downloads);
                if (remoteAlbumRebuild.IsPending)
                {
                    downloads.Add(new CloudSyncChangeItem(
                        CloudSyncChangeDirection.Download,
                        "remote-album-rebuild-pending",
                        "Canonical album rebuild pending",
                        StudioCanonicalAlbumRebuildPolicy.Describe(
                            remoteAlbumRebuild)));
                }
            }
        }

        return new CloudSyncPreviewPlan(
            project.Identity.Code,
            deviceLabel,
            uploads,
            downloads,
            blocked,
            canUploadProjectInformation,
            canUploadCompanyAssignment,
            canEditBuildingComposition,
            canPublishCanonicalTitleBlock,
            authorizedSources,
            authorizedComponents,
            authorizedComponentClaims);
    }

    private static void AddCanonicalChange(
        bool pending,
        bool canManage,
        string code,
        string title,
        string detail,
        ICollection<CloudSyncChangeItem> uploads,
        ICollection<CloudSyncChangeItem> blocked)
    {
        if (!pending)
            return;

        var item = new CloudSyncChangeItem(
            canManage ? CloudSyncChangeDirection.Upload : CloudSyncChangeDirection.Blocked,
            code,
            title,
            canManage
                ? detail
                : "ProjectAdmin эсвэл DesignCompanyAdmin эрх шаардлагатай. Локал өөрчлөлт pending хэвээр үлдэнэ.");
        if (canManage)
            uploads.Add(item);
        else
            blocked.Add(item);
    }

    private static ComponentAuthority ResolveComponentAuthority(
        ProjectWorkspace project,
        string componentCode,
        string currentEmail,
        bool canManageCanonical,
        IReadOnlyDictionary<string, ProjectSourceEditAuthority> sourceAuthorities,
        string? currentDeviceFingerprint,
        Func<ProjectDesignSource, bool> hasVerifiedPayload,
        Func<ProjectFileReference, bool> hasVerifiedDocumentPayload,
        Func<ProjectVisualizationImage, bool> hasVerifiedVisualizationPayload)
    {
        string code = (componentCode ?? "").Trim();
        if (StudioSourceRemovalOutbox.IsStaged(
                project,
                code,
                currentEmail,
                currentDeviceFingerprint))
        {
            return new ComponentAuthority(
                true,
                "Альбумын source component устгах",
                "Яг энэ бүртгэл/төхөөрөмжийн хүсэлтээр Cloud registry row-г эхэлж retire хийгээд, дараа нь canonical album-аас component-ийг хасна.");
        }

        if (code.Equals(
                ProjectCloudSyncMetadata.SiteContextComponentCode,
                StringComparison.OrdinalIgnoreCase))
        {
            ProjectSiteContextEditAuthority site =
                ProjectSiteContextEditingPolicy.Resolve(project, currentEmail);
            ProjectDesignSource? siteSource = string.IsNullOrWhiteSpace(site.SourceId)
                ? null
                : project.Sources.FirstOrDefault(source =>
                    source.Id.Equals(
                        site.SourceId,
                        StringComparison.OrdinalIgnoreCase));
            bool hasExactLocalSource =
                siteSource is not null &&
                StudioRuntimeSourceScope.IsAuthorizedLocal(
                    project,
                    siteSource,
                    currentEmail,
                    currentDeviceFingerprint,
                    hasVerifiedPayload);
            bool isCleared = !project.SiteContext.Boundary.HasGeometry &&
                !project.SiteContext.LocationScheme.HasSnapshot &&
                !project.SiteContext.SurroundingsOverview.HasSnapshot;
            bool authorized = site.CanEdit && (hasExactLocalSource || isCleared);
            return new ComponentAuthority(
                authorized,
                "Байршлын схем / Орчны тойм",
                authorized
                    ? "Ерөнхий төлөвлөгөөний source owner-ийн өөрчлөлтийг илгээнэ"
                    : site.CanEdit
                        ? "SiteContext source нь энэ бүртгэл/төхөөрөмжийн баталгаатай локал payload биш. Cloud хувилбар read-only."
                        : site.Message);
        }

        if (IsAuxiliaryComponentCode(code))
        {
            bool hasExactLocalPayload =
                StudioAuxiliarySourceLocalityPolicy.IsAlbumComponentAuthorized(
                    project,
                    code,
                    currentEmail,
                    currentDeviceFingerprint,
                    hasVerifiedDocumentPayload,
                    hasVerifiedVisualizationPayload);
            return new ComponentAuthority(
                hasExactLocalPayload,
                ComponentTitle(code),
                hasExactLocalPayload
                    ? "Зөвхөн энэ бүртгэл/төхөөрөмжид баталгаажсан physical source component шинэчлэгдэнэ"
                    : "ATD/visualization component нь энэ бүртгэл/төхөөрөмжийн баталгаатай локал payload биш. Cloud хувилбар read-only.");
        }

        if (ProjectCloudSyncMetadata.IsBuildingSubCoverComponentCode(code))
        {
            string identity = code[
                ProjectCloudSyncMetadata.BuildingSubCoverComponentCodePrefix.Length..]
                .Trim();
            bool referencesKnownBuilding =
                StudioAlbumComponentIdentity.TryResolveBuildingGroup(
                    project,
                    identity,
                    out _);
            bool canEditComposition =
                ProjectCloudSyncAuthority.CanEditBuildingComposition(
                    project.Cloud,
                    currentEmail);
            bool canPublishSubCover =
                canManageCanonical ||
                (referencesKnownBuilding && canEditComposition);
            return new ComponentAuthority(
                canPublishSubCover,
                ComponentTitle(code),
                canPublishSubCover
                    ? "Барилгын иж бүрдлийг засах эрхээр тухайн барилгын canonical дэд нүүрийг шинэчилнэ"
                    : !referencesKnownBuilding
                        ? "Дэд нүүрний component нь одоогийн canonical барилгын төрөлтэй таарахгүй тул хаалаа."
                        : "Барилгын иж бүрдэл болон дэд нүүр шинэчлэх concept.write эрх шаардлагатай.");
        }

        ProjectDesignSource? source = ResolveComponentSource(project, code);
        if (source is not null)
        {
            string sourceKey = ProjectCloudSyncMetadata.CloudSourceKey(source);
            ProjectSourceEditAuthority authority =
                sourceAuthorities.TryGetValue(
                    SourceComponentIdentity(source),
                    out ProjectSourceEditAuthority? pending)
                    ? pending
                    : ProjectCloudSyncAuthority.ResolveSource(project, source, currentEmail);
            bool hasExactLocalSource =
                authority.CanEdit &&
                StudioRuntimeSourceScope.IsAuthorizedLocal(
                    project,
                    source,
                    currentEmail,
                    currentDeviceFingerprint,
                    hasVerifiedPayload);
            return new ComponentAuthority(
                hasExactLocalSource,
                $"Альбумын source component: {SourceLabel(source)}",
                hasExactLocalSource
                    ? $"SourceKey {sourceKey}-ийн зөвхөн энэ эзэмшигчийн component шинэчлэгдэнэ"
                    : authority.CanEdit
                        ? "Source component нь энэ бүртгэл/төхөөрөмжийн баталгаатай локал payload биш. Cloud хувилбар read-only."
                        : authority.Message);
        }

        ProjectCloudAlbumComponentReference? shared =
            (project.Cloud.SharedAlbumComponents ?? []).FirstOrDefault(item =>
                item.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
        if (shared is not null &&
            shared.ComponentKind.Equals(
                StudioAlbumComponentIdentity.SourceComponentKind,
                StringComparison.OrdinalIgnoreCase))
        {
            return new ComponentAuthority(
                false,
                $"Альбумын source component: {shared.Label}",
                $"SourceKey {shared.SourceKey} Cloud mirror дээр read-only. Энэ төхөөрөмжид баталгаатай локал source холбоогүй.");
        }

        string baseCode =
            StudioAlbumComponentIdentity.BaseSourceCode(code);
        if (StudioAlbumComponentIdentity.IsOwnedSourceCode(baseCode) ||
            baseCode.StartsWith(
                "source:",
                StringComparison.OrdinalIgnoreCase))
        {
            return new ComponentAuthority(
                false,
                "Альбумын source component",
                "Локал source олдоогүй тул Cloud component read-only хэвээр үлдэнэ.");
        }

        return new ComponentAuthority(
            canManageCanonical,
            ComponentTitle(code),
            canManageCanonical
                ? "Каноник project/company өгөгдлөөс дахин зурж Cloud album-д нэгтгэнэ"
                : "Каноник generated component-ийг зөвхөн project admin шинэчилнэ. Pending хэвээр үлдэнэ.");
    }

    private static ProjectDesignSource? ResolveComponentSource(
        ProjectWorkspace project,
        string componentCode)
    {
        string normalized = StudioAlbumComponentIdentity.BaseSourceCode(componentCode);
        if (StudioAlbumComponentIdentity.IsOwnedSourceCode(normalized))
        {
            ProjectDesignSource? exact = project.Sources.FirstOrDefault(source =>
            {
                string owner = ProjectCloudSyncMetadata.CloudOwnerEmail(source);
                return !string.IsNullOrWhiteSpace(owner) &&
                    StudioAlbumComponentIdentity.SourceCode(
                        owner,
                        ProjectCloudSyncMetadata.CloudSourceKey(source))
                    .Equals(normalized, StringComparison.OrdinalIgnoreCase);
            });
            if (exact is not null)
                return exact;

            string[] parts = normalized.Split(':', 3);
            string sourceKey = parts.Length == 3 ? parts[2] : "";
            return StudioLegacySourceResolver.ResolveUniqueSourceKey(
                project,
                sourceKey);
        }

        if (!normalized.StartsWith("source:", StringComparison.OrdinalIgnoreCase))
            return null;

        string identity = normalized["source:".Length..].Trim();
        return StudioLegacySourceResolver.Resolve(project, identity);
    }

    private static bool IsAuxiliaryComponentCode(string code)
    {
        string baseCode = StudioAlbumComponentIdentity.BaseSourceCode(code);
        return code.Equals(
                ProjectCloudSyncMetadata.ApprovedAtdComponentCode,
                StringComparison.OrdinalIgnoreCase) ||
            code.Equals(
                ProjectCloudSyncMetadata.VisualizationsComponentCode,
                StringComparison.OrdinalIgnoreCase) ||
            baseCode.EndsWith(
                ":" + StudioAlbumComponentIdentity.AtdSourceKey,
                StringComparison.OrdinalIgnoreCase) ||
            baseCode.EndsWith(
                ":" + StudioAlbumComponentIdentity.VisualizationSourceKey,
                StringComparison.OrdinalIgnoreCase);
    }

    private static string SourceComponentIdentity(ProjectDesignSource source)
    {
        string owner = ProjectCloudSyncMetadata.CloudOwnerEmail(source);
        string sourceKey = ProjectCloudSyncMetadata.CloudSourceKey(source);
        return !string.IsNullOrWhiteSpace(owner)
            ? StudioAlbumComponentIdentity.SourceCode(owner, sourceKey)
            : "legacy-source:" + source.Id.Trim() + ":" + sourceKey.Trim();
    }

    private static string ComponentTitle(string code)
    {
        if (code.Equals(ProjectCloudSyncMetadata.ApprovedAtdComponentCode, StringComparison.OrdinalIgnoreCase))
            return "Батлагдсан архитектур төлөвлөлтийн даалгавар";
        if (code.Equals(ProjectCloudSyncMetadata.VisualizationsComponentCode, StringComparison.OrdinalIgnoreCase))
            return "Харагдах байдлын хуудас";
        if (code.Equals(ProjectCloudSyncMetadata.CompanyRegistrationComponentCode, StringComparison.OrdinalIgnoreCase))
            return "Байгууллагын гэрчилгээ";
        if (code.Equals(ProjectCloudSyncMetadata.CompanyLicenseComponentCode, StringComparison.OrdinalIgnoreCase))
            return "Тусгай зөвшөөрөл";
        if (ProjectCloudSyncMetadata.IsBuildingSubCoverComponentCode(code))
            return "Барилгын дэд нүүр";
        return "Studio generated component";
    }

    private static string SourceLabel(ProjectSourceSyncCandidate source) =>
        string.IsNullOrWhiteSpace(source.Source.Name)
            ? source.SourceDocumentReference
            : source.Source.Name;

    private static string SourceLabel(ProjectDesignSource source) =>
        string.IsNullOrWhiteSpace(source.Name)
            ? ProjectCloudSyncMetadata.CloudSourceKey(source)
            : source.Name;

    private static void AddRemoteSourceChanges(
        ProjectWorkspace project,
        StudioCloudProjectDetail detail,
        ICollection<CloudSyncChangeItem> downloads)
    {
        IReadOnlyList<StudioCloudSourcePackage> remoteSources =
            StudioCloudSourcePackageReconciliation.ActiveCanonical(
                detail.DesignPackages.SelectMany(package => package.SourcePackages));
        IReadOnlyList<ProjectCloudSourceReference> localSources =
            project.Cloud.SharedSources ?? [];

        foreach (StudioCloudSourcePackage remoteSource in remoteSources)
        {
            ProjectCloudSourceReference? localSource =
                ResolveLocalSource(localSources, remoteSource);
            bool isNew = localSource is null;
            if (!isNew && !SourceChanged(localSource!, remoteSource))
                continue;

            string sourceKey = string.IsNullOrWhiteSpace(remoteSource.SourceKey)
                ? remoteSource.SourceId
                : remoteSource.SourceKey;
            downloads.Add(new CloudSyncChangeItem(
                CloudSyncChangeDirection.Download,
                "remote-source:" + SourceStreamIdentity(remoteSource),
                isNew
                    ? $"Шинэ source: {SourceLabel(remoteSource)}"
                    : $"Source шинэчлэгдсэн: {SourceLabel(remoteSource)}",
                $"{OwnerLabel(EffectiveRemoteOwner(remoteSource))} · " +
                $"{remoteSource.SheetCount} хуудас · {remoteSource.SourceApplication} · " +
                $"SourceKey {sourceKey}; бүртгэл болон proxy/component өгөгдөл шинэчлэгдэнэ, native файл татахгүй"));
        }
    }

    private static ProjectCloudSourceReference? ResolveLocalSource(
        IReadOnlyList<ProjectCloudSourceReference> localSources,
        StudioCloudSourcePackage remoteSource)
    {
        if (!string.IsNullOrWhiteSpace(remoteSource.SourceKey))
        {
            List<ProjectCloudSourceReference> keyMatches = localSources
                .Where(source =>
                    source.SourceKey.Equals(
                        remoteSource.SourceKey,
                        StringComparison.OrdinalIgnoreCase) &&
                    !source.Status.Equals("Retired", StringComparison.OrdinalIgnoreCase))
                .ToList();

            string registeredBy = NormalizeEmail(remoteSource.RegisteredBy);
            if (!string.IsNullOrWhiteSpace(registeredBy))
            {
                ProjectCloudSourceReference? registeredMatch =
                    keyMatches.FirstOrDefault(source =>
                        NormalizeEmail(source.RegisteredBy).Equals(
                            registeredBy,
                            StringComparison.OrdinalIgnoreCase));
                if (registeredMatch is not null)
                    return registeredMatch;
            }

            string owner = EffectiveRemoteOwner(remoteSource);
            if (!string.IsNullOrWhiteSpace(owner))
            {
                ProjectCloudSourceReference? ownerMatch =
                    keyMatches.FirstOrDefault(source =>
                        EffectiveLocalOwner(source).Equals(
                            owner,
                            StringComparison.OrdinalIgnoreCase));
                if (ownerMatch is not null)
                    return ownerMatch;
            }

            if (keyMatches.Count == 1)
                return keyMatches[0];
        }

        if (!string.IsNullOrWhiteSpace(remoteSource.SourceId))
        {
            ProjectCloudSourceReference? sourceIdMatch = localSources.FirstOrDefault(source =>
                source.SourceId.Equals(
                    remoteSource.SourceId,
                    StringComparison.OrdinalIgnoreCase));
            if (sourceIdMatch is not null)
                return sourceIdMatch;
        }

        string reference = NormalizeReference(remoteSource.SourceDocumentReference);
        string remoteOwner = EffectiveRemoteOwner(remoteSource);
        return localSources.FirstOrDefault(source =>
            NormalizeReference(source.SourceDocumentReference).Equals(
                reference,
                StringComparison.OrdinalIgnoreCase) &&
            source.SourceApplication.Equals(
                remoteSource.SourceApplication,
                StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrWhiteSpace(remoteOwner) ||
             EffectiveLocalOwner(source).Equals(
                 remoteOwner,
                 StringComparison.OrdinalIgnoreCase)));
    }

    private static bool SourceChanged(
        ProjectCloudSourceReference localSource,
        StudioCloudSourcePackage remoteSource) =>
        !localSource.ManifestId.Equals(
            remoteSource.ManifestId,
            StringComparison.OrdinalIgnoreCase) ||
        !localSource.ContentHash.Equals(
            remoteSource.ContentHash,
            StringComparison.OrdinalIgnoreCase) ||
        localSource.SheetCount != remoteSource.SheetCount ||
        !localSource.Status.Equals(
            remoteSource.Status,
            StringComparison.OrdinalIgnoreCase) ||
        !localSource.SourceApplication.Equals(
            remoteSource.SourceApplication,
            StringComparison.OrdinalIgnoreCase) ||
        !NormalizeReference(localSource.SourceDocumentReference).Equals(
            NormalizeReference(remoteSource.SourceDocumentReference),
            StringComparison.OrdinalIgnoreCase) ||
        !EffectiveLocalOwner(localSource).Equals(
            EffectiveRemoteOwner(remoteSource),
            StringComparison.OrdinalIgnoreCase);

    private static string SourceStreamIdentity(StudioCloudSourcePackage source)
    {
        string sourceKey = string.IsNullOrWhiteSpace(source.SourceKey)
            ? source.SourceId
            : source.SourceKey;
        string contributor = NormalizeEmail(source.RegisteredBy);
        if (string.IsNullOrWhiteSpace(contributor))
            contributor = EffectiveRemoteOwner(source);
        return sourceKey.Trim() + ":" + contributor;
    }

    private static string SourceLabel(StudioCloudSourcePackage source)
    {
        string reference = NormalizeReference(source.SourceDocumentReference);
        if (!string.IsNullOrWhiteSpace(reference))
            return reference;
        if (!string.IsNullOrWhiteSpace(source.SourceApplication))
            return source.SourceApplication;
        return string.IsNullOrWhiteSpace(source.SourceKey)
            ? source.SourceId
            : source.SourceKey;
    }

    private static string EffectiveRemoteOwner(StudioCloudSourcePackage source)
    {
        string custodian = NormalizeEmail(source.CustodianEmail);
        return string.IsNullOrWhiteSpace(custodian)
            ? NormalizeEmail(source.RegisteredBy)
            : custodian;
    }

    private static string EffectiveLocalOwner(ProjectCloudSourceReference source)
    {
        string custodian = NormalizeEmail(source.CustodianEmail);
        if (!string.IsNullOrWhiteSpace(custodian))
            return custodian;
        string owner = NormalizeEmail(source.OwnerEmail);
        return string.IsNullOrWhiteSpace(owner)
            ? NormalizeEmail(source.RegisteredBy)
            : owner;
    }

    private static string OwnerLabel(string? value)
    {
        string owner = NormalizeEmail(value);
        return string.IsNullOrWhiteSpace(owner)
            ? "Эзэмшигч тодорхойгүй"
            : owner;
    }

    private static string NormalizeReference(string? value)
    {
        string reference = (value ?? "").Trim().Replace('\\', '/');
        int separator = reference.LastIndexOf('/');
        return separator >= 0
            ? reference[(separator + 1)..].Trim()
            : reference;
    }

    private static string NormalizeEmail(string? value) =>
        (value ?? "").Trim().ToLowerInvariant();

    private sealed record ComponentAuthority(
        bool CanEdit,
        string Title,
        string Message);
}
