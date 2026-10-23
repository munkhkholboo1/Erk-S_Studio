using System.Net;
using System.Security.Cryptography.X509Certificates;

namespace ErkS.Sync.Core;

/// <summary>What the engine needs of this machine's key - a seam so tests use throwaway keys.</summary>
public interface ISyncDevice
{
    string DeviceKeyId { get; }

    byte[] SignSession(byte[] nonce);

    X509Certificate2 TlsCertificate { get; }
}

/// <summary>
/// Maps a folder's canonical path to where it is on THIS disk. Identity in production; a
/// second instance on the same machine maps the canonical root to its own scratch folder
/// (ERKS_SYNC_PATH_MAP="D:\Cloud Work=>E:\scratch\b") - the task's «логик зам ба физик хавтсыг
/// тусгаарлаж хоёр instance-ийг нэг машин дээр туршина».
/// </summary>
public sealed class SyncPathMap
{
    private readonly (string From, string To)? _map;

    public SyncPathMap(string? specification = null)
    {
        specification ??= Environment.GetEnvironmentVariable("ERKS_SYNC_PATH_MAP");
        if (string.IsNullOrWhiteSpace(specification))
            return;
        string[] parts = specification.Split("=>", 2, StringSplitOptions.TrimEntries);
        if (parts.Length == 2 && parts[0].Length > 0 && parts[1].Length > 0)
            _map = (SyncPath.Canonical(parts[0]), SyncPath.Canonical(parts[1]));
    }

    public bool IsMapped => _map is not null;

    public string Physical(string canonicalPath)
    {
        string canonical = SyncPath.Canonical(canonicalPath);
        if (_map is not { } map || !SyncPath.IsSameOrInside(map.From, canonical))
            return canonical;
        string rest = canonical.Length > map.From.Length ? canonical[map.From.Length..].TrimStart('\\') : "";
        return rest.Length == 0 ? map.To : Path.Combine(map.To, rest);
    }
}

/// <summary>
/// The background half of SYN 1.1, run by ErkS.Sync.exe with or without Studio:
/// device session → the folders this device is attached to → index each one present here →
/// heartbeat → announce on the LAN → fetch every attached peer's index → write the state.
///
/// 🔴 IT DECIDES NOTHING ABOUT FILES. It reads, hashes and compares; it never writes into a
/// project folder (1.1 moves no content) and never resolves a difference.
/// </summary>
public sealed class SyncEngine : IAsyncDisposable
{
    private readonly SyncHome _home;
    private readonly SyncServerClient _server;
    private readonly ISyncDevice? _device;
    private readonly SyncPathMap _paths;
    private readonly SemaphoreSlim _wake = new(0);
    private readonly Dictionary<string, SyncIndex> _indexes = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private IReadOnlyList<SyncServerDeviceFolder> _folders = [];
    private SyncDeviceSession? _session;
    private SyncLanDiscovery? _discovery;
    private SyncLanExchange? _exchange;

    public SyncEngine(SyncHome home, SyncServerClient server, ISyncDevice? device, SyncPathMap? paths = null)
    {
        _home = home;
        _server = server;
        _device = device;
        _paths = paths ?? new SyncPathMap();
    }

    public TimeSpan Period { get; init; } = TimeSpan.FromSeconds(60);

    public SyncState LastState { get; private set; } = new();

