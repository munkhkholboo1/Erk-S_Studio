using System.Security.Cryptography;

namespace ErkS.Platform.Core;

/// <summary>
/// Copies approved foundation documents into their owning local store. Native
/// design files are intentionally outside this boundary.
/// </summary>
public static class ProjectDocumentFileStore
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf",
        ".png",
        ".jpg",
        ".jpeg",
    };

    public static string StoreInsideProject(
        string projectPath,
        string category,
        string sourcePath)
    {
        string projectFolder = ProjectWorkspacePaths.GetProjectFolder(projectPath);
        string targetFolder = Path.Combine(
            projectFolder,
            "foundation",
            "documents",
            SafeSegment(category));
        string targetPath = CopyToOwnedFolder(sourcePath, targetFolder);
        return ProjectWorkspacePaths.ToRelativePath(projectPath, targetPath);
    }

    public static string StoreInsideFolder(
        string ownerFolder,
        string category,
        string sourcePath)
    {
        string targetFolder = Path.Combine(
            Path.GetFullPath(ownerFolder),
            "documents",
            SafeSegment(category));
        return CopyToOwnedFolder(sourcePath, targetFolder);
    }

    public static string ComputeSha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string CopyToOwnedFolder(string sourcePath, string targetFolder)
    {
        string fullSourcePath = Path.GetFullPath(sourcePath);
        if (!File.Exists(fullSourcePath))
            throw new FileNotFoundException("Document file was not found.", fullSourcePath);

        string extension = Path.GetExtension(fullSourcePath).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension))
            throw new InvalidDataException("Only PDF, PNG and JPEG documents are supported.");
        if (extension == ".jpeg")
            extension = ".jpg";

        Directory.CreateDirectory(targetFolder);
        string hash = ComputeSha256(fullSourcePath);
        string targetPath = Path.Combine(targetFolder, hash + extension);
        if (!File.Exists(targetPath))
            File.Copy(fullSourcePath, targetPath, overwrite: false);
        return targetPath;
    }

    private static string SafeSegment(string value)
    {
        string safe = new((value ?? "")
            .Where(character => char.IsLetterOrDigit(character) || character is '-' or '_')
            .Take(80)
            .ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "document" : safe;
    }
}

/// <summary>
/// Keeps content-addressed storage names out of user-facing document and asset
/// lists while retaining those names on disk for integrity and deduplication.
/// </summary>
public static class ProjectAssetDisplayName
{
    public static string ForDocument(ProjectFileReference document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return Resolve(
            document.OriginalFileName,
            document.RelativePath,
            string.IsNullOrWhiteSpace(document.Title) ? "Баримт" : document.Title);
    }

    public static string Resolve(
        string? originalFileName,
        string? storedPath,
        string fallbackTitle)
    {
        string originalName = FileName(originalFileName);
        if (!string.IsNullOrWhiteSpace(originalName) && !IsContentAddressed(originalName))
            return originalName;

        string storedName = FileName(storedPath);
        if (!string.IsNullOrWhiteSpace(storedName) && !IsContentAddressed(storedName))
            return storedName;

        string extension = Path.GetExtension(originalName);
        if (string.IsNullOrWhiteSpace(extension))
            extension = Path.GetExtension(storedName);
        string title = string.IsNullOrWhiteSpace(fallbackTitle) ? "Файл" : fallbackTitle.Trim();
        return string.IsNullOrWhiteSpace(extension) || Path.HasExtension(title)
            ? title
            : title + extension.ToLowerInvariant();
    }

    public static bool IsContentAddressed(string? fileName)
    {
        string stem = Path.GetFileNameWithoutExtension(FileName(fileName));
        return stem.Length == 64 && stem.All(Uri.IsHexDigit);
    }

    private static string FileName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "";
        return Path.GetFileName(path.Trim());
    }
}
