using ErkS.Platform.Core;

namespace ErkS.Platform.Pdf;

public sealed class ProjectAssetSourceReconciliationResult
{
    private readonly HashSet<string> changedDocumentCategories =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> changedVisualizationIds =
        new(StringComparer.OrdinalIgnoreCase);

    public int UpdatedDocumentCount { get; internal set; }
    public int MissingDocumentCount { get; internal set; }
    public int RestoredDocumentCount { get; internal set; }
    public int UpdatedVisualizationCount { get; internal set; }
    public int MissingVisualizationCount { get; internal set; }
    public int RestoredVisualizationCount { get; internal set; }

    /// <summary>
    /// Assets whose watched original is not on this machine while the project's
    /// own copy is intact. They stay in the album; only the link is broken.
    /// </summary>
    public int BrokenLinkCount { get; internal set; }
    public int ErrorCount { get; internal set; }
    public IReadOnlyCollection<string> ChangedDocumentCategories =>
        changedDocumentCategories;
    public IReadOnlyCollection<string> ChangedVisualizationIds =>
        changedVisualizationIds;

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
        changedDocumentCategories.UnionWith(other.changedDocumentCategories);
        changedVisualizationIds.UnionWith(other.changedVisualizationIds);
    }

    internal void RecordDocumentChange(ProjectFileReference document) =>
        changedDocumentCategories.Add(document.Category?.Trim() ?? "");

    internal void RecordVisualizationChange(ProjectVisualizationImage image) =>
        changedVisualizationIds.Add(image.Id?.Trim() ?? "");
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
        string projectPath) =>
        ReconcileProject(
            project,
            projectPath,
            documentScope: null,
            visualizationScope: null);

    public static ProjectAssetSourceReconciliationResult ReconcileProject(
        ProjectWorkspace project,
        string projectPath,
        Func<ProjectFileReference, bool>? documentScope,
        Func<ProjectVisualizationImage, bool>? visualizationScope)
    {
        ArgumentNullException.ThrowIfNull(project);
        string fullProjectPath = Path.GetFullPath(projectPath);
        var result = new ProjectAssetSourceReconciliationResult();
        documentScope ??= static _ => true;
        visualizationScope ??= static _ => true;

        ReconcileDocuments(
            project.Foundation.InitiationBasis.Documents,
            ResolveProjectDocumentPath,
            StoreProjectDocument,
            fullProjectPath,
            result,
            documentScope);
        ReconcileDocuments(
            project.Foundation.PlanningTask.Documents,
            ResolveProjectDocumentPath,
            StoreProjectDocument,
            fullProjectPath,
            result,
            documentScope);
        ReconcileDocuments(
            project.Foundation.DesignCompany.OrganizationSnapshot.RegistrationCertificateDocuments,
            ResolveProjectDocumentPath,
            StoreProjectDocument,
            fullProjectPath,
            result,
            documentScope);
        ReconcileDocuments(
            project.Foundation.DesignCompany.OrganizationSnapshot.DesignLicenseDocuments,
            ResolveProjectDocumentPath,
            StoreProjectDocument,
            fullProjectPath,
            result,
            documentScope);
        ReconcileVisualizations(
            project,
            fullProjectPath,
            result,
            visualizationScope);
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
            result,
            scope: null);
        ReconcileDocuments(
            profile.DesignLicenseDocuments,
            ResolveAbsoluteDocumentPath,
            (_, document, sourcePath) => store.StoreDocument(
                profile.OrganizationId,
                document.Category,
                sourcePath),
            context: "",
            result,
            scope: null);
        return result;
    }

    private static void ReconcileDocuments(
        IEnumerable<ProjectFileReference> documents,
        Func<string, ProjectFileReference, string> resolveStoredPath,
        Func<string, ProjectFileReference, string, string> storeLinkedSource,
        string context,
        ProjectAssetSourceReconciliationResult result,
        Func<ProjectFileReference, bool>? scope)
    {
        scope ??= static _ => true;
        foreach (ProjectFileReference document in documents
                     .Where(item => item is not null)
                     .Where(scope))
        {
            document.LinkedSourcePath = document.LinkedSourcePath?.Trim() ?? "";
            document.Version = Math.Max(1, document.Version);
            string storedPath = resolveStoredPath(context, document);
            string linkedPath = ResolveOptionalFullPath(document.LinkedSourcePath);
            bool hasLinkedSource = !string.IsNullOrWhiteSpace(document.LinkedSourcePath);
            // Documents deliberately do NOT follow the visualization rule below.
            // An approved planning task whose source is gone stops appearing in
            // the album even though the owned copy survives on disk, and three
            // tests hold that line. Whether an official document should behave
            // like a render is a decision above this layer, not a defect here.
            if (document.LinkedSourceMissing && FileExists(linkedPath))
            {
                document.LinkedSourceMissing = false;
                result.RecordDocumentChange(document);
            }

            string inspectionPath = hasLinkedSource ? linkedPath : storedPath;
            if (string.IsNullOrWhiteSpace(inspectionPath) || !File.Exists(inspectionPath))
            {
                if (document.IsAvailable)
                {
                    document.IsAvailable = false;
                    result.MissingDocumentCount++;
                    result.RecordDocumentChange(document);
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
                    result.RecordDocumentChange(document);
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
                {
                    result.RestoredDocumentCount++;
                    result.RecordDocumentChange(document);
                }
                if (changed)
                {
                    result.UpdatedDocumentCount++;
                    result.RecordDocumentChange(document);
                }
            }
            catch (Exception exception) when (
                exception is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                result.ErrorCount++;
                if (document.IsAvailable)
                {
                    document.IsAvailable = false;
                    result.MissingDocumentCount++;
                    result.RecordDocumentChange(document);
                }
            }
        }
    }

    private static bool FileExists(string? path) =>
        !string.IsNullOrWhiteSpace(path) && File.Exists(path);

    private static void ReconcileVisualizations(
        ProjectWorkspace project,
        string projectPath,
        ProjectAssetSourceReconciliationResult result,
        Func<ProjectVisualizationImage, bool>? scope)
    {
        scope ??= static _ => true;
        project.Visualizations.Normalize(project.ProjectId);
        foreach (ProjectVisualizationImage image in project.Visualizations
                     .ImagesForProject(project.ProjectId)
                     .Where(scope))
        {
            string storedPath = ResolveProjectPath(projectPath, image.RelativePath);
            string linkedPath = ResolveOptionalFullPath(image.LinkedSourcePath);
            bool hasLinkedSource = !string.IsNullOrWhiteSpace(image.LinkedSourcePath);
            if (hasLinkedSource && !FileExists(linkedPath) && FileExists(storedPath))
            {
                hasLinkedSource = false;
                if (!image.LinkedSourceMissing)
                {
                    image.LinkedSourceMissing = true;
                    result.BrokenLinkCount++;
                    result.RecordVisualizationChange(image);
                }
            }
            else if (image.LinkedSourceMissing && FileExists(linkedPath))
            {
                image.LinkedSourceMissing = false;
                result.RecordVisualizationChange(image);
            }

            string inspectionPath = hasLinkedSource ? linkedPath : storedPath;
            if (string.IsNullOrWhiteSpace(inspectionPath) || !File.Exists(inspectionPath))
            {
                if (image.IsAvailable)
                {
                    image.IsAvailable = false;
                    result.MissingVisualizationCount++;
                    result.RecordVisualizationChange(image);
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
                    result.RecordVisualizationChange(image);
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
                {
                    result.RestoredVisualizationCount++;
                    result.RecordVisualizationChange(image);
                }
                if (changed)
                {
                    result.UpdatedVisualizationCount++;
                    result.RecordVisualizationChange(image);
                }
            }
            catch (Exception exception) when (
                exception is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                result.ErrorCount++;
                if (image.IsAvailable)
                {
                    image.IsAvailable = false;
                    result.MissingVisualizationCount++;
                    result.RecordVisualizationChange(image);
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