    /// <summary>Run a pass now (a folder was registered, a verb asked for status).</summary>
    public void Poke() => _wake.Release();

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (_device is not null)
        {
            try
            {
                _exchange = new SyncLanExchange(
                    new SyncLanIdentity(_device.DeviceKeyId, _device.TlsCertificate),
                    AllowedDevices,
                    IndexJson);
                _discovery = new SyncLanDiscovery(_device.DeviceKeyId);
            }
            catch (Exception exception) when (exception is System.Net.Sockets.SocketException or IOException)
            {
                // No LAN half (port taken, no network). The server half still runs.
            }
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await PassAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            try
            {
                await _wake.WaitAsync(Period, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    public async Task PassAsync(CancellationToken cancellationToken)
    {
        if (_device is null)
        {
            Write(new SyncState
            {
                ServerStatus = "no_device_key",
                ServerMessage = "Энэ машинд Studio-гийн төхөөрөмжийн түлхүүр алга. Studio-д нэвтэрч нэг удаа нээнэ үү.",
            });
            return;
        }

        string serverStatus = "ok";
        string serverMessage = "";
        try
        {
            await EnsureSessionAsync(cancellationToken);
            IReadOnlyList<SyncServerDeviceFolder> folders = await _server.DeviceFoldersAsync(_session!.AccessToken, cancellationToken);
            lock (_gate)
                _folders = folders;
            await _server.DeviceHeartbeatAsync(_session.AccessToken, cancellationToken);
        }
        catch (SyncServerException exception)
        {
            if (exception.Status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                _session = null;
            serverStatus = exception.Code == "sync_device_session_refused" ? "session_refused" : "server_refused";
            serverMessage = exception.Message;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            serverStatus = "offline";
            serverMessage = "Сервертэй холбогдож чадсангүй — сүүлд мэдсэн хавтсуудаар ажиллаж байна.";
        }

        IReadOnlyList<SyncServerDeviceFolder> known;
        lock (_gate)
            known = _folders;

        var states = new List<SyncFolderState>();
        foreach (SyncServerDeviceFolder entry in known)
            states.Add(await FolderPassAsync(entry, cancellationToken));

        if (_discovery is not null && _exchange is not null)
        {
            try
            {
                await _discovery.AnnounceAsync(
                    new SyncLanAnnouncement(1, _device.DeviceKeyId, _exchange.Port,
                        states.Where(state => state.PresentHere).Select(state => state.FolderId).ToList()),
                    cancellationToken);
            }
            catch (Exception exception) when (exception is System.Net.Sockets.SocketException or ObjectDisposedException)
            {
            }
        }

        Write(new SyncState
        {
            DeviceKeyId = _device.DeviceKeyId,
            ServerStatus = serverStatus,
            ServerMessage = serverMessage,
            LanPort = _exchange?.Port ?? 0,
            Folders = states,
        });
    }

    private async Task EnsureSessionAsync(CancellationToken cancellationToken)
    {
        if (_session is { } session && session.AccessTokenExpiresAtUtc > DateTimeOffset.UtcNow.AddMinutes(10))
            return;
        byte[] nonce = await _server.DeviceChallengeAsync(_device!.DeviceKeyId, cancellationToken);
        _session = await _server.DeviceSessionAsync(_device.DeviceKeyId, nonce, _device.SignSession(nonce), cancellationToken);
    }

    private async Task<SyncFolderState> FolderPassAsync(SyncServerDeviceFolder entry, CancellationToken cancellationToken)
    {
        SyncServerFolder folder = entry.Folder;
        var state = new SyncFolderState
        {
            FolderId = folder.FolderId,
            ProjectId = folder.ProjectId,
            ProjectCode = folder.ProjectCode,
            ProjectName = folder.ProjectName,
            CanonicalPath = folder.CanonicalPath,
        };
        string physical = _paths.Physical(folder.CanonicalPath);
        SyncFolderIdentity? marker = SyncFolderMarker.TryRead(physical);
        if (marker is null || !marker.FolderId.Equals(folder.FolderId, StringComparison.OrdinalIgnoreCase))
        {
            return state with
            {
                LastError = Directory.Exists(physical)
                    ? "Хавтас энэ машинд байна, гэхдээ синк хавтсанд холбогдоогүй."
                    : "Хавтас энэ машинд алга.",
            };
        }

        SyncIndex index;
        try
        {
            SyncIndex? previous;
            lock (_gate)
                previous = _indexes.GetValueOrDefault(folder.FolderId) ?? SyncIndexStore.TryLoad(_home.IndexPath(folder.FolderId));
            index = await Task.Run(() => SyncIndexBuilder.Build(physical, folder.FolderId, previous, cancellationToken), cancellationToken);
            SyncIndexStore.Save(index, _home.IndexPath(folder.FolderId));
            lock (_gate)
                _indexes[folder.FolderId] = index;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return state with { PresentHere = true, LastError = "Индекс үүсгэж чадсангүй: " + exception.Message };
        }

        var peers = new List<SyncPeerState>();
        foreach (SyncServerDevice device in entry.Devices)
        {
            if (device.DeviceKeyId.Equals(_device!.DeviceKeyId, StringComparison.OrdinalIgnoreCase))
                continue;
            peers.Add(await PeerPassAsync(folder.FolderId, device, index, cancellationToken));
        }

        return state with
        {
            PresentHere = true,
            IndexedAtUtc = index.BuiltAtUtc,
            FileCount = index.Entries.Count,
            TotalBytes = index.Entries.Sum(entry => entry.Size),
            Peers = peers,
        };
    }

    private async Task<SyncPeerState> PeerPassAsync(string folderId, SyncServerDevice device, SyncIndex mine, CancellationToken cancellationToken)
    {
        var peer = new SyncPeerState
        {
            DeviceKeyId = device.DeviceKeyId,
            AccountEmail = device.AccountEmail,
            ServerLastSeenAtUtc = device.LastSeenAtUtc,
        };
        string stored = _home.PeerIndexPath(folderId, device.DeviceKeyId);
        SyncLanPeer? heard = _discovery?.Find(device.DeviceKeyId);
        string error = "";
        if (heard is not null && heard.FolderIds.Contains(folderId, StringComparer.OrdinalIgnoreCase) && _exchange is not null)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(30));
                SyncIndex theirs = await _exchange.FetchIndexAsync(heard.Endpoint, device.DeviceKeyId, folderId, timeout.Token);
                SyncIndexStore.Save(theirs with { BuiltAtUtc = DateTimeOffset.UtcNow }, stored);
                peer = peer with { LanAddress = heard.Endpoint.ToString() };
            }
            catch (Exception exception) when (exception is SyncLanException or IOException or System.Security.Authentication.AuthenticationException or System.Net.Sockets.SocketException or OperationCanceledException)
            {
                error = exception is SyncLanException lan ? lan.Message : "LAN-аар холбогдож чадсангүй.";
            }
        }
        else if (heard is null)
        {
            error = "LAN дээр олдсонгүй (унтарсан эсвэл өөр сүлжээнд).";
        }

        SyncIndex? theirsKnown = SyncIndexStore.TryLoad(stored);
        if (theirsKnown is null)
            return peer with { LastError = error };
        SyncIndexDifference difference = SyncIndexComparison.Compare(mine, theirsKnown);
        return peer with
        {
            ExchangedAtUtc = theirsKnown.BuiltAtUtc,
            LastError = error,
            SameCount = difference.SameCount,
            DifferentCount = difference.Different.Count,
            OnlyHereCount = difference.OnlyHere.Count,
            OnlyThereCount = difference.OnlyThere.Count,
        };
    }

    private IReadOnlySet<string> AllowedDevices(string folderId)
    {
        lock (_gate)
        {
            return _folders
                .Where(entry => entry.Folder.FolderId.Equals(folderId, StringComparison.OrdinalIgnoreCase))
                .SelectMany(entry => entry.Devices)
                .Select(device => device.DeviceKeyId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
    }

    private string? IndexJson(string folderId)
    {
        lock (_gate)
            return _indexes.TryGetValue(folderId, out SyncIndex? index) ? SyncIndexStore.Serialize(index) : null;
    }

    private void Write(SyncState state)
    {
        LastState = state with { UpdatedAtUtc = DateTimeOffset.UtcNow, ProcessId = Environment.ProcessId };
        try
        {
            SyncStateStore.Save(_home, LastState);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        _discovery?.Dispose();
        if (_exchange is not null)
            await _exchange.DisposeAsync();
        _wake.Dispose();
    }
}
