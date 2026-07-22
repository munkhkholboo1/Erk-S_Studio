using ErkS.Platform.Core;

namespace ErkS.Platform.Pdf;

public sealed class ProjectAssetSourceReconciliationResult
{
    public int UpdatedDocumentCount { get; internal set; }
    public int MissingDocumentCount { get; internal set; }
    public int RestoredDocumentCount { get; internal set; }
    public int UpdatedVisualizationCount { get; internal set; }
    public int MissingVisualizationCount { get; internal set; }
    public int RestoredVisualizationCount { get; internal set; }
    public int ErrorCount { get; internal set; }

    public bool Changed =>
        UpdatedDocumentCount > 0 ||
        MissingDocumentCount > 0 ||
        RestoredDocumentCount > 0 ||
        UpdatedVisualizationCount > 0 ||
        MissingVisualizationCount > 0 ||
        RestoredVisualizationCount > 0;

    public void Merge(ProjectAssetSourceReconciliationResult other)
    {
        ArgumentNullException.ThrowIfNull(other);
        UpdatedDocumentCount += other.UpdatedDocumentCount;
        MissingDocumentCount += other.MissingDocumentCount;
        RestoredDocumentCount += other.RestoredDocumentCount;
        UpdatedVisualizationCount += other.UpdatedVisualizationCount;
        MissingVisualizationCount += other.MissingVisualizationCount;
        RestoredVisualizationCount += other.RestoredVisualizationCount;
        ErrorCount += other.ErrorCount;
    }
}

/// <summary>
/// Reconciles linked Studio assets with their owned project/company copies.
/// Missing links are kept as records for recovery but are excluded from album
/// plans through IsAvailable=false. No source or owned file is deleted here.
/// </summary>
public static class ProjectAssetSourceReconciler
{
    public static ProjectAssetSourceReconciliationResult ReconcileProject(
        ProjectWorkspace project,
        string projectPath)
    {
        ArgumentNullException.ThrowIfNull(project);
        string fullProjectPath = Path.GetFullPath(projectPath);
        var result = new ProjectAssetSourceReconciliationResult();

        ReconcileDocuments(
            project.Foundation.InitiationBasis.Documents,
            ResolveProjectDocumentPath,
            StoreProjectDocument,
            fullProjectPath,
            result);
        ReconcileDocuments(
            project.Foundation.PlanningTask.Documents,
            ResolveProjectDocumentPath,
            StoreProjectDocument,
            fullProjectPath,
            result);
        ReconcileDocuments(
            project.Foundation.DesignCompany.OrganizationSnapshot.RegistrationCertificateDocuments,
            ResolveProjectDocumentPath,
            StoreProjectDocument,
            fullProjectPath,
            result);
        ReconcileDocuments(
            project.Foundation.DesignCompany.OrganizationSnapshot.DesignLicenseDocuments,
            ResolveProjectDocumentPath,
            StoreProjectDocument,
            fullProjectPath,
            result);
        ReconcileVisualizations(project, fullProjectPath, result);
        return result;
    }

