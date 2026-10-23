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
    public FakeDevice[] Devices => devices.ToArray();

    public SyncServerClient Client() => new(new HttpClient(this, disposeHandler: false) { BaseAddress = new Uri("https://erk-s.test") });

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string path = request.RequestUri!.AbsolutePath;
        HttpResponseMessage response = path switch
        {
            "/api/cloud-era/v1/sync/device/challenge" => Json(new { nonce = "AAEC", expiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(2) }),
            "/api/cloud-era/v1/sync/device/session" => Json(new { accessToken = "t", accessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(12), deviceKeyId = "x" }),
            "/api/cloud-era/v1/sync/device/heartbeat" => new HttpResponseMessage(HttpStatusCode.NoContent),
            "/api/cloud-era/v1/sync/device/folders" => Json(new
            {
                folders = new[]
                {
                    new
                    {
                        folder = new SyncServerFolder("f-1", "p-1", "ERIN-2026", "Эрин", canonicalPath, "owner@example.mn", "", DateTimeOffset.UtcNow),
                        devices = devices.Select(device => new SyncServerDevice(device.DeviceKeyId, "", "member@example.mn", "", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)).ToArray(),
                    },
                },
            }),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        };
        return Task.FromResult(response);
    }

    private static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };
}
