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
/// The background half of SYN, run by ErkS.Sync.exe with or without Studio:
/// device session → the folders this device is attached to → index each one present here and
/// give every file its version → heartbeat → announce on the LAN → from every attached peer
/// its index, then (1.2) the files whose newer version it holds → write the state.
///
/// 🔴 IT NEVER RESOLVES A DIFFERENCE BY OVERWRITING. A peer's file replaces this machine's
/// only when its version came after this machine's; two edits of one version are both kept;
/// copies that never shared a version are only shown (<see cref="SyncTransferPlanner"/>).
/// </summary>
public sealed partial class SyncEngine : IAsyncDisposable
{
    private readonly SyncHome _home;
    private readonly SyncServerClient _server;
    private readonly ISyncDevice? _device;
    private readonly SyncPathMap _paths;
    private readonly SemaphoreSlim _wake = new(0, 1); // pokes while a pass runs make ONE more pass
    private readonly Dictionary<string, SyncIndex> _indexes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _roots = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SyncPeerState> _lastReceipts = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private IReadOnlyList<SyncServerDeviceFolder> _folders = [];
    private SyncDeviceSession? _session;

    private readonly Dictionary<string, (IReadOnlySet<string> Old, IReadOnlySet<string> New)> _redacted = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, (string Token, IReadOnlyList<SyncRedaction> List)> _serverRedactions = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, (int Unsent, string Error)> _redactionSend = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The server's redactions for a folder, and this machine's own ones it does not hold yet sent -
    /// every pass, each on its own (one refused never stops the next), until the server has them all
    /// (Erk-S Sync, 2026-10-26: two recorded before the route existed never went). Only a successful
    /// read is kept for the session; a failure is never cached. What is still unsent, and why, goes to
    /// the folder's status (Twin).
    /// </summary>
    private async Task<IReadOnlyList<SyncRedaction>> ServerRedactionsAsync(string folderId, string physical, CancellationToken cancellationToken)
    {
        SyncRedaction[] own = SyncRedactions.Load(physical)
            .Where(entry => entry.By.Equals(_device!.DeviceKeyId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (_session is not { } relay)
        {
            lock (_gate)
            {
                IReadOnlyList<SyncRedaction> cached = _serverRedactions.GetValueOrDefault(folderId).List ?? [];
                _redactionSend[folderId] = (Unsent(own, cached).Length, own.Length > 0 && Unsent(own, cached).Length > 0 ? "Сервертэй холбогдоогүй — дараа илгээнэ." : "");
                return cached;
            }
        }
        IReadOnlyList<SyncRedaction>? list = null;
        lock (_gate)
        {
            if (_serverRedactions.TryGetValue(folderId, out var known) && known.Token == relay.AccessToken)
                list = known.List;
        }
        string error = "";
        if (list is null)
        {
            try
            {
                list = await _server.RedactionsAsync(relay.AccessToken, folderId, cancellationToken);
                lock (_gate)
                    _serverRedactions[folderId] = (relay.AccessToken, list);
            }
            catch (Exception exception) when (exception is SyncServerException or HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
            {
                error = "Цэвэрлэлтийн жагсаалтыг серверээс авч чадсангүй: " + Describe(exception);
                list = [];
            }
        }
        foreach (SyncRedaction mine in Unsent(own, list))
        {
            try
            {
                await _server.PostRedactionAsync(relay.AccessToken, folderId, mine, cancellationToken);
                list = [.. list, mine];
                lock (_gate)
                    _serverRedactions[folderId] = (relay.AccessToken, list);
            }
            catch (SyncServerException exception) when (exception.Code == OutdatedCode)
            {
                error = Outdated();
                break;
            }
            catch (Exception exception) when (exception is SyncServerException or HttpRequestException or TaskCanceledException)
            {
                error = "Цэвэрлэлтийн бүртгэл серверт хүрсэнгүй: " + Describe(exception);
            }
        }
        int unsent = Unsent(own, list).Length;
        lock (_gate)
            _redactionSend[folderId] = (unsent, unsent > 0 ? error : "");
        return list;
    }

    private const string OutdatedCode = "sync_client_outdated";

    /// <summary>
    /// 426 sync_client_outdated (SRV fd2b60d): the server writes a device's version when a session
    /// opens. A SYN at or above the minimum whose session predates it drops the session - the next pass
    /// opens one with its version and writes. One below the minimum must be updated first (owner,
    /// 2026-10-26: a member not updated does not shape the others' folders).
    /// </summary>
    private string Outdated()
    {
        int minimum = _session?.MinSyncProtocol ?? 0;
        if (SyncLagging.Protocol >= minimum)
        {
            // The server's record predates this session: a new one tells it this protocol.
            _session = null;
            Poke();
            return "Серверийн сесс шинэчлэгдэж байна — дараагийн шалгалтаар илгээнэ.";
        }
        return "Erk-S Sync-ийн шинэчлэл шаардлагатай (синкийн дүрэм өөрчлөгдсөн) — Studio-г шинэчилнэ үү.";
    }

    private static SyncRedaction[] Unsent(IEnumerable<SyncRedaction> own, IReadOnlyList<SyncRedaction> server) =>
        own.Where(entry => !server.Any(known =>
                known.Old.Equals(entry.Old, StringComparison.OrdinalIgnoreCase) &&
                known.Path.Replace('/', '\\').Trim('\\').Equals(entry.Path.Replace('/', '\\').Trim('\\'), StringComparison.OrdinalIgnoreCase)))
            .ToArray();

    private static string Describe(Exception exception) => exception switch
    {
        SyncServerException refused => $"{(int)refused.Status} {refused.Code}".Trim(),
        TaskCanceledException => "хугацаа хэтэрсэн",
        _ => "сүлжээ",
    };

    /// <summary>The folder's state with what is still unsent of its redactions, and why.</summary>
    private SyncFolderState WithRedactions(SyncFolderState folder)
    {
        (double? p50, double? p95) = Latency(folder.FolderId);
        folder = folder with { ChangesetLatencyP50Seconds = p50, ChangesetLatencyP95Seconds = p95, EventsConnected = _eventsConnected };
        lock (_gate)
        {
            return _redactionSend.TryGetValue(folder.FolderId, out var send)
                ? folder with { RedactionsUnsent = send.Unsent, RedactionError = send.Error }
                : folder;
        }
    }

    /// <summary>
    /// A redaction recorded here goes to the server at once; when it cannot, the reason comes back
    /// (for the `redact` answer) and the next pass tries again.
    /// </summary>
    public async Task<string> SendRedactionAsync(string folderId, SyncRedaction redaction, CancellationToken cancellationToken)
    {
        if (_session is not { } relay)
            return "Сервертэй холбогдоогүй — дараагийн шалгалтаар илгээнэ.";
        try
        {
            await _server.PostRedactionAsync(relay.AccessToken, folderId, redaction, cancellationToken);
            lock (_gate)
            {
                if (_serverRedactions.TryGetValue(folderId, out var known) && known.Token == relay.AccessToken)
                    _serverRedactions[folderId] = (known.Token, [.. known.List, redaction]);
            }
            return "";
        }
        catch (Exception exception) when (exception is SyncServerException or HttpRequestException or TaskCanceledException)
        {
            return "Цэвэрлэлтийн бүртгэл серверт хүрсэнгүй: " + Describe(exception) + " — дараагийн шалгалтаар дахин илгээнэ.";
        }
    }

    private SyncLanDiscovery? _discovery;
    private SyncLanExchange? _exchange;
    private int _disposed;

    public SyncEngine(SyncHome home, SyncServerClient server, ISyncDevice? device, SyncPathMap? paths = null)
    {
        _home = home;
        _server = server;
        _device = device;
        _paths = paths ?? new SyncPathMap();
        _centrals = new SyncCentralService(home, _ => Task.FromResult(
            _session is { } session && server.BaseAddress is { } address ? ((Uri, string)?)(address, session.AccessToken) : null));
    }

    private readonly SyncCentralService _centrals;

    /// <summary>
    /// collab-whoami for the folder holding <paramref name="physicalRoot"/>: this device's seat and
    /// the people and seats attached to the folder (the project's team). Names stay on the pipe.
    /// </summary>
    public SyncCollabWhoAmI? WhoAmI(string physicalRoot)
    {
        if (_device is null)
            return null;
        SyncServerDeviceFolder? entry;
        lock (_gate)
            entry = _folders.FirstOrDefault(item =>
                string.Equals(Path.GetFullPath(_paths.Physical(item.Folder.CanonicalPath)), Path.GetFullPath(physicalRoot), StringComparison.OrdinalIgnoreCase));
        if (entry is null)
            return null;
        // A partner machine that says what it is acting as is taken at its word (seat id and role
        // only); one that says nothing is its server record.
        SyncCollabMember Member(SyncServerDevice device)
        {
            SyncCollabMachine? told = device.DeviceKeyId.Equals(_device.DeviceKeyId, StringComparison.OrdinalIgnoreCase)
                ? null
                : SyncCollabMachine.Read(physicalRoot, device.DeviceKeyId);
            return told is { Seat.Length: > 0 }
                ? new SyncCollabMember(told.Seat, told.Seat.StartsWith("bot-", StringComparison.Ordinal) ? "Бот " + device.BotId : device.AccountEmail, "member")
                : ServerMember(device);
        }
        SyncCollabMember ServerMember(SyncServerDevice device) => new(
            SyncSeats.Of(device.SeatId, device.BotId),
            device.BotId.Length > 0 ? "Бот " + device.BotId : device.AccountEmail,
            // No owner and member on a shared file (owner, 2026-10-26: «эзэн гишүүн гэдэг ойлголтыг
            // байхгүй болго») - every seat writes as an equal; the field stays for older CGA.
            "member");
        // Every attached device's seat, the owner first; one entry per seat, its machines' writers in it.
        List<SyncCollabMember> team = entry.Devices
            .Select(device => (Member: Member(device), Writer: SyncSeats.WriterOf(Member(device).Seat, device.DeviceKeyId)))
            .Where(item => item.Member.Seat.Length > 0)
            .GroupBy(item => item.Member.Seat, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First().Member with { Writers = group.Select(item => item.Writer).Distinct().ToList() })
            .OrderBy(member => member.Role == "owner" ? 0 : 1)
            .ToList();
        SyncServerDevice? me = entry.Devices.FirstOrDefault(device => device.DeviceKeyId.Equals(_device.DeviceKeyId, StringComparison.OrdinalIgnoreCase));
        if (me is null)
            return null;
        SyncCollabMember self = ServerMember(me);
        // The seat Studio is acting as, not the one the machine is attached with: a bot seat only
        // while bot mode is chosen; the owner signed in as themselves writes as their own seat.
        // A file that cannot be read now (being replaced, damaged) keeps the last identity read;
        // none ever read means the machine's own account - the bot only when chosen (CLAUDE.md §2).
        SyncActiveIdentity? read = SyncActiveIdentity.Read(_home);
        lock (_gate)
        {
            if (read is not null)
                _lastActing = read;
            read = _lastActing;
        }
        SyncActiveIdentity acting = read ?? new SyncActiveIdentity { AccountEmail = me.AccountEmail };
        if (acting is { ActingAsBot: false, AccountEmail.Length: > 0 } && me.BotId.Length > 0)
        {
            SyncServerDevice? theirs = entry.Devices.FirstOrDefault(device =>
                device.BotId.Length == 0 && device.SeatId.Length > 0 && device.AccountEmail.Equals(acting.AccountEmail, StringComparison.OrdinalIgnoreCase));
            string seatId = theirs?.SeatId
                ?? (me.AccountEmail.Equals(acting.AccountEmail, StringComparison.OrdinalIgnoreCase) ? me.SeatId : "");
            string seat = SyncSeats.Of(seatId, "");
            self = new SyncCollabMember(
                seat,
                acting.AccountEmail,
                "member");
            string writer = SyncSeats.WriterOf(seat, me.DeviceKeyId);
            if (seat.Length > 0)
            {
                int at = team.FindIndex(member => member.Seat.Equals(seat, StringComparison.OrdinalIgnoreCase));
                if (at >= 0)
                    team[at] = team[at] with { Role = self.Role, Writers = [.. team[at].Writers, writer] };
                else
                    team.Insert(0, self with { Writers = [writer] });
            }
            return new SyncCollabWhoAmI(self.Seat, self.DisplayName, entry.Folder.ProjectId, team) { Writer = writer };
        }
        return new SyncCollabWhoAmI(self.Seat, self.DisplayName, entry.Folder.ProjectId, team)
        {
            Writer = SyncSeats.WriterOf(self.Seat, me.DeviceKeyId),
        };
    }

    private readonly Dictionary<string, IReadOnlySet<string>> _held = new(StringComparer.OrdinalIgnoreCase);

    private SyncActiveIdentity? _lastActing;

    private IReadOnlySet<string> HeldOf(string folderId)
    {
        lock (_gate)
            return _held.TryGetValue(folderId, out IReadOnlySet<string>? held) ? held : new HashSet<string>();
    }

    /// <summary>The server and this device's token, for a member's road to a central's host; null while signed out of the server.</summary>
    public (Uri Server, string Token)? CentralRoad =>
        _session is { } session && _server.BaseAddress is { } address ? (address, session.AccessToken) : null;

    /// <summary>Erk-S Central off - tests of plain file sync, which have no relay to serve a central on.</summary>
    public bool ServeCentrals { get; init; } = true;

    public TimeSpan Period { get; init; } = TimeSpan.FromSeconds(60);

    public SyncState LastState { get; private set; } = new();

    /// <summary>Raised after every state write - the tray follows it instead of asking on a clock.</summary>
    public event Action<SyncState>? StateWritten;

    /// <summary>Run a pass now (a folder was registered, a verb asked for status).</summary>
    public void Poke()
    {
        try
        {
            _wake.Release();
        }
        catch (Exception exception) when (exception is SemaphoreFullException or ObjectDisposedException)
        {
            // A pass is already due (or the engine is gone).
        }
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (_device is not null)
        {
            try
            {
                _exchange = new SyncLanExchange(
                    new SyncLanIdentity(_device.DeviceKeyId, _device.TlsCertificate),
                    AllowedDevices,
                    IndexJson,
                    ReadChunk);
                _exchange.ReverseOffered += Poke;
                // A partner that reached us is found here from now on, though its multicast never arrives.
                _exchange.PeerReached += (deviceKeyId, endpoint, folderId) => _discovery?.Learn(deviceKeyId, endpoint, folderId);
                _discovery = new SyncLanDiscovery(_device.DeviceKeyId);
                _discovery.IndexChanged += (_, _) => Debounced();
            }
            catch (Exception exception) when (exception is System.Net.Sockets.SocketException or IOException)
            {
                // No LAN half (port taken, no network). The server half still runs.
            }
        }

        if (_device is not null)
            _ = EventsLoopAsync(cancellationToken);
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
                await _wake.WaitAsync(_eventsConnected ? PushedPeriod : Period, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    public async Task PassAsync(CancellationToken cancellationToken)
    {
        PassStarted();
        try
        {
            await PassCoreAsync(cancellationToken);
        }
        finally
        {
            PassFinished();
        }
    }

    private async Task PassCoreAsync(CancellationToken cancellationToken)
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
            _server.LanEndpoints = SyncLanAddresses.Own(_exchange?.Port ?? 0);
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
            states.Add(WithRedactions(await FolderPassAsync(entry, cancellationToken)));

        if (_discovery is not null && _exchange is not null)
        {
            try
            {
                Dictionary<string, string> stamps;
                lock (_gate)
                    stamps = _indexes.ToDictionary(pair => pair.Key, pair => Stamp(pair.Value), StringComparer.OrdinalIgnoreCase);
                // Also told one by one to every partner the server says is on a private network.
                List<System.Net.IPAddress> partners = known
                    .SelectMany(entry => entry.Devices)
                    .Where(device => !device.DeviceKeyId.Equals(_device.DeviceKeyId, StringComparison.OrdinalIgnoreCase))
                    .SelectMany(device => SyncLanAddresses.Parse(device.LanEndpoints))
                    .Select(endpoint => endpoint.Address)
                    .ToList();
                await _discovery.AnnounceAsync(
                    new SyncLanAnnouncement(1, _device.DeviceKeyId, _exchange.Port,
                        states.Where(state => state.PresentHere).Select(state => state.FolderId).ToList(),
                        stamps),
                    cancellationToken,
                    partners);
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

    private readonly Dictionary<string, DateTimeOffset> _lanHintMissed = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Where the server says a partner's LAN exchange listens, when it was not heard here (its multicast
    /// does not reach this machine). An address that did not answer is not tried again for 10 minutes -
    /// the partner is likely on another network, and the server road takes it.
    /// </summary>
    private IPEndPoint? LanHint(SyncServerDevice device)
    {
        lock (_gate)
        {
            if (_lanHintMissed.TryGetValue(device.DeviceKeyId, out DateTimeOffset missed) && DateTimeOffset.UtcNow - missed < TimeSpan.FromMinutes(10))
                return null;
        }
        IReadOnlyList<IPEndPoint> endpoints = SyncLanAddresses.Parse(device.LanEndpoints);
        if (endpoints.Count == 0)
            return null;
        // One that shares a /24 with this machine first - the same network, most likely.
        HashSet<string> mine = SyncLanAddresses.Own(1)
            .Select(text => IPEndPoint.TryParse(text, out IPEndPoint? own) ? Subnet(own.Address) : "")
            .ToHashSet();
        return endpoints.FirstOrDefault(endpoint => mine.Contains(Subnet(endpoint.Address))) ?? endpoints[0];

        static string Subnet(IPAddress address) => string.Join('.', address.GetAddressBytes().Take(3));
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

        // Shared CGA files first (К5): a base for a machine without the DWG, old checkpoints and
        // changesets every machine has applied retired - before the index sees the folder.
        SyncIndex? previous;
        lock (_gate)
            previous = _indexes.GetValueOrDefault(folder.FolderId) ?? SyncIndexStore.TryLoad(_home.IndexPath(folder.FolderId));
        Dictionary<string, SyncIndexEntry> knownHere = (previous?.Entries ?? [])
            .GroupBy(entry => entry.RelativePath, SyncPath.Comparer)
            .ToDictionary(group => group.Key, group => group.First(), SyncPath.Comparer);
        // Every hash a partner has held for a path (its current one and its version chain): a DWG
        // here with one of them was synced, not edited here.
        SyncIndex[] partnerIndexes = entry.Devices
            .Where(device => !device.DeviceKeyId.Equals(_device!.DeviceKeyId, StringComparison.OrdinalIgnoreCase))
            .Select(device => SyncIndexStore.TryLoad(_home.PeerIndexPath(folder.FolderId, device.DeviceKeyId)))
            .OfType<SyncIndex>()
            .ToArray();
        IReadOnlySet<string> PartnerHashes(string path) => partnerIndexes
            .SelectMany(partner => partner.Entries.Where(item => SyncPath.Comparer.Equals(item.RelativePath, path)))
            .SelectMany(item => (item.Version?.History ?? []).Prepend(item.Sha256))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        // Tell the partners which seat this machine acts as - only in folders that share CGA files.
        if (Directory.Exists(Path.Combine(physical, SyncCollab.DirectoryName)) && WhoAmI(physical) is { Seat.Length: > 0 } acting)
        {
            try
            {
                SyncCollabMachine.Write(physical, _device!.DeviceKeyId, new SyncCollabMachine(acting.Seat, acting.Team.FirstOrDefault(member => member.Seat == acting.Seat)?.Role ?? "member"));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
        // Redactions first (owner, 2026-10-26): a content redacted anywhere leaves this machine
        // before it is indexed or uploaded - the server's list too, for a machine back from offline.
        IReadOnlyList<SyncRedaction> redactions = SyncRedactions.Load(physical, await ServerRedactionsAsync(folder.FolderId, physical, cancellationToken));
        SyncRedactions.Apply(physical, _home, redactions);
        IReadOnlySet<string> redactedOld = SyncRedactions.OldHashes(redactions);
        lock (_gate)
            _redacted[folder.FolderId] = (redactedOld, SyncRedactions.NewHashes(redactions));
        IReadOnlySet<string> retired = SyncCollab.Maintain(physical, _home, path => knownHere.GetValueOrDefault(path), _device!.DeviceKeyId, PartnerHashes);
        SyncIndex index;
        try
        {
            index = await Task.Run(() => SyncIndexBuilder.Build(physical, folder.FolderId, previous, cancellationToken), cancellationToken);
            SyncVersionLedger ledger = SyncVersionLedgerStore.Load(_home.LedgerPath(folder.FolderId));
            ledger.Adopt(SyncDecisionStore.Load(_home.DecisionsPath(folder.FolderId)));
            // A CGA changeset's kept copy is the shared file's own business (its status says «дугаар
            // давхардсан» while one is left), and a redacted content is no decision for anyone: neither
            // waits for a person here (Erk-S Sync, 2026-10-26 - «Зөрүүтэй файл 3» from a lagging partner).
            ledger.ForgetKept(path => SyncCollab.IsChangesetPath(path), redactedOld);
            index = ledger.Stamp(index, _device!.DeviceKeyId);
            if (redactedOld.Count > 0)
            {
                // Held open and not removed yet: still never indexed, never uploaded.
                index = index with
                {
                    Entries = index.Entries
                        .Where(item => !redactedOld.Contains(item.Sha256))
                        .Select(item => item.Version is { } chain && (chain.History.Any(redactedOld.Contains) || chain.Overrode.Any(redactedOld.Contains))
                            ? item with
                            {
                                Version = chain with
                                {
                                    History = chain.History.Where(sha => !redactedOld.Contains(sha)).ToList(),
                                    Overrode = chain.Overrode.Where(sha => !redactedOld.Contains(sha)).ToList(),
                                },
                            }
                            : item)
                        .ToList(),
                };
            }
            SyncVersionLedgerStore.Save(ledger, _home.LedgerPath(folder.FolderId));
            SyncIndexStore.Save(index, _home.IndexPath(folder.FolderId));
            IReadOnlySet<string> held = SyncCollab.HeldPaths(physical).Concat(retired).ToHashSet(SyncPath.Comparer);
            lock (_gate)
            {
                _indexes[folder.FolderId] = index;
                _roots[folder.FolderId] = physical;
            }
            Watch(physical);
            lock (_gate)
            {
                _held[folder.FolderId] = held;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return state with { PresentHere = true, LastError = "Индекс үүсгэж чадсангүй: " + exception.Message };
        }

        // Shown before the peers are asked, so a long first transfer has a folder to report into -
        // with what the last pass knew, so a pass stopped midway does not report less than before.
        SyncFolderState? before = LastState.Folders.FirstOrDefault(item => item.FolderId.Equals(folder.FolderId, StringComparison.OrdinalIgnoreCase));
        UpsertFolder(state with
        {
            PresentHere = true,
            IndexedAtUtc = index.BuiltAtUtc,
            FileCount = index.Entries.Count,
            TotalBytes = index.Entries.Sum(entry => entry.Size),
            Peers = before?.Peers ?? [],
            ConflictCount = SyncVersionLedgerStore.Load(_home.LedgerPath(folder.FolderId)).Kept.Values.Sum(list => list.Count),
            Centrals = before?.Centrals ?? [],
            UploadedBytes = before?.UploadedBytes ?? 0,
            UploadPendingBytes = before?.UploadPendingBytes ?? 0,
            LastUploadBytesPerSecond = before?.LastUploadBytesPerSecond ?? 0,
        });

        string uploadError = "";
        if (entry.Devices.Any(device => !device.DeviceKeyId.Equals(_device!.DeviceKeyId, StringComparison.OrdinalIgnoreCase)))
            uploadError = await UploadAsync(folder.FolderId, index, cancellationToken);

        var peers = new List<SyncPeerState>();
        foreach (SyncServerDevice device in entry.Devices)
        {
            if (device.DeviceKeyId.Equals(_device!.DeviceKeyId, StringComparison.OrdinalIgnoreCase))
                continue;
            peers.Add(await PeerPassAsync(folder.FolderId, physical, device, index, cancellationToken));
        }
        if (peers.Any(peer => peer.LastReceivedAtUtc > index.BuiltAtUtc))
            Poke(); // what arrived is indexed - and counted - on the very next pass

        IReadOnlyList<SyncCentralState> centrals = ServeCentrals
            ? _centrals.Refresh(
                folder.FolderId,
                physical,
                index,
                entry.Devices.Select(device => SyncIndexStore.TryLoad(_home.PeerIndexPath(folder.FolderId, device.DeviceKeyId))).OfType<SyncIndex>())
            : [];

        SyncFolderState? shown;
        lock (_gate)
            shown = LastState.Folders.FirstOrDefault(item => item.FolderId.Equals(folder.FolderId, StringComparison.OrdinalIgnoreCase));
        return state with
        {
            LastError = uploadError,
            Centrals = centrals,
            Collabs = SyncCollab.Files(physical)
                .Select(file => SyncCollab.WithServer(SyncCollab.Status(physical, _home, file.FileId, WhoAmI(physical)?.Writer ?? ""), physical, OnServer(folder.FolderId)))
                .ToList(),
            UploadedBytes = shown?.UploadedBytes ?? 0,
            UploadPendingBytes = shown?.UploadPendingBytes ?? 0,
            LastUploadBytesPerSecond = shown?.LastUploadBytesPerSecond ?? 0,
            ConflictCount = SyncVersionLedgerStore.Load(_home.LedgerPath(folder.FolderId)).Kept.Values.Sum(list => list.Count),
            PresentHere = true,
            IndexedAtUtc = index.BuiltAtUtc,
            FileCount = index.Entries.Count,
            TotalBytes = index.Entries.Sum(entry => entry.Size),
            Peers = peers,
        };
    }

    private async Task<SyncPeerState> PeerPassAsync(string folderId, string physical, SyncServerDevice device, SyncIndex mine, CancellationToken cancellationToken)
    {
        var peer = new SyncPeerState
        {
            DeviceKeyId = device.DeviceKeyId,
            AccountEmail = device.AccountEmail,
            ServerLastSeenAtUtc = device.LastSeenAtUtc,
            Lagging = SyncLagging.IsLagging(device, DateTimeOffset.UtcNow, _session?.MinSyncProtocol ?? 0),
        };
        string stored = _home.PeerIndexPath(folderId, device.DeviceKeyId);
        SyncLanPeer? heard = _discovery?.Find(device.DeviceKeyId);
        bool heardHere = heard is not null && heard.FolderIds.Contains(folderId, StringComparer.OrdinalIgnoreCase);
        IPEndPoint? lanEndpoint = heardHere ? heard!.Endpoint : LanHint(device);
        bool hinted = lanEndpoint is not null && !heardHere;
        string error = "";
        bool viaLan = false;
        // The partner opened a connection to us and offered it back (our firewall-free side reaches it,
        // its side cannot reach us): ask on it as on our own.
        if (_exchange?.TakeReverse(device.DeviceKeyId, folderId) is { } offered)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TransferWindow);
                SyncIndex theirs = await offered.FetchIndexAsync(timeout.Token);
                SyncIndexStore.Save(theirs with { BuiltAtUtc = DateTimeOffset.UtcNow }, stored);
                peer = peer with { LanAddress = heard?.Endpoint.ToString() ?? "", Route = "lan" };
                viaLan = true;
                peer = await TransferAsync(folderId, physical, mine, theirs,
                    (file, chunk, token) => offered.FetchChunkAsync(file.RelativePath, file.Sha256, chunk, token), peer, timeout.Token);
            }
            catch (Exception exception) when (exception is SyncLanException or IOException or OperationCanceledException or InvalidDataException)
            {
                if (cancellationToken.IsCancellationRequested)
                    throw;
                viaLan = false;
                error = exception is SyncLanException lan ? lan.Message : "LAN-аар холбогдож чадсангүй.";
            }
            finally
            {
                _exchange.Release(device.DeviceKeyId, folderId);
            }
        }
        else if (lanEndpoint is not null && _exchange is not null)
        {
            SyncLanSession? session = null;
            var extraSessions = new List<SyncLanSession>();
            bool handedOff = false;
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                // An address only the server named may be on another network: a short try, then the server road.
                timeout.CancelAfter(hinted ? TimeSpan.FromSeconds(4) : TimeSpan.FromSeconds(30));
                session = await _exchange.OpenAsync(lanEndpoint, device.DeviceKeyId, folderId, timeout.Token);
                SyncIndex theirs = await session.FetchIndexAsync(timeout.Token);
                SyncIndexStore.Save(theirs with { BuiltAtUtc = DateTimeOffset.UtcNow }, stored);
                peer = peer with { LanAddress = lanEndpoint.ToString(), Route = "lan" };
                if (hinted)
                {
                    lock (_gate)
                        _lanHintMissed.Remove(device.DeviceKeyId);
                }
                viaLan = true;
                timeout.CancelAfter(TransferWindow);
                // More connections for the pieces when there is something to take: one TCP stream over
                // Wi-Fi used a quarter of it (Erk-S Sync, 2026-10-27). Pieces go round them in turn.
                if (PlanFor(folderId, physical, mine, theirs, SyncVersionLedgerStore.Load(_home.LedgerPath(folderId))).Actions.Count > 0)
                {
                    for (int extra = 1; extra < LanStreams; extra++)
                    {
                        try
                        {
                            extraSessions.Add(await _exchange.OpenAsync(lanEndpoint, device.DeviceKeyId, folderId, timeout.Token));
                        }
                        catch (Exception exception) when (exception is SyncLanException or IOException or System.Security.Authentication.AuthenticationException or System.Net.Sockets.SocketException)
                        {
                            break; // the one connection still carries everything
                        }
                    }
                }
                SyncLanSession[] lanes = [session, .. extraSessions];
                peer = await TransferAsync(folderId, physical, mine, theirs,
                    (file, chunk, token) => lanes[chunk % lanes.Length].FetchChunkAsync(file.RelativePath, file.Sha256, chunk, token), peer, timeout.Token, lanes.Length);
                // A partner that has not reached us lately (its firewall, or ours) gets this connection
                // back to ask on - served in the background, so neither pass waits for the other.
                if (!_exchange.ReachedUsLately(device.DeviceKeyId))
                {
                    SyncLanSession back = session;
                    handedOff = true;
                    _ = Task.Run(async () =>
                    {
                        await using (back)
                            await _exchange.OfferBackAsync(back, device.DeviceKeyId, cancellationToken);
                    }, CancellationToken.None);
                }
            }
            catch (Exception exception) when (exception is SyncLanException or IOException or System.Security.Authentication.AuthenticationException or System.Net.Sockets.SocketException or OperationCanceledException or InvalidDataException)
            {
                if (cancellationToken.IsCancellationRequested)
                    throw;
                if (hinted && !viaLan)
                {
                    lock (_gate)
                        _lanHintMissed[device.DeviceKeyId] = DateTimeOffset.UtcNow;
                }
                error = exception is SyncLanException lan ? lan.Message : "LAN-аар холбогдож чадсангүй.";
            }
            finally
            {
                if (!handedOff && session is not null)
                    await session.DisposeAsync();
                foreach (SyncLanSession extra in extraSessions)
                    await extra.DisposeAsync();
            }
        }
        else if (heard is null)
        {
            error = "LAN дээр олдсонгүй (унтарсан эсвэл өөр сүлжээнд).";
        }

        // 🔴 CLOUD SYNC (owner, 2026-10-24: «бид чинь cloud sync хийх гээд байгаа шүү дээ»): a
        // partner on another network is synced through the server - its index and the pieces it
        // put there. Same planner, same whole-file-or-nothing; only the road differs.
        if (!viaLan && _session is { } relay)
        {
            try
            {
                using var window = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                window.CancelAfter(TransferWindow);
                string? json = await _server.GetIndexAsync(relay.AccessToken, folderId, device.DeviceKeyId, window.Token);
                if (json is null)
                {
                    error = "Хамтрагч индексээ серверт хараахан тавиагүй (түүний Erk-S Sync шинэчлэгдээгүй эсвэл унтарсан).";
                }
                else
                {
                    SyncIndex served = SyncIndexStore.Deserialize(json) ?? throw new InvalidDataException("bad index");
                    // What is not whole on the server yet is left as if unseen - not taken, not a deletion
                    // (owner, 2026-10-27: its author may have gone offline mid-upload; the others go on).
                    int incomplete = served.Entries.Count(entry => entry.Incomplete);
                    SyncIndex theirs = incomplete == 0 ? served : served with { Entries = served.Entries.Where(entry => !entry.Incomplete).ToList() };
                    error = "";
                    peer = peer with { Route = "server", ServerIncompleteCount = incomplete };
                    lock (_gate)
                    {
                        if (!_peersOnServer.TryGetValue(folderId, out var byPeer))
                            _peersOnServer[folderId] = byPeer = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
                        byPeer[device.DeviceKeyId] = theirs.Entries.Select(entry => entry.RelativePath).ToHashSet(SyncPath.Comparer);
                    }
                    if (peer.Lagging)
                    {
                        // Long offline or not updated: its index is no source here (the stored copy is
                        // not refreshed from it either, so nothing is planned against it).
                        error = "Хоцорсон гишүүн — шинэчлэгдэж, онлайн болох хүртэл түүнээс файл авахгүй.";
                    }
                    else
                    {
                        SyncIndexStore.Save(theirs with { BuiltAtUtc = DateTimeOffset.UtcNow }, stored);
                        peer = await TransferAsync(folderId, physical, mine, theirs,
                            (file, chunk, token) => RelayChunkAsync(relay.AccessToken, folderId, file, chunk, token), peer, window.Token);
                    }
                }
            }
            catch (Exception exception) when (exception is SyncServerException or HttpRequestException or IOException or OperationCanceledException or InvalidDataException or System.Text.Json.JsonException)
            {
                if (cancellationToken.IsCancellationRequested)
                    throw;
                error = exception is SyncServerException refused
                    ? "Сервероор дамжуулж чадсангүй: " + refused.Message
                    : exception is OperationCanceledException
                        ? "Сервероор дамжуулалт энэ удаад хугацаандаа багтсангүй — дараагийн шалгалтаар үргэлжилнэ."
                        : "Сервероор дамжуулж чадсангүй (сүлжээ).";
            }
        }

        SyncIndex? theirsKnown = SyncIndexStore.TryLoad(stored);
        if (theirsKnown is null)
            return peer with { LastError = error };
        SyncIndexDifference difference = SyncIndexComparison.Compare(mine, theirsKnown);
        SyncCarriedDifference carried = SyncIndexComparison.CompareCarried(mine, theirsKnown, HeldOf(folderId));
        // What this machine still needs from this partner (owner, 2026-10-26: «онлайн гишүүний зөвхөн
        // өөрийнх нь файл хэдэн хувь синк хийгдсэн нь чухал») - what the partner still needs from here
        // is its own number, not this machine's.
        SyncTransferPlan needed = PlanFor(folderId, physical, mine, theirsKnown, SyncVersionLedgerStore.Load(_home.LedgerPath(folderId)));
        long neededBytes = needed.Actions.Where(action => action.Kind != SyncTransferKind.KeepBoth).Sum(action => action.Theirs.Size);
        return peer with
        {
            Transferring = false,
            CurrentFile = "",
            TransferBytesPerSecond = 0,
            TransferSecondsLeft = null,
            SyncedPercent = carried.Difference.SyncedPercent,
            SyncedBytes = carried.Difference.SameBytes,
            UnionBytes = carried.Difference.UnionBytes,
            NeededFiles = needed.Actions.Count(action => action.Kind != SyncTransferKind.KeepBoth),
            NeededBytes = neededBytes,
            HereBytes = mine.Entries.Sum(entry => entry.Size),
            NotCarriedCount = carried.NotCarriedCount,
            NotCarriedBytes = carried.NotCarriedBytes,
            LastError = error.Length > 0 ? error : peer.LastError,
            ExchangedAtUtc = theirsKnown.BuiltAtUtc,
            SameCount = difference.SameCount,
            DifferentCount = difference.Different.Count,
            OnlyHereCount = difference.OnlyHere.Count,
            OnlyThereCount = difference.OnlyThere.Count,
        };
    }

    /// <summary>How long one peer's files may take in one pass; the rest come on the next.</summary>
    public TimeSpan TransferWindow { get; init; } = TimeSpan.FromMinutes(20);

    /// <summary>
    /// 1.2: the files of <paramref name="theirs"/> this machine should take, taken over the
    /// already verified session - fetched, replaced or kept beside, never overwritten blind.
    /// </summary>
    /// <summary>What a partner's index means here - the one planning both the transfer and the «needed here» count use.</summary>
    private SyncTransferPlan PlanFor(string folderId, string physical, SyncIndex mine, SyncIndex theirs, SyncVersionLedger ledger)
    {
        // A DWG shared block by block (CGA К1) never travels as bytes - its changesets do.
        IReadOnlySet<string> held = HeldOf(folderId);
        if (held.Count > 0)
            theirs = theirs with { Entries = theirs.Entries.Where(entry => !held.Contains(entry.RelativePath)).ToList() };
        // A shared file's checkpoints travel only to the machine that needs the newest one (lazy).
        IReadOnlySet<string> asked;
        lock (_gate)
        {
            // An ask is done once the newest checkpoint is placed here.
            _checkpointsAsked.RemoveWhere(fileId => SyncCollab.Local(_home, fileId).PlacedCheckpoint >= Math.Max(1, SyncCollab.NewestCheckpoint(physical, fileId)));
            asked = new HashSet<string>(_checkpointsAsked, StringComparer.OrdinalIgnoreCase);
        }
        IReadOnlySet<string> notWanted = SyncCollab.CheckpointsNotWanted(physical, _home, theirs.Entries, asked);
        if (notWanted.Count > 0)
            theirs = theirs with { Entries = theirs.Entries.Where(entry => !notWanted.Contains(entry.RelativePath)).ToList() };
        (IReadOnlySet<string> Old, IReadOnlySet<string> New) redacted;
        lock (_gate)
            redacted = _redacted.GetValueOrDefault(folderId, (new HashSet<string>(), new HashSet<string>()));
        var authoritative = new HashSet<string>(SyncCollab.Restored(physical), StringComparer.OrdinalIgnoreCase);
        authoritative.UnionWith(redacted.New);
        return SyncTransferPlanner.Plan(mine, theirs, ledger, SyncDecisionStore.Load(_home.DecisionsPath(folderId)), authoritative, redacted.Old);
    }

    private async Task<SyncPeerState> TransferAsync(
        string folderId,
        string physical,
        SyncIndex mine,
        SyncIndex theirs,
        Func<SyncIndexEntry, int, CancellationToken, Task<byte[]>> fetch,
        SyncPeerState peer,
        CancellationToken cancellationToken,
        int streams = 1)
    {
        var timings = new SyncApplyTimings();
        string ledgerPath = _home.LedgerPath(folderId);
        SyncVersionLedger ledger = SyncVersionLedgerStore.Load(ledgerPath);
        SyncTransferPlan plan = PlanFor(folderId, physical, mine, theirs, ledger);
        int received = 0, kept = 0, waiting = 0;
        long bytes = 0;
        string lastError = "";
        long totalBytes = plan.Actions.Sum(action => action.Theirs.Size), doneBytes = 0;
        int filesDone = 0;
        DateTime lastReport = DateTime.MinValue;
        var rate = new SyncTransferRate();
        DateTimeOffset started = DateTimeOffset.UtcNow;
        rate.Add(0, started);
        void Report(string current, long size, long done, bool force = false)
        {
            if (!force && (DateTime.UtcNow - lastReport).TotalMilliseconds < 1000)
                return;
            lastReport = DateTime.UtcNow;
            rate.Add(doneBytes, DateTimeOffset.UtcNow);
            ReportPeer(folderId, peer with
            {
                Transferring = true,
                TransferFiles = plan.Actions.Count,
                TransferFilesDone = filesDone,
                TransferTotalBytes = totalBytes,
                TransferDoneBytes = doneBytes,
                CurrentFile = current,
                CurrentFileBytes = size,
                CurrentFileDoneBytes = done,
                TransferBytesPerSecond = rate.BytesPerSecond,
                TransferSecondsLeft = rate.SecondsLeft(totalBytes, doneBytes),
            });
        }
        foreach (SyncTransferAction action in plan.Actions)
        {
            SyncIndexEntry file = action.Theirs;
            SyncApplyResult result;
            long fileDone = 0;
            Report(file.RelativePath, file.Size, 0, force: true);
            try
            {
                result = await SyncFileAssembler.ApplyAsync(
                    physical,
                    action,
                    mine,
                    (chunk, token) => fetch(file, chunk, token),
                    DateTimeOffset.UtcNow,
                    cancellationToken,
                    piece =>
                    {
                        fileDone += piece;
                        doneBytes += piece;
                        Report(file.RelativePath, file.Size, fileDone);
                    },
                    timings);
            }
            catch (SyncLanException exception)
            {
                // The peer changed the file since its index: it comes with the next index.
                waiting++;
                lastError = exception.Message;
                continue;
            }
            if (result.Outcome == SyncApplyOutcome.Waiting)
            {
                waiting++;
                doneBytes += file.Size - fileDone;
                continue;
            }
            filesDone++;
            bytes += (long)result.ChunksFetched * SyncIndex.ChunkSize;
            if (action.Kind == SyncTransferKind.KeepBoth)
            {
                ledger.Keep(file.RelativePath, file.Sha256);
                kept++;
            }
            else
            {
                ledger.Applied(file.RelativePath, file.Version!);
                received++;
                if (SyncCollab.IsChangesetPath(file.RelativePath))
                    NoteLatency(folderId, DateTime.UtcNow - file.LastWriteUtc);
            }
            SyncVersionLedgerStore.Save(ledger, ledgerPath);
        }
        // What arrived stays reported until something else arrives: the pass right after a
        // transfer has nothing to take, and a count that flickers back to 0 tells nobody anything.
        string receiptKey = folderId + "|" + peer.DeviceKeyId;
        SyncPeerState receipt;
        lock (_gate)
        {
            if (received + kept > 0)
            {
                double seconds = (DateTimeOffset.UtcNow - started).TotalSeconds;
                _lastReceipts[receiptKey] = new SyncPeerState
                {
                    LastTransferBytesPerSecond = seconds > 0 ? bytes / seconds : 0,
                    LastTransferWaitSeconds = timings.WaitSeconds,
                    LastTransferHashSeconds = timings.HashSeconds,
                    LastTransferDiskSeconds = timings.DiskSeconds,
                    LastTransferStreams = streams,
                    ReceivedCount = received,
                    KeptBothCount = kept,
                    BytesReceived = bytes,
                    LastReceivedAtUtc = DateTimeOffset.UtcNow,
                };
            }
            receipt = _lastReceipts.GetValueOrDefault(receiptKey) ?? new SyncPeerState();
        }
        return peer with
        {
            ReceivedCount = receipt.ReceivedCount,
            KeptBothCount = receipt.KeptBothCount,
            BytesReceived = receipt.BytesReceived,
            LastReceivedAtUtc = receipt.LastReceivedAtUtc,
            LastTransferBytesPerSecond = receipt.LastTransferBytesPerSecond,
            LastTransferWaitSeconds = receipt.LastTransferWaitSeconds,
            LastTransferHashSeconds = receipt.LastTransferHashSeconds,
            LastTransferDiskSeconds = receipt.LastTransferDiskSeconds,
            LastTransferStreams = receipt.LastTransferStreams,
            WaitingCount = waiting,
            AheadHereCount = plan.AheadHere,
            UnrelatedCount = plan.Unrelated,
            LastError = lastError,
        };
    }

    /// <summary>A piece of a file this machine holds, for an attached peer - only the version in this machine's index.</summary>
    private byte[]? ReadChunk(string folderId, string relativePath, string sha256, int chunk)
    {
        SyncIndexEntry? entry;
        string? root;
        lock (_gate)
        {
            entry = _indexes.GetValueOrDefault(folderId)?.Entries.FirstOrDefault(item =>
                SyncPath.Comparer.Equals(item.RelativePath, relativePath) &&
                item.Sha256.Equals(sha256, StringComparison.OrdinalIgnoreCase));
            root = _roots.GetValueOrDefault(folderId);
        }
        if (entry is null || root is null || chunk < 0 || chunk >= entry.ChunkSha256.Count ||
            !SyncTransferPlanner.IsCarried(entry.Kind) || HeldOf(folderId).Contains(entry.RelativePath))
        {
            return null;
        }
        string path = Path.GetFullPath(Path.Combine(root, entry.RelativePath));
        if (!SyncPath.IsSameOrInside(root, path))
            return null;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            long offset = (long)chunk * SyncIndex.ChunkSize;
            int length = (int)Math.Min(SyncIndex.ChunkSize, stream.Length - offset);
            if (length <= 0)
                return null;
            stream.Position = offset;
            byte[] piece = new byte[length];
            stream.ReadExactly(piece);
            // Changed since the index: no piece rather than a wrong one.
            return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(piece))
                .Equals(entry.ChunkSha256[chunk], StringComparison.OrdinalIgnoreCase) ? piece : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>A piece from the server by its hash; one it does not hold yet makes the file wait.</summary>
    private async Task<byte[]> RelayChunkAsync(string token, string folderId, SyncIndexEntry file, int chunk, CancellationToken cancellationToken)
    {
        if (chunk < 0 || chunk >= file.ChunkSha256.Count)
            throw new SyncLanException("sync_chunk_none", "Файлын хэсэг индекст алга.");
        return await _server.GetChunkAsync(token, folderId, file.ChunkSha256[chunk], cancellationToken)
            ?? throw new SyncLanException("sync_chunk_none", "Файлын хэсэг серверт хараахан ирээгүй — дараагийн шалгалтаар.");
    }

    /// <summary>How long one pass may spend putting pieces on the server; the rest go on the next.</summary>
    public TimeSpan UploadWindow { get; init; } = TimeSpan.FromMinutes(10);

    private readonly Dictionary<string, string> _uploadedIndex = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Cloud sync, the sending half: this machine's index and the pieces of the files that travel
    /// go on the server, so a partner on any network can take them. A Revit central model never
    /// goes; a piece that changed since the index is skipped until the next pass.
    /// </summary>
    private readonly HashSet<string> _checkpointsAsked = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>CGA needs the newest checkpoint of this shared file here (a new local): it is fetched on the next pass.</summary>
    public void AskCheckpoint(string fileId)
    {
        lock (_gate)
            _checkpointsAsked.Add(fileId);
        Poke();
    }

    private readonly Dictionary<string, HashSet<string>> _ownOnServer = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, HashSet<string>>> _peersOnServer = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Files of a folder known to be whole on the server: this machine's, sent with every piece, and
    /// those a partner's server index names (the server shows an index only with its pieces, SRV).
    /// Empty until the first pass with the server.
    /// </summary>
    public IReadOnlySet<string> OnServer(string folderId)
    {
        lock (_gate)
        {
            var known = new HashSet<string>(SyncPath.Comparer);
            if (_ownOnServer.TryGetValue(folderId, out var own))
                known.UnionWith(own);
            if (_peersOnServer.TryGetValue(folderId, out var byPeer))
            {
                foreach (HashSet<string> paths in byPeer.Values)
                    known.UnionWith(paths);
            }
            return known;
        }
    }

    /// <summary>This machine's own files not yet whole on the server, per folder - what an unexpected shutdown would keep from the others.</summary>
    public int UnsentToServer(string folderId, SyncIndex index)
    {
        lock (_gate)
        {
            if (!_ownOnServer.TryGetValue(folderId, out var own))
                return index.Entries.Count(entry => SyncTransferPlanner.IsCarried(entry.Kind));
            return index.Entries.Count(entry => SyncTransferPlanner.IsCarried(entry.Kind) && !own.Contains(entry.RelativePath));
        }
    }

    /// <summary>Connections opened to a LAN partner for the pieces of a transfer (the index uses the first).</summary>
    public int LanStreams { get; init; } = 3;

    /// <summary>Pieces sent to the server at once.</summary>
    public int UploadLanes { get; init; } = 4;

    private readonly Dictionary<string, List<double>> _latencies = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// How long a changeset took from its writer's disk to this one (its file time there → arrival here;
    /// the machines' clocks are NTP-close). The last 50 per folder give p50/p95 for the status (Twin).
    /// </summary>
    private void NoteLatency(string folderId, TimeSpan latency)
    {
        if (latency < TimeSpan.Zero || latency > TimeSpan.FromDays(1))
            return; // an old changeset fetched late, or a clock far off: not a measure of speed
        lock (_gate)
        {
            if (!_latencies.TryGetValue(folderId, out List<double>? list))
                _latencies[folderId] = list = [];
            list.Add(latency.TotalSeconds);
            if (list.Count > 50)
                list.RemoveAt(0);
        }
    }

    private (double? P50, double? P95) Latency(string folderId)
    {
        lock (_gate)
        {
            if (!_latencies.TryGetValue(folderId, out List<double>? list) || list.Count == 0)
                return (null, null);
            double[] sorted = [.. list.Order()];
            return (sorted[(sorted.Length - 1) / 2], sorted[(int)Math.Ceiling(0.95 * sorted.Length) - 1]);
        }
    }

    private async Task<string> UploadAsync(string folderId, SyncIndex index, CancellationToken cancellationToken)
    {
        if (_session is not { } relay)
            return "";
        try
        {
            string json = SyncIndexStore.Serialize(index);
            string shape = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(SyncIndexStore.Serialize(index with { BuiltAtUtc = default }))));
            bool indexSent;
            lock (_gate)
                indexSent = _uploadedIndex.TryGetValue(folderId, out string? sent) && sent == shape;
            if (!indexSent)
            {
                await _server.PutIndexAsync(relay.AccessToken, folderId, json, cancellationToken);
                lock (_gate)
                    _uploadedIndex[folderId] = shape;
            }

            var pieces = new Dictionary<string, (SyncIndexEntry Entry, int Chunk)>(StringComparer.OrdinalIgnoreCase);
            foreach (SyncIndexEntry entry in index.Entries)
            {
                if (!SyncTransferPlanner.IsCarried(entry.Kind) || SyncExclusions.IsExcludedFile(Path.GetFileName(entry.RelativePath)) || HeldOf(folderId).Contains(entry.RelativePath))
                    continue;
                for (int chunk = 0; chunk < entry.ChunkSha256.Count; chunk++)
                    pieces.TryAdd(entry.ChunkSha256[chunk], (entry, chunk));
            }
            var missing = new List<string>();
            int unnamed = 0;
            async Task AskAsync()
            {
                missing.Clear();
                unnamed = 0;
                foreach (string[] batch in pieces.Keys.Chunk(1000))
                {
                    (IReadOnlyList<string> lacking, IReadOnlyList<string> notNamed) = await _server.MissingAndUnnamedAsync(relay.AccessToken, folderId, batch, cancellationToken);
                    missing.AddRange(lacking);
                    unnamed += notNamed.Count;
                }
            }
            await AskAsync();
            if (unnamed > 0)
            {
                // No stored index names our pieces (SRV 8cddd94 - a restored server, a lost index): send it
                // once more and ask again; a second «unnamed» is an error, not a loop.
                await _server.PutIndexAsync(relay.AccessToken, folderId, json, cancellationToken);
                lock (_gate)
                    _uploadedIndex[folderId] = shape;
                await AskAsync();
                if (unnamed > 0)
                {
                    lock (_gate)
                        _uploadedIndex.Remove(folderId);
                    return $"Сервер манай индексийг таньсангүй ({unnamed} хэсэг «unnamed») — дараагийн шалгалтаар дахин илгээнэ.";
                }
            }

            long pending = missing.Sum(sha => pieces.TryGetValue(sha, out var at) ? PieceLength(at.Entry, at.Chunk) : 0);
            long done = 0;
            var rate = new SyncTransferRate();
            DateTimeOffset started = DateTimeOffset.UtcNow;
            rate.Add(0, started);
            DateTime lastReport = DateTime.MinValue;
            void Report(bool force = false)
            {
                if (!force && (DateTime.UtcNow - lastReport).TotalMilliseconds < 1000)
                    return;
                lastReport = DateTime.UtcNow;
                rate.Add(done, DateTimeOffset.UtcNow);
                ReportFolder(folderId, folder => folder with
                {
                    Uploading = done < pending,
                    UploadPendingBytes = pending,
                    UploadedBytes = done,
                    UploadBytesPerSecond = rate.BytesPerSecond,
                    UploadSecondsLeft = rate.SecondsLeft(pending, done),
                });
            }
            if (pending > 0)
                Report(force: true);
            using var window = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            window.CancelAfter(UploadWindow);
            // The rate counts the pieces' own time - not the index and the questions before them
            // (Erk-S Sync, 2026-10-26: «6.1 KB/s» for small files) - and pieces go four at a time,
            // so a folder of small files is not one round trip each.
            DateTimeOffset piecesStarted = DateTimeOffset.UtcNow;
            using var lanes = new SemaphoreSlim(UploadLanes);
            var sending = new List<Task>();
            var notThere = missing.ToHashSet(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (string sha in missing)
                {
                    if (!pieces.TryGetValue(sha, out var at))
                        continue;
                    byte[]? piece = ReadChunk(folderId, at.Entry.RelativePath, at.Entry.Sha256, at.Chunk);
                    if (piece is null)
                        continue;
                    await lanes.WaitAsync(window.Token);
                    sending.Add(Task.Run(async () =>
                    {
                        try
                        {
                            await _server.PutChunkAsync(relay.AccessToken, folderId, sha, piece, window.Token);
                            lock (_gate)
                            {
                                done += piece.Length;
                                notThere.Remove(sha);
                            }
                            lock (rate)
                                Report();
                        }
                        finally
                        {
                            lanes.Release();
                        }
                    }, window.Token));
                    sending.RemoveAll(task => task.IsCompletedSuccessfully);
                    if (sending.FirstOrDefault(task => task.IsFaulted) is { } failed)
                        await failed; // a refusal (426, 409 …) surfaces as before
                }
                await Task.WhenAll(sending);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // The window closed: the rest go on the next pass.
            }
            double seconds = (DateTimeOffset.UtcNow - piecesStarted).TotalSeconds;
            // What is on the server now, whole: the index names it and every piece of it is there
            // (owner, 2026-10-27: a member who sent and went offline must not make others wait - the
            // server is the member that stays; its author needs to know when that is true).
            lock (_gate)
            {
                _ownOnServer[folderId] = index.Entries.Where(entry => SyncTransferPlanner.IsCarried(entry.Kind))
                    .Where(entry => entry.ChunkSha256.All(sha => !notThere.Contains(sha)))
                    .Select(entry => entry.RelativePath)
                    .ToHashSet(SyncPath.Comparer);
            }
            ReportFolder(folderId, folder => folder with
            {
                Uploading = false,
                UploadPendingBytes = Math.Max(0, pending - done),
                UploadedBytes = done,
                UploadBytesPerSecond = 0,
                UploadSecondsLeft = null,
                LastUploadBytesPerSecond = done > 0 && seconds > 0 ? done / seconds : folder.LastUploadBytesPerSecond,
            });
            return "";
        }
        catch (SyncServerException exception) when (exception.Code == OutdatedCode)
        {
            return Outdated();
        }
        catch (SyncServerException exception) when (exception.Code == "sync_chunk_unnamed")
        {
            lock (_gate)
                _uploadedIndex.Remove(folderId);
            return "Сервер индексийг дахин хүсэв — дараагийн шалгалтаар илгээнэ.";
        }
        catch (SyncServerException exception) when (exception.Code is "sync_relay_storage_full" or "sync_quota_exceeded")
        {
            return "Серверийн синкийн сан дүүрсэн — зай гартал хүлээнэ.";
        }
        catch (SyncServerException exception) when (exception.Code == "sync_rate_limited")
        {
            return "Сервер түр хязгаарлалаа — дараагийн шалгалтаар үргэлжилнэ.";
        }
        catch (SyncServerException exception) when (exception.Status == HttpStatusCode.NotFound && exception.Code.StartsWith("http_", StringComparison.Ordinal))
        {
            return "Сервер cloud sync-ийг хараахан дэмжихгүй байна (одоохондоо зөвхөн LAN).";
        }
        catch (Exception exception) when (exception is SyncServerException or HttpRequestException or IOException or TaskCanceledException)
        {
            if (cancellationToken.IsCancellationRequested)
                throw;
            return "Серверт илгээж чадсангүй: " + exception.Message;
        }
    }

    private static long PieceLength(SyncIndexEntry entry, int chunk) =>
        Math.Min(SyncIndex.ChunkSize, entry.Size - (long)chunk * SyncIndex.ChunkSize);

    private void ReportFolder(string folderId, Func<SyncFolderState, SyncFolderState> change)
    {
        SyncFolderState? folder;
        lock (_gate)
            folder = LastState.Folders.FirstOrDefault(item => item.FolderId.Equals(folderId, StringComparison.OrdinalIgnoreCase));
        if (folder is not null)
            UpsertFolder(change(folder));
    }

    /// <summary>Puts <paramref name="folder"/> into the state now and writes it - the pass is still running.</summary>
    private void UpsertFolder(SyncFolderState folder)
    {
        folder = WithRedactions(folder);
        lock (_gate)
        {
            var folders = LastState.Folders.Where(item => !item.FolderId.Equals(folder.FolderId, StringComparison.OrdinalIgnoreCase)).ToList();
            folders.Add(folder);
            LastState = LastState with { Folders = folders };
        }
        Write(LastState);
    }

    /// <summary>A peer's transfer, reported while it runs (at most once a second), so a person sees how far it is.</summary>
    private void ReportPeer(string folderId, SyncPeerState peer)
    {
        SyncFolderState? folder;
        lock (_gate)
            folder = LastState.Folders.FirstOrDefault(item => item.FolderId.Equals(folderId, StringComparison.OrdinalIgnoreCase));
        if (folder is null)
            return;
        var peers = folder.Peers.Where(item => !item.DeviceKeyId.Equals(peer.DeviceKeyId, StringComparison.OrdinalIgnoreCase)).ToList();
        peers.Add(peer);
        UpsertFolder(folder with { Peers = peers });
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
        WriteStatus(running: true);
        StateWritten?.Invoke(LastState);
    }

    /// <summary>Every pass (at least once per <see cref="Period"/>) and once more, «not running», at the end.</summary>
    private void WriteStatus(bool running)
    {
        try
        {
            SyncStatusReport.Write(_home.StatusPath, SyncStatusReport.Of(LastState, running));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;
        if (LastState.UpdatedAtUtc != default)
            WriteStatus(running: false);
        DisposePush();
        _discovery?.Dispose();
        if (_exchange is not null)
            await _exchange.DisposeAsync();
        await _centrals.DisposeAsync();
        _wake.Dispose();
    }
}
