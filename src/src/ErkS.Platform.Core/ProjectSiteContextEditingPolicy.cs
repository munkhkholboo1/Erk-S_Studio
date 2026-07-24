namespace ErkS.Platform.Core;

/// <summary>
/// Resolves the single contributor who may edit the Studio-generated location
/// scheme and surroundings overview. The controlling source remains local;
/// Cloud stores only its stable identity and current custodian.
/// </summary>
public sealed record ProjectSiteContextEditAuthority(
    bool CanEdit,
    string SourceId,
    string SourceKey,
    string OwnerEmail,
    string Message,
    string SourceOwnerEmail = "");

public sealed record ProjectSiteContextSourceLock(
    string SourceKey,
    string OwnerEmail,
    string SourceOwnerEmail = "");

public static class ProjectSiteContextEditingPolicy
{
    public const string SiteContextComponentKind = "SiteContext";

    public static ProjectSiteContextSourceLock? ResolveCanonicalSourceLock(
        ProjectWorkspace project)
    {
        ArgumentNullException.ThrowIfNull(project);
        ProjectCloudAlbumComponentReference? component =
            (project.Cloud?.SharedAlbumComponents ?? [])
            .FirstOrDefault(item =>
                item.Code.Equals(
                    ProjectCloudSyncMetadata.SiteContextComponentCode,
                    StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(item.SourceKey));
        if (component is null)
            return null;

        string sourceKey = component.SourceKey.Trim();
        string sourceOwnerEmail = NormalizeEmail(component.OwnerEmail);
        ProjectCloudSourceReference? sharedSource = ResolveSharedSource(
            project,
            sourceKey,
            sourceOwnerEmail);
        if (!string.IsNullOrWhiteSpace(sharedSource?.RegisteredBy))
            sourceOwnerEmail = NormalizeEmail(sharedSource.RegisteredBy);
        string currentCustodian = EffectiveController(sharedSource);
        if (string.IsNullOrWhiteSpace(currentCustodian))
            currentCustodian = sourceOwnerEmail;
        return new ProjectSiteContextSourceLock(
            sourceKey,
            currentCustodian,
            sourceOwnerEmail);
    }

    public static bool MatchesCanonicalSource(
        ProjectWorkspace project,
        ProjectDesignSource source)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(source);
        ProjectSiteContextSourceLock? sourceLock = ResolveCanonicalSourceLock(project);
        if (sourceLock is null)
            return true;

        string sourceKey = ProjectCloudSyncMetadata.CloudSourceKey(source);
        if (!sourceKey.Equals(sourceLock.SourceKey, StringComparison.OrdinalIgnoreCase))
            return false;
        string sourceOwnerEmail = string.IsNullOrWhiteSpace(sourceLock.SourceOwnerEmail)
            ? sourceLock.OwnerEmail
            : sourceLock.SourceOwnerEmail;
        if (string.IsNullOrWhiteSpace(sourceOwnerEmail))
            return true;
        return ProjectCloudSyncMetadata.CloudOwnerEmail(source).Equals(
            sourceOwnerEmail,
            StringComparison.OrdinalIgnoreCase);
    }

