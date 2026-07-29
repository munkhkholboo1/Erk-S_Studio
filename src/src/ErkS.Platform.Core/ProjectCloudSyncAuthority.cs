namespace ErkS.Platform.Core;

/// <summary>
/// Describes who may publish a single source stream. A user's email identifies
/// the contributor, while SourceKey keeps separate devices and native files
/// from becoming one shared editable value.
/// </summary>
public sealed record ProjectSourceEditAuthority(
    bool CanEdit,
    string SourceKey,
    string OwnerEmail,
    string Message);

public static class ProjectCloudSyncAuthority
{
    public static bool CanManageCanonicalMetadata(
        ProjectCloudLink? cloud,
        string? currentUserEmail)
    {
        if (cloud is null ||
            !cloud.PermissionSnapshotBelongsTo(currentUserEmail))
            return false;
        if (cloud.HasScope("project.metadata.write", currentUserEmail))
            return true;
        if (cloud.HasScope("team.manage", currentUserEmail))
            return true;

        return (cloud.CurrentUserRoles ?? []).Any(role =>
            role.Equals("ProjectAdmin", StringComparison.OrdinalIgnoreCase) ||
            role.Equals("DesignCompanyAdmin", StringComparison.OrdinalIgnoreCase));
    }

    public static bool CanEditBuildingComposition(
        ProjectCloudLink? cloud,
        string? currentUserEmail) =>
        cloud is not null &&
        cloud.HasScope("concept.write", currentUserEmail);

    public static ProjectSourceEditAuthority ResolveSource(
        ProjectWorkspace project,
        ProjectDesignSource source,
        string? currentUserEmail)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(source);

        string sourceKey = ProjectCloudSyncMetadata.CloudSourceKey(source).Trim();
        string currentEmail = NormalizeEmail(currentUserEmail);
        string localOwner = ProjectCloudSyncMetadata.CloudOwnerEmail(source);
        ProjectCloudLink cloud = project.Cloud ?? new ProjectCloudLink();
        bool cloudProject =
            cloud.Origin.Equals(
                ProjectOrigins.Cloud,
                StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(cloud.ServerProjectId);

        if (!cloudProject)
        {
            return Allowed(
                sourceKey,
                string.IsNullOrWhiteSpace(localOwner) ? currentEmail : localOwner);
        }
        if (string.IsNullOrWhiteSpace(currentEmail))
        {
            return Denied(
                sourceKey,
                localOwner,
                "Cloud эх үүсвэр шинэчлэхийн өмнө бүртгэлээрээ нэвтэрнэ үү.");
        }

        List<ProjectCloudSourceReference> matching = (cloud.SharedSources ?? [])
            .Where(item =>
                string.Equals(item.SourceKey, sourceKey, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(item.Status, "Retired", StringComparison.OrdinalIgnoreCase))
            .ToList();
        ProjectCloudSourceReference? shared = ResolveSharedSource(
            matching,
            localOwner,
            currentEmail);
        string controller = EffectiveController(shared);

        if (!string.IsNullOrWhiteSpace(controller) &&
            !controller.Equals(currentEmail, StringComparison.OrdinalIgnoreCase))
        {
            return Denied(
                sourceKey,
                controller,
                $"Энэ эх үүсвэрийг {controller} хэрэглэгч хариуцаж байна.");
        }
        if (shared is null &&
            !string.IsNullOrWhiteSpace(localOwner) &&
            !localOwner.Equals(currentEmail, StringComparison.OrdinalIgnoreCase))
        {
            return Denied(
                sourceKey,
                localOwner,
                $"Энэ төхөөрөмжийн эх үүсвэр {localOwner} хэрэглэгчид холбогдсон байна.");
        }

        // A new SourceKey is an independent stream. The signed-in user may
        // register it even when the same account owns other source keys on
        // another computer.
        return Allowed(
            sourceKey,
            string.IsNullOrWhiteSpace(controller) ? currentEmail : controller);
    }

    private static ProjectCloudSourceReference? ResolveSharedSource(
        IReadOnlyList<ProjectCloudSourceReference> matching,
        string localOwner,
        string currentEmail)
    {
        if (!string.IsNullOrWhiteSpace(localOwner))
        {
            ProjectCloudSourceReference? local = matching.FirstOrDefault(item =>
                EffectiveController(item).Equals(
                    localOwner,
                    StringComparison.OrdinalIgnoreCase) ||
                NormalizeEmail(item.RegisteredBy).Equals(
                    localOwner,
                    StringComparison.OrdinalIgnoreCase));
            if (local is not null)
                return local;
        }

        ProjectCloudSourceReference? current = matching.FirstOrDefault(item =>
            EffectiveController(item).Equals(
                currentEmail,
                StringComparison.OrdinalIgnoreCase) ||
            NormalizeEmail(item.RegisteredBy).Equals(
                currentEmail,
                StringComparison.OrdinalIgnoreCase));
        // A legacy registry may contain the same SourceKey for several
        // contributors. A matching contributor can keep editing their stream,
        // but a third user must not claim that occupied key.
        return current ?? matching.FirstOrDefault();
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

    private static ProjectSourceEditAuthority Allowed(
        string sourceKey,
        string ownerEmail) =>
        new(
            true,
            sourceKey,
            ownerEmail,
            "Эх үүсвэрийн өөрчлөлтийг Cloud ERA руу илгээх эрхтэй.");

    private static ProjectSourceEditAuthority Denied(
        string sourceKey,
        string ownerEmail,
        string message) =>
        new(false, sourceKey, ownerEmail, message);

    private static string NormalizeEmail(string? value) =>
        (value ?? "").Trim().ToLowerInvariant();
}
