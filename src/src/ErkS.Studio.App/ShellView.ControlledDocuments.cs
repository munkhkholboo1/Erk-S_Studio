using System.IO;
using ErkS.Platform.Core;
using ErkS.Platform.Pdf;

namespace ErkS.Studio;

internal enum ControlledDocumentMergeAction
{
    None,
    UploadLocal,
    DownloadCloud,
    Conflict,
}

internal sealed record ControlledDocumentMergeDecision(
    ControlledDocumentMergeAction Action,
    string Message);

internal static class ControlledDocumentMergePolicy
{
    public static ControlledDocumentMergeDecision Decide(
        PlanningTaskInformation local,
        StudioCloudControlledDocument cloud)
    {
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(cloud);

        List<ProjectFileReference> localFiles = local.Documents
            .Where(IsApprovedAtd)
            .Where(item => item.IsAvailable)
            .ToList();
        HashSet<string> localHashes = localFiles
            .Select(item => NormalizeHash(item.Sha256))
            .Where(item => item.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        HashSet<string> cloudHashes = cloud.CurrentFiles
            .Select(item => NormalizeHash(item.Sha256))
            .Where(item => item.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool sameSet = localFiles.Count == cloud.CurrentFiles.Count &&
            localHashes.SetEquals(cloudHashes);
        bool explicitPending = local.DocumentCloudSyncStatus.Equals(
                ProjectDocumentCloudSyncStatuses.PendingUpload,
                StringComparison.OrdinalIgnoreCase) ||
            localFiles.Any(item => item.CloudSyncStatus.Equals(
                ProjectDocumentCloudSyncStatuses.PendingUpload,
                StringComparison.OrdinalIgnoreCase));
        bool explicitConflict = local.DocumentCloudSyncStatus.Equals(
                ProjectDocumentCloudSyncStatuses.Conflict,
                StringComparison.OrdinalIgnoreCase) ||
            localFiles.Any(item => item.CloudSyncStatus.Equals(
                ProjectDocumentCloudSyncStatuses.Conflict,
                StringComparison.OrdinalIgnoreCase));
        bool legacyLocalOnly = localFiles.Count > 0 &&
            localFiles.All(item => string.IsNullOrWhiteSpace(item.ServerFileRevisionId));

        if (explicitConflict)
        {
            return new ControlledDocumentMergeDecision(
                ControlledDocumentMergeAction.Conflict,
                "АТД-ийн локал болон Cloud хувилбарын зөрчлийг шийдвэрлээгүй байна.");
        }

        if (sameSet)
        {
            return new ControlledDocumentMergeDecision(
                ControlledDocumentMergeAction.None,
                "АТД-ийн current file set өөрчлөгдөөгүй.");
        }

        if (explicitPending || legacyLocalOnly)
        {
            if (local.ServerDocumentVersion > 0 &&
                local.ServerDocumentVersion != cloud.Version)
            {
                return new ControlledDocumentMergeDecision(
                    ControlledDocumentMergeAction.Conflict,
                    "АТД-г локалд зассанаас хойш Cloud хувилбар өөрчлөгдсөн байна.");
            }
            if (local.ServerDocumentVersion == 0 && cloud.CurrentFiles.Count > 0)
            {
                return new ControlledDocumentMergeDecision(
                    ControlledDocumentMergeAction.Conflict,
                    "Локал болон Cloud-д тус тусдаа АТД байна. Аль нэгийг нь чимээгүй дарж болохгүй.");
            }

            return new ControlledDocumentMergeDecision(
                ControlledDocumentMergeAction.UploadLocal,
                "Локал АТД Cloud sync хүлээж байна.");
        }

        return new ControlledDocumentMergeDecision(
            ControlledDocumentMergeAction.DownloadCloud,
            "Cloud ERA-д АТД-ийн шинэ current file set байна.");
    }

    private static bool IsApprovedAtd(ProjectFileReference document) =>
        document.Category.Equals(
            ProjectDocumentCategories.ApprovedPlanningTask,
            StringComparison.OrdinalIgnoreCase);

    private static string NormalizeHash(string? value) => (value ?? "").Trim().ToLowerInvariant();
}

internal sealed partial class ShellView
{
    private const string IssuedAtdRequirementKey = "ISSUED-ATD-DOCUMENT";

    private async Task<ControlledDocumentSyncResult> ReconcileAtdControlledDocumentAsync(
        string projectId,
        string projectConcurrencyToken,
        bool allowUpload)
    {
        _ = projectConcurrencyToken;
        _ = allowUpload;
        IReadOnlyList<StudioCloudControlledDocument> documents =
            await account.ListControlledDocumentsAsync(projectId);
        StudioCloudControlledDocument? cloudDocument = documents.FirstOrDefault(item =>
            item.RequirementKeys.Contains(
                IssuedAtdRequirementKey,
                StringComparer.OrdinalIgnoreCase));
        PlanningTaskInformation planningTask = state.Project.Foundation.PlanningTask;
        string ownerEmail = CurrentCloudOwnerEmail();
        foreach (ProjectFileReference local in ApprovedAtdDocuments(planningTask.Documents))
        {
            if (string.IsNullOrWhiteSpace(local.CloudOwnerEmail))
            {
                local.CloudOwnerEmail = ownerEmail;
                if (string.IsNullOrWhiteSpace(local.CloudContributionId))
                    local.CloudContributionId = Guid.NewGuid().ToString("N");
                local.CloudSyncStatus = ProjectDocumentCloudSyncStatuses.PendingUpload;
            }

            StudioCloudFile? legacyCloudFile = cloudDocument?.CurrentFiles.FirstOrDefault(file =>
                !string.IsNullOrWhiteSpace(local.Sha256) &&
                file.Sha256.Equals(local.Sha256, StringComparison.OrdinalIgnoreCase) &&
                (file.UploadedBy ?? "").Equals(local.CloudOwnerEmail, StringComparison.OrdinalIgnoreCase));
            if (legacyCloudFile is not null)
            {
                local.ServerDocumentId = cloudDocument!.DocumentId;
                local.ServerFileId = legacyCloudFile.FileId;
                local.ServerFileRevisionId = legacyCloudFile.FileRevisionId;
                local.ServerDocumentVersion = cloudDocument.Version;
                local.CloudSyncStatus = ProjectDocumentCloudSyncStatuses.Synced;
            }
        }

        if (cloudDocument is not null)
        {
            planningTask.ServerDocumentId = cloudDocument.DocumentId;
            planningTask.ServerDocumentVersion = cloudDocument.Version;
        }
        planningTask.DocumentCloudSyncStatus = ApprovedAtdDocuments(planningTask.Documents).Any(document =>
            IsDocumentOwnedBy(document, ownerEmail) &&
            document.CloudSyncStatus.Equals(ProjectDocumentCloudSyncStatuses.PendingUpload, StringComparison.OrdinalIgnoreCase))
                ? ProjectDocumentCloudSyncStatuses.PendingUpload
                : ProjectDocumentCloudSyncStatuses.Synced;
        return new ControlledDocumentSyncResult(
            false,
            false,
            "АТД эх файлыг Cloud ERA-аар солихгүй. Энэ хэрэглэгчийн АТД зөвхөн өөрийн album component хэлбэрээр merge хийгдэнэ.");
    }

    private async Task DownloadAtdCurrentSetAsync(
        PlanningTaskInformation planningTask,
        StudioCloudControlledDocument cloudDocument)
    {
        string projectPath = state.ProjectPath
            ?? throw new InvalidOperationException("Project path is unavailable.");
        string cloudFolder = Path.Combine(
            ProjectWorkspacePaths.GetProjectFolder(projectPath),
            "foundation",
            "documents",
            ProjectDocumentCategories.ApprovedPlanningTask,
            "cloud");
        Directory.CreateDirectory(cloudFolder);

        var downloaded = new List<ProjectFileReference>();
        foreach (StudioCloudFile file in cloudDocument.CurrentFiles)
        {
            string extension = SafeControlledDocumentExtension(file.FileName, file.ContentType);
            string stableName = !string.IsNullOrWhiteSpace(file.FileRevisionId)
                ? file.FileRevisionId
                : file.FileId;
            string targetPath = Path.Combine(cloudFolder, stableName + extension);
            if (!File.Exists(targetPath) ||
                !ProjectDocumentFileStore.ComputeSha256(targetPath).Equals(
                    file.Sha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                await account.DownloadControlledFileAsync(file, targetPath);
            }

            ProjectDocumentAssetInspection inspection = ProjectDocumentAssetInspector.Inspect(targetPath);
            downloaded.Add(new ProjectFileReference
            {
                Category = ProjectDocumentCategories.ApprovedPlanningTask,
                Title = "Батлагдсан архитектур төлөвлөлтийн даалгавар",
                RelativePath = ProjectWorkspacePaths.ToRelativePath(projectPath, targetPath),
                OriginalFileName = file.FileName,
                IsAvailable = true,
                ContentType = inspection.ContentType,
                SizeBytes = inspection.SizeBytes,
                PageCount = inspection.PageCount,
                ServerDocumentId = cloudDocument.DocumentId,
                ServerFileId = file.FileId,
                ServerFileRevisionId = file.FileRevisionId,
                ServerDocumentVersion = cloudDocument.Version,
                CloudSyncStatus = ProjectDocumentCloudSyncStatuses.Synced,
                Sha256 = inspection.Sha256,
                Version = Math.Max(1, cloudDocument.Version),
                AddedAtUtc = file.UploadedAtUtc,
            });
        }

        List<ProjectFileReference> documents = planningTask.Documents;
        documents.RemoveAll(item => item.Category.Equals(
            ProjectDocumentCategories.ApprovedPlanningTask,
            StringComparison.OrdinalIgnoreCase));
        documents.AddRange(downloaded);
        planningTask.ServerDocumentId = cloudDocument.DocumentId;
        planningTask.ServerDocumentVersion = cloudDocument.Version;
        planningTask.DocumentCloudSyncStatus = ProjectDocumentCloudSyncStatuses.Synced;
        CleanupStaleCloudControlledFiles(cloudFolder, downloaded, projectPath);
    }

    private static void MarkAtdDocumentsSynced(
        PlanningTaskInformation planningTask,
        StudioCloudControlledDocument cloudDocument)
    {
        planningTask.ServerDocumentId = cloudDocument.DocumentId;
        planningTask.ServerDocumentVersion = cloudDocument.Version;
        planningTask.DocumentCloudSyncStatus = ProjectDocumentCloudSyncStatuses.Synced;
        foreach (ProjectFileReference local in ApprovedAtdDocuments(planningTask.Documents))
        {
            StudioCloudFile? cloudFile = cloudDocument.CurrentFiles.FirstOrDefault(item =>
                item.Sha256.Equals(local.Sha256, StringComparison.OrdinalIgnoreCase));
            if (cloudFile is null)
                continue;
            local.ServerDocumentId = cloudDocument.DocumentId;
            local.ServerFileId = cloudFile.FileId;
            local.ServerFileRevisionId = cloudFile.FileRevisionId;
            local.ServerDocumentVersion = cloudDocument.Version;
            local.CloudSyncStatus = ProjectDocumentCloudSyncStatuses.Synced;
        }
    }

    private static void MarkAtdDocumentConflict(
        PlanningTaskInformation planningTask,
        StudioCloudControlledDocument cloudDocument)
    {
        planningTask.ServerDocumentId = cloudDocument.DocumentId;
        planningTask.DocumentCloudSyncStatus = ProjectDocumentCloudSyncStatuses.Conflict;
        foreach (ProjectFileReference local in ApprovedAtdDocuments(planningTask.Documents))
            local.CloudSyncStatus = ProjectDocumentCloudSyncStatuses.Conflict;
    }

    private static string SafeControlledDocumentExtension(string fileName, string contentType)
    {
        string extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (extension == ".jpeg")
            extension = ".jpg";
        if (extension is ".pdf" or ".png" or ".jpg")
            return extension;
        return contentType.ToLowerInvariant() switch
        {
            "application/pdf" => ".pdf",
            "image/png" => ".png",
            "image/jpeg" => ".jpg",
            _ => throw new InvalidDataException("Cloud controlled document file type is unsupported."),
        };
    }

    private static void CleanupStaleCloudControlledFiles(
        string cloudFolder,
        IReadOnlyList<ProjectFileReference> current,
        string projectPath)
    {
        HashSet<string> keep = current
            .Select(item => ProjectWorkspacePaths.ResolveInsideProject(projectPath, item.RelativePath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string path in Directory.EnumerateFiles(cloudFolder))
        {
            if (keep.Contains(Path.GetFullPath(path)))
                continue;
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private sealed record ControlledDocumentSyncResult(
        bool Uploaded,
        bool HasPendingOrConflict,
        string Message);
}
