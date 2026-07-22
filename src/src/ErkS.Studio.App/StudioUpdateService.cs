using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using ErkS.Platform.Core;

namespace ErkS.Studio;

internal sealed class StudioUpdateLatestResponse
{
    public string ProductCode { get; set; } = "";

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
    public const string ProductionUpdatePublisher = "Erk-S LLC";
    public const string DevelopmentUpdatePublisher = "Erk-S CityGen DevMod Local";
    private static readonly IReadOnlyList<string> ProductionPinnedCertificateSha256 =
    [
        "A8A0A7C1435FC0E63A39CB3D101D9A532E1736D83FCBB65246DCA5B485636D8A",
    ];

    public static string DisplayVersion => typeof(StudioReleaseInfo).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(StudioReleaseInfo).Assembly.GetName().Version?.ToString()
        ?? "Unknown";

    public static bool IsDevelopmentBuild => DisplayVersion.Contains("-dev", StringComparison.OrdinalIgnoreCase);

    public static string ExpectedUpdatePublisher => IsDevelopmentBuild
        ? Environment.GetEnvironmentVariable("ERKS_STUDIO_DEV_UPDATE_PUBLISHER")?.Trim()
            ?? DevelopmentUpdatePublisher
        : ProductionUpdatePublisher;

    public static IReadOnlyList<string> PinnedUntrustedRootCertificateSha256 => IsDevelopmentBuild
        ? Array.Empty<string>()
        : ProductionPinnedCertificateSha256;

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

internal sealed class StudioUpdateService : IUpdatesClient, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient httpClient = new() { Timeout = TimeSpan.FromMinutes(30) };
    private readonly IAuthenticodeTrustVerifier authenticodeVerifier;

    public StudioUpdateService()
        : this(new WindowsAuthenticodeTrustVerifier())
    {
    }

    internal StudioUpdateService(IAuthenticodeTrustVerifier authenticodeVerifier)
    {
        this.authenticodeVerifier = authenticodeVerifier
            ?? throw new ArgumentNullException(nameof(authenticodeVerifier));
    }

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
        ValidateCatalogEntry(result);
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

        ValidateCatalogEntry(update);
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
            await UpdatePackageSecurityPolicy.VerifyInstallerAsync(
                partial,
                update.Sha256,
                StudioReleaseInfo.ExpectedUpdatePublisher,
                authenticodeVerifier,
                StudioReleaseInfo.PinnedUntrustedRootCertificateSha256,
                cancellationToken).ConfigureAwait(true);
            ValidateInstallerIdentity(partial, update.Version);
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

    public async Task VerifyAndLaunchInstallerAsync(
        string installerPath,
        StudioUpdateLatestResponse update,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        ValidateCatalogEntry(update);
        await UpdatePackageSecurityPolicy.VerifyInstallerAsync(
            installerPath,
            update.Sha256,
            StudioReleaseInfo.ExpectedUpdatePublisher,
            authenticodeVerifier,
            StudioReleaseInfo.PinnedUntrustedRootCertificateSha256,
            cancellationToken).ConfigureAwait(true);
        ValidateInstallerIdentity(installerPath, update.Version);

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
        if (!Uri.TryCreate(value.Trim().TrimEnd('/') + "/", UriKind.Absolute, out Uri? uri))
        {
            throw new InvalidOperationException("Studio update server нь HTTPS эсвэл local loopback хаяг байх ёстой.");
        }

        UpdatePackageSecurityPolicy.ValidateTransport(uri, StudioReleaseInfo.IsDevelopmentBuild);
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
        UpdatePackageSecurityPolicy.ValidateTransport(result, StudioReleaseInfo.IsDevelopmentBuild);
        return result;
    }

    internal static void ValidateCatalogEntry(StudioUpdateLatestResponse update)
    {
        ArgumentNullException.ThrowIfNull(update);
        if (!string.IsNullOrWhiteSpace(update.ProductCode) &&
            !update.ProductCode.Trim().Equals(StudioReleaseInfo.ProductCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Шинэчлэлтийн багц '{update.ProductCode}' бүтээгдэхүүнийх байна. Erk-S Studio update биш тул татахгүй.");
        }

        if (string.IsNullOrWhiteSpace(update.DownloadUrl))
        {
            if (update.IsUpdateAvailable)
                throw new InvalidDataException("Studio update download URL дутуу байна.");
            return;
        }

        string path = Uri.TryCreate(update.DownloadUrl, UriKind.Absolute, out Uri? absolute)
            ? absolute.AbsolutePath
            : update.DownloadUrl.Split('?', '#')[0];
        path = Uri.UnescapeDataString(path).Replace('\\', '/');
        string fileName = Path.GetFileName(path);
        bool isStudioFolder = path.Contains("/ErkS.Studio/", StringComparison.OrdinalIgnoreCase);
        bool isStudioInstaller = fileName.StartsWith("ErkS_Studio_", StringComparison.OrdinalIgnoreCase) &&
            fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
        if (!isStudioFolder || !isStudioInstaller)
        {
            throw new InvalidDataException(
                "Server өөр бүтээгдэхүүний багц заасан тул Studio update-ийг татсангүй.");
        }
    }

    internal static void ValidateInstallerIdentity(string installerPath, string expectedVersion)
    {
        FileVersionInfo versionInfo = FileVersionInfo.GetVersionInfo(installerPath);
        if (!string.Equals(versionInfo.ProductName?.Trim(), "Erk-S Studio", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Downloaded package is not an Erk-S Studio installer.");

        string actual = NormalizeProductVersion(versionInfo.ProductVersion);
        string expected = NormalizeProductVersion(expectedVersion);
        if (!string.IsNullOrWhiteSpace(expected) &&
            !string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Studio installer version mismatch. Expected '{expectedVersion}', received '{versionInfo.ProductVersion}'.");
        }
    }

    private static string NormalizeProductVersion(string? value)
    {
        string result = (value ?? "").Trim();
        int metadataStart = result.IndexOf('+', StringComparison.Ordinal);
        if (metadataStart >= 0)
            result = result[..metadataStart].Trim();
        if (result.StartsWith("Demo ", StringComparison.OrdinalIgnoreCase))
            result = result[5..].Trim();
        return result.TrimStart('v', 'V');
    }

    private static void ValidateSha256(string value)
    {
        string normalized = NormalizeSha256(value);
        if (normalized.Length != 64 || normalized.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidOperationException("Шинэчлэлтийн SHA-256 баталгаажуулалт дутуу байна.");
    }

    private static string NormalizeSha256(string value) => (value ?? "")
        .Trim()
        .Replace(" ", "", StringComparison.Ordinal)
        .Replace("-", "", StringComparison.Ordinal);

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
