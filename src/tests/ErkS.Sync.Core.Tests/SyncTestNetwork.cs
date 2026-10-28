using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace ErkS.Sync.Core.Tests;

/// <summary>A machine's device key, thrown away with the test.</summary>
internal sealed class FakeDevice : ISyncDevice, IDisposable
{
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public FakeDevice()
    {
        DeviceKeyId = SyncDeviceKeyIds.Of(_key.ExportSubjectPublicKeyInfo());
        TlsCertificate = SyncDeviceKey.CreateTlsCertificate(_key);
    }

    public string DeviceKeyId { get; }

    public X509Certificate2 TlsCertificate { get; }

    public byte[] SignSession(byte[] nonce) => SyncDeviceKey.SignSession(_key, nonce);

    public void Dispose()
    {
        TlsCertificate.Dispose();
        _key.Dispose();
    }
}

/// <summary>The SRV device routes, answering the way the contract says: one folder, these devices on it.</summary>
internal sealed class FakeServer(string canonicalPath, IReadOnlyList<FakeDevice> devices) : HttpMessageHandler
{
    private readonly object _gate = new();
    private readonly Dictionary<string, string> _indexes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, byte[]> _chunks = new(StringComparer.OrdinalIgnoreCase);

    public FakeDevice[] Devices => devices.ToArray();

    /// <summary>The redactions route (SRV e0eae2d); off, the server is one deployed before it (404).</summary>
    public bool RedactionsRoute { get; set; }

    public List<string> Redactions { get; } = [];

    /// <summary>Writes refused once with 426 sync_client_outdated (a session opened before the server knew versions).</summary>
    public int OutdatedWrites { get; set; }

    public int Sessions { get; private set; }

    /// <summary>The next missing-pieces answers call every piece «unnamed» (the server lost the index).</summary>
    public int UnnamedAnswers { get; set; }

    public int IndexPuts { get; private set; }

    /// <summary>A partner's index names entries not yet whole as «incomplete» (SRV 18200f7).</summary>
    public bool MarksIncomplete { get; set; }

    /// <summary>Piece uploads fail (the author's network cut mid-upload): the index arrives, the pieces do not.</summary>
    public bool DropChunkPuts { get; set; }

    /// <summary>The missing-pieces answer comes back in reverse (a real server's set order is not the files').</summary>
    public bool ReverseMissing { get; set; }

    /// <summary>Every piece put, in the order it arrived.</summary>
    public List<string> ChunkPutOrder { get; } = [];

    /// <summary>Each piece put takes this long (a slow uplink).</summary>
    public TimeSpan PutDelay { get; set; }

    /// <summary>Each piece fetched takes this long (a slow downlink).</summary>
    public TimeSpan GetDelay { get; set; }

    /// <summary>The cloud relay routes (contract syn-cloud-relay); off, the server is the one SYN 1.2 knew.</summary>
    public bool Relay { get; init; }

    /// <summary>How the server lists each device; by default a member account with seat id 8f3a1c.</summary>
    public Func<FakeDevice, SyncServerDevice>? Describe { get; init; }

    public IReadOnlyCollection<string> ChunksHeld
    {
        get
        {
            lock (_gate)
                return _chunks.Keys.ToArray();
        }
    }

