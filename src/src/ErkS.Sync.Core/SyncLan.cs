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
/// <param name="Stamps">Per folder, a short stamp of this machine's index - a partner on the LAN that
/// sees it change takes the change at once instead of waiting for its next pass. Absent from an older SYN.</param>
public sealed record SyncLanAnnouncement(int V, string DeviceKeyId, int Port, IReadOnlyList<string> FolderIds, IReadOnlyDictionary<string, string>? Stamps = null);

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
    private readonly ConcurrentDictionary<string, string> _stamps = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A partner here announced a changed index of a folder (deviceKeyId, folderId).</summary>
    public event Action<string, string>? IndexChanged;

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

    /// <summary>
    /// Sends the announcement to the multicast group and, one by one, to every machine already known
    /// here (heard, learned from its connection, or named by the server) - a network that drops
    /// multicast between two machines still carries it (Erk-S Sync, 2026-10-27: the desktop never heard
    /// the laptop's multicast, the laptop heard the desktop's).
    /// </summary>
    public async Task AnnounceAsync(SyncLanAnnouncement announcement, CancellationToken cancellationToken = default, IEnumerable<IPAddress>? alsoTo = null)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(announcement, Json);
        await _udp.SendAsync(payload, new IPEndPoint(Group, Port), cancellationToken);
        IEnumerable<IPAddress> targets = _peers.Values.Select(peer => peer.Endpoint.Address).Concat(alsoTo ?? []);
        foreach (IPAddress target in targets.Where(SyncLanAddresses.IsPrivate).Distinct())
        {
            try
            {
                await _udp.SendAsync(payload, new IPEndPoint(target, Port), cancellationToken);
            }
            catch (SocketException)
            {
                // That one is gone; the others still hear.
            }
        }
    }

    /// <summary>
    /// A machine that opened a verified connection here, at the address it came from and the port it
    /// listens on: it is found from now on as if heard - its multicast may never reach this machine.
    /// </summary>
    public void Learn(string deviceKeyId, IPEndPoint endpoint, string folderId)
    {
        if (deviceKeyId.Equals(_selfDeviceKeyId, StringComparison.OrdinalIgnoreCase) || endpoint.Port is <= 0 or > 65535)
            return;
        _peers.AddOrUpdate(
            deviceKeyId,
            _ => new SyncLanPeer(deviceKeyId, endpoint, [folderId], DateTimeOffset.UtcNow),
            (_, known) => known with
            {
                Endpoint = endpoint,
                FolderIds = known.FolderIds.Contains(folderId, StringComparer.OrdinalIgnoreCase) ? known.FolderIds : [.. known.FolderIds, folderId],
                HeardAtUtc = DateTimeOffset.UtcNow,
            });
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
                foreach ((string folderId, string stamp) in heard.Stamps ?? new Dictionary<string, string>())
                {
                    string key = heard.DeviceKeyId + "|" + folderId;
                    if (_stamps.TryGetValue(key, out string? before) && before == stamp)
                        continue;
                    _stamps[key] = stamp;
                    IndexChanged?.Invoke(heard.DeviceKeyId, folderId);
                }
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
/// The index travels, and since 1.2 the pieces of files the asking machine is taking.
/// </summary>
public sealed class SyncLanExchange : IAsyncDisposable
{
    private const int MaxFrame = 256 * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly SyncLanIdentity _identity;
    private readonly Func<string, IReadOnlySet<string>> _allowedDevices;
    private readonly Func<string, string?> _indexJson;
    private readonly Func<string, string, string, int, byte[]?> _readChunk;
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (SyncLanSession Session, TaskCompletionSource Done)> _reverse = new(StringComparer.OrdinalIgnoreCase);

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTimeOffset> _inbound = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether this partner opened a verified connection to us in the last 10 minutes (then it needs no offer back).</summary>
    public bool ReachedUsLately(string peerDeviceKeyId) =>
        _inbound.TryGetValue(peerDeviceKeyId, out DateTimeOffset at) && DateTimeOffset.UtcNow - at < TimeSpan.FromMinutes(10);

    /// <summary>A partner here opened a connection and offered it back (its firewall lets it reach us, ours not it): run a pass.</summary>
    public event Action? ReverseOffered;

    /// <summary>A verified partner reached us and said where it listens (device key id, its address and port, the folder).</summary>
    public event Action<string, IPEndPoint, string>? PeerReached;

    /// <param name="allowedDevices">Device key ids the server lists for a folder id.</param>
    /// <param name="indexJson">This machine's index of a folder, serialized, or null when it holds none.</param>
    /// <param name="readChunk">
    /// (folder id, relative path, file hash, piece) → the piece's bytes, or null when this machine
    /// does not hold that version of that file (1.2). Only files in this machine's own index.
    /// </param>
    public SyncLanExchange(
        SyncLanIdentity identity,
        Func<string, IReadOnlySet<string>> allowedDevices,
        Func<string, string?> indexJson,
        Func<string, string, string, int, byte[]?>? readChunk = null,
        int port = 0)
    {
        _identity = identity;
        _allowedDevices = allowedDevices;
        _indexJson = indexJson;
        _readChunk = readChunk ?? ((_, _, _, _) => null);
        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();
        _ = AcceptAsync(_stop.Token);
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>Asks <paramref name="peerDeviceKeyId"/> at <paramref name="endpoint"/> for its index of a folder.</summary>
    public async Task<SyncIndex> FetchIndexAsync(IPEndPoint endpoint, string peerDeviceKeyId, string folderId, CancellationToken cancellationToken)
    {
        await using SyncLanSession session = await OpenAsync(endpoint, peerDeviceKeyId, folderId, cancellationToken);
        return await session.FetchIndexAsync(cancellationToken);
    }

    /// <summary>
    /// One verified connection to a folder's other machine, for the index and then the pieces
    /// of its files - the handshake and the key check happen once, not per piece.
    /// </summary>
    public async Task<SyncLanSession> OpenAsync(IPEndPoint endpoint, string peerDeviceKeyId, string folderId, CancellationToken cancellationToken)
    {
        var client = new TcpClient(endpoint.AddressFamily);
        Tune(client);
        SslStream? tls = null;
        try
        {
            await client.ConnectAsync(endpoint, cancellationToken);
            tls = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
            await AuthenticateAsync(tls, peerDeviceKeyId, folderId, cancellationToken);
            return new SyncLanSession(client, tls, folderId, listenPort: Port);
        }
        catch
        {
            if (tls is not null)
                await tls.DisposeAsync();
            client.Dispose();
            throw;
        }
    }

    private async Task AuthenticateAsync(SslStream tls, string peerDeviceKeyId, string folderId, CancellationToken cancellationToken)
    {
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

    /// <summary>Large socket buffers and no Nagle: a 4 MiB piece flows without waiting on small windows (Erk-S Sync, 2026-10-27: LAN at 1/10 of the Wi-Fi).</summary>
    internal static void Tune(TcpClient client)
    {
        client.NoDelay = true;
        client.ReceiveBufferSize = 1024 * 1024;
        client.SendBufferSize = 1024 * 1024;
    }

    private async Task ServeAsync(TcpClient client, CancellationToken cancellationToken)
    {
        Tune(client);
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
                if (remote.Length > 0)
                    _inbound[remote] = DateTimeOffset.UtcNow;
                IPAddress? from = (client.Client.RemoteEndPoint as IPEndPoint)?.Address;
                if (from is { IsIPv4MappedToIPv6: true })
                    from = from.MapToIPv4();
                string? reverseFolder = await ServeRequestsAsync(tls, remote, timeout, acceptReverse: true, from);
                if (reverseFolder is null)
                    return;
                // The partner offered this connection back: this machine asks on it now (its own pass
                // takes it), the partner serves; the connection closes when the pass is done with it.
                var session = new SyncLanSession(null, tls, reverseFolder, ownsTls: false);
                var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                string key = remote + "|" + reverseFolder;
                if (_reverse.TryRemove(key, out var stale))
                    stale.Done.TrySetResult();
                _reverse[key] = (session, done);
                ReverseOffered?.Invoke();
                await Task.WhenAny(done.Task, Task.Delay(TimeSpan.FromMinutes(5), cancellationToken));
                _reverse.TryRemove(new KeyValuePair<string, (SyncLanSession, TaskCompletionSource)>(key, (session, done)));
            }
            catch (Exception exception) when (exception is IOException or AuthenticationException or JsonException or OperationCanceledException or SocketException or InvalidDataException)
            {
                // A peer that cannot complete the handshake or the request simply gets nothing.
            }
        }
    }

    /// <summary>
    /// Answers requests on a verified connection until it closes. Returns the folder of a «reverse»
    /// offer when <paramref name="acceptReverse"/> (this side then asks), else null.
    /// </summary>
    private async Task<string?> ServeRequestsAsync(SslStream tls, string remote, CancellationTokenSource timeout, bool acceptReverse, IPAddress? from = null)
    {
        // One connection carries the index and then pieces; each request is checked
        // against the folder's list again, and the connection lives while it is used.
        while (true)
        {
            byte[]? frame = await TryReadFrameAsync(tls, timeout.Token);
            if (frame is null)
                return null;
            timeout.CancelAfter(TimeSpan.FromMinutes(2));
            ExchangeRequest? request = JsonSerializer.Deserialize<ExchangeRequest>(frame, Json);
            string? refusal =
                request is null || string.IsNullOrWhiteSpace(request.FolderId) ? "sync_lan_bad_request" :
                remote.Length == 0 || !_allowedDevices(request.FolderId).Contains(remote) ? "sync_lan_peer_not_attached" :
                null;
            if (refusal is not null)
            {
                await WriteFrameAsync(tls, JsonSerializer.SerializeToUtf8Bytes(new ExchangeResponse(refusal, null), Json), timeout.Token);
                return null;
            }
            if (from is not null && request!.ListenPort is > 0 and <= 65535)
                PeerReached?.Invoke(remote, new IPEndPoint(from, request.ListenPort.Value), request.FolderId);
            if (request!.Kind == "reverse" && acceptReverse)
            {
                await WriteFrameAsync(tls, JsonSerializer.SerializeToUtf8Bytes(new ExchangeResponse(null, null, Reverse: true), Json), timeout.Token);
                return request.FolderId;
            }
            if (request.Kind == "chunk")
            {
                byte[]? piece = request.Path is { Length: > 0 } path && request.Sha256 is { Length: > 0 } sha && request.Chunk >= 0
                    ? _readChunk(request.FolderId, path, sha, request.Chunk)
                    : null;
                await WriteFrameAsync(tls, JsonSerializer.SerializeToUtf8Bytes(new ExchangeResponse(piece is null ? "sync_lan_no_chunk" : null, null), Json), timeout.Token);
                if (piece is not null)
                    await WriteFrameAsync(tls, piece, timeout.Token);
                continue;
            }
            ExchangeResponse response = _indexJson(request.FolderId) is { } index
                ? new ExchangeResponse(null, index)
                : new ExchangeResponse("sync_lan_no_index", null);
            await WriteFrameAsync(tls, JsonSerializer.SerializeToUtf8Bytes(response, Json), timeout.Token);
        }
    }

    /// <summary>
    /// The partner's connection offered back to this machine, when there is one for the folder: this
    /// side asks on it as on its own. <see cref="Release"/> it when done.
    /// </summary>
    public SyncLanSession? TakeReverse(string peerDeviceKeyId, string folderId) =>
        _reverse.TryGetValue(peerDeviceKeyId + "|" + folderId, out var offered) ? offered.Session : null;

    public void Release(string peerDeviceKeyId, string folderId)
    {
        if (_reverse.TryRemove(peerDeviceKeyId + "|" + folderId, out var offered))
            offered.Done.TrySetResult();
    }

    /// <summary>
    /// After this machine took what it needed on <paramref name="session"/>, offer the connection back so
    /// the partner - which may not be able to open one to us (a firewall on this side) - asks on it too
    /// (Erk-S Sync, 2026-10-26: LAN worked one way only). This side serves until the partner closes it.
    /// An older partner answers with its index instead: then nothing happens.
    /// </summary>
    public async Task OfferBackAsync(SyncLanSession session, string peerDeviceKeyId, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        ExchangeResponse answer = await session.AskAsync(new ExchangeRequest(session.FolderId, "reverse"), timeout.Token);
        if (answer.Reverse != true)
            return;
        try
        {
            await ServeRequestsAsync(session.Tls, peerDeviceKeyId, timeout, acceptReverse: false);
        }
        catch (Exception exception) when (exception is IOException or JsonException or OperationCanceledException or InvalidDataException)
        {
            // The partner went: what it took, it took.
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

    /// <summary>The next frame, or null when the other end closed the connection between frames.</summary>
    private static async Task<byte[]?> TryReadFrameAsync(Stream stream, CancellationToken cancellationToken)
    {
        byte[] length = new byte[4];
        int read = await stream.ReadAtLeastAsync(length, 4, throwOnEndOfStream: false, cancellationToken);
        if (read == 0)
            return null;
        if (read < 4)
            throw new InvalidDataException("frame header cut short");
        int size = BinaryPrimitives.ReadInt32BigEndian(length);
        if (size is < 0 or > MaxFrame)
            throw new InvalidDataException("frame too large");
        byte[] payload = new byte[size];
        await stream.ReadExactlyAsync(payload, cancellationToken);
        return payload;
    }

    internal static async Task WriteFrameAsync(Stream stream, byte[] payload, CancellationToken cancellationToken)
    {
        byte[] length = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, payload.Length);
        await stream.WriteAsync(length, cancellationToken);
        await stream.WriteAsync(payload, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    internal static async Task<byte[]> ReadFrameAsync(Stream stream, CancellationToken cancellationToken)
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

    /// <summary>«index» (or none, as 1.1 sent it) or «chunk» with the file, its hash and the piece.</summary>
    /// <param name="ListenPort">Where the asking machine listens (sent with «index»): the serving side can reach it back.</param>
    internal sealed record ExchangeRequest(string FolderId, string? Kind = null, string? Path = null, string? Sha256 = null, int Chunk = -1, int? ListenPort = null);

    /// <param name="Reverse">The offer of a «reverse» was taken: the asking side serves from now on.</param>
    internal sealed record ExchangeResponse(string? Error, string? Index, bool? Reverse = null);
}

/// <summary>An open, verified connection to one machine of one folder.</summary>
public sealed class SyncLanSession : IAsyncDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly TcpClient? _client;
    private readonly SslStream _tls;
    private readonly string _folderId;
    private readonly bool _ownsTls;
    private readonly int _listenPort;

    internal SyncLanSession(TcpClient? client, SslStream tls, string folderId, bool ownsTls = true, int listenPort = 0)
    {
        _client = client;
        _tls = tls;
        _folderId = folderId;
        _ownsTls = ownsTls;
        _listenPort = listenPort;
    }

    internal SslStream Tls => _tls;

    internal string FolderId => _folderId;

    public async Task<SyncIndex> FetchIndexAsync(CancellationToken cancellationToken)
    {
        SyncLanExchange.ExchangeResponse response = await AskAsync(
            new SyncLanExchange.ExchangeRequest(_folderId, "index", ListenPort: _listenPort > 0 ? _listenPort : null), cancellationToken);
        if (!string.IsNullOrEmpty(response.Error))
            throw new SyncLanException(response.Error, "Төхөөрөмж индексээ өгсөнгүй: " + response.Error);
        return SyncIndexStore.Deserialize(response.Index ?? "")
            ?? throw new SyncLanException("sync_lan_bad_index", "Индекс уншигдсангүй.");
    }

    /// <summary>
    /// Piece <paramref name="chunk"/> of the version <paramref name="sha256"/> of a file - unchecked; the caller checks its hash.
    /// Calls may overlap: the requests go out at once and the answers are read back in the same order
    /// (pipelining - the serving side answers one after another on the one connection), so the next
    /// pieces are already on the way while one is written to disk.
    /// </summary>
    public async Task<byte[]> FetchChunkAsync(string relativePath, string sha256, int chunk, CancellationToken cancellationToken) =>
        (await AskCoreAsync(new SyncLanExchange.ExchangeRequest(_folderId, "chunk", relativePath, sha256, chunk), cancellationToken, withPiece: true)).Piece!;

    private readonly SemaphoreSlim _send = new(1, 1);
    private Task _readTail = Task.CompletedTask;
    private volatile bool _broken;

    internal async Task<SyncLanExchange.ExchangeResponse> AskAsync(SyncLanExchange.ExchangeRequest request, CancellationToken cancellationToken) =>
        (await AskCoreAsync(request, cancellationToken, withPiece: false)).Response;

    private async Task<(SyncLanExchange.ExchangeResponse Response, byte[]? Piece)> AskCoreAsync(SyncLanExchange.ExchangeRequest request, CancellationToken cancellationToken, bool withPiece)
    {
        var mine = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task before;
        await _send.WaitAsync(cancellationToken);
        try
        {
            if (_broken)
                throw new IOException("The LAN connection broke.");
            await SyncLanExchange.WriteFrameAsync(_tls, JsonSerializer.SerializeToUtf8Bytes(request, Json), cancellationToken);
            before = _readTail;
            _readTail = mine.Task;
        }
        catch
        {
            _broken = true;
            throw;
        }
        finally
        {
            _send.Release();
        }
        try
        {
            await before;
            if (_broken)
                throw new IOException("The LAN connection broke.");
            SyncLanExchange.ExchangeResponse response = JsonSerializer.Deserialize<SyncLanExchange.ExchangeResponse>(
                    await SyncLanExchange.ReadFrameAsync(_tls, cancellationToken), Json)
                ?? throw new SyncLanException("sync_lan_bad_reply", "Хариу уншигдсангүй.");
            if (!withPiece)
                return (response, null);
            if (!string.IsNullOrEmpty(response.Error))
                throw new SyncLanException(response.Error, "Төхөөрөмж файлын хэсгийг өгсөнгүй (тэр хувилбар өөрчлөгдсөн байж магадгүй).");
            return (response, await SyncLanExchange.ReadFrameAsync(_tls, cancellationToken));
        }
        catch (Exception exception) when (exception is not SyncLanException)
        {
            _broken = true; // a read cut short leaves the stream between frames: nothing after it can be read
            throw;
        }
        finally
        {
            mine.TrySetResult();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!_ownsTls)
            return; // a connection offered back: its owner closes it
        await _tls.DisposeAsync();
        _client?.Dispose();
    }
}

public sealed class SyncLanException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>This machine's private LAN addresses, and the check that an address is one.</summary>
public static class SyncLanAddresses
{
    /// <summary>10/8, 172.16/12, 192.168/16 - the only addresses SYN tells the server or announces to (never public, never link-local).</summary>
    public static bool IsPrivate(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        if (address.AddressFamily != AddressFamily.InterNetwork)
            return false;
        byte[] b = address.GetAddressBytes();
        return b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168);
    }

    /// <summary>«ip:port» for each private IPv4 of an interface that is up (not loopback, not a tunnel).</summary>
    public static IReadOnlyList<string> Own(int port)
    {
        if (port <= 0)
            return [];
        var found = new List<string>();
        try
        {
            foreach (System.Net.NetworkInformation.NetworkInterface nic in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up ||
                    nic.NetworkInterfaceType is System.Net.NetworkInformation.NetworkInterfaceType.Loopback or System.Net.NetworkInformation.NetworkInterfaceType.Tunnel)
                {
                    continue;
                }
                foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
                {
                    if (IsPrivate(unicast.Address))
                        found.Add(unicast.Address + ":" + port);
                }
            }
        }
        catch (System.Net.NetworkInformation.NetworkInformationException)
        {
        }
        return found.Distinct().ToList();
    }

    /// <summary>The private endpoints a partner told the server it listens on.</summary>
    public static IReadOnlyList<IPEndPoint> Parse(IEnumerable<string>? endpoints) =>
        (endpoints ?? [])
            .Select(text => IPEndPoint.TryParse(text, out IPEndPoint? endpoint) ? endpoint : null)
            .Where(endpoint => endpoint is not null && endpoint.Port is > 0 and <= 65535 && IsPrivate(endpoint.Address))
            .Select(endpoint => endpoint!)
            .ToList();
}

/// <summary>The one derivation of a device key id from a public key, shared by every caller.</summary>
public static class SyncDeviceKeyIds
{
    public static string Of(byte[] subjectPublicKeyInfo) =>
        Convert.ToHexString(SHA256.HashData(subjectPublicKeyInfo));
}
