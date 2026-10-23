using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace ErkS.Sync.Core;

/// <summary>A project's sync folder as the server records it (contract sync-folder, registerFolder.response200).</summary>
public sealed record SyncServerFolder(
    string FolderId,
    string ProjectId,
    string ProjectCode,
    string ProjectName,
    string CanonicalPath,
    string OwnerAccountEmail,
    string OwnerBotId,
    DateTimeOffset CreatedAtUtc);

/// <summary>One machine attached to a sync folder.</summary>
public sealed record SyncServerDevice(
    string DeviceKeyId,
    string PublicKeySpki,
    string AccountEmail,
    string BotId,
    DateTimeOffset AttachedAtUtc,
    DateTimeOffset LastSeenAtUtc);

public sealed record SyncServerDeviceFolder(SyncServerFolder Folder, IReadOnlyList<SyncServerDevice> Devices);

public sealed record SyncDeviceSession(string AccessToken, DateTimeOffset AccessTokenExpiresAtUtc, string DeviceKeyId);

/// <summary>A refusal the server named: its code, its sentence, the HTTP status.</summary>
public sealed class SyncServerException(HttpStatusCode status, string code, string message)
    : Exception(message)
{
    public HttpStatusCode Status { get; } = status;

    public string Code { get; } = code;
}

