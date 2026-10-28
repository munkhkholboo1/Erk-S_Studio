using System.Net;
using System.Security.Cryptography.X509Certificates;
using ErkS.Updates;

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

    /// <summary>What came from each partner (folder|device), counted file by file and kept across restarts (received.json).</summary>
    private Dictionary<string, SyncReceived>? _received;

    /// <summary>What arrived in a folder during this pass beyond its index (new files, bytes added): the folder's counts show it at once.</summary>
    private readonly Dictionary<string, (int Files, long Bytes)> _folderArrivals = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Files that arrived from a partner during this pass (folder|device → path|sha): the pass's own «%» counts them.</summary>
    private readonly Dictionary<string, HashSet<string>> _arrivedThisPass = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset _receivedSavedAt;
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
        // Why the connection failed, in the member's words (TLS, DNS, firewall, clock) - not «network».
        _ => NetworkFailure.Explain(exception).Message,
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

    // Kept under the server's per-device limits (SRV 18200f7: 12000 pieces and 1200 questions per 10 minutes),
    // so a big folder is not refused halfway (Erk-S Sync, 2026-10-28).
    private readonly SyncPacer _pieces = new(15);
    private readonly SyncPacer _questions = new(1.5);

    /// <summary>Pieces a second to and from the server (shared by every folder and partner); 15 by default.</summary>
    public double PiecesPerSecond { init => _pieces = new SyncPacer(value); }

    /// <summary>The server limited us: said in the pass's stage, so the Twin and the person see why it waits.</summary>
    private void Limited(SyncServerException refused, TimeSpan pause) =>
        Stage($"сервер хязгаарлав ({(refused.LimitName.Length > 0 ? refused.LimitName : refused.Code)}) — {Math.Ceiling(pause.TotalSeconds)} с хүлээж үргэлжилнэ");

    private CancellationTokenSource _hurry = new();
    private DateTimeOffset? _longSince;

    /// <summary>
    /// A small thing that must travel now (owner, 2026-10-26: a changeset «үүсмэгц шууд») while a long upload or
    /// download holds the pass (Erk-S Sync, 2026-10-28: 14 GB of AC/2026/01 would hold Nairamdal's changesets
    /// for 10+ minutes): the long transfer stops where it is - what went stays - and a new pass starts, the
    /// shared-file folders and their changesets first; the long one goes on after. <paramref name="after"/>:
    /// how long the long work runs before it gives way (a partner's index changing often must not stop it
    /// over and over); a changeset made here gives way at once.
    /// </summary>
    public void Hurry(TimeSpan? after = null)
    {
        Poke();
        lock (_gate)
        {
            if (_longSince is not { } since)
                return;
            TimeSpan wait = (after ?? TimeSpan.Zero) - (DateTimeOffset.UtcNow - since);
            if (wait <= TimeSpan.Zero)
                _hurry.Cancel();
            else
                _hurry.CancelAfter(wait);
        }
    }

    /// <summary>A long transfer begins (or ends): it may be asked to give way while it runs.</summary>
    private CancellationToken LongWork(bool begins)
    {
        lock (_gate)
        {
            _longSince = begins ? DateTimeOffset.UtcNow : null;
            return _hurry.Token;
        }
    }

    private bool Hurried
    {
        get
        {
            lock (_gate)
                return _hurry.IsCancellationRequested;
        }
    }

    /// <summary>The «.erks-sync-ignore» rules of this machine's copy of a folder (its EE projects).</summary>
    private static SyncIgnore IgnoreOf(string physical, SyncIndex mine) =>
        SyncIgnore.Of(physical, mine.Entries.Where(entry => IsIgnoreMarker(entry.RelativePath)).Select(entry => entry.RelativePath));

    private static bool IsIgnoreMarker(string relativePath) =>
        Path.GetFileName(relativePath).Equals(SyncIgnore.FileName, StringComparison.OrdinalIgnoreCase);

    /// <summary>A partner's index without what this folder does not carry: never taken, never «different», never in «%».</summary>
    private static SyncIndex Carried(SyncIndex theirs, SyncIgnore ignore) =>
        ignore.IsEmpty ? theirs : theirs with { Entries = theirs.Entries.Where(entry => !ignore.Ignores(entry.RelativePath)).ToList() };

    /// <summary>Paths in the shared-file area (changesets, checkpoints, notes) - they go before everything else.</summary>
    private static bool IsCollabPath(string relativePath) =>
        relativePath.StartsWith(SyncCollab.DirectoryName + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
        relativePath.StartsWith(SyncCollab.DirectoryName + "/", StringComparison.OrdinalIgnoreCase);

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
        lock (_gate)
        {
            _hurry.Dispose();
            _hurry = new CancellationTokenSource();
            _longSince = null;
        }
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
        string serverReason = "";
        Stage("сервертэй холбогдож байна");
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
            // Erk-S Sync / Master, 2026-10-28: a partner whose TLS failed was told «offline» - the reason is
            // said (clock, antivirus, root store, DNS …), and that the LAN still works while the server does not.
            NetworkFailureReason reason = NetworkFailure.Explain(exception);
            serverStatus = "offline";
            serverReason = reason.Code;
            serverMessage = reason.Message + " LAN-аар ажилласаар байна; сервер хэрэгтэй (өөр сүлжээний) хамтрагчид хүрэхгүй.";
        }

        IReadOnlyList<SyncServerDeviceFolder> known;
        lock (_gate)
            known = _folders;

        var states = new List<SyncFolderState>();
        // Folders with shared files first: their changesets are what must not wait behind a big folder.
        foreach (SyncServerDeviceFolder entry in known.OrderBy(item =>
            Directory.Exists(Path.Combine(_paths.Physical(item.Folder.CanonicalPath), SyncCollab.DirectoryName)) ? 0 : 1))
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
            PassStage = "дууссан",
            PassStageAtUtc = DateTimeOffset.UtcNow,
            DeviceKeyId = _device.DeviceKeyId,
            ServerStatus = serverStatus,
            ServerMessage = serverMessage,
            ServerReason = serverReason,
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
        Dictionary<string, int> placedBefore = SyncCollab.Files(physical)
            .ToDictionary(file => file.FileId, file => SyncCollab.Local(_home, file.FileId).PlacedCheckpoint, StringComparer.OrdinalIgnoreCase);
        IReadOnlySet<string> retired = SyncCollab.Maintain(physical, _home, path => knownHere.GetValueOrDefault(path), _device!.DeviceKeyId, PartnerHashes);
        // A checkpoint just put in place of the central (checked, atomic): CGA makes its local now.
        foreach ((string fileId, int placedEarlier) in placedBefore)
        {
            int placed = SyncCollab.Local(_home, fileId).PlacedCheckpoint;
            if (placed > placedEarlier)
                CollabCheckpointPlaced?.Invoke(fileId, placed);
        }
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
            SyncFileAssembler.TidyParts(physical);
            lock (_gate)
            {
                _held[folder.FolderId] = held;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return state with { PresentHere = true, LastError = "Индекс үүсгэж чадсангүй: " + exception.Message };
        }

        lock (_gate)
            _folderArrivals.Remove(folder.FolderId);
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
        {
            Stage($"«{folder.ProjectCode}»: серверт илгээж байна");
            uploadError = await UploadAsync(folder.FolderId, index, cancellationToken);
        }
        // This machine's own changesets whole on the server: CGA says «Илгээгдлээ ✓ (серверт)» and drops
        // its closing warning - told when the count changes, never polled.
        foreach (SyncCollabFile shared in SyncCollab.Files(physical))
        {
            long unsent = SyncCollab.WithServer(SyncCollab.Status(physical, _home, shared.FileId, WhoAmI(physical)?.Writer ?? ""), physical, OnServer(folder.FolderId), ServerKnown(folder.FolderId)).UnsentToServer;
            bool changed;
            lock (_gate)
            {
                changed = !_unsentTold.TryGetValue(shared.FileId, out long told) || told != unsent;
                _unsentTold[shared.FileId] = unsent;
            }
            if (changed)
                CollabOnServer?.Invoke(shared.FileId, unsent);
        }

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
                .Select(file => SyncCollab.WithServer(SyncCollab.Status(physical, _home, file.FileId, WhoAmI(physical)?.Writer ?? ""), physical, OnServer(folder.FolderId), ServerKnown(folder.FolderId)))
                .ToList(),
            UploadedBytes = shown?.UploadedBytes ?? 0,
            UploadPendingBytes = shown?.UploadPendingBytes ?? 0,
            LastUploadBytesPerSecond = shown?.LastUploadBytesPerSecond ?? 0,
            ConflictCount = SyncVersionLedgerStore.Load(_home.LedgerPath(folder.FolderId)).Kept.Values.Sum(list => list.Count),
            PresentHere = true,
            IndexedAtUtc = index.BuiltAtUtc,
            FileCount = index.Entries.Count + Arrivals(folder.FolderId).Files,
            TotalBytes = index.Entries.Sum(entry => entry.Size) + Arrivals(folder.FolderId).Bytes,
            Peers = peers,
        };
    }

    private (int Files, long Bytes) Arrivals(string folderId)
    {
        lock (_gate)
            return _folderArrivals.GetValueOrDefault(folderId);
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
        string who = device.DeviceKeyId.Length >= 8 ? device.DeviceKeyId[..8] : device.DeviceKeyId;
        SyncIgnore ignore = IgnoreOf(physical, mine);
        Stage($"хамтрагч {who}: LAN-ийг оролдож байна");
        // The partner opened a connection to us and offered it back (our firewall-free side reaches it,
        // its side cannot reach us): ask on it as on our own.
        if (_exchange?.TakeReverse(device.DeviceKeyId, folderId) is { } offered)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TransferWindow);
                SyncIndex theirs = await offered.FetchIndexAsync(timeout.Token);
                SyncIndex taken = Carried(theirs, ignore);
                SyncIndexStore.Save(theirs with { BuiltAtUtc = DateTimeOffset.UtcNow }, stored);
                peer = peer with { LanAddress = heard?.Endpoint.ToString() ?? "", Route = "lan" };
                viaLan = true;
                Stage($"хамтрагч {who}: LAN-аар татаж байна");
                using (var work = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, LongWork(begins: true)))
                    peer = await TransferAsync(folderId, physical, mine, taken,
                        (file, chunk, token) => offered.FetchChunkAsync(file.RelativePath, file.Sha256, chunk, token), peer, work.Token);
                LongWork(begins: false);
            }
            catch (Exception exception) when (exception is SyncLanException or IOException or OperationCanceledException or InvalidDataException)
            {
                if (cancellationToken.IsCancellationRequested)
                    throw;
                viaLan = false;
                error = exception is SyncLanException lan ? lan.Message : "LAN-аар холбогдож чадсангүй.";
                if (exception is OperationCanceledException && Hurried)
                {
                    viaLan = true; // gave way to a changeset mid-transfer: not a failure, and not the server road now
                    error = "";
                }
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
                SyncIndex theirs = Carried(await session.FetchIndexAsync(timeout.Token), ignore);
                SyncIndexStore.Save(theirs with { BuiltAtUtc = DateTimeOffset.UtcNow }, stored);
                peer = peer with { LanAddress = lanEndpoint.ToString(), Route = "lan" };
                if (hinted)
                {
                    lock (_gate)
                        _lanHintMissed.Remove(device.DeviceKeyId);
                }
                viaLan = true;
                Stage($"хамтрагч {who}: LAN-аар татаж байна");
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
                using (var work = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, LongWork(begins: true)))
                    peer = await TransferAsync(folderId, physical, mine, theirs,
                        (file, chunk, token) => lanes[chunk % lanes.Length].FetchChunkAsync(file.RelativePath, file.Sha256, chunk, token), peer, work.Token, lanes.Length);
                LongWork(begins: false);
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
                if (exception is OperationCanceledException && Hurried)
                {
                    viaLan = true; // gave way to a changeset mid-transfer: not a failure, and not the server road now
                    error = "";
                }
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
                using var window = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, LongWork(begins: true));
                window.CancelAfter(TransferWindow);
                Stage($"хамтрагч {who}: серверээс индекс авч байна");
                string? json = await _server.GetIndexAsync(relay.AccessToken, folderId, device.DeviceKeyId, window.Token);
                Stage($"хамтрагч {who}: серверээр татаж байна");
                if (json is null)
                {
                    error = "Хамтрагч индексээ серверт хараахан тавиагүй (түүний Erk-S Sync шинэчлэгдээгүй эсвэл унтарсан).";
                }
                else
                {
                    SyncIndex served = Carried(SyncIndexStore.Deserialize(json) ?? throw new InvalidDataException("bad index"), ignore);
                    // What is not whole on the server yet is left as if unseen - not taken, not a deletion
                    // (owner, 2026-10-27: its author may have gone offline mid-upload; the others go on).
                    int incomplete = served.Entries.Count(entry => entry.Incomplete);
                    SyncIndex theirs = incomplete == 0 ? served : served with { Entries = served.Entries.Where(entry => !entry.Incomplete).ToList() };
                    // Not taken, but waited for: a file the partner is still sending that this machine lacks holds
                    // its «%» below 100 (Erk-S Sync, 2026-10-28: an empty laptop read 100% with 13 339 on the way).
                    Dictionary<string, string> held = mine.Entries.ToDictionary(entry => entry.RelativePath, entry => entry.Sha256, SyncPath.Comparer);
                    SyncIndexEntry[] awaited = served.Entries
                        .Where(entry => entry.Incomplete && SyncTransferPlanner.IsCarried(entry.Kind) &&
                            !(held.TryGetValue(entry.RelativePath, out string? sha) && sha.Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase)))
                        .ToArray();
                    error = "";
                    peer = peer with { Route = "server", ServerIncompleteCount = awaited.Length, ServerIncompleteBytes = awaited.Sum(entry => entry.Size) };
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
                    : exception is OperationCanceledException && Hurried
                        ? "" // gave way to a changeset; it goes on in the next pass
                    : exception is OperationCanceledException
                        ? "Сервероор дамжуулалт энэ удаад хугацаандаа багтсангүй — дараагийн шалгалтаар үргэлжилнэ."
                        : "Сервероор дамжуулж чадсангүй: " + NetworkFailure.Explain(exception).Message;
            }
        }

        SyncIndex? theirsKnown = SyncIndexStore.TryLoad(stored) is { } known ? Carried(known, ignore) : null;
        if (theirsKnown is null)
            return peer with { LastError = error };
        SyncIndexDifference difference = SyncIndexComparison.Compare(mine, theirsKnown);
        SyncCarriedDifference carried = SyncIndexComparison.CompareCarried(mine, theirsKnown, HeldOf(folderId));
        // What this machine still needs from this partner (owner, 2026-10-26: «онлайн гишүүний зөвхөн
        // өөрийнх нь файл хэдэн хувь синк хийгдсэн нь чухал») - what the partner still needs from here
        // is its own number, not this machine's.
        SyncTransferPlan needed = PlanFor(folderId, physical, mine, theirsKnown, SyncVersionLedgerStore.Load(_home.LedgerPath(folderId)));
        // This machine's index is from the start of the pass: what arrived since is here, not still needed.
        HashSet<string> cameNow;
        lock (_gate)
            cameNow = _arrivedThisPass.Remove(folderId + "|" + device.DeviceKeyId, out HashSet<string>? came) ? came : [];
        SyncTransferAction[] stillNeeded = needed.Actions
            .Where(action => action.Kind != SyncTransferKind.KeepBoth && !cameNow.Contains(action.Theirs.RelativePath + "|" + action.Theirs.Sha256))
            .ToArray();
        long cameBytes = needed.Actions.Where(action => cameNow.Contains(action.Theirs.RelativePath + "|" + action.Theirs.Sha256)).Sum(action => action.Theirs.Size);
        long neededBytes = stillNeeded.Sum(action => action.Theirs.Size);
        return WithReceipt(folderId, peer) with
        {
            Transferring = false,
            CurrentFile = "",
            TransferBytesPerSecond = 0,
            TransferSecondsLeft = null,
            SyncedPercent = carried.Difference.SyncedPercent,
            SyncedBytes = carried.Difference.SameBytes,
            UnionBytes = carried.Difference.UnionBytes,
            NeededFiles = stillNeeded.Length,
            NeededBytes = neededBytes,
            HereBytes = mine.Entries.Sum(entry => entry.Size) + cameBytes,
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
        long hereBefore = mine.Entries.Sum(entry => entry.Size);
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
                // «%» and what arrived move while the files come, not only at the end of the pass.
                HereBytes = hereBefore + doneBytes,
                NeededBytes = Math.Max(0, totalBytes - doneBytes),
                NeededFiles = Math.Max(0, plan.Actions.Count - filesDone),
                LastTransferBytes = bytes,
                ReceivedCount = Totals(folderId, peer.DeviceKeyId).Count,
                KeptBothCount = Totals(folderId, peer.DeviceKeyId).Kept,
                BytesReceived = Totals(folderId, peer.DeviceKeyId).Bytes,
                LastReceivedAtUtc = Totals(folderId, peer.DeviceKeyId).AtUtc,
            });
        }
        try
        {
            // The shared files' changesets first; the order is otherwise the plan's.
            SyncIgnore landed = new();
        foreach (SyncTransferAction action in plan.Actions.OrderBy(action =>
            IsCollabPath(action.Theirs.RelativePath) || IsIgnoreMarker(action.Theirs.RelativePath) ? 0 : 1))
            {
                if (!landed.IsEmpty && landed.Ignores(action.Theirs.RelativePath))
                continue; // an EE project's marker that just arrived: not carried
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
                // The bytes that travelled (a small file is not a whole 4 MiB piece).
                long fetched = Math.Min(file.Size, (long)result.ChunksFetched * SyncIndex.ChunkSize);
                bytes += fetched;
                if (action.Kind == SyncTransferKind.KeepBoth)
                {
                    ledger.Keep(file.RelativePath, file.Sha256);
                    kept++;
                    CountArrival(folderId, peer.DeviceKeyId, 0, 1, file.Size);
                }
                else
                {
                    ledger.Applied(file.RelativePath, file.Version!);
                    received++;
                    if (IsIgnoreMarker(file.RelativePath))
                        landed.Add(physical, Path.GetDirectoryName(file.RelativePath) ?? "");
                    // The file's bytes: pieces fetched in a pass that was cut off are part of what came.
                    CountArrival(folderId, peer.DeviceKeyId, 1, 0, file.Size);
                    SyncIndexEntry? before = mine.Entries.FirstOrDefault(entry => SyncPath.Comparer.Equals(entry.RelativePath, file.RelativePath));
                    lock (_gate)
                    {
                        (int files, long added) = _folderArrivals.GetValueOrDefault(folderId);
                        _folderArrivals[folderId] = (files + (before is null ? 1 : 0), added + file.Size - (before?.Size ?? 0));
                    }
                    lock (_gate)
                    {
                        string key = folderId + "|" + peer.DeviceKeyId;
                        if (!_arrivedThisPass.TryGetValue(key, out HashSet<string>? arrived))
                            _arrivedThisPass[key] = arrived = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        arrived.Add(file.RelativePath + "|" + file.Sha256);
                    }
                    if (SyncCollab.IsChangesetPath(file.RelativePath))
                        NoteLatency(folderId, DateTime.UtcNow - file.LastWriteUtc);
                    // A checkpoint arrived: the next pass puts it in place now, not at the fallback period.
                    if (file.RelativePath.Contains(Path.DirectorySeparatorChar + "checkpoints" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                        file.RelativePath.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase))
                        Poke();
                }
                SyncVersionLedgerStore.Save(ledger, ledgerPath);
            }
        }
        finally
        {
            // A transfer cut off by its window or by a changeset still counts what arrived (Erk-S Sync,
            // 2026-10-28: 601 files came through the server and the row said 0).
            NoteReceipt(folderId, peer.DeviceKeyId, received, kept, bytes, started, timings, streams);
        }
        // What arrived stays reported until something else arrives: the pass right after a
        // transfer has nothing to take, and a count that flickers back to 0 tells nobody anything.
        return WithReceipt(folderId, peer) with
        {
            WaitingCount = waiting,
            AheadHereCount = plan.AheadHere,
            UnrelatedCount = plan.Unrelated,
            LastError = lastError,
        };
    }

    private string ReceivedPath => Path.Combine(_home.Root, "received.json");

    private Dictionary<string, SyncReceived> ReceivedTotals()
    {
        if (_received is not null)
            return _received;
        try
        {
            _received = File.Exists(ReceivedPath)
                ? System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, SyncReceived>>(File.ReadAllText(ReceivedPath)) ?? []
                : [];
        }
        catch (Exception exception) when (exception is IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            _received = [];
        }
        _received = new Dictionary<string, SyncReceived>(_received, StringComparer.OrdinalIgnoreCase);
        return _received;
    }

    /// <summary>All that came from <paramref name="deviceKeyId"/> into this folder since it joined (Erk-S Sync, 2026-10-28: a restart started the count at 0).</summary>
    private SyncReceived Totals(string folderId, string deviceKeyId)
    {
        lock (_gate)
            return ReceivedTotals().GetValueOrDefault(folderId + "|" + deviceKeyId) ?? new SyncReceived(0, 0, 0, null);
    }

    /// <summary>One file arrived: counted at once, written down every few seconds and at the end of the transfer.</summary>
    private void CountArrival(string folderId, string deviceKeyId, int count, int kept, long bytes)
    {
        lock (_gate)
        {
            Dictionary<string, SyncReceived> totals = ReceivedTotals();
            SyncReceived was = totals.GetValueOrDefault(folderId + "|" + deviceKeyId) ?? new SyncReceived(0, 0, 0, null);
            totals[folderId + "|" + deviceKeyId] = new SyncReceived(was.Count + count, was.Kept + kept, was.Bytes + bytes, DateTimeOffset.UtcNow);
            if (DateTimeOffset.UtcNow - _receivedSavedAt > TimeSpan.FromSeconds(5))
                SaveReceived();
        }
    }

    private void SaveReceived()
    {
        if (_received is null)
            return;
        try
        {
            Directory.CreateDirectory(_home.Root);
            File.WriteAllText(ReceivedPath + ".tmp", System.Text.Json.JsonSerializer.Serialize(_received));
            File.Move(ReceivedPath + ".tmp", ReceivedPath, overwrite: true);
            _receivedSavedAt = DateTimeOffset.UtcNow;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>What arrived from a partner in a transfer, kept until something else arrives (a pass with nothing to take keeps it).</summary>
    private void NoteReceipt(string folderId, string deviceKeyId, int received, int kept, long bytes, DateTimeOffset started, SyncApplyTimings timings, int streams)
    {
        lock (_gate)
            SaveReceived();
        if (received + kept == 0)
            return;
        double seconds = (DateTimeOffset.UtcNow - started).TotalSeconds;
        lock (_gate)
        {
            _lastReceipts[folderId + "|" + deviceKeyId] = new SyncPeerState
            {
                LastTransferBytesPerSecond = seconds > 0 ? bytes / seconds : 0,
                LastTransferWaitSeconds = timings.WaitSeconds,
                LastTransferHashSeconds = timings.HashSeconds,
                LastTransferDiskSeconds = timings.DiskSeconds,
                LastTransferStreams = streams,
                ReceivedCount = received,
                KeptBothCount = kept,
                BytesReceived = bytes,
                LastTransferBytes = bytes,
                LastReceivedAtUtc = DateTimeOffset.UtcNow,
            };
        }
    }

    /// <summary>A peer's row with the last transfer's receipt - LAN or server, finished or cut off.</summary>
    private SyncPeerState WithReceipt(string folderId, SyncPeerState peer)
    {
        SyncPeerState receipt;
        lock (_gate)
            receipt = _lastReceipts.GetValueOrDefault(folderId + "|" + peer.DeviceKeyId) ?? new SyncPeerState();
        SyncReceived total = Totals(folderId, peer.DeviceKeyId);
        return peer with
        {
            ReceivedCount = total.Count,
            KeptBothCount = total.Kept,
            BytesReceived = total.Bytes,
            LastReceivedAtUtc = total.AtUtc,
            LastTransferBytesPerSecond = receipt.LastTransferBytesPerSecond,
            LastTransferWaitSeconds = receipt.LastTransferWaitSeconds,
            LastTransferHashSeconds = receipt.LastTransferHashSeconds,
            LastTransferDiskSeconds = receipt.LastTransferDiskSeconds,
            LastTransferStreams = receipt.LastTransferStreams,
            LastTransferBytes = receipt.LastTransferBytes,
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
        return await PatientAsync(async next =>
            {
                await _pieces.WaitAsync(next);
                return await _server.GetChunkAsync(token, folderId, file.ChunkSha256[chunk], next);
            }, cancellationToken, limited: Limited)
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
    private readonly Dictionary<string, long> _unsentTold = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A shared file's own changesets not yet whole on the server changed (file id, how many are left; 0 = all there).</summary>
    public event Action<string, long>? CollabOnServer;

    /// <summary>A shared file's checkpoint was put in place of its central here (file id, checkpoint number).</summary>
    public event Action<string, int>? CollabCheckpointPlaced;

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

    /// <summary>Whether SYN has met the server for this folder (sent its index, or read a partner's there).</summary>
    public bool ServerKnown(string folderId)
    {
        lock (_gate)
            return _ownOnServer.ContainsKey(folderId) || _peersOnServer.ContainsKey(folderId);
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

    /// <summary>
    /// A server call that waits out a rate limit and goes on (Erk-S Sync, 2026-10-28: a 14 GB upload hit
    /// sync_rate_limited and then sat until the fallback pass): the server's Retry-After, else 5 s doubling
    /// to 30 s - until <paramref name="cancellationToken"/> (the upload's window) ends.
    /// </summary>
    internal static async Task<T> PatientAsync<T>(
        Func<CancellationToken, Task<T>> call,
        CancellationToken cancellationToken,
        Func<TimeSpan, CancellationToken, Task>? wait = null,
        Action<SyncServerException, TimeSpan>? limited = null)
    {
        wait ??= Task.Delay;
        // Without Retry-After: 30 s, then 60 (SRV, 2026-10-28 - its windows are minutes long).
        TimeSpan next = TimeSpan.FromSeconds(30);
        while (true)
        {
            try
            {
                return await call(cancellationToken);
            }
            catch (SyncServerException refused) when (refused.RateLimited)
            {
                TimeSpan pause;
                if (refused.RetryAfter is { } asked)
                {
                    pause = TimeSpan.FromSeconds(Math.Min(asked.TotalSeconds, 300));
                }
                else
                {
                    pause = next;
                    next = TimeSpan.FromSeconds(Math.Min(next.TotalSeconds * 2, 60));
                }
                limited?.Invoke(refused, pause);
                await wait(pause, cancellationToken);
            }
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
                foreach (string[] batch in pieces.Keys.Chunk(4096))
                {
                    (IReadOnlyList<string> lacking, IReadOnlyList<string> notNamed) = await PatientAsync(
                        async token =>
                        {
                            await _questions.WaitAsync(token);
                            return await _server.MissingAndUnnamedAsync(relay.AccessToken, folderId, batch, token);
                        },
                        cancellationToken,
                        limited: Limited);
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

            // File by file, the small ones first (Erk-S Sync, 2026-10-28: the server's «missing» came back in
            // hash order, 4 lanes spread every file thin, and after 1.3 GB not one of 13 339 files was whole - so
            // the partner, who takes only whole files, took nothing for an hour). Now each file is finished before
            // the next, and the partner starts taking them while the upload goes on.
            missing = missing
                .Where(pieces.ContainsKey)
                .OrderBy(sha => IsCollabPath(pieces[sha].Entry.RelativePath) ? 0 : 1)
                .ThenBy(sha => pieces[sha].Entry.Size)
                .ThenBy(sha => pieces[sha].Entry.RelativePath, StringComparer.Ordinal)
                .ThenBy(sha => pieces[sha].Chunk)
                .ToList();
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
            using var window = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, LongWork(begins: true));
            window.CancelAfter(UploadWindow);
            // The rate counts the pieces' own time - not the index and the questions before them
            // (Erk-S Sync, 2026-10-26: «6.1 KB/s» for small files) - and pieces go four at a time,
            // so a folder of small files is not one round trip each.
            DateTimeOffset piecesStarted = DateTimeOffset.UtcNow;
            using var lanes = new SemaphoreSlim(UploadLanes);
            var sending = new List<Task>();
            var notThere = missing.ToHashSet(StringComparer.OrdinalIgnoreCase);
            void MarkOnServer()
            {
                lock (_gate)
                {
                    _ownOnServer[folderId] = index.Entries.Where(entry => SyncTransferPlanner.IsCarried(entry.Kind))
                        .Where(entry => entry.ChunkSha256.All(sha => !notThere.Contains(sha)))
                        .Select(entry => entry.RelativePath)
                        .ToHashSet(SyncPath.Comparer);
                }
            }
            // Known before a piece goes: if the upload breaks off, what is still missing is said, not hidden.
            MarkOnServer();
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
                            await PatientAsync(async token =>
                            {
                                await _pieces.WaitAsync(token);
                                await _server.PutChunkAsync(relay.AccessToken, folderId, sha, piece, token);
                                return true;
                            }, window.Token, limited: Limited);
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
            LongWork(begins: false);
            double seconds = (DateTimeOffset.UtcNow - piecesStarted).TotalSeconds;
            // What is on the server now, whole: the index names it and every piece of it is there
            // (owner, 2026-10-27: a member who sent and went offline must not make others wait - the
            // server is the member that stays; its author needs to know when that is true).
            MarkOnServer();
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
        catch (SyncServerException exception) when (exception.RateLimited)
        {
            // The index itself was limited: the next pass comes after the wait, not after the fallback period.
            TimeSpan after = exception.RetryAfter ?? TimeSpan.FromSeconds(15);
            _ = Task.Delay(after, cancellationToken).ContinueWith(_ => Poke(), TaskScheduler.Default);
            return $"Сервер түр хязгаарлалаа — {Math.Ceiling(after.TotalSeconds)} с-ийн дараа үргэлжилнэ.";
        }
        catch (SyncServerException exception) when (exception.Status == HttpStatusCode.NotFound && exception.Code.StartsWith("http_", StringComparison.Ordinal))
        {
            return "Сервер cloud sync-ийг хараахан дэмжихгүй байна (одоохондоо зөвхөн LAN).";
        }
        catch (Exception exception) when (exception is SyncServerException or HttpRequestException or IOException or TaskCanceledException)
        {
            if (cancellationToken.IsCancellationRequested)
                throw;
            return "Серверт илгээж чадсангүй: " + (exception is SyncServerException ? exception.Message : NetworkFailure.Explain(exception).Message);
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

    /// <summary>
    /// What the pass is doing now, written at once (Erk-S Sync, 2026-10-28: a laptop's status.json stopped for
    /// eight minutes and nobody could tell where its pass stood). The Twin sees the stage and since when.
    /// </summary>
    private void Stage(string what)
    {
        lock (_gate)
            LastState = LastState with { PassStage = what, PassStageAtUtc = DateTimeOffset.UtcNow };
        Write(LastState);
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
        SyncIndex? index;
        lock (_gate)
            index = _indexes.GetValueOrDefault(folderId);
        // The folder's counts move with what arrives (Erk-S Sync, 2026-10-28: a 20-minute pass showed 789 files of 1528).
        UpsertFolder(index is null
            ? folder with { Peers = peers }
            : folder with
            {
                Peers = peers,
                FileCount = index.Entries.Count + Arrivals(folderId).Files,
                TotalBytes = index.Entries.Sum(entry => entry.Size) + Arrivals(folderId).Bytes,
            });
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
