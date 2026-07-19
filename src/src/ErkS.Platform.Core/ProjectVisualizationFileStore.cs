using System.Security.Cryptography;

namespace ErkS.Platform.Core;

/// <summary>Copies verified visualization images into the project-owned source store.</summary>
public static class ProjectVisualizationFileStore
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".jpg",
        ".jpeg",
    };

    public static string StoreInsideProject(string projectPath, string sourcePath)
    {
        string fullSourcePath = Path.GetFullPath(sourcePath);
        if (!File.Exists(fullSourcePath))
            throw new FileNotFoundException("Visualization image was not found.", fullSourcePath);

        string extension = Path.GetExtension(fullSourcePath).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension))
            throw new InvalidDataException("Only PNG and JPEG visualization images are supported.");
        if (extension == ".jpeg")
            extension = ".jpg";

        string targetFolder = Path.Combine(
            ProjectWorkspacePaths.GetProjectFolder(projectPath),
            "sources",
            "visualizations",
            "images");
        Directory.CreateDirectory(targetFolder);
        string targetPath = Path.Combine(targetFolder, ComputeSha256(fullSourcePath) + extension);
        if (!File.Exists(targetPath))
            File.Copy(fullSourcePath, targetPath, overwrite: false);
        return ProjectWorkspacePaths.ToRelativePath(projectPath, targetPath);
    }

    public static string ComputeSha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