    public SyncServerClient Client() => new(new HttpClient(this, disposeHandler: false) { BaseAddress = new Uri("https://erk-s.test") });

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string path = request.RequestUri!.AbsolutePath;
        string token = request.Headers.Authorization?.Parameter ?? "";
        const string relay = "/api/cloud-era/v1/sync/device/folders/f-1";
        if (path == "/api/cloud-era/v1/sync/device/session")
        {
            using var body = System.Text.Json.JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            string key = body.RootElement.GetProperty("deviceKeyId").GetString() ?? "";
            Sessions++;
            return Json(new { accessToken = "t-" + key, accessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(12), deviceKeyId = key });
        }
        if (Relay && path.StartsWith(relay, StringComparison.Ordinal))
        {
            string rest = path[relay.Length..];
            byte[] content = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            lock (_gate)
            {
                if (rest == "/index" && request.Method == HttpMethod.Put && OutdatedWrites > 0)
                {
                    OutdatedWrites--;
                    return new HttpResponseMessage((HttpStatusCode)426) { Content = JsonContent.Create(new { code = "sync_client_outdated", message = "Erk-S Sync хуучирсан — Studio-г шинэчилнэ үү." }) };
                }
                if (rest == "/index" && request.Method == HttpMethod.Put)
                {
                    IndexPuts++;
                    _indexes[token] = System.Text.Encoding.UTF8.GetString(content);
                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                }
                if (rest.StartsWith("/devices/", StringComparison.Ordinal) && rest.EndsWith("/index", StringComparison.Ordinal))
                {
                    string key = Uri.UnescapeDataString(rest["/devices/".Length..^"/index".Length]);
                    if (!_indexes.TryGetValue("t-" + key, out string? index))
                        return new HttpResponseMessage(HttpStatusCode.NotFound);
                    if (MarksIncomplete)
                    {
                        // SRV 18200f7: an entry whose last piece has not arrived is marked, not dropped.
                        var node = System.Text.Json.Nodes.JsonNode.Parse(index)!;
                        foreach (var entry in node["entries"]!.AsArray())
                        {
                            if (entry!["chunkSha256"]!.AsArray().Any(sha => !_chunks.ContainsKey(sha!.GetValue<string>())))
                                entry["incomplete"] = true;
                        }
                        index = node.ToJsonString();
                    }
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(index) };
                }
                if (rest == "/redactions" && RedactionsRoute)
                {
                    if (request.Method == HttpMethod.Post)
                    {
                        Redactions.Add(System.Text.Encoding.UTF8.GetString(content));
                        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
                    }
                    return Redactions.Count == 0
                        ? new HttpResponseMessage(HttpStatusCode.NotFound)
                        : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[" + string.Join(",", Redactions) + "]") };
                }
                if (rest == "/chunks/missing" && UnnamedAnswers > 0)
                {
                    UnnamedAnswers--;
                    using var asked = System.Text.Json.JsonDocument.Parse(content);
                    string[] unnamed = asked.RootElement.GetProperty("sha256").EnumerateArray().Select(item => item.GetString() ?? "").ToArray();
                    return Json(new { missing = Array.Empty<string>(), unnamed });
                }
                if (rest == "/chunks/missing")
                {
                    using var body = System.Text.Json.JsonDocument.Parse(content);
                    string[] missing = body.RootElement.GetProperty("sha256").EnumerateArray()
                        .Select(item => item.GetString() ?? "").Where(sha => !_chunks.ContainsKey(sha)).ToArray();
                    if (ReverseMissing)
                        Array.Reverse(missing);
                    return Json(new { missing });
                }
                if (rest.StartsWith("/chunks/", StringComparison.Ordinal))
                {
                    string sha = Uri.UnescapeDataString(rest["/chunks/".Length..]);
                    if (request.Method == HttpMethod.Put)
                    {
                        if (PutDelay > TimeSpan.Zero)
                        {
                            Monitor.Exit(_gate);
                            try
                            {
                                Thread.Sleep(PutDelay);
                            }
                            finally
                            {
                                Monitor.Enter(_gate);
                            }
                        }
                        if (DropChunkPuts)
                            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                        if (!Convert.ToHexStringLower(SHA256.HashData(content)).Equals(sha, StringComparison.OrdinalIgnoreCase))
                            return new HttpResponseMessage(HttpStatusCode.BadRequest);
                        _chunks[sha] = content;
                        ChunkPutOrder.Add(sha);
                        return new HttpResponseMessage(HttpStatusCode.NoContent);
                    }
                    if (GetDelay > TimeSpan.Zero)
                    {
                        Monitor.Exit(_gate);
                        try
                        {
                            Thread.Sleep(GetDelay);
                        }
                        finally
                        {
                            Monitor.Enter(_gate);
                        }
                    }
                    return _chunks.TryGetValue(sha, out byte[]? held)
                        ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(held) }
                        : new HttpResponseMessage(HttpStatusCode.NotFound);
                }
            }
        }
        HttpResponseMessage response = path switch
        {
            "/api/cloud-era/v1/sync/device/challenge" => Json(new { nonce = "AAEC", expiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(2) }),
            "/api/cloud-era/v1/sync/device/heartbeat" => new HttpResponseMessage(HttpStatusCode.NoContent),
            "/api/cloud-era/v1/sync/device/folders" => Json(new
            {
                folders = new[]
                {
                    new
                    {
                        folder = new SyncServerFolder("f-1", "p-1", "ERIN-2026", "Эрин", canonicalPath, "owner@example.mn", "", DateTimeOffset.UtcNow),
                        devices = devices.Select(device => Describe?.Invoke(device) ?? new SyncServerDevice(device.DeviceKeyId, "", "member@example.mn", "", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "8f3a1c")).ToArray(),
                    },
                },
            }),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        };
        return response;
    }

    private static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };
}
