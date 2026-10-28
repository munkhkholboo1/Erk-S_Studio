using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace ErkS.Updates.Tests;

/// <summary>
/// EE / Master / Erk-S Sync, 2026-10-28: a partner's machine said only «The SSL connection could not be
/// established». A failed request now says its class (tls, dns, refused …) and cause - the certificate's
/// issuer, its validity against this machine's clock - without needing the internet to test it.
/// </summary>
public sealed class ACONNECTIONThatFAILSSaysWHYTests
{
    [Fact]
    public async Task AnINTERCEPTEDCertificateNamesItsIssuer()
    {
        using X509Certificate2 certificate = SelfSigned("Contoso AntiVirus Web Shield", DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        await using var server = new TlsServer(certificate);

        NetworkFailureReason reason = await FailureAsync(server.Url);

        Assert.Equal("tls", reason.Code);
        Assert.Equal("tls-intercepted", reason.Detail);
        Assert.Contains("Contoso AntiVirus Web Shield", reason.Message);
        Assert.Contains("SSL", reason.Message);
    }

    [Fact]
    public async Task AnEXPIREDCertificateBlamesTheCLOCK()
    {
        using X509Certificate2 certificate = SelfSigned("Expired Test", DateTimeOffset.UtcNow.AddDays(-60), DateTimeOffset.UtcNow.AddDays(-30));
        await using var server = new TlsServer(certificate);

        NetworkFailureReason reason = await FailureAsync(server.Url);

        Assert.Equal(("tls", "tls-clock"), (reason.Code, reason.Detail));
        Assert.Contains("цаг", reason.Message);
    }

    [Fact]
    public async Task AnUNKNOWNHostIsDNSAndACLOSEDPortIsREFUSED()
    {
        NetworkFailureReason dns = await FailureAsync(new Uri("https://erks-nothing-here.invalid/"));
        Assert.Equal("dns", dns.Code);

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop(); // nothing listens there now
        NetworkFailureReason refused = await FailureAsync(new Uri($"https://127.0.0.1:{port}/"));
        Assert.Equal("refused", refused.Code);
    }

    [Fact]
    public void AREASONIsExplainedOnceAndTheCHAINIsKept()
    {
        var inner = new System.Security.Authentication.AuthenticationException("The remote certificate is invalid because of errors in the certificate chain: UntrustedRoot");
        var outer = new HttpRequestException(HttpRequestError.SecureConnectionError, "The SSL connection could not be established, see inner exception.", inner);

        NetworkFailureReason reason = NetworkFailure.Explain(outer, "erk-s.mn");

        Assert.Equal("tls", reason.Code);
        Assert.Contains("UntrustedRoot", reason.Message);
        Assert.Contains("erk-s.mn", reason.Message);
        Assert.True(NetworkFailure.IsConnection(outer));
    }

    private static async Task<NetworkFailureReason> FailureAsync(Uri url)
    {
        using HttpClient client = NetworkFailure.CreateClient(TimeSpan.FromSeconds(15));
        HttpRequestException failure = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(url));
        NetworkFailureReason reason = NetworkFailure.Explain(failure);
        Assert.Equal(reason.Message, failure.Message); // what the caller shows is the reason itself
        return reason;
    }

    private static X509Certificate2 SelfSigned(string name, DateTimeOffset from, DateTimeOffset until)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest($"CN={name}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        using X509Certificate2 created = request.CreateSelfSigned(from, until);
        return X509CertificateLoader.LoadPkcs12(created.Export(X509ContentType.Pfx), null);
    }

    /// <summary>A loopback TLS endpoint that presents one certificate and closes.</summary>
    private sealed class TlsServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _serving;

        public TlsServer(X509Certificate2 certificate)
        {
            _listener.Start();
            Url = new Uri($"https://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/");
            _serving = Task.Run(async () =>
            {
                while (!_stop.IsCancellationRequested)
                {
                    try
                    {
                        using TcpClient client = await _listener.AcceptTcpClientAsync(_stop.Token);
                        await using var tls = new SslStream(client.GetStream());
                        await tls.AuthenticateAsServerAsync(certificate);
                    }
                    catch (Exception exception) when (exception is IOException or System.Security.Authentication.AuthenticationException or OperationCanceledException or SocketException)
                    {
                    }
                }
            });
        }

        public Uri Url { get; }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            _listener.Stop();
            try
            {
                await _serving.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (TimeoutException)
            {
            }
            _stop.Dispose();
        }
    }
}
