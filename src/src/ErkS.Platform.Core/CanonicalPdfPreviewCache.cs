using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace ErkS.Platform.Core;

/// <summary>
/// Copies canonical PDFs to a versioned viewer cache so browser/PDF preview
/// processes never hold the replaceable album output open.
/// </summary>
public sealed class CanonicalPdfPreviewCache
{
    private readonly string cacheRoot;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> copyGates =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Lazy<Task> initialCleanup;

    public CanonicalPdfPreviewCache(string cacheRoot)
    {
        if (string.IsNullOrWhiteSpace(cacheRoot))
            throw new ArgumentException("PDF preview cache root is required.", nameof(cacheRoot));

        this.cacheRoot = Path.GetFullPath(cacheRoot);
        initialCleanup = new Lazy<Task>(
            () => Task.Run(CleanupStalePreviews),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public async Task<string> GetPreviewPathAsync(
        string pdfPath,
        CancellationToken cancellationToken = default)
    {
        string sourcePath = Path.GetFullPath(pdfPath);
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("PDF preview source was not found.", sourcePath);

        Directory.CreateDirectory(cacheRoot);
        await initialCleanup.Value.WaitAsync(cancellationToken).ConfigureAwait(false);

        for (int attempt = 0; attempt < 3; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var before = new FileInfo(sourcePath);
            string pathKey = Convert.ToHexString(SHA256.HashData(
                    Encoding.UTF8.GetBytes(sourcePath.ToUpperInvariant())))
                .ToLowerInvariant()[..20];
            string versionKey = $"{before.LastWriteTimeUtc.Ticks:x16}-{before.Length:x16}";
            string previewPath = Path.Combine(cacheRoot, $"{pathKey}-{versionKey}.pdf");
            if (File.Exists(previewPath))
                return previewPath;

            SemaphoreSlim copyGate = copyGates.GetOrAdd(
                previewPath,
                static _ => new SemaphoreSlim(1, 1));
            await copyGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (File.Exists(previewPath))
                    return previewPath;

                string temporaryPath = previewPath + ".tmp-" + Guid.NewGuid().ToString("N");
                try
                {
                    await using (var source = new FileStream(
                                     sourcePath,
                                     FileMode.Open,
                                     FileAccess.Read,
                                     FileShare.ReadWrite | FileShare.Delete,
                                     1024 * 1024,
                                     FileOptions.Asynchronous | FileOptions.SequentialScan))
                    await using (var target = new FileStream(
                                     temporaryPath,
                                     FileMode.CreateNew,
                                     FileAccess.Write,
                                     FileShare.None,
                                     1024 * 1024,
                                     FileOptions.Asynchronous | FileOptions.SequentialScan))
                    {
                        await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
                    }

                    var after = new FileInfo(sourcePath);
                    if (before.Length != after.Length ||
                        before.LastWriteTimeUtc != after.LastWriteTimeUtc)
                    {
                        continue;
                    }

                    File.Move(temporaryPath, previewPath, overwrite: true);
                    return previewPath;
                }
                finally
                {
                    TryDelete(temporaryPath);
                }
            }
            finally
            {
                copyGate.Release();
            }
        }

        throw new IOException("PDF changed while its preview was being prepared. Please retry.");
    }

    private void CleanupStalePreviews()
    {
        try
        {
            DateTime staleBefore = DateTime.UtcNow.AddDays(-7);
            foreach (string file in Directory.EnumerateFiles(
                         cacheRoot,
                         "*",
                         SearchOption.TopDirectoryOnly))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(file) < staleBefore)
                        File.Delete(file);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
        catch (DirectoryNotFoundException)
        {
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
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
