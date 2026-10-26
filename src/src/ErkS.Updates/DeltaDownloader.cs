using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace ErkS.Updates;

/// <summary>
/// Fetches a plan's files into a staging folder (contract §3): by hash from
/// <c>/updates/{product}/files/{sha256}</c>, each checked against the signed manifest's hash,
/// a broken transfer resumed with Range, files already staged skipped.
/// </summary>
public sealed class DeltaDownloader
{
    private readonly HttpClient _http;
    private readonly Uri _server;
    private readonly string _productCode;

    /// <summary>
    /// When set, a staged .exe or .dll that carries an Authenticode signature must be signed by a
    /// certificate of this thumbprint (the Erk-S pin). Unsigned files rest on the manifest's hash.
    /// </summary>
    public string? PinnedSignerThumbprint { get; set; } = ManifestSignature.ErkSThumbprint;

    public DeltaDownloader(HttpClient http, Uri server, string productCode)
    {
        _http = http;
        _server = new Uri(server.GetLeftPart(UriPartial.Authority) + "/");
        _productCode = productCode;
    }

    /// <summary>Where a file of that hash is kept in the staging folder.</summary>
    public static string StagedPath(string stagingDirectory, string sha256) => Path.Combine(stagingDirectory, "files", sha256);

    public async Task DownloadAsync(UpdatePlan plan, string stagingDirectory, IProgress<UpdateProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.Combine(stagingDirectory, "files"));
        var report = new UpdateProgress { TotalBytes = plan.DownloadBytes, FilesTotal = plan.Changed.Count };
        var clock = Stopwatch.StartNew();
        long fetchedThisRun = 0;
        foreach (UpdateFile file in plan.Changed)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string staged = StagedPath(stagingDirectory, file.Sha256);
            if (!(File.Exists(staged) && new FileInfo(staged).Length == file.Size && Hashes.Sha256OfFile(staged) == file.Sha256))
            {
                long before = File.Exists(staged + ".part") ? new FileInfo(staged + ".part").Length : 0;
                await FetchAsync(file, staged, bytes =>
                {
                    fetchedThisRun += bytes;
                    report.DoneBytes += bytes;
                    report.BytesPerSecond = clock.Elapsed.TotalSeconds > 0.5 ? fetchedThisRun / clock.Elapsed.TotalSeconds : 0;
                    progress?.Report(report);
                }, cancellationToken).ConfigureAwait(false);
                report.DoneBytes += before; // resumed bytes count as done
            }
            else
            {
                report.DoneBytes += file.Size;
            }
            report.FilesDone++;
            progress?.Report(report);
        }
    }

    private async Task FetchAsync(UpdateFile file, string staged, Action<long> received, CancellationToken cancellationToken)
    {
        string part = staged + ".part";
        for (int attempt = 1; ; attempt++)
        {
            long have = File.Exists(part) ? new FileInfo(part).Length : 0;
            if (have > file.Size)
            {
                File.Delete(part);
                have = 0;
            }
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_server, $"updates/{Uri.EscapeDataString(_productCode)}/files/{file.Sha256}"));
                if (have > 0)
                    request.Headers.Range = new RangeHeaderValue(have, null);
                using HttpResponseMessage response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                if (have > 0 && response.StatusCode == HttpStatusCode.OK)
                    have = 0; // the server sent the whole file: start over
                else if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException($"Шинэчлэлийн файлыг татаж чадсангүй: {(int)response.StatusCode}");
                using (Stream body = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var output = new FileStream(part, have > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
                {
                    byte[] buffer = new byte[1 << 16];
                    int read;
                    while ((read = await body.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        await output.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
                        received(read);
                    }
                }
                string sha = Hashes.Sha256OfFile(part);
                if (!sha.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(part);
                    throw new InvalidDataException("Татсан файлын хэш таарахгүй байна: " + file.Path);
                }
                CheckSigner(part, file.Path);
                if (File.Exists(staged))
                    File.Delete(staged);
                File.Move(part, staged);
                return;
            }
            catch (Exception exception) when (attempt < 4 && exception is HttpRequestException or IOException && !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(attempt * 2), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private void CheckSigner(string path, string manifestPath)
    {
        if (string.IsNullOrEmpty(PinnedSignerThumbprint))
            return;
        string extension = Path.GetExtension(manifestPath);
        if (!extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".dll", StringComparison.OrdinalIgnoreCase))
            return;
        X509Certificate? signer;
        try
        {
            signer = X509Certificate.CreateFromSignedFile(path);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return; // not Authenticode-signed: the signed manifest's hash already vouches for it
        }
        string thumbprint = new X509Certificate2(signer).Thumbprint;
        if (thumbprint.Equals(PinnedSignerThumbprint, StringComparison.OrdinalIgnoreCase))
            return;
        if (new X509Certificate2(signer).Subject.IndexOf("Microsoft", StringComparison.OrdinalIgnoreCase) >= 0)
            return; // the .NET runtime's own files
        throw new InvalidDataException("Файлын гарын үсэг Erk-S-ийнх биш: " + manifestPath);
    }
}
