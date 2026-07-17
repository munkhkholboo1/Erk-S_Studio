using System.Net.Http.Headers;

namespace ErkS.Platform.Publishing;

public sealed class PublishSettings
{
    public string ServerUrl { get; set; } = "https://erk-s.mn";
    public string ProjectCode { get; set; } = "";
    public string Email { get; set; } = "";
}

public sealed class PublishResult
{
    public required bool Success { get; init; }
    public required string Message { get; init; }
    public string AlbumUrl { get; init; } = "";
}

/// <summary>
/// Uploads a composed album PDF to the Erk-S server so project participants
/// see the latest album in real time.
///
/// NOTE: the server-side endpoint (/api/platform/albums) is not implemented
/// yet in Erk-S-Server; this client defines the contract the server will
/// fulfil. Until then calls return a friendly failure.
/// </summary>
public sealed class AlbumPublishClient
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromMinutes(5),
    };

    public async Task<PublishResult> UploadAlbumAsync(
        PublishSettings settings,
        string albumPdfPath,
        string albumTitle,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(albumPdfPath))
        {
            return new PublishResult { Success = false, Message = $"Album PDF not found: {albumPdfPath}" };
        }

        try
        {
            using var content = new MultipartFormDataContent();
            content.Add(new StringContent(settings.ProjectCode), "projectCode");
            content.Add(new StringContent(albumTitle), "title");
            await using var stream = File.OpenRead(albumPdfPath);
            var fileContent = new StreamContent(stream);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            content.Add(fileContent, "album", Path.GetFileName(albumPdfPath));

            var url = settings.ServerUrl.TrimEnd('/') + "/api/platform/albums";
            using var response = await Http.PostAsync(url, content, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? new PublishResult { Success = true, Message = "Album published.", AlbumUrl = body.Trim() }
                : new PublishResult
                {
                    Success = false,
                    Message = $"Server refused the upload ({(int)response.StatusCode}). Publishing endpoint is not live yet.",
                };
        }
        catch (Exception exception)
        {
            return new PublishResult { Success = false, Message = $"Publish failed: {exception.Message}" };
        }
    }
}
