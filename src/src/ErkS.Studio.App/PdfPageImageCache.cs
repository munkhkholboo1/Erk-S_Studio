using System.IO;
using System.Windows.Media.Imaging;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace ErkS.Studio;

/// <summary>Renders album PDF pages once and reuses the frozen WPF images.</summary>
internal sealed class PdfPageImageCache
{
    private readonly SemaphoreSlim renderGate = new(1, 1);
    private readonly Dictionary<PageImageKey, BitmapSource> images = [];
    private string? documentKey;
    private PdfDocument? document;

    public async Task<BitmapSource?> GetPageAsync(
        string pdfPath,
        int pageNumber,
        uint pixelWidth,
        CancellationToken cancellationToken)
    {
        if (pageNumber < 1 || pixelWidth == 0 || !File.Exists(pdfPath))
        {
            return null;
        }

        var fullPath = await PdfPreviewFileCache.GetPreviewPathAsync(pdfPath, cancellationToken);
        var file = new FileInfo(fullPath);
        var currentDocumentKey = $"{fullPath}|{file.Length}|{file.LastWriteTimeUtc.Ticks}";
        var imageKey = new PageImageKey(currentDocumentKey, pageNumber, pixelWidth);
        lock (images)
        {
            if (images.TryGetValue(imageKey, out var cached))
            {
                return cached;
            }
        }

        await renderGate.WaitAsync(cancellationToken);
        try
        {
            lock (images)
            {
                if (images.TryGetValue(imageKey, out var cached))
                {
                    return cached;
                }
            }

            if (!string.Equals(documentKey, currentDocumentKey, StringComparison.Ordinal))
            {
                var storageFile = await StorageFile.GetFileFromPathAsync(fullPath);
                cancellationToken.ThrowIfCancellationRequested();
                document = await PdfDocument.LoadFromFileAsync(storageFile);
                documentKey = currentDocumentKey;
                lock (images)
                {
                    images.Clear();
                }
            }

            if (document is null || pageNumber > document.PageCount)
            {
                return null;
            }

            using var page = document.GetPage((uint)(pageNumber - 1));
            var aspectRatio = page.Size.Width <= 0 ? 1.0 : page.Size.Height / page.Size.Width;
            var pixelHeight = Math.Max(1u, (uint)Math.Round(pixelWidth * aspectRatio));
            using var output = new InMemoryRandomAccessStream();
            var options = new PdfPageRenderOptions
            {
                DestinationWidth = pixelWidth,
                DestinationHeight = pixelHeight,
            };
            await page.RenderToStreamAsync(output, options);
            cancellationToken.ThrowIfCancellationRequested();

            output.Seek(0);
            using var reader = new DataReader(output.GetInputStreamAt(0));
            var byteCount = checked((uint)output.Size);
            await reader.LoadAsync(byteCount);
            cancellationToken.ThrowIfCancellationRequested();
            var bytes = new byte[byteCount];
            reader.ReadBytes(bytes);

            using var stream = new MemoryStream(bytes, writable: false);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();

            lock (images)
            {
                images[imageKey] = bitmap;
            }
            return bitmap;
        }
        finally
        {
            renderGate.Release();
        }
    }

    private readonly record struct PageImageKey(string DocumentKey, int PageNumber, uint PixelWidth);
}
