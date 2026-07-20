using System;
using System.IO;
using System.Security.Cryptography;

namespace ErkS.Studio;

internal static class CloudAlbumCacheMaintenance
{
    public static bool IsHealthy(string cacheRoot, string? currentPdfPath, string expectedSha256)
    {
        if (string.IsNullOrWhiteSpace(cacheRoot) ||
            string.IsNullOrWhiteSpace(currentPdfPath) ||
            !File.Exists(currentPdfPath))
        {
            return false;
        }

        string root = Path.GetFullPath(cacheRoot);
        string candidate = Path.GetFullPath(currentPdfPath);
        if (!ErkS.Platform.Core.ProjectWorkspacePaths.IsInside(root, candidate))
            return false;

        string expected = (expectedSha256 ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(expected))
            return true;

        using FileStream stream = File.OpenRead(candidate);
        string actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        return actual.Equals(expected, StringComparison.OrdinalIgnoreCase);
    }

    public static int Cleanup(string cacheRoot, string? keepPdfPath)
    {
        if (string.IsNullOrWhiteSpace(cacheRoot) || !Directory.Exists(cacheRoot))
            return 0;

        string root = Path.GetFullPath(cacheRoot);
        string keep = string.IsNullOrWhiteSpace(keepPdfPath)
            ? ""
            : Path.GetFullPath(keepPdfPath);
        int deleted = 0;
        foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly))
        {
            string fullPath = Path.GetFullPath(file);
            if (!ErkS.Platform.Core.ProjectWorkspacePaths.IsInside(root, fullPath) ||
                (!string.IsNullOrWhiteSpace(keep) &&
                 fullPath.Equals(keep, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            string name = Path.GetFileName(fullPath);
            bool ownedCacheFile = Path.GetExtension(fullPath).Equals(".pdf", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith(".download", StringComparison.OrdinalIgnoreCase) ||
                name.Contains(".tmp", StringComparison.OrdinalIgnoreCase);
            if (!ownedCacheFile)
                continue;

            try
            {
                File.Delete(fullPath);
                deleted++;
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return deleted;
    }
}