    public static ProjectAssetSourceReconciliationResult ReconcileCompanyProfile(
        CompanyProfile profile,
        CompanyLibraryStore store)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(store);
        var result = new ProjectAssetSourceReconciliationResult();
        ReconcileDocuments(
            profile.RegistrationCertificateDocuments,
            ResolveAbsoluteDocumentPath,
            (_, document, sourcePath) => store.StoreDocument(
                profile.OrganizationId,
                document.Category,
                sourcePath),
            context: "",
            result);
        ReconcileDocuments(
            profile.DesignLicenseDocuments,
            ResolveAbsoluteDocumentPath,
            (_, document, sourcePath) => store.StoreDocument(
                profile.OrganizationId,
                document.Category,
                sourcePath),
            context: "",
            result);
        return result;
    }

    private static void ReconcileDocuments(
        IEnumerable<ProjectFileReference> documents,
        Func<string, ProjectFileReference, string> resolveStoredPath,
        Func<string, ProjectFileReference, string, string> storeLinkedSource,
        string context,
        ProjectAssetSourceReconciliationResult result)
    {
        foreach (ProjectFileReference document in documents.Where(item => item is not null))
        {
            document.LinkedSourcePath = document.LinkedSourcePath?.Trim() ?? "";
            document.Version = Math.Max(1, document.Version);
            string storedPath = resolveStoredPath(context, document);
            string linkedPath = ResolveOptionalFullPath(document.LinkedSourcePath);
            bool hasLinkedSource = !string.IsNullOrWhiteSpace(document.LinkedSourcePath);
            string inspectionPath = hasLinkedSource ? linkedPath : storedPath;
            if (string.IsNullOrWhiteSpace(inspectionPath) || !File.Exists(inspectionPath))
            {
                if (document.IsAvailable)
                {
                    document.IsAvailable = false;
                    result.MissingDocumentCount++;
                }
                continue;
            }

            if (CanUseCachedDocumentInspection(
                    document,
                    inspectionPath,
                    storedPath,
                    hasLinkedSource))
            {
                if (!document.IsAvailable)
                {
                    document.IsAvailable = true;
                    result.RestoredDocumentCount++;
                }
                continue;
            }

            try
            {
                ProjectDocumentAssetInspection inspection = ProjectDocumentAssetInspector.Inspect(inspectionPath);
                bool wasAvailable = document.IsAvailable;
                bool sourceContentChanged = !string.Equals(
                    document.Sha256,
                    inspection.Sha256,
                    StringComparison.OrdinalIgnoreCase);
                bool ownedCopyMissing = string.IsNullOrWhiteSpace(storedPath) || !File.Exists(storedPath);
                bool changed = sourceContentChanged || ownedCopyMissing ||
                    !string.Equals(document.ContentType, inspection.ContentType, StringComparison.OrdinalIgnoreCase) ||
                    document.PageCount != inspection.PageCount ||
                    document.SizeBytes != inspection.SizeBytes;

                if (hasLinkedSource && (sourceContentChanged || ownedCopyMissing))
                {
                    string nextStoredPath = storeLinkedSource(context, document, inspectionPath);
                    if (!string.Equals(document.RelativePath, nextStoredPath, StringComparison.OrdinalIgnoreCase))
                    {
                        document.RelativePath = nextStoredPath;
                        changed = true;
                    }
                }

                DateTimeOffset? sourceWriteTime = hasLinkedSource
                    ? new DateTimeOffset(File.GetLastWriteTimeUtc(inspectionPath), TimeSpan.Zero)
                    : document.LinkedSourceLastWriteTimeUtc;
                if (document.LinkedSourceLastWriteTimeUtc != sourceWriteTime)
                {
                    document.LinkedSourceLastWriteTimeUtc = sourceWriteTime;
                    changed = true;
                }

                if (hasLinkedSource && string.IsNullOrWhiteSpace(document.OriginalFileName))
                {
                    document.OriginalFileName = Path.GetFileName(inspectionPath);
                    changed = true;
                }
                document.ContentType = inspection.ContentType;
                document.PageCount = inspection.PageCount;
                document.SizeBytes = inspection.SizeBytes;
                document.Sha256 = inspection.Sha256;
                document.IsAvailable = true;
                if (sourceContentChanged)
                    document.Version = Math.Max(1, document.Version) + 1;
                if (!wasAvailable)
                    result.RestoredDocumentCount++;
                if (changed)
                    result.UpdatedDocumentCount++;
            }
            catch (Exception exception) when (
                exception is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                result.ErrorCount++;
                if (document.IsAvailable)
                {
                    document.IsAvailable = false;
                    result.MissingDocumentCount++;
                }
            }
        }
    }

    private static void ReconcileVisualizations(
        ProjectWorkspace project,
        string projectPath,
        ProjectAssetSourceReconciliationResult result)
    {
        project.Visualizations.Normalize(project.ProjectId);
        foreach (ProjectVisualizationImage image in project.Visualizations.ImagesForProject(project.ProjectId))
        {
            string storedPath = ResolveProjectPath(projectPath, image.RelativePath);
            string linkedPath = ResolveOptionalFullPath(image.LinkedSourcePath);
            bool hasLinkedSource = !string.IsNullOrWhiteSpace(image.LinkedSourcePath);
            string inspectionPath = hasLinkedSource ? linkedPath : storedPath;
            if (string.IsNullOrWhiteSpace(inspectionPath) || !File.Exists(inspectionPath))
            {
                if (image.IsAvailable)
                {
                    image.IsAvailable = false;
                    result.MissingVisualizationCount++;
                }
                continue;
            }


            if (CanUseCachedVisualizationInspection(
                    image,
                    inspectionPath,
                    storedPath,
                    hasLinkedSource))
            {
                if (!image.IsAvailable)
                {
                    image.IsAvailable = true;
                    result.RestoredVisualizationCount++;
                }
                continue;
            }

            try
            {
                ProjectDocumentAssetInspection inspection = ProjectDocumentAssetInspector.Inspect(inspectionPath);
                if (inspection.PixelWidth <= 0 || inspection.PixelHeight <= 0)
                    throw new InvalidDataException("Visualization source must be a PNG or JPEG image.");

                bool wasAvailable = image.IsAvailable;
                bool sourceContentChanged = !string.Equals(
                    image.Sha256,
                    inspection.Sha256,
                    StringComparison.OrdinalIgnoreCase);
                bool ownedCopyMissing = string.IsNullOrWhiteSpace(storedPath) || !File.Exists(storedPath);
                bool changed = sourceContentChanged || ownedCopyMissing ||
                    !string.Equals(image.ContentType, inspection.ContentType, StringComparison.OrdinalIgnoreCase) ||
                    image.SizeBytes != inspection.SizeBytes ||
                    image.PixelWidth != inspection.PixelWidth ||
                    image.PixelHeight != inspection.PixelHeight;

                if (hasLinkedSource && (sourceContentChanged || ownedCopyMissing))
                {
                    string nextStoredPath = ProjectVisualizationFileStore.StoreInsideProject(
                        projectPath,
                        inspectionPath);
                    if (!string.Equals(image.RelativePath, nextStoredPath, StringComparison.OrdinalIgnoreCase))
                    {
                        image.RelativePath = nextStoredPath;
                        changed = true;
                    }
                }

                DateTimeOffset? sourceWriteTime = hasLinkedSource
                    ? new DateTimeOffset(File.GetLastWriteTimeUtc(inspectionPath), TimeSpan.Zero)
                    : image.LinkedSourceLastWriteTimeUtc;
                if (image.LinkedSourceLastWriteTimeUtc != sourceWriteTime)
                {
                    image.LinkedSourceLastWriteTimeUtc = sourceWriteTime;
                    changed = true;
                }

                if (hasLinkedSource && string.IsNullOrWhiteSpace(image.OriginalFileName))
                {
                    image.OriginalFileName = Path.GetFileName(inspectionPath);
                    changed = true;
                }
                image.ContentType = inspection.ContentType;
                image.SizeBytes = inspection.SizeBytes;
                image.PixelWidth = inspection.PixelWidth;
                image.PixelHeight = inspection.PixelHeight;
                image.Sha256 = inspection.Sha256;
                image.IsAvailable = true;
                if (sourceContentChanged)
                    image.Version = Math.Max(1, image.Version) + 1;
                if (!wasAvailable)
                    result.RestoredVisualizationCount++;
                if (changed)
                    result.UpdatedVisualizationCount++;
            }
            catch (Exception exception) when (
                exception is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                result.ErrorCount++;
                if (image.IsAvailable)
                {
                    image.IsAvailable = false;
                    result.MissingVisualizationCount++;
                }
            }
        }
    }

    private static bool CanUseCachedDocumentInspection(
        ProjectFileReference document,
        string inspectionPath,
        string storedPath,
        bool hasLinkedSource)
    {
        if (document.SizeBytes <= 0 ||
            document.PageCount <= 0 ||
            string.IsNullOrWhiteSpace(document.ContentType) ||
            string.IsNullOrWhiteSpace(document.Sha256))
        {
            return false;
        }

        return CanUseCachedFileInspection(
            inspectionPath,
            storedPath,
            hasLinkedSource,
            document.SizeBytes,
            document.LinkedSourceLastWriteTimeUtc);
    }

    private static bool CanUseCachedVisualizationInspection(
        ProjectVisualizationImage image,
        string inspectionPath,
        string storedPath,
        bool hasLinkedSource)
    {
        if (image.SizeBytes <= 0 ||
            image.PixelWidth <= 0 ||
            image.PixelHeight <= 0 ||
            string.IsNullOrWhiteSpace(image.ContentType) ||
            string.IsNullOrWhiteSpace(image.Sha256))
        {
            return false;
        }

        return CanUseCachedFileInspection(
            inspectionPath,
            storedPath,
            hasLinkedSource,
            image.SizeBytes,
            image.LinkedSourceLastWriteTimeUtc);
    }

    private static bool CanUseCachedFileInspection(
        string inspectionPath,
        string storedPath,
        bool hasLinkedSource,
        long knownSize,
        DateTimeOffset? knownLinkedWriteTimeUtc)
    {
        try
        {
            var file = new FileInfo(inspectionPath);
            if (!file.Exists || file.Length != knownSize)
                return false;

            if (!hasLinkedSource)
                return true;

            if (string.IsNullOrWhiteSpace(storedPath) ||
                !File.Exists(storedPath) ||
                knownLinkedWriteTimeUtc is null)
            {
                return false;
            }

            var currentWriteTimeUtc = new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero);
            return currentWriteTimeUtc == knownLinkedWriteTimeUtc.Value;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    private static string ResolveProjectDocumentPath(string projectPath, ProjectFileReference document) =>
        ResolveProjectPath(projectPath, document.RelativePath);

    private static string StoreProjectDocument(
        string projectPath,
        ProjectFileReference document,
        string sourcePath) => ProjectDocumentFileStore.StoreInsideProject(
            projectPath,
            document.Category,
            sourcePath);

    private static string ResolveAbsoluteDocumentPath(string _, ProjectFileReference document)
    {
        if (string.IsNullOrWhiteSpace(document.RelativePath) ||
            !Path.IsPathRooted(document.RelativePath))
        {
            return "";
        }
        return ResolveOptionalFullPath(document.RelativePath);
    }

    private static string ResolveProjectPath(string projectPath, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "";
        try
        {
            return Path.IsPathRooted(path)
                ? Path.GetFullPath(path)
                : ProjectWorkspacePaths.ResolveInsideProject(projectPath, path);
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or UnauthorizedAccessException or
                ArgumentException or NotSupportedException)
        {
            return "";
        }
    }

    private static string ResolveOptionalFullPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "";
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                ArgumentException or NotSupportedException)
        {
            return "";
        }
    }
}
