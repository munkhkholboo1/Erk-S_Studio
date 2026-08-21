using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ErkS.Studio;

/// <summary>
/// The artwork the site publishes, kept on this machine after the first look.
/// The home page is the Platform's own shop window, so it has to open at once
/// on the second run even when the network is slow or absent.
/// </summary>
internal sealed class StudioSiteImageCache : IDisposable
{
    private const long MaximumImageBytes = 12L * 1024L * 1024L;

    private readonly HttpClient httpClient = new() { Timeout = TimeSpan.FromSeconds(45) };
    private readonly Dictionary<string, ImageSource> memory = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Task<ImageSource?>> inFlight = new(StringComparer.OrdinalIgnoreCase);
    private readonly string cacheFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Erk-S",
        "Studio",
        "site-images");

    /// <summary>The picture if it is already in hand, without going anywhere for it.</summary>
    public ImageSource? Peek(string url) =>
        memory.TryGetValue(Key(url), out ImageSource? value) ? value : null;

    /// <summary>
    /// The picture, from memory, then from this machine's cache, then from the
    /// site. Two callers asking at once share one download.
    /// </summary>
    public Task<ImageSource?> GetAsync(string url, CancellationToken cancellationToken = default)
    {
        string address = (url ?? "").Trim();
        if (address.Length == 0)
            return Task.FromResult<ImageSource?>(null);

        string key = Key(address);
        if (memory.TryGetValue(key, out ImageSource? cached))
            return Task.FromResult<ImageSource?>(cached);

        if (inFlight.TryGetValue(key, out Task<ImageSource?>? running))
            return running;

        Task<ImageSource?> fetch = LoadAsync(address, key, cancellationToken);
        inFlight[key] = fetch;
        return fetch;
    }

    private async Task<ImageSource?> LoadAsync(string url, string key, CancellationToken cancellationToken)
    {
        try
        {
            ImageSource? fromDisk = ReadFromDisk(key);
            if (fromDisk is not null)
            {
                memory[key] = fromDisk;
                return fromDisk;
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ||
                uri.Scheme is not ("https" or "http"))
            {
                return null;
            }

            using HttpResponseMessage response = await httpClient
                .GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(true);
            if (!response.IsSuccessStatusCode)
                return null;

            string contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            if (contentType is not ("image/png" or "image/jpeg" or "image/webp"))
                return null;
            if (response.Content.Headers.ContentLength > MaximumImageBytes)
                return null;

            byte[] bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(true);
            if (bytes.Length == 0 || bytes.LongLength > MaximumImageBytes)
                return null;

            ImageSource? image = Decode(bytes);
            if (image is null)
                return null;

            WriteToDisk(key, bytes);
            memory[key] = image;
            return image;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException or IOException or NotSupportedException)
        {
            return null;
        }
        finally
        {
            inFlight.Remove(key);
        }
    }

    private ImageSource? ReadFromDisk(string key)
    {
        try
        {
            string path = Path.Combine(cacheFolder, key);
            return File.Exists(path) ? Decode(File.ReadAllBytes(path)) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void WriteToDisk(string key, byte[] bytes)
    {
        try
        {
            Directory.CreateDirectory(cacheFolder);
            string path = Path.Combine(cacheFolder, key);
            File.WriteAllBytes(path + ".tmp", bytes);
            File.Move(path + ".tmp", path, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A picture that cannot be kept is still a picture that can be shown.
        }
    }

    private static ImageSource? Decode(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception exception) when (exception is NotSupportedException or IOException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// A file name that is stable for one address and safe on this file system.
    /// The address is hashed rather than sanitised so two different pictures can
    /// never collide onto one cached file.
    /// </summary>
    private static string Key(string url) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes((url ?? "").Trim())))[..24] + ".img";

    public void Dispose() => httpClient.Dispose();
}
