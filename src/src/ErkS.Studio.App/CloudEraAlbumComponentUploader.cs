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
        if (uploads.Count > 32)
        {
            throw new StudioAccountException(
                "Album component sync accepts at most 32 components per request.");
        }
        if (uploads.Any(item => !item.Remove && !File.Exists(item.PdfPath)))
            throw new StudioAccountException("One or more rendered album component PDFs are unavailable.");

        string revisionId = (expectedRevisionId ?? "").Trim();
        string concurrencyToken = (projectConcurrencyToken ?? "").Trim();
        if (string.IsNullOrWhiteSpace(revisionId) || string.IsNullOrWhiteSpace(concurrencyToken))
            throw new StudioAccountException("Canonical album revision/version is missing. Refresh and try again.");

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(revisionId), "expectedRevisionId");
        content.Add(new StringContent(concurrencyToken), "projectConcurrencyToken");

        List<StudioCloudAlbumComponentUploadDescriptor> descriptors = [];
        for (int index = 0; index < uploads.Count; index++)
        {
            StudioAlbumComponentUpload component = uploads[index];
            string fieldName = $"component{index}";
            descriptors.Add(new StudioCloudAlbumComponentUploadDescriptor
            {
                FieldName = fieldName,
                Code = component.Code,
                Label = component.Label,
                Order = component.Order,
                Remove = component.Remove,
                SourceKey = component.SourceKey,
                ComponentKind = component.ComponentKind,
                SectionKey = component.SectionKey,
                SequenceKey = component.SequenceKey,
                Pages = (component.Pages ?? [])
                    .Select(page => new StudioCloudAlbumComponentPage
                    {
                        PageNumber = page.PageNumber,
                        PageKey = page.PageKey,
                        SortKey = page.SortKey,
                        SectionKey = page.SectionKey,
                        SequenceKey = page.SequenceKey,
                    })
                    .ToList(),
            });

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
        }

        content.Add(
            new StringContent(JsonSerializer.Serialize(descriptors, JsonOptions)),
            "components");

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
        return await ReadResponseAsync(
            response,
            uploads,
            cancellationToken).ConfigureAwait(true);
    }

    private static async Task<StudioCloudAlbumRevision> ReadResponseAsync(
        HttpResponseMessage response,
        IReadOnlyList<StudioAlbumComponentUpload> components,
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
                List<string> names = components
                    .Where(component => !component.Remove)
                    .Select(component => Path.GetFileName(component.PdfPath))
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .ToList();
                if (names.Count == 0)
                {
                    names = components
                        .Select(component => component.Label)
                        .Where(name => !string.IsNullOrWhiteSpace(name))
                        .ToList();
                }

                string subject = names.Count == 1
                    ? $"album component '{names[0]}'"
                    : $"album component batch containing {names.Count} files " +
                      $"({string.Join(", ", names.Select(name => $"'{name}'"))})";
                throw new StudioAccountException(
                    $"Cloud ERA rejected {subject} because the upload is too large (HTTP 413).",
                    response.StatusCode,
                    "album_component_too_large",
                    StudioCloudTraceIdentifier.Resolve(response, error),
                    error?.FieldErrors);
            }

            string message = string.IsNullOrWhiteSpace(error?.Message)
                ? $"Cloud ERA server error: {(int)response.StatusCode} {response.ReasonPhrase}"
                : error.Message;
            throw new StudioAccountException(
                message,
                response.StatusCode,
                error?.Code ?? "",
                StudioCloudTraceIdentifier.Resolve(response, error),
                error?.FieldErrors);
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
