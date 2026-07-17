using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace ErkS.Studio;

internal sealed class StudioUpdateLatestResponse
{
    public bool IsUpdateAvailable { get; set; }

    public string Version { get; set; } = "";

    public string DownloadUrl { get; set; } = "";

    public string Sha256 { get; set; } = "";

    public string ReleaseNotes { get; set; } = "";

    public bool IsRequired { get; set; }

    public string ServerUrl { get; set; } = "";
}

internal sealed record StudioUpdateProgress(int? Percent, string Message);

internal static class StudioReleaseInfo
{
    public const string ProductCode = "ErkS.Studio";
    public const string DefaultServerUrl = "https://erk-s.mn";

    public static string DisplayVersion => typeof(StudioReleaseInfo).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(StudioReleaseInfo).Assembly.GetName().Version?.ToString()
        ?? "Unknown";

    public static bool IsDevelopmentBuild => DisplayVersion.Contains("-dev", StringComparison.OrdinalIgnoreCase);

    public static string ApiVersion
    {
        get
        {
            string value = DisplayVersion.Trim();
            if (value.StartsWith("Demo ", StringComparison.OrdinalIgnoreCase))
                value = value[5..].Trim();

            int metadataStart = value.IndexOf('+', StringComparison.Ordinal);
            return metadataStart > 0 ? value[..metadataStart].Trim() : value;
        }
    }
}