    public static ProjectSiteContextEditAuthority Resolve(
        ProjectWorkspace project,
        string? currentUserEmail)
    {
        ArgumentNullException.ThrowIfNull(project);
        string currentEmail = NormalizeEmail(currentUserEmail);
        ProjectSiteBoundary boundary = project.SiteContext?.Boundary ?? new ProjectSiteBoundary();
        string boundarySourceId = boundary.HasGeometry
            ? boundary.SourceId?.Trim() ?? ""
            : "";

        ProjectSiteContextSourceLock? sourceLock = ResolveCanonicalSourceLock(project);
        string canonicalSourceKey = sourceLock?.SourceKey ?? "";
        string canonicalOwnerEmail = sourceLock?.OwnerEmail ?? "";
        string canonicalSourceOwnerEmail = sourceLock?.SourceOwnerEmail ?? "";

        ProjectDesignSource? localSource = ResolveLocalSource(
            project,
            boundarySourceId,
            canonicalSourceKey,
            canonicalSourceOwnerEmail,
            currentEmail);
        if (localSource is null)
        {
            return Denied(
                string.IsNullOrWhiteSpace(canonicalSourceKey)
                    ? "AutoCAD/CityGen эх үүсвэрээ Ерөнхий төлөвлөгөө гэж ангилсны дараа байршлын зураг засах эрх нээгдэнэ."
                    : "Энэ байршлын зурагт хамаарах ерөнхий төлөвлөгөөний эх үүсвэр энэ төхөөрөмжид холбогдоогүй байна.",
                sourceKey: canonicalSourceKey,
                ownerEmail: canonicalOwnerEmail);
        }
        if (localSource.Kind is not (DesignSourceKind.AutoCad or DesignSourceKind.CityGen))
        {
            return Denied(
                "Байршлын схемийг зөвхөн AutoCAD/CityGen ерөнхий төлөвлөгөөний эх үүсвэрээс удирдана.",
                localSource.Id,
                ProjectCloudSyncMetadata.CloudSourceKey(localSource));
        }
        if (ProjectDesignSourceClassification.IsExplicitlyBuilding(localSource))
        {
            return Denied(
                "Барилгын зураг гэж ангилсан эх үүсвэр байршлын схемийг удирдахгүй.",
                localSource.Id,
                ProjectCloudSyncMetadata.CloudSourceKey(localSource));
        }

        string sourceKey = ProjectCloudSyncMetadata.CloudSourceKey(localSource).Trim();
        if (!string.IsNullOrWhiteSpace(canonicalSourceKey) &&
            !sourceKey.Equals(canonicalSourceKey, StringComparison.OrdinalIgnoreCase))
        {
            return Denied(
                "Байршлын зураг өөр ерөнхий төлөвлөгөөний эх үүсвэрт түгжигдсэн байна.",
                localSource.Id,
                canonicalSourceKey,
                canonicalOwnerEmail);
        }
        bool matchesBoundary = !string.IsNullOrWhiteSpace(boundarySourceId) &&
                               (localSource.Id.Equals(
                                    boundarySourceId,
                                    StringComparison.OrdinalIgnoreCase) ||
                                sourceKey.Equals(
                                    boundarySourceId,
                                    StringComparison.OrdinalIgnoreCase));
        if (sourceLock is null &&
            !matchesBoundary &&
            !ProjectDesignSourceClassification.IsGeneralPlan(localSource))
        {
            return Denied(
                "Эх үүсвэрийг Ерөнхий төлөвлөгөө гэж ангилсны дараа байршлын зураг засна.",
                localSource.Id,
                sourceKey);
        }

        ProjectCloudSourceReference? sharedSource = ResolveSharedSource(
            project,
            sourceKey,
            canonicalSourceOwnerEmail);
        string ownerEmail = EffectiveController(sharedSource);
        if (string.IsNullOrWhiteSpace(ownerEmail))
            ownerEmail = canonicalOwnerEmail;
        if (string.IsNullOrWhiteSpace(ownerEmail))
            ownerEmail = ProjectCloudSyncMetadata.CloudOwnerEmail(localSource);
        string sourceOwnerEmail = NormalizeEmail(sharedSource?.RegisteredBy);
        if (string.IsNullOrWhiteSpace(sourceOwnerEmail))
            sourceOwnerEmail = canonicalSourceOwnerEmail;
        if (string.IsNullOrWhiteSpace(sourceOwnerEmail))
            sourceOwnerEmail = ProjectCloudSyncMetadata.CloudOwnerEmail(localSource);

        bool cloudProject =
            project.Cloud?.Origin.Equals(ProjectOrigins.Cloud, StringComparison.OrdinalIgnoreCase) == true &&
            !string.IsNullOrWhiteSpace(project.Cloud.ServerProjectId);
        if (cloudProject && string.IsNullOrWhiteSpace(currentEmail))
        {
            return Denied(
                "Cloud төслийн байршлын зургийг засахын өмнө бүртгэлээрээ нэвтэрнэ үү.",
                localSource.Id,
                sourceKey,
                ownerEmail);
        }
        if (cloudProject &&
            !string.IsNullOrWhiteSpace(ownerEmail) &&
            !ownerEmail.Equals(currentEmail, StringComparison.OrdinalIgnoreCase))
        {
            return Denied(
                $"Байршлын схемийг ерөнхий төлөвлөгөөний эх үүсвэр хариуцсан {ownerEmail} хэрэглэгч засна.",
                localSource.Id,
                sourceKey,
                ownerEmail);
        }

        if (string.IsNullOrWhiteSpace(ownerEmail))
            ownerEmail = currentEmail;
        return new ProjectSiteContextEditAuthority(
            true,
            localSource.Id,
            sourceKey,
            ownerEmail,
            "Ерөнхий төлөвлөгөөний эх үүсвэрээс байршлын схем засах эрх нээгдсэн.",
            sourceOwnerEmail);
    }

