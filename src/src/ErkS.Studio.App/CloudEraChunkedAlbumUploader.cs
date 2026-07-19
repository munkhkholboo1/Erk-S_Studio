using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

namespace ErkS.Studio;

internal static class CloudEraChunkedAlbumUploader
{
    public const int PreferredChunkBytes = 8 * 1024 * 1024;
    public const int MaximumAcceptedChunkBytes = 12 * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    public static async Task<StudioCloudAlbumRevision> UploadAsync(
        HttpClient httpClient,
        string serverUrl,
        string accessToken,
        string projectId,
        string albumId,
        string pdfPath,
        int pageCount,
        string pageSizeSummary,
        string projectConcurrencyToken,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        FileInfo file = new(pdfPath);
        if (string.IsNullOrWhiteSpace(projectConcurrencyToken))
            throw new StudioAccountException("Canonical project version is missing. Start Sync again.");
        if (!file.Exists)
            throw new StudioAccountException("Синк хийх альбумын PDF олдсонгүй.");

        string sha256;
        await using (FileStream hashSource = file.OpenRead())
        {
            sha256 = Convert.ToHexString(await SHA256.HashDataAsync(hashSource, cancellationToken))
                .ToLowerInvariant();
        }

        string basePath = "/api/cloud-era/v1/projects/" + Uri.EscapeDataString(projectId) +
            "/albums/" + Uri.EscapeDataString(albumId) + "/revisions/uploads";
        StudioCloudAlbumUploadSession session = await SendJsonAsync<StudioCloudAlbumUploadStartRequest, StudioCloudAlbumUploadSession>(
            httpClient,
            HttpMethod.Post,
            BuildUri(serverUrl, basePath),
            accessToken,
            new StudioCloudAlbumUploadStartRequest
            {
                FileName = file.Name,
                SizeBytes = file.Length,
                Sha256 = sha256,
                PageCount = pageCount,
                PageSizeSummary = pageSizeSummary ?? "",
                ChunkSizeBytes = PreferredChunkBytes,
                ProjectConcurrencyToken = projectConcurrencyToken.Trim(),
            },
            cancellationToken).ConfigureAwait(true);

        if (string.IsNullOrWhiteSpace(session.UploadId) ||
            session.ChunkSizeBytes is < 1 or > MaximumAcceptedChunkBytes ||
            session.TotalChunks < 1)
        {
            throw new StudioAccountException("Cloud ERA server album upload session-ийн мэдээлэл буруу байна.");
        }

        HashSet<int> received = (session.ReceivedChunks ?? [])
            .Where(index => index >= 0 && index < session.TotalChunks)
            .ToHashSet();
        await using FileStream source = new(pdfPath, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, useAsync: true);
        for (int index = 0; index < session.TotalChunks; index++)
        {
            if (received.Contains(index))
                continue;

            long offset = (long)index * session.ChunkSizeBytes;
            int length = checked((int)Math.Min(session.ChunkSizeBytes, file.Length - offset));
            if (length < 1)
                throw new StudioAccountException("Cloud ERA server album chunk-ийн тоо файлын хэмжээтэй тохирохгүй байна.");
            byte[] chunk = new byte[length];
            source.Position = offset;
            await source.ReadExactlyAsync(chunk.AsMemory(), cancellationToken);
            string chunkSha256 = Convert.ToHexString(SHA256.HashData(chunk)).ToLowerInvariant();
            string chunkPath = basePath + "/" + Uri.EscapeDataString(session.UploadId) +
                "/chunks/" + index.ToString(CultureInfo.InvariantCulture);
            await SendChunkWithRetryAsync(
                httpClient,
                BuildUri(serverUrl, chunkPath),
                accessToken,
                chunk,
                chunkSha256,
                cancellationToken).ConfigureAwait(true);
        }

        string completePath = basePath + "/" + Uri.EscapeDataString(session.UploadId) + "/complete";
        using HttpRequestMessage completeRequest = AuthorizedRequest(
            HttpMethod.Post,
            BuildUri(serverUrl, completePath),
            accessToken);
        using HttpResponseMessage completeResponse = await httpClient.SendAsync(
            completeRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(true);
        StudioCloudAlbumRevision revision = await ReadResponseAsync<StudioCloudAlbumRevision>(
            completeResponse,
            cancellationToken).ConfigureAwait(true);
        if (!revision.PdfSha256.Equals(sha256, StringComparison.OrdinalIgnoreCase))
            throw new StudioAccountException("Cloud ERA server хүлээн авсан альбумын SHA-256 локал PDF-тэй тохирохгүй байна.");
        return revision;
    }

    private static async Task SendChunkWithRetryAsync(
        HttpClient httpClient,
        Uri uri,
        string accessToken,
        byte[] chunk,
        string chunkSha256,
        CancellationToken cancellationToken)
    {
        const int attempts = 3;
        for (int attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                using HttpRequestMessage request = AuthorizedRequest(HttpMethod.Put, uri, accessToken);
                request.Headers.Add("X-Chunk-SHA256", chunkSha256);
                request.Content = new ByteArrayContent(chunk);
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                using HttpResponseMessage response = await httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken).ConfigureAwait(true);
                if (response.IsSuccessStatusCode)
                    return;
                if (attempt == attempts || !IsRetryable(response.StatusCode))
                    await ReadResponseAsync<StudioCloudAlbumUploadSession>(response, cancellationToken).ConfigureAwait(true);
            }
            catch (HttpRequestException) when (attempt < attempts)
            {
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < attempts)
            {
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken).ConfigureAwait(true);
        }

        throw new StudioAccountException("Альбумын хэсгийг Cloud ERA server рүү илгээж чадсангүй.");
    }

    private static bool IsRetryable(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests ||
        (int)statusCode >= 500;

    private static async Task<TResponse> SendJsonAsync<TRequest, TResponse>(
        HttpClient httpClient,
        HttpMethod method,
        Uri uri,
        string accessToken,
        TRequest value,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = AuthorizedRequest(method, uri, accessToken);
        request.Content = JsonContent.Create(value, options: JsonOptions);
        using HttpResponseMessage response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(true);
        return await ReadResponseAsync<TResponse>(response, cancellationToken).ConfigureAwait(true);
    }

    private static HttpRequestMessage AuthorizedRequest(HttpMethod method, Uri uri, string accessToken)
    {
        HttpRequestMessage request = new(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    private static async Task<TResponse> ReadResponseAsync<TResponse>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            StudioCloudApiError? error = null;
            try
            {
                error = await response.Content.ReadFromJsonAsync<StudioCloudApiError>(JsonOptions, cancellationToken)
                    .ConfigureAwait(true);
            }
            catch (Exception parseError) when (parseError is JsonException or NotSupportedException)
            {
            }

            string message = string.IsNullOrWhiteSpace(error?.Message)
                ? $"Cloud ERA server алдаа: {(int)response.StatusCode} {response.ReasonPhrase}"
                : error.Message;
            throw new StudioAccountException(message, response.StatusCode, error?.Code ?? "");
        }

        TResponse? value = await response.Content.ReadFromJsonAsync<TResponse>(JsonOptions, cancellationToken)
            .ConfigureAwait(true);
        return value ?? throw new StudioAccountException("Cloud ERA server хоосон хариу өглөө.");
    }

    private static Uri BuildUri(string serverUrl, string path)
    {
        Uri root = new(serverUrl.TrimEnd('/') + "/", UriKind.Absolute);
        return new Uri(root, path.TrimStart('/'));
    }
}
