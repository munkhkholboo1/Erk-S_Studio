using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

public sealed class CloudAlbumCacheMaintenanceTests
{
    [Fact]
    public void CleanupKeepsOnlyCurrentPdfAndRemovesInterruptedDownloads()
    {
        string root = Path.Combine(Path.GetTempPath(), "erks-cloud-cache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string current = Path.Combine(root, "album-R3.pdf");
        string old = Path.Combine(root, "album-R2.pdf");
        string interrupted = Path.Combine(root, "album-R4.pdf.download");
        string unrelated = Path.Combine(root, "notes.txt");
        File.WriteAllBytes(current, [1, 2, 3]);
        File.WriteAllBytes(old, [4, 5, 6]);
        File.WriteAllBytes(interrupted, [7, 8]);
        File.WriteAllText(unrelated, "keep");

        try
        {
            int deleted = CloudAlbumCacheMaintenance.Cleanup(root, current);

            Assert.Equal(2, deleted);
            Assert.True(File.Exists(current));
            Assert.False(File.Exists(old));
            Assert.False(File.Exists(interrupted));
            Assert.True(File.Exists(unrelated));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void HealthCheckDetectsMissingOrCorruptCachedPdf()
    {
        string root = Path.Combine(Path.GetTempPath(), "erks-cloud-cache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string current = Path.Combine(root, "album-R3.pdf");
        File.WriteAllBytes(current, [1, 2, 3]);
        string sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData([1, 2, 3]))
            .ToLowerInvariant();

        try
        {
            Assert.True(CloudAlbumCacheMaintenance.IsHealthy(root, current, sha256));
            Assert.False(CloudAlbumCacheMaintenance.IsHealthy(root, current, new string('f', 64)));
            File.Delete(current);
            Assert.False(CloudAlbumCacheMaintenance.IsHealthy(root, current, sha256));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CleanupWithoutCurrentRevisionRemovesEveryOwnedCacheArtifact()
    {
        string root = Path.Combine(Path.GetTempPath(), "erks-cloud-cache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string oldPdf = Path.Combine(root, "album-R2.pdf");
        string interrupted = Path.Combine(root, "album-R3.pdf.download");
        string temporary = Path.Combine(root, "album-R4.pdf.tmp-123");
        string unrelated = Path.Combine(root, "notes.txt");
        File.WriteAllBytes(oldPdf, [1]);
        File.WriteAllBytes(interrupted, [2]);
        File.WriteAllBytes(temporary, [3]);
        File.WriteAllText(unrelated, "keep");

        try
        {
            int deleted = CloudAlbumCacheMaintenance.Cleanup(root, keepPdfPath: null);

            Assert.Equal(3, deleted);
            Assert.False(File.Exists(oldPdf));
            Assert.False(File.Exists(interrupted));
            Assert.False(File.Exists(temporary));
            Assert.True(File.Exists(unrelated));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