    private static ProjectDesignSource? ResolveLocalSource(
        ProjectWorkspace project,
        string boundarySourceId,
        string canonicalSourceKey,
        string canonicalOwnerEmail,
        string currentUserEmail)
    {
        List<ProjectDesignSource> candidates = (project.Sources ?? [])
            .Where(source => source.Kind is DesignSourceKind.AutoCad or DesignSourceKind.CityGen)
            .ToList();
        if (!string.IsNullOrWhiteSpace(canonicalSourceKey))
        {
            List<ProjectDesignSource> matches = candidates
                .Where(source => ProjectCloudSyncMetadata.CloudSourceKey(source).Equals(
                    canonicalSourceKey,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (!string.IsNullOrWhiteSpace(canonicalOwnerEmail))
            {
                ProjectDesignSource? owned = matches.FirstOrDefault(source =>
                    ProjectCloudSyncMetadata.CloudOwnerEmail(source).Equals(
                        canonicalOwnerEmail,
                        StringComparison.OrdinalIgnoreCase));
                if (owned is not null)
                    return owned;
            }
            ProjectDesignSource? current = matches.FirstOrDefault(source =>
                source.Id.Equals(boundarySourceId, StringComparison.OrdinalIgnoreCase));
            return current ?? (matches.Count == 1 ? matches[0] : null);
        }

        ProjectDesignSource? boundarySource = candidates.FirstOrDefault(source =>
            source.Id.Equals(boundarySourceId, StringComparison.OrdinalIgnoreCase) ||
            ProjectCloudSyncMetadata.CloudSourceKey(source).Equals(
                boundarySourceId,
                StringComparison.OrdinalIgnoreCase));
        if (boundarySource is not null)
            return boundarySource;

        List<ProjectDesignSource> generalPlanSources = candidates
            .Where(ProjectDesignSourceClassification.IsGeneralPlan)
            .ToList();
        if (!string.IsNullOrWhiteSpace(currentUserEmail))
        {
            ProjectDesignSource? controlled = generalPlanSources
                .Where(source => ResolveSourceController(project, source).Equals(
                    currentUserEmail,
                    StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(source => source.LastPackageAtUtc ?? source.CreatedAtUtc)
                .ThenBy(source => source.Id, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (controlled is not null)
                return controlled;
        }

        return generalPlanSources.Count == 1 ? generalPlanSources[0] : null;
    }

    private static string ResolveSourceController(
        ProjectWorkspace project,
        ProjectDesignSource source)
    {
        string localOwner = ProjectCloudSyncMetadata.CloudOwnerEmail(source);
        ProjectCloudSourceReference? sharedSource = ResolveSharedSource(
            project,
            ProjectCloudSyncMetadata.CloudSourceKey(source),
            localOwner);
        string controller = EffectiveController(sharedSource);
        return string.IsNullOrWhiteSpace(controller) ? localOwner : controller;
    }

    private static ProjectCloudSourceReference? ResolveSharedSource(
        ProjectWorkspace project,
        string sourceKey,
        string canonicalOwnerEmail)
    {
        List<ProjectCloudSourceReference> matches = (project.Cloud?.SharedSources ?? [])
            .Where(source =>
                source.SourceKey.Equals(sourceKey, StringComparison.OrdinalIgnoreCase) &&
                !source.Status.Equals("Retired", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (!string.IsNullOrWhiteSpace(canonicalOwnerEmail))
        {
            ProjectCloudSourceReference? exact = matches.FirstOrDefault(source =>
                NormalizeEmail(source.RegisteredBy).Equals(
                    canonicalOwnerEmail,
                    StringComparison.OrdinalIgnoreCase)) ??
                matches.FirstOrDefault(source =>
                    NormalizeEmail(source.OwnerEmail).Equals(
                        canonicalOwnerEmail,
                        StringComparison.OrdinalIgnoreCase));
            if (exact is not null)
                return exact;
        }
        return matches.Count == 1 ? matches[0] : null;
    }

    private static string EffectiveController(ProjectCloudSourceReference? source)
    {
        if (source is null)
            return "";
        string custodian = NormalizeEmail(source.CustodianEmail);
        if (!string.IsNullOrWhiteSpace(custodian))
            return custodian;
        string owner = NormalizeEmail(source.OwnerEmail);
        return string.IsNullOrWhiteSpace(owner)
            ? NormalizeEmail(source.RegisteredBy)
            : owner;
    }

    private static ProjectSiteContextEditAuthority Denied(
        string message,
        string sourceId = "",
        string sourceKey = "",
        string ownerEmail = "") => new(
        false,
        sourceId,
        sourceKey,
        ownerEmail,
        message);

    private static string NormalizeEmail(string? value) =>
        (value ?? "").Trim().ToLowerInvariant();
}
