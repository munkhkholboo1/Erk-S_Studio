using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace ErkS.Sync.Core;

/// <summary>Who this machine is on the LAN: its key id and the certificate over its device key.</summary>
public sealed record SyncLanIdentity(string DeviceKeyId, X509Certificate2 Certificate);

/// <summary>A machine heard on the LAN. Says where it listens; proves nothing until TLS.</summary>
public sealed record SyncLanAnnouncement(int V, string DeviceKeyId, int Port, IReadOnlyList<string> FolderIds);

public sealed record SyncLanPeer(string DeviceKeyId, IPEndPoint Endpoint, IReadOnlyList<string> FolderIds, DateTimeOffset HeardAtUtc);

/// <summary>
/// Finds the folder's other machines on the local network.
///
/// ⚠ A PLAIN MULTICAST HELLO, NOT A TRUST DECISION. An announcement only says «I am at this
/// address»; who is really there is settled by mutual TLS against the public keys the
/// server lists for the folder. A forged hello gets a connection refused at the handshake.
/// (Task 1.1 names mDNS; this is a minimal multicast of the same role on a private group, so
/// it needs no service registration and two instances on one machine hear each other.)
/// </summary>
public sealed class SyncLanDiscovery : IDisposable
{
    public static readonly IPAddress Group = IPAddress.Parse("239.255.77.77");
    public const int Port = 47321;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly UdpClient _udp;
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentDictionary<string, SyncLanPeer> _peers = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _selfDeviceKeyId;

    public SyncLanDiscovery(string selfDeviceKeyId)
    {
        _selfDeviceKeyId = selfDeviceKeyId;
        _udp = new UdpClient(AddressFamily.InterNetwork);
        _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _udp.Client.Bind(new IPEndPoint(IPAddress.Any, Port));
        _udp.JoinMulticastGroup(Group);
        _udp.MulticastLoopback = true;
        _ = ListenAsync(_stop.Token);
    }

    public IReadOnlyCollection<SyncLanPeer> Peers => _peers.Values.ToList();

    public SyncLanPeer? Find(string deviceKeyId) =>
        _peers.TryGetValue(deviceKeyId, out SyncLanPeer? peer) ? peer : null;

    public async Task AnnounceAsync(SyncLanAnnouncement announcement, CancellationToken cancellationToken = default)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(announcement, Json);
        await _udp.SendAsync(payload, new IPEndPoint(Group, Port), cancellationToken);
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                UdpReceiveResult received = await _udp.ReceiveAsync(cancellationToken);
                SyncLanAnnouncement? heard = JsonSerializer.Deserialize<SyncLanAnnouncement>(received.Buffer, Json);
                if (heard is null || heard.V != 1 || string.IsNullOrWhiteSpace(heard.DeviceKeyId) ||
                    heard.DeviceKeyId.Equals(_selfDeviceKeyId, StringComparison.OrdinalIgnoreCase) ||
                    heard.Port is <= 0 or > 65535)
                {
                    continue;
                }
                _peers[heard.DeviceKeyId] = new SyncLanPeer(
                    heard.DeviceKeyId,
                    new IPEndPoint(received.RemoteEndPoint.Address, heard.Port),
                    heard.FolderIds ?? [],
                    DateTimeOffset.UtcNow);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception) when (exception is SocketException or JsonException or ObjectDisposedException)
            {
                if (cancellationToken.IsCancellationRequested)
                    return;
            }
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _udp.Dispose();
        _stop.Dispose();
    }
}

/// <summary>
/// Serves this machine's index of a folder to the folder's other machines, and fetches theirs.
///
/// 🔴 BOTH ENDS PROVE THEIR KEY. The TLS handshake requires a client certificate; after it,
/// the public key on each side must be one the SERVER lists as attached to the folder asked
/// about. A key that is not on the folder's list gets nothing, whatever it announced.
/// Only the index travels - paths, sizes, hashes. File content is step 1.2.
/// </summary>
public sealed class SyncLanExchange : IAsyncDisposable
{
    private const int MaxFrame = 256 * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly SyncLanIdentity _identity;
    private readonly Func<string, IReadOnlySet<string>> _allowedDevices;
    private readonly Func<string, string?> _indexJson;
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stop = new();