internal sealed class StudioUpdateService : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient httpClient = new() { Timeout = TimeSpan.FromMinutes(30) };

    public async Task<StudioUpdateLatestResponse> CheckAsync(CancellationToken cancellationToken = default)
    {
        Uri server = ResolveUpdateServer();
        string product = Uri.EscapeDataString(StudioReleaseInfo.ProductCode);
        string version = Uri.EscapeDataString(StudioReleaseInfo.ApiVersion);
        Uri endpoint = new(server, $"api/updates/latest?productCode={product}&currentVersion={version}");
        using HttpResponseMessage response = await httpClient.GetAsync(endpoint, cancellationToken).ConfigureAwait(true);
        response.EnsureSuccessStatusCode();
        await using Stream body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(true);
        StudioUpdateLatestResponse result = await JsonSerializer.DeserializeAsync<StudioUpdateLatestResponse>(
            body,
            JsonOptions,
            cancellationToken).ConfigureAwait(true) ?? new StudioUpdateLatestResponse();
        result.ServerUrl = server.GetLeftPart(UriPartial.Authority);
        return result;
    }

    public async Task<string> DownloadAsync(
        StudioUpdateLatestResponse update,
        IProgress<StudioUpdateProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        if (!update.IsUpdateAvailable)
            throw new InvalidOperationException("Суулгах шинэ хувилбар алга.");

        ValidateSha256(update.Sha256);
        Uri downloadUri = ResolveDownloadUri(update);
        string safeVersion = SanitizeFileName(string.IsNullOrWhiteSpace(update.Version) ? "latest" : update.Version);
        string workDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Erk-S Studio",
            "Updates",
            safeVersion);
        Directory.CreateDirectory(workDirectory);
        string destination = Path.Combine(workDirectory, $"ErkS_Studio_{safeVersion}_Setup.exe");
        string partial = destination + ".download";

        try
        {
            if (File.Exists(partial))
                File.Delete(partial);

            using HttpResponseMessage response = await httpClient.GetAsync(
                downloadUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(true);
            response.EnsureSuccessStatusCode();

            long? total = response.Content.Headers.ContentLength;
            progress?.Report(new StudioUpdateProgress(total is > 0 ? 0 : null, "Шинэ хувилбарыг татаж байна..."));
            await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(true);
            await using (FileStream target = new(
                partial,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                byte[] buffer = new byte[128 * 1024];
                long received = 0;
                int lastPercent = -1;
                while (true)
                {
                    int read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(true);
                    if (read == 0)
                        break;

                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(true);
                    received += read;
                    int? percent = total is > 0
                        ? (int)Math.Clamp(Math.Floor(received * 100d / total.Value), 0, 100)
                        : null;
                    if (percent.HasValue && percent.Value == lastPercent)
                        continue;

                    lastPercent = percent ?? lastPercent;
                    string size = total is > 0
                        ? $"{ToMegabytes(received):0.0} / {ToMegabytes(total.Value):0.0} MB"
                        : $"{ToMegabytes(received):0.0} MB";
                    progress?.Report(new StudioUpdateProgress(percent, $"Татаж байна: {size}"));
                }
            }

            progress?.Report(new StudioUpdateProgress(null, "Татсан багцыг шалгаж байна..."));
            await VerifySha256Async(partial, update.Sha256, cancellationToken).ConfigureAwait(true);
            EnsureWindowsExecutable(partial);
            File.Move(partial, destination, true);
            progress?.Report(new StudioUpdateProgress(100, "Шинэчлэлт суулгахад бэлэн боллоо."));
            return destination;
        }
        catch
        {
            TryDelete(partial);
            throw;
        }
    }

    public static void LaunchInstaller(string installerPath)
    {
        if (string.IsNullOrWhiteSpace(installerPath) || !File.Exists(installerPath))
            throw new FileNotFoundException("Шинэчлэлтийн installer олдсонгүй.", installerPath);

        _ = Process.Start(new ProcessStartInfo
        {
            FileName = installerPath,
            WorkingDirectory = Path.GetDirectoryName(installerPath) ?? AppContext.BaseDirectory,
            UseShellExecute = true,
        }) ?? throw new InvalidOperationException("Шинэчлэлтийн installer эхэлсэнгүй.");
    }

    public void Dispose() => httpClient.Dispose();

    private static Uri ResolveUpdateServer()
    {
        string value = Environment.GetEnvironmentVariable("ERKS_STUDIO_UPDATE_SERVER_URL")
            ?? StudioReleaseInfo.DefaultServerUrl;
        if (!Uri.TryCreate(value.Trim().TrimEnd('/') + "/", UriKind.Absolute, out Uri? uri) ||
            !IsAllowedTransport(uri))
        {
            throw new InvalidOperationException("Studio update server нь HTTPS эсвэл local loopback хаяг байх ёстой.");
        }

        return uri;
    }

    private static Uri ResolveDownloadUri(StudioUpdateLatestResponse update)
    {
        Uri server = string.IsNullOrWhiteSpace(update.ServerUrl)
            ? ResolveUpdateServer()
            : new Uri(update.ServerUrl.Trim().TrimEnd('/') + "/", UriKind.Absolute);
        Uri result = Uri.TryCreate(update.DownloadUrl, UriKind.Absolute, out Uri? absolute)
            ? absolute
            : new Uri(server, (update.DownloadUrl ?? "").TrimStart('/'));
        if (!IsAllowedTransport(result))
            throw new InvalidOperationException("Шинэчлэлтийн багцыг хамгаалалтгүй хаягаас татахгүй.");
        return result;
    }

    private static bool IsAllowedTransport(Uri uri)
    {
        return uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) && uri.IsLoopback;
    }

    private static void ValidateSha256(string value)
    {
        string normalized = NormalizeSha256(value);
        if (normalized.Length != 64 || normalized.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidOperationException("Шинэчлэлтийн SHA-256 баталгаажуулалт дутуу байна.");
    }

    private static async Task VerifySha256Async(string path, string expected, CancellationToken cancellationToken)
    {
        await using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        string actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(true));
        if (!actual.Equals(NormalizeSha256(expected), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Татсан шинэчлэлтийн SHA-256 тохирсонгүй. Багцыг суулгахгүй.");
    }

    private static string NormalizeSha256(string value) => (value ?? "")
        .Trim()
        .Replace(" ", "", StringComparison.Ordinal)
        .Replace("-", "", StringComparison.Ordinal);

    private static void EnsureWindowsExecutable(string path)
    {
        FileInfo file = new(path);
        if (file.Length < 64 * 1024)
            throw new InvalidDataException("Шинэчлэлтийн installer бүрэн татагдсангүй.");

        using FileStream stream = File.OpenRead(path);
        if (stream.ReadByte() != 'M' || stream.ReadByte() != 'Z')
            throw new InvalidDataException("Татсан шинэчлэлт Windows installer биш байна.");
    }

    private static string SanitizeFileName(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        return string.Concat((value ?? "latest").Select(character => invalid.Contains(character) ? '_' : character));
    }

    private static double ToMegabytes(long bytes) => bytes / 1024d / 1024d;

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }
}
