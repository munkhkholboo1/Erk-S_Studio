using System.IO;
using System.Security.Cryptography;
using ErkS.Platform.Core;

namespace ErkS.Studio;

/// <summary>
/// Device-locality boundary for physical Studio assets that are not design
/// package sources (ATD/project documents and visualization images).
///
/// For a Cloud-linked project, locality is an explicit three-part fact:
/// immutable contributor account + local binding account/device + verified
/// payload. Blank legacy bindings are Cloud/read-only until the user explicitly
/// adds or relinks a file. Offline projects retain their legacy local behavior.
/// </summary>
internal static class StudioAuxiliarySourceLocalityPolicy
{
    public static bool IsLocalDocument(
        ProjectWorkspace project,
        ProjectFileReference document,
        string? currentAccountEmail,
        string? currentDeviceFingerprint,
        bool hasVerifiedPayload)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(document);
        return !document.IsCloudPlaceholder &&
            hasVerifiedPayload &&
            (!IsCloudLinked(project) ||
             BindingMatches(
                 document.CloudOwnerEmail,
                 document.LocalBindingAccountEmail,
                 document.LocalBindingDeviceFingerprint,
                 currentAccountEmail,
                 currentDeviceFingerprint));
    }

    public static bool IsLocalVisualizationImage(
        ProjectWorkspace project,
        ProjectVisualizationImage image,
        string? currentAccountEmail,
        string? currentDeviceFingerprint,
        bool hasVerifiedPayload)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(image);
        return !image.IsCloudPlaceholder &&
            hasVerifiedPayload &&
            (!IsCloudLinked(project) ||
             BindingMatches(
                 image.CloudOwnerEmail,
                 image.LocalBindingAccountEmail,
                 image.LocalBindingDeviceFingerprint,
                 currentAccountEmail,
                 currentDeviceFingerprint));
    }

    public static bool BindingMatches(
        ProjectWorkspace project,
        ProjectFileReference document,
        string? currentAccountEmail,
        string? currentDeviceFingerprint)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(document);
        return !document.IsCloudPlaceholder &&
            (!IsCloudLinked(project) ||
             BindingMatches(
                 document.CloudOwnerEmail,
                 document.LocalBindingAccountEmail,
                 document.LocalBindingDeviceFingerprint,
                 currentAccountEmail,
                 currentDeviceFingerprint));
    }

    public static bool BindingMatches(
        ProjectWorkspace project,
        ProjectVisualizationImage image,
        string? currentAccountEmail,
        string? currentDeviceFingerprint)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(image);
        return !image.IsCloudPlaceholder &&
            (!IsCloudLinked(project) ||
             BindingMatches(
                 image.CloudOwnerEmail,
                 image.LocalBindingAccountEmail,
                 image.LocalBindingDeviceFingerprint,
                 currentAccountEmail,
                 currentDeviceFingerprint));
    }

    public static bool CanExplicitlyBind(
        ProjectWorkspace project,
        ProjectFileReference document,
        string? currentAccountEmail)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(document);
        return CanExplicitlyBind(
            project,
            document.CloudOwnerEmail,
            document.IsCloudPlaceholder,
            currentAccountEmail);
    }

    public static bool CanExplicitlyBind(
        ProjectWorkspace project,
        ProjectVisualizationImage image,
        string? currentAccountEmail)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(image);
        return CanExplicitlyBind(
            project,
            image.CloudOwnerEmail,
            image.IsCloudPlaceholder,
            currentAccountEmail);
    }

    public static void Bind(
        ProjectWorkspace project,
        ProjectFileReference document,
        string? currentAccountEmail,
        string? currentDeviceFingerprint)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(document);
        if (!IsCloudLinked(project))
        {
            BindOffline(
                currentAccountEmail,
                currentDeviceFingerprint,
                value => document.LocalBindingAccountEmail = value,
                value => document.LocalBindingDeviceFingerprint = value);
            return;
        }

        (string account, string device) = RequireExplicitCloudBinding(
            project,
            document.CloudOwnerEmail,
            document.IsCloudPlaceholder,
            currentAccountEmail,
            currentDeviceFingerprint);
        document.CloudOwnerEmail = account;
        document.LocalBindingAccountEmail = account;
        document.LocalBindingDeviceFingerprint = device;
        document.IsCloudPlaceholder = false;
        if (string.IsNullOrWhiteSpace(document.CloudContributionId))
            document.CloudContributionId = Guid.NewGuid().ToString("N");
    }

    public static void Bind(
        ProjectWorkspace project,
        ProjectVisualizationImage image,
        string? currentAccountEmail,
        string? currentDeviceFingerprint)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(image);
        if (!IsCloudLinked(project))
        {
            BindOffline(
                currentAccountEmail,
                currentDeviceFingerprint,
                value => image.LocalBindingAccountEmail = value,
                value => image.LocalBindingDeviceFingerprint = value);
            return;
        }

        (string account, string device) = RequireExplicitCloudBinding(
            project,
            image.CloudOwnerEmail,
            image.IsCloudPlaceholder,
            currentAccountEmail,
            currentDeviceFingerprint);
        image.CloudOwnerEmail = account;
        image.LocalBindingAccountEmail = account;
        image.LocalBindingDeviceFingerprint = device;
        image.IsCloudPlaceholder = false;
        if (string.IsNullOrWhiteSpace(image.CloudContributionId))
            image.CloudContributionId = Guid.NewGuid().ToString("N");
    }

    public static IReadOnlyList<ProjectFileReference> LocalDocuments(
        ProjectWorkspace project,
        IEnumerable<ProjectFileReference> documents,
        string? currentAccountEmail,
        string? currentDeviceFingerprint,
        Func<ProjectFileReference, bool> hasVerifiedPayload) =>
        (documents ?? [])
            .Where(document => document is not null &&
                IsLocalDocument(
                    project,
                    document,
                    currentAccountEmail,
                    currentDeviceFingerprint,
                    hasVerifiedPayload(document)))
            .ToList();

    public static ProjectVisualizationSource CreateLocalVisualizationSnapshot(
        ProjectWorkspace project,
        string? currentAccountEmail,
        string? currentDeviceFingerprint,
        Func<ProjectVisualizationImage, bool> hasVerifiedPayload)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(hasVerifiedPayload);
        ProjectVisualizationSource source = project.Visualizations ??
            new ProjectVisualizationSource();
        source.Normalize(project.ProjectId);
        List<ProjectVisualizationImage> images = source
            .ImagesForProject(project.ProjectId)
            .Where(image => IsLocalVisualizationImage(
                project,
                image,
                currentAccountEmail,
                currentDeviceFingerprint,
                hasVerifiedPayload(image)))
            .Select(image => image.Clone())
            .ToList();
        return new ProjectVisualizationSource
        {
            OwnerProjectId = project.ProjectId,
            IsConfigured = images.Count > 0,
            Title = source.Title,
            ImagesPerPage = source.ImagesPerPage,
            Images = images,
        };
    }

    public static bool IsAlbumComponentAuthorized(
        ProjectWorkspace project,
        string? componentCode,
        string? currentAccountEmail,
        string? currentDeviceFingerprint,
        Func<ProjectFileReference, bool> hasVerifiedDocumentPayload,
        Func<ProjectVisualizationImage, bool> hasVerifiedVisualizationPayload)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(hasVerifiedDocumentPayload);
        ArgumentNullException.ThrowIfNull(hasVerifiedVisualizationPayload);
        string code = (componentCode ?? "").Trim();
        string current = NormalizeEmail(currentAccountEmail);
        if (string.IsNullOrWhiteSpace(current))
            return !IsAuxiliaryComponentCode(code);

        string atdCode = StudioAlbumComponentIdentity.SourceCode(
            current,
            StudioAlbumComponentIdentity.AtdSourceKey);
        string visualizationCode = StudioAlbumComponentIdentity.SourceCode(
            current,
            StudioAlbumComponentIdentity.VisualizationSourceKey);
        string baseCode = StudioAlbumComponentIdentity.BaseSourceCode(code);
        ProjectLocalAlbumComponentClaim? exactClaim =
            ProjectCloudSyncMetadata.PendingAlbumComponentClaim(
                project,
                code,
                current,
                currentDeviceFingerprint);
        bool hasScopedClaims = (project.Cloud.PendingAlbumComponentClaims ?? [])
            .Any(claim => claim.ComponentCode.Equals(
                code,
                StringComparison.OrdinalIgnoreCase));
        if (hasScopedClaims && exactClaim is null)
            return false;
        if (exactClaim?.IsRemoval == true)
            return true;

        bool atd = code.Equals(
                ProjectCloudSyncMetadata.ApprovedAtdComponentCode,
                StringComparison.OrdinalIgnoreCase) ||
            baseCode.Equals(atdCode, StringComparison.OrdinalIgnoreCase);
        if (atd)
        {
            return project.Foundation.PlanningTask.Documents.Any(document =>
                document.Category.Equals(
                    ProjectDocumentCategories.ApprovedPlanningTask,
                    StringComparison.OrdinalIgnoreCase) &&
                IsLocalDocument(
                    project,
                    document,
                    current,
                    currentDeviceFingerprint,
                    hasVerifiedDocumentPayload(document)));
        }

        bool visualization = code.Equals(
                ProjectCloudSyncMetadata.VisualizationsComponentCode,
                StringComparison.OrdinalIgnoreCase) ||
            baseCode.Equals(visualizationCode, StringComparison.OrdinalIgnoreCase);
        if (visualization)
        {
            project.Visualizations.Normalize(project.ProjectId);
            return project.Visualizations
                .ImagesForProject(project.ProjectId)
                .Any(image => IsLocalVisualizationImage(
                    project,
                    image,
                    current,
                    currentDeviceFingerprint,
                    hasVerifiedVisualizationPayload(image)));
        }

        // A source-owned ATD/visualization component for another account is
        // Cloud-only even if a copied project mirror retained a pending flag.
        return !IsAuxiliaryComponentCode(code);
    }

    public static bool HasVerifiedPayload(
        string projectPath,
        ProjectFileReference document) =>
        VerifyPayload(
            ResolvePayloadPath(
                projectPath,
                document.RelativePath,
                document.LinkedSourcePath),
            document.Sha256);

    public static bool HasVerifiedPayload(
        string projectPath,
        ProjectVisualizationImage image) =>
        VerifyPayload(
            ResolvePayloadPath(
                projectPath,
                image.RelativePath,
                image.LinkedSourcePath),
            image.Sha256);

    private static bool VerifyPayload(string path, string? expectedSha256)
    {
        if (!File.Exists(path))
            return false;
        string expected = NormalizeHash(expectedSha256);
        if (string.IsNullOrWhiteSpace(expected))
            return true;
        try
        {
            using FileStream stream = File.OpenRead(path);
            string actual = Convert.ToHexString(
                SHA256.HashData(stream)).ToLowerInvariant();
            return actual.Equals(expected, StringComparison.Ordinal);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string ResolvePayloadPath(
        string projectPath,
        string? relativePath,
        string? linkedPath)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(relativePath))
            {
                return Path.IsPathRooted(relativePath)
                    ? Path.GetFullPath(relativePath)
                    : ProjectWorkspacePaths.ResolveInsideProject(
                        projectPath,
                        relativePath);
            }

            return string.IsNullOrWhiteSpace(linkedPath)
                ? ""
                : Path.GetFullPath(linkedPath);
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or
                UnauthorizedAccessException or ArgumentException or
                NotSupportedException)
        {
            return "";
        }
    }

    private static string NormalizeHash(string? value) =>
        (value ?? "")
            .Trim()
            .Replace("-", "", StringComparison.Ordinal)
            .ToLowerInvariant();

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

    public static bool IsCloudLinked(ProjectWorkspace project) =>
        project.Cloud.Origin.Equals(
            ProjectOrigins.Cloud,
            StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(project.Cloud.ServerProjectId);

    private static bool BindingMatches(
        string? immutableOwnerEmail,
        string? bindingAccountEmail,
        string? bindingDeviceFingerprint,
        string? currentAccountEmail,
        string? currentDeviceFingerprint)
    {
        string owner = NormalizeEmail(immutableOwnerEmail);
        string bindingAccount = NormalizeEmail(bindingAccountEmail);
        string currentAccount = NormalizeEmail(currentAccountEmail);
        string bindingDevice = NormalizeDevice(bindingDeviceFingerprint);
        string currentDevice = NormalizeDevice(currentDeviceFingerprint);
        return !string.IsNullOrWhiteSpace(owner) &&
            !string.IsNullOrWhiteSpace(bindingAccount) &&
            !string.IsNullOrWhiteSpace(currentAccount) &&
            !string.IsNullOrWhiteSpace(bindingDevice) &&
            !string.IsNullOrWhiteSpace(currentDevice) &&
            owner.Equals(currentAccount, StringComparison.OrdinalIgnoreCase) &&
            bindingAccount.Equals(currentAccount, StringComparison.OrdinalIgnoreCase) &&
            bindingDevice.Equals(currentDevice, StringComparison.Ordinal);
    }

    private static bool CanExplicitlyBind(
        ProjectWorkspace project,
        string? immutableOwnerEmail,
        bool isCloudPlaceholder,
        string? currentAccountEmail)
    {
        if (isCloudPlaceholder)
            return false;
        if (!IsCloudLinked(project))
            return true;
        string current = NormalizeEmail(currentAccountEmail);
        string owner = NormalizeEmail(immutableOwnerEmail);
        return !string.IsNullOrWhiteSpace(current) &&
            (string.IsNullOrWhiteSpace(owner) ||
             owner.Equals(current, StringComparison.OrdinalIgnoreCase));
    }

    private static (string Account, string Device) RequireExplicitCloudBinding(
        ProjectWorkspace project,
        string? immutableOwnerEmail,
        bool isCloudPlaceholder,
        string? currentAccountEmail,
        string? currentDeviceFingerprint)
    {
        if (!CanExplicitlyBind(
                project,
                immutableOwnerEmail,
                isCloudPlaceholder,
                currentAccountEmail))
        {
            throw new InvalidOperationException(
                "This Cloud source belongs to another participant and cannot be adopted on this device.");
        }

        string account = NormalizeEmail(currentAccountEmail);
        string device = NormalizeDevice(currentDeviceFingerprint);
        if (string.IsNullOrWhiteSpace(account) ||
            string.IsNullOrWhiteSpace(device))
        {
            throw new InvalidOperationException(
                "A signed-in account and this device identity are required to link a Cloud source payload.");
        }
        return (account, device);
    }

    private static void BindOffline(
        string? currentAccountEmail,
        string? currentDeviceFingerprint,
        Action<string> setAccount,
        Action<string> setDevice)
    {
        string account = NormalizeEmail(currentAccountEmail);
        string device = NormalizeDevice(currentDeviceFingerprint);
        if (!string.IsNullOrWhiteSpace(account) &&
            !string.IsNullOrWhiteSpace(device))
        {
            setAccount(account);
            setDevice(device);
        }
    }

    private static string NormalizeEmail(string? value) =>
        (value ?? "").Trim().ToLowerInvariant();

    private static string NormalizeDevice(string? value) =>
        (value ?? "").Trim().ToLowerInvariant();
}
