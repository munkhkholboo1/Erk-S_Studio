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
/// <param name="SeatId">
/// The seat's opaque id from SRV - random, kept per account, never derived from the e-mail
/// (CLAUDE.md §2); the CGA shared file's writer. Empty from a server that does not give it yet.
/// </param>
public sealed record SyncServerDevice(
    string DeviceKeyId,
    string PublicKeySpki,
    string AccountEmail,
    string BotId,
    DateTimeOffset AttachedAtUtc,
    DateTimeOffset LastSeenAtUtc,
    string SeatId = "",
    string ClientVersion = "",
    int SyncProtocol = 0,
    IReadOnlyList<string>? LanEndpoints = null);

public sealed record SyncServerDeviceFolder(SyncServerFolder Folder, IReadOnlyList<SyncServerDevice> Devices);

/// <param name="MinSyncProtocol">The lowest sync protocol the server lets write (<see cref="SyncLagging.Protocol"/>); 0 from an older server.</param>
public sealed record SyncDeviceSession(string AccessToken, DateTimeOffset AccessTokenExpiresAtUtc, string DeviceKeyId, string MinSyncClientVersion = "", int MinSyncProtocol = 0);

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
/// 1.1 sent nothing about a file. Since 2026-10-24 (owner: «бид чинь cloud sync хийх гээд байгаа
/// шүү дээ») the relay routes carry a device's index and the pieces of the files that travel, so a
/// partner on another network syncs through the server (_shared/syn-cloud-relay-contract-2026-10-24.md);
/// only device tokens of attached devices reach them, and a Revit central model never goes.
///
/// Two credentials, never mixed: a person's Cloud ERA token for registering and joining
/// (a person's decision on a project), and the device token for what ErkS.Sync.exe does alone.
/// </summary>
public sealed class SyncServerClient(HttpClient http)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>The server this client talks to - the relay's WebSocket goes to the same place.</summary>
    public Uri? BaseAddress => http.BaseAddress;

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

    /// <summary>
    /// This SYN's release («0.001.108») - sent with every session so the server can refuse writes from a
    /// client older than the shared minimum (owner, 2026-10-26: «программаа шинэчлээгүй … удсан гишүүн
    /// бусад гишүүдийн ажиллагаанд нөлөөлөх учиргүй»).
    /// </summary>
    public string ClientVersion { get; init; } = "";

    /// <summary>
    /// «ip:port» where this machine's LAN exchange listens, private IPv4 only - sent with the session and
    /// each heartbeat, so a partner whose multicast never reaches us tries TCP at it directly.
    /// </summary>
    public IReadOnlyList<string> LanEndpoints { get; set; } = [];

    public Task<SyncDeviceSession> DeviceSessionAsync(string deviceKeyId, byte[] nonce, byte[] signature, CancellationToken cancellationToken = default) =>
        SendAsync<SyncDeviceSession>(
            HttpMethod.Post,
            "/api/cloud-era/v1/sync/device/session",
            null,
            new { deviceKeyId, nonce = Base64UrlEncode(nonce), signature = Convert.ToBase64String(signature), clientVersion = ClientVersion, syncProtocol = SyncLagging.Protocol, lanEndpoints = LanEndpoints },
            cancellationToken)!;

    public async Task<IReadOnlyList<SyncServerDeviceFolder>> DeviceFoldersAsync(string deviceToken, CancellationToken cancellationToken = default)
    {
        DeviceFoldersResponse? response = await SendAsync<DeviceFoldersResponse>(HttpMethod.Get, "/api/cloud-era/v1/sync/device/folders", deviceToken, null, cancellationToken);
        return response?.Folders ?? [];
    }

    public Task DeviceHeartbeatAsync(string deviceToken, CancellationToken cancellationToken = default) =>
        SendAsync<object>(HttpMethod.Post, "/api/cloud-era/v1/sync/device/heartbeat", deviceToken, new { lanEndpoints = LanEndpoints }, cancellationToken);

    // ---- relay (device token): a partner anywhere -----------------------------------------

    private static string Relay(string folderId) => $"/api/cloud-era/v1/sync/device/folders/{Escape(folderId)}";

    public async Task PutIndexAsync(string deviceToken, string folderId, string indexJson, CancellationToken cancellationToken = default)
    {
        using var content = new StringContent(indexJson, System.Text.Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await RawAsync(HttpMethod.Put, Relay(folderId) + "/index", deviceToken, content, cancellationToken);
    }

    /// <summary>A peer's latest index on the server, or null when it has put none.</summary>
    public async Task<string?> GetIndexAsync(string deviceToken, string folderId, string deviceKeyId, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage? response = await RawOrNoneAsync(HttpMethod.Get, Relay(folderId) + $"/devices/{Escape(deviceKeyId)}/index", deviceToken, cancellationToken);
        return response is null ? null : await response.Content.ReadAsStringAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> MissingChunksAsync(string deviceToken, string folderId, IReadOnlyList<string> sha256, CancellationToken cancellationToken = default) =>
        (await MissingAndUnnamedAsync(deviceToken, folderId, sha256, cancellationToken)).Missing;

    /// <summary>
    /// What the server lacks, and what it will not take because no stored index names it (SRV 8cddd94:
    /// the index first, the pieces after). Unnamed pieces of this machine's own index mean the server
    /// no longer holds that index - the caller sends it again.
    /// </summary>
    public async Task<(IReadOnlyList<string> Missing, IReadOnlyList<string> Unnamed)> MissingAndUnnamedAsync(string deviceToken, string folderId, IReadOnlyList<string> sha256, CancellationToken cancellationToken = default)
    {
        MissingResponse? response = await SendAsync<MissingResponse>(HttpMethod.Post, Relay(folderId) + "/chunks/missing", deviceToken, new { sha256 }, cancellationToken);
        return (response?.Missing ?? [], response?.Unnamed ?? []);
    }

    public async Task PutChunkAsync(string deviceToken, string folderId, string sha256, byte[] piece, CancellationToken cancellationToken = default)
    {
        using var content = new ByteArrayContent(piece);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using HttpResponseMessage response = await RawAsync(HttpMethod.Put, Relay(folderId) + $"/chunks/{Escape(sha256)}", deviceToken, content, cancellationToken);
    }

    /// <summary>A piece by its hash, or null when the server does not hold it (yet).</summary>
    public async Task<byte[]?> GetChunkAsync(string deviceToken, string folderId, string sha256, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage? response = await RawOrNoneAsync(HttpMethod.Get, Relay(folderId) + $"/chunks/{Escape(sha256)}", deviceToken, cancellationToken);
        return response is null ? null : await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    /// <summary>
    /// The folder's redactions on the server (SRV, _shared/syn-redaction-design-2026-10-26.md) - what a
    /// machine back from offline must take out before it uploads anything. None from an older server.
    /// </summary>
    public async Task<IReadOnlyList<SyncRedaction>> RedactionsAsync(string deviceToken, string folderId, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage? response = await RawOrNoneAsync(HttpMethod.Get, Relay(folderId) + "/redactions", deviceToken, cancellationToken);
        if (response is null)
            return [];
        string json = await response.Content.ReadAsStringAsync(cancellationToken);
        return System.Text.Json.JsonSerializer.Deserialize<List<SyncRedaction>>(json, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)) ?? [];
    }

    /// <summary>Records a signed redaction on the server, which then refuses the old content (410) and drops it from indexes.</summary>
    public async Task PostRedactionAsync(string deviceToken, string folderId, SyncRedaction redaction, CancellationToken cancellationToken = default)
    {
        string json = System.Text.Json.JsonSerializer.Serialize(redaction, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await RawAsync(HttpMethod.Post, Relay(folderId) + "/redactions", deviceToken, content, cancellationToken);
    }

    private async Task<HttpResponseMessage?> RawOrNoneAsync(HttpMethod method, string path, string token, CancellationToken cancellationToken)
    {
        try
        {
            return await RawAsync(method, path, token, null, cancellationToken);
        }
        catch (SyncServerException exception) when (exception.Status is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            return null; // 410: a redacted content the server will never give
        }
    }

    private async Task<HttpResponseMessage> RawAsync(HttpMethod method, string path, string token, HttpContent? content, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        HttpResponseMessage response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        if (response.IsSuccessStatusCode)
            return response;
        using (response)
            throw await RefusalAsync(response, cancellationToken);
    }

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
            throw await RefusalAsync(response, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NoContent || typeof(T) == typeof(object))
            return default;
        return await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken);
    }

    private static async Task<SyncServerException> RefusalAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
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
            return new SyncServerException(
                response.StatusCode,
                string.IsNullOrWhiteSpace(code) ? $"http_{(int)response.StatusCode}" : code,
                string.IsNullOrWhiteSpace(message) ? $"Сервер татгалзлаа ({(int)response.StatusCode})." : message);
        }
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

    private sealed record MissingResponse(List<string>? Missing, List<string>? Unnamed);

    private sealed record ChallengeResponse(string? Nonce);

    private sealed record FoldersResponse(List<SyncServerFolder>? Folders);

    private sealed record DevicesResponse(List<SyncServerDevice>? Devices);

    private sealed record DeviceFoldersResponse(List<SyncServerDeviceFolder>? Folders);
}