/// <summary>
/// The SRV routes of SYN 1.1 (Erk-S-Server docs/contracts/sync-folder.example.json).
///
/// 🔴 NOTHING ABOUT A FILE GOES OUT. Every request here carries a path of the FOLDER, a key id
/// and a project id - never a file name, a hash or an index (task 1.1, hard boundary).
///
/// Two credentials, never mixed: a person's Cloud ERA token for registering and joining
/// (a person's decision on a project), and the device token for what ErkS.Sync.exe does alone.
/// </summary>
public sealed class SyncServerClient(HttpClient http)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // ---- person (Cloud ERA token) -------------------------------------------------------

    public Task<SyncServerFolder> RegisterFolderAsync(string personToken, string projectId, string canonicalPath, string deviceKeyId, CancellationToken cancellationToken = default) =>
        SendAsync<SyncServerFolder>(HttpMethod.Post, $"/api/cloud-era/v1/projects/{Escape(projectId)}/sync-folder", personToken, new { canonicalPath, deviceKeyId }, cancellationToken)!;

    /// <summary>The project's folder, or null when it has none.</summary>
    public async Task<SyncServerFolder?> GetFolderAsync(string personToken, string projectId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await SendAsync<SyncServerFolder>(HttpMethod.Get, $"/api/cloud-era/v1/projects/{Escape(projectId)}/sync-folder", personToken, null, cancellationToken);
        }
        catch (SyncServerException exception) when (exception.Code == "sync_folder_not_found")
        {
            return null;
        }
    }

    public Task UnregisterFolderAsync(string personToken, string projectId, CancellationToken cancellationToken = default) =>
        SendAsync<object>(HttpMethod.Delete, $"/api/cloud-era/v1/projects/{Escape(projectId)}/sync-folder", personToken, null, cancellationToken);

    public async Task<IReadOnlyList<SyncServerFolder>> MyFoldersAsync(string personToken, CancellationToken cancellationToken = default)
    {
        FoldersResponse? response = await SendAsync<FoldersResponse>(HttpMethod.Get, "/api/cloud-era/v1/sync-folders/mine", personToken, null, cancellationToken);
        return response?.Folders ?? [];
    }

    public Task<SyncServerDevice> AttachDeviceAsync(string personToken, string folderId, string deviceKeyId, CancellationToken cancellationToken = default) =>
        SendAsync<SyncServerDevice>(HttpMethod.Post, $"/api/cloud-era/v1/sync-folders/{Escape(folderId)}/devices", personToken, new { deviceKeyId }, cancellationToken)!;

    public Task DetachDeviceAsync(string personToken, string folderId, string deviceKeyId, CancellationToken cancellationToken = default) =>
        SendAsync<object>(HttpMethod.Delete, $"/api/cloud-era/v1/sync-folders/{Escape(folderId)}/devices/{Escape(deviceKeyId)}", personToken, null, cancellationToken);

    public async Task<IReadOnlyList<SyncServerDevice>> ListDevicesAsync(string personToken, string folderId, CancellationToken cancellationToken = default)
    {
        DevicesResponse? response = await SendAsync<DevicesResponse>(HttpMethod.Get, $"/api/cloud-era/v1/sync-folders/{Escape(folderId)}/devices", personToken, null, cancellationToken);
        return response?.Devices ?? [];
    }

    // ---- device (no person) -------------------------------------------------------------

    public async Task<byte[]> DeviceChallengeAsync(string deviceKeyId, CancellationToken cancellationToken = default)
    {
        ChallengeResponse? response = await SendAsync<ChallengeResponse>(HttpMethod.Post, "/api/cloud-era/v1/sync/device/challenge", null, new { deviceKeyId }, cancellationToken);
        return Base64UrlDecode(response?.Nonce ?? "");
    }

    public Task<SyncDeviceSession> DeviceSessionAsync(string deviceKeyId, byte[] nonce, byte[] signature, CancellationToken cancellationToken = default) =>
        SendAsync<SyncDeviceSession>(
            HttpMethod.Post,
            "/api/cloud-era/v1/sync/device/session",
            null,
            new { deviceKeyId, nonce = Base64UrlEncode(nonce), signature = Convert.ToBase64String(signature) },
            cancellationToken)!;

    public async Task<IReadOnlyList<SyncServerDeviceFolder>> DeviceFoldersAsync(string deviceToken, CancellationToken cancellationToken = default)
    {
        DeviceFoldersResponse? response = await SendAsync<DeviceFoldersResponse>(HttpMethod.Get, "/api/cloud-era/v1/sync/device/folders", deviceToken, null, cancellationToken);
        return response?.Folders ?? [];
    }

    public Task DeviceHeartbeatAsync(string deviceToken, CancellationToken cancellationToken = default) =>
        SendAsync<object>(HttpMethod.Post, "/api/cloud-era/v1/sync/device/heartbeat", deviceToken, null, cancellationToken);

    // ---- plumbing ------------------------------------------------------------------------

    private async Task<T?> SendAsync<T>(HttpMethod method, string path, string? token, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
            request.Content = JsonContent.Create(body, options: Json);
        using HttpResponseMessage response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            string code = response.Headers.TryGetValues("X-ErkS-Reason-Code", out IEnumerable<string>? values)
                ? values.FirstOrDefault() ?? ""
                : "";
            string message = "";
            try
            {
                ErrorResponse? error = await response.Content.ReadFromJsonAsync<ErrorResponse>(Json, cancellationToken);
                code = string.IsNullOrWhiteSpace(code) ? error?.Code ?? "" : code;
                message = error?.Message ?? "";
            }
            catch (Exception exception) when (exception is JsonException or NotSupportedException)
            {
            }
            throw new SyncServerException(
                response.StatusCode,
                string.IsNullOrWhiteSpace(code) ? $"http_{(int)response.StatusCode}" : code,
                string.IsNullOrWhiteSpace(message) ? $"Сервер татгалзлаа ({(int)response.StatusCode})." : message);
        }
        if (response.StatusCode == HttpStatusCode.NoContent || typeof(T) == typeof(object))
            return default;
        return await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken);
    }

    private static string Escape(string value) => Uri.EscapeDataString(value ?? "");

    private static string Base64UrlEncode(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        string padded = value.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
        return Convert.FromBase64String(padded);
    }

    private sealed record ErrorResponse(string? Code, string? Message);

    private sealed record ChallengeResponse(string? Nonce);

    private sealed record FoldersResponse(List<SyncServerFolder>? Folders);

    private sealed record DevicesResponse(List<SyncServerDevice>? Devices);

    private sealed record DeviceFoldersResponse(List<SyncServerDeviceFolder>? Folders);
}
