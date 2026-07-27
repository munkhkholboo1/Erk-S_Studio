using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace ErkS.Studio;

internal static class CloudEraAlbumComponentUploader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    public static async Task<StudioCloudAlbumRevision> MergeAsync(
        HttpClient httpClient,
        string serverUrl,
        string accessToken,
        string projectId,
        string albumId,
        string expectedRevisionId,
        string projectConcurrencyToken,
        IReadOnlyList<StudioAlbumComponentUpload> components,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        List<StudioAlbumComponentUpload> uploads = (components ?? [])
            .Where(item => item is not null)
            .ToList();
        if (uploads.Count == 0)
            throw new StudioAccountException("No album source component was selected for sync.");
        if (uploads.Any(item => !item.Remove && !File.Exists(item.PdfPath)))
            throw new StudioAccountException("One or more rendered album component PDFs are unavailable.");

        string revisionId = (expectedRevisionId ?? "").Trim();
        string concurrencyToken = (projectConcurrencyToken ?? "").Trim();
        if (string.IsNullOrWhiteSpace(revisionId) || string.IsNullOrWhiteSpace(concurrencyToken))
            throw new StudioAccountException("Canonical album revision/version is missing. Refresh and try again.");

        StudioCloudAlbumRevision? result = null;
        for (int index = 0; index < uploads.Count; index++)
        {
            StudioAlbumComponentUpload component = uploads[index];
            using var content = new MultipartFormDataContent();
            content.Add(new StringContent(revisionId), "expectedRevisionId");
            content.Add(new StringContent(concurrencyToken), "projectConcurrencyToken");

            const string fieldName = "component0";
            var descriptor = new StudioCloudAlbumComponentUploadDescriptor
            {
                FieldName = fieldName,
                Code = component.Code,
                Label = component.Label,
                Order = component.Order,
                Remove = component.Remove,
                SourceKey = component.SourceKey,
                ComponentKind = component.ComponentKind,
            };
            content.Add(
                new StringContent(JsonSerializer.Serialize(new[] { descriptor }, JsonOptions)),
                "components");

            if (!component.Remove)
            {
                var stream = new FileStream(
                    component.PdfPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read);
                var file = new StreamContent(stream);
                file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
                content.Add(file, fieldName, Path.GetFileName(component.PdfPath));
            }

            string path = "/api/cloud-era/v1/projects/" + Uri.EscapeDataString(projectId) +
                "/albums/" + Uri.EscapeDataString(albumId) + "/components";
            using HttpRequestMessage request = new(HttpMethod.Put, BuildUri(serverUrl, path))
            {
                Content = content,
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using HttpResponseMessage response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(true);
            result = await ReadResponseAsync(
                response,
                component,
                cancellationToken).ConfigureAwait(true);
            revisionId = result.RevisionId;

            if (index >= uploads.Count - 1)
                continue;

            concurrencyToken = (response.Headers.ETag?.Tag ?? "").Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(concurrencyToken))
            {
                throw new StudioAccountException(
                    "Cloud ERA server did not return the updated project version after component sync. " +
                    "Update the server and refresh the project before retrying.");
            }
        }

        return result!;
    }

    private static async Task<StudioCloudAlbumRevision> ReadResponseAsync(
        HttpResponseMessage response,
        StudioAlbumComponentUpload component,
        CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            StudioCloudApiError? error = null;
            try
            {
                error = await response.Content.ReadFromJsonAsync<StudioCloudApiError>(
                    JsonOptions,
                    cancellationToken).ConfigureAwait(true);
            }
            catch (Exception exception) when (exception is JsonException or NotSupportedException)
            {
            }

            if ((int)response.StatusCode == 413)
            {
                string fileName = component.Remove
                    ? component.Label
                    : Path.GetFileName(component.PdfPath);
                throw new StudioAccountException(
                    $"Cloud ERA rejected album component '{fileName}' because the upload is too large (HTTP 413).",
                    response.StatusCode,
                    "album_component_too_large");
            }

            string message = string.IsNullOrWhiteSpace(error?.Message)
                ? $"Cloud ERA server error: {(int)response.StatusCode} {response.ReasonPhrase}"
                : error.Message;
            throw new StudioAccountException(message, response.StatusCode, error?.Code ?? "");
        }

        StudioCloudAlbumRevision? value =
            await response.Content.ReadFromJsonAsync<StudioCloudAlbumRevision>(
                JsonOptions,
                cancellationToken).ConfigureAwait(true);
        return value ?? throw new StudioAccountException("Cloud ERA server returned an empty album revision.");
    }

    private static Uri BuildUri(string serverUrl, string path) =>
        new(new Uri(serverUrl.TrimEnd('/') + "/"), path.TrimStart('/'));
}