    /// <param name="allowedDevices">Device key ids the server lists for a folder id.</param>
    /// <param name="indexJson">This machine's index of a folder, serialized, or null when it holds none.</param>
    public SyncLanExchange(
        SyncLanIdentity identity,
        Func<string, IReadOnlySet<string>> allowedDevices,
        Func<string, string?> indexJson,
        int port = 0)
    {
        _identity = identity;
        _allowedDevices = allowedDevices;
        _indexJson = indexJson;
        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();
        _ = AcceptAsync(_stop.Token);
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>Asks <paramref name="peerDeviceKeyId"/> at <paramref name="endpoint"/> for its index of a folder.</summary>
    public async Task<SyncIndex> FetchIndexAsync(IPEndPoint endpoint, string peerDeviceKeyId, string folderId, CancellationToken cancellationToken)
    {
        using var client = new TcpClient(endpoint.AddressFamily);
        await client.ConnectAsync(endpoint, cancellationToken);
        await using var tls = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
        await tls.AuthenticateAsClientAsync(
            new SslClientAuthenticationOptions
            {
                TargetHost = "erks-sync",
                ClientCertificates = [_identity.Certificate],
                EnabledSslProtocols = SslProtocols.Tls13 | SslProtocols.Tls12,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                RemoteCertificateValidationCallback = (_, _, _, _) => true,
            },
            cancellationToken);
        string remote = KeyIdOf(tls.RemoteCertificate);
        if (!remote.Equals(peerDeviceKeyId, StringComparison.OrdinalIgnoreCase) ||
            !_allowedDevices(folderId).Contains(remote))
        {
            throw new SyncLanException("sync_lan_peer_not_attached", "Хариулсан төхөөрөмж энэ хавтсанд холбогдоогүй байна.");
        }

        await WriteFrameAsync(tls, JsonSerializer.SerializeToUtf8Bytes(new ExchangeRequest(folderId), Json), cancellationToken);
        ExchangeResponse response = JsonSerializer.Deserialize<ExchangeResponse>(await ReadFrameAsync(tls, cancellationToken), Json)
            ?? throw new SyncLanException("sync_lan_bad_reply", "Хариу уншигдсангүй.");
        if (!string.IsNullOrEmpty(response.Error))
            throw new SyncLanException(response.Error, "Төхөөрөмж индексээ өгсөнгүй: " + response.Error);
        return SyncIndexStore.Deserialize(response.Index ?? "")
            ?? throw new SyncLanException("sync_lan_bad_index", "Индекс уншигдсангүй.");
    }

    private async Task AcceptAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }
            _ = ServeAsync(client, cancellationToken);
        }
    }

    private async Task ServeAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromMinutes(2));
                await using var tls = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
                await tls.AuthenticateAsServerAsync(
                    new SslServerAuthenticationOptions
                    {
                        ServerCertificate = _identity.Certificate,
                        ClientCertificateRequired = true,
                        EnabledSslProtocols = SslProtocols.Tls13 | SslProtocols.Tls12,
                        CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                        RemoteCertificateValidationCallback = (_, certificate, _, _) => certificate is not null,
                    },
                    timeout.Token);
                string remote = KeyIdOf(tls.RemoteCertificate);
                ExchangeRequest? request = JsonSerializer.Deserialize<ExchangeRequest>(await ReadFrameAsync(tls, timeout.Token), Json);
                ExchangeResponse response;
                if (request is null || string.IsNullOrWhiteSpace(request.FolderId))
                    response = new ExchangeResponse("sync_lan_bad_request", null);
                else if (remote.Length == 0 || !_allowedDevices(request.FolderId).Contains(remote))
                    response = new ExchangeResponse("sync_lan_peer_not_attached", null);
                else if (_indexJson(request.FolderId) is not { } index)
                    response = new ExchangeResponse("sync_lan_no_index", null);
                else
                    response = new ExchangeResponse(null, index);
                await WriteFrameAsync(tls, JsonSerializer.SerializeToUtf8Bytes(response, Json), timeout.Token);
            }
            catch (Exception exception) when (exception is IOException or AuthenticationException or JsonException or OperationCanceledException or SocketException or InvalidDataException)
            {
                // A peer that cannot complete the handshake or the request simply gets nothing.
            }
        }
    }

    /// <summary>The device key id behind a certificate: SHA-256 of its public key, as the server computes it.</summary>
    public static string KeyIdOf(X509Certificate? certificate)
    {
        if (certificate is null)
            return "";
        using var certificate2 = new X509Certificate2(certificate);
        using ECDsa? key = certificate2.GetECDsaPublicKey();
        return key is null ? "" : SyncDeviceKeyIds.Of(key.ExportSubjectPublicKeyInfo());
    }

    private static async Task WriteFrameAsync(Stream stream, byte[] payload, CancellationToken cancellationToken)
    {
        byte[] length = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, payload.Length);
        await stream.WriteAsync(length, cancellationToken);
        await stream.WriteAsync(payload, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static async Task<byte[]> ReadFrameAsync(Stream stream, CancellationToken cancellationToken)
    {
        byte[] length = new byte[4];
        await stream.ReadExactlyAsync(length, cancellationToken);
        int size = BinaryPrimitives.ReadInt32BigEndian(length);
        if (size is < 0 or > MaxFrame)
            throw new InvalidDataException("frame too large");
        byte[] payload = new byte[size];
        await stream.ReadExactlyAsync(payload, cancellationToken);
        return payload;
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        _stop.Dispose();
    }

    private sealed record ExchangeRequest(string FolderId);

    private sealed record ExchangeResponse(string? Error, string? Index);
}

public sealed class SyncLanException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>The one derivation of a device key id from a public key, shared by every caller.</summary>
public static class SyncDeviceKeyIds
{
    public static string Of(byte[] subjectPublicKeyInfo) =>
        Convert.ToHexString(SHA256.HashData(subjectPublicKeyInfo));
}
