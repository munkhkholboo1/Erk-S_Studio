using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;

namespace ErkS.Sync.Core;

/// <summary>A changeset of another member that arrived here (collab-subscribe, _shared/syn-push-latency-plan-2026-10-26.md).</summary>
public sealed record SyncCollabArrival(string FileId, string Writer, long Seq, string Path);

/// <summary>A product's published version on one update channel ("" for the main one; CGA has «2026», «2027» …).</summary>
public sealed record SyncRelease(string ProductCode, string Channel, string Version);

/// <summary>
/// Changes are felt, not polled for (owner, 2026-10-24: «өөрчлөлтийг мэдэрч уншдаг болгохгуй бол
/// дэмий ачаалал үүсгэнэ»; 2026-10-26: «байж болох хамгийн хурдан»): a file written in a sync folder
/// (FileSystemWatcher, debounced), a partner's index changed on the LAN (its announcement), the
/// server's events socket (a partner put a new index) - each runs a pass at once. The fixed period
/// stays only as a fallback: long while the server's events reach us, short while they do not.
/// </summary>
public sealed partial class SyncEngine
{
    /// <summary>The fallback period while the server's events socket is connected.</summary>
    public static readonly TimeSpan PushedPeriod = TimeSpan.FromMinutes(10);

    /// <summary>How often the events socket pings the server to prove it is still alive.</summary>
    public static readonly TimeSpan KeepAlive = TimeSpan.FromSeconds(20);

    private readonly Dictionary<string, FileSystemWatcher> _watchers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _collabSeen = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(long Target, TaskCompletionSource Done)> _passWaiters = [];
    private Timer? _debounce;
    private long _passesStarted;
    private long _passesFinished;
    private volatile bool _eventsConnected;

    /// <summary>Raised for each changeset of another member that arrived since the last pass.</summary>
    public event Action<SyncCollabArrival>? CollabArrived;

    private readonly Dictionary<string, SyncRelease> _releases = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A product's new version was published (product code, version) - told by the server the moment it
    /// is published, never polled for (owner, 2026-10-26: «шинэ хувилбар нийтлэхэд л мэдэгддэг байх хэрэгтэй»).
    /// </summary>
    public event Action<SyncRelease>? ReleasePublished;

    /// <summary>The newest version the server announced per product and channel since this SYN started.</summary>
    public IReadOnlyList<SyncRelease> Releases
    {
        get
        {
            lock (_gate)
                return [.. _releases.Values];
        }
    }

    /// <summary>Whether the server's events reach this machine now (Twin, status).</summary>
    public bool EventsConnected => _eventsConnected;

    /// <summary>
    /// Runs a pass now and waits for it, at most <paramref name="max"/> (collab-pull: a Generate takes
    /// the newest work - the plan's «≤2 с»). Returns whether a pass begun after the call finished.
    /// </summary>
    public async Task<bool> RefreshAsync(TimeSpan max, CancellationToken cancellationToken)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
            _passWaiters.Add((Interlocked.Read(ref _passesStarted) + 1, done));
        Poke();
        Task finished = await Task.WhenAny(done.Task, Task.Delay(max, cancellationToken));
        return finished == done.Task;
    }

    private void PassStarted() => Interlocked.Increment(ref _passesStarted);

    /// <summary>A whole pass has run: <see cref="LastState"/> lists every folder, not only the ones reached so far.</summary>
    public bool FirstPassDone => Interlocked.Read(ref _passesFinished) > 0;

    private void PassFinished()
    {
        long finished = Interlocked.Read(ref _passesStarted);
        Interlocked.Increment(ref _passesFinished);
        List<TaskCompletionSource> ready;
        lock (_gate)
        {
            ready = _passWaiters.Where(waiter => waiter.Target <= finished).Select(waiter => waiter.Done).ToList();
            _passWaiters.RemoveAll(waiter => waiter.Target <= finished);
        }
        foreach (TaskCompletionSource waiter in ready)
            waiter.TrySetResult();
        RaiseArrivals();
    }

    /// <summary>A sync folder is watched from its first pass on; writes in it run a pass (1.5 s after the last one).</summary>
    private void Watch(string physical)
    {
        lock (_gate)
        {
            if (_watchers.ContainsKey(physical) || Volatile.Read(ref _disposed) == 1)
                return;
            try
            {
                var watcher = new FileSystemWatcher(physical)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                    InternalBufferSize = 64 * 1024,
                };
                watcher.Changed += (_, change) => Felt(physical, change.FullPath);
                watcher.Created += (_, change) => Felt(physical, change.FullPath);
                watcher.Deleted += (_, change) => Felt(physical, change.FullPath);
                watcher.Renamed += (_, change) => Felt(physical, change.FullPath);
                watcher.Error += (_, _) => Debounced(); // the buffer overflowed: look at everything
                watcher.EnableRaisingEvents = true;
                _watchers[physical] = watcher;
            }
            catch (Exception exception) when (exception is ArgumentException or IOException or PlatformNotSupportedException or UnauthorizedAccessException)
            {
                // Not watchable (a network share without notifications): the fallback period covers it.
            }
        }
    }

    private void Felt(string physical, string fullPath)
    {
        string relative = Path.GetRelativePath(physical, fullPath);
        // SYN's own bookkeeping, and the shared-file folder SYN itself writes (a publish pokes on its own).
        if (relative.StartsWith(SyncExclusions.MarkerDirectoryName, StringComparison.OrdinalIgnoreCase) ||
            relative.StartsWith(SyncCollab.DirectoryName, StringComparison.OrdinalIgnoreCase) ||
            SyncExclusions.IsExcludedFile(Path.GetFileName(fullPath)))
        {
            return;
        }
        Debounced();
    }

    private void Debounced()
    {
        lock (_gate)
        {
            if (Volatile.Read(ref _disposed) == 1)
                return;
            _debounce ??= new Timer(_ => Poke());
            _debounce.Change(1500, Timeout.Infinite);
        }
    }

    /// <summary>Each other member's changesets that are new here since the last pass (the first pass only primes).</summary>
    private void RaiseArrivals()
    {
        var arrivals = new List<SyncCollabArrival>();
        Dictionary<string, string> roots;
        lock (_gate)
            roots = new Dictionary<string, string>(_roots, StringComparer.OrdinalIgnoreCase);
        foreach (SyncFolderState folder in LastState.Folders)
        {
            if (!roots.TryGetValue(folder.FolderId, out string? physical))
                continue;
            IReadOnlyList<SyncCollabFile> files = SyncCollab.Files(physical);
            foreach (SyncCollabStatus collab in folder.Collabs)
            {
                string central = files.FirstOrDefault(file => file.FileId.Equals(collab.FileId, StringComparison.OrdinalIgnoreCase)) is { } shared
                    ? Path.Combine(physical, shared.RelativePath)
                    : "";
                foreach (SyncCollabWriter writer in collab.Writers)
                {
                    if (writer.Writer.Equals(collab.OwnWriter, StringComparison.OrdinalIgnoreCase))
                        continue;
                    string key = collab.FileId + "|" + writer.Writer;
                    lock (_gate)
                    {
                        if (_collabSeen.TryGetValue(key, out long seen))
                        {
                            for (long seq = seen + 1; seq <= writer.LastInLog; seq++)
                                arrivals.Add(new SyncCollabArrival(collab.FileId, writer.Writer, seq, central));
                        }
                        _collabSeen[key] = Math.Max(seen, writer.LastInLog);
                    }
                }
            }
        }
        foreach (SyncCollabArrival arrival in arrivals)
            CollabArrived?.Invoke(arrival);
    }

    /// <summary>
    /// The server's events socket (SRV, «…/sync/device/events»): any message means a partner put a new
    /// index - a pass runs now. Reconnects with a growing wait (5 s … 10 min); while it is down the
    /// fallback period is the short one.
    /// </summary>
    private async Task EventsLoopAsync(CancellationToken cancellationToken)
    {
        TimeSpan wait = TimeSpan.FromSeconds(5);
        while (!cancellationToken.IsCancellationRequested)
        {
            if (_session is { } session && _server.BaseAddress is { } address)
            {
                using var socket = new ClientWebSocket();
                socket.Options.SetRequestHeader("Authorization", "Bearer " + session.AccessToken);
                // A connection that died without closing (Wi-Fi, sleep, a router dropping it) must be noticed:
                // our own ping, and no pong within 30 s ends it - then it reconnects and «hello» brings what was
                // missed (Erk-S Sync, 2026-10-27: the laptop said «connected» and never heard 125).
                socket.Options.KeepAliveInterval = KeepAlive;
                socket.Options.KeepAliveTimeout = TimeSpan.FromSeconds(30);
                var uri = new UriBuilder(address)
                {
                    Scheme = address.Scheme == Uri.UriSchemeHttps ? "wss" : "ws",
                    Path = "/api/cloud-era/v1/sync/device/events",
                }.Uri;
                try
                {
                    using var connect = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    connect.CancelAfter(TimeSpan.FromSeconds(20));
                    await socket.ConnectAsync(uri, connect.Token);
                    _eventsConnected = true;
                    wait = TimeSpan.FromSeconds(5);
                    Poke(); // whatever happened while it was down
                    byte[] buffer = new byte[16 * 1024];
                    var message = new MemoryStream();
                    while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
                    {
                        WebSocketReceiveResult received = await socket.ReceiveAsync(buffer, cancellationToken);
                        if (received.MessageType == WebSocketMessageType.Close)
                            break;
                        message.Write(buffer, 0, received.Count);
                        if (!received.EndOfMessage)
                            continue;
                        Heard(message.ToArray());
                        message.SetLength(0);
                    }
                }
                catch (Exception exception) when (exception is WebSocketException or IOException or OperationCanceledException or InvalidOperationException or HttpRequestException)
                {
                    if (cancellationToken.IsCancellationRequested)
                        return;
                }
                finally
                {
                    _eventsConnected = false;
                }
            }
            try
            {
                await Task.Delay(wait, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            wait = TimeSpan.FromTicks(Math.Min(wait.Ticks * 2, PushedPeriod.Ticks));
        }
    }

    /// <summary>
    /// One server event (SRV 195fefa): «index» - a partner put a new index: a pass; «redactions» - the
    /// server's list grew: read it again, then a pass; «hello» - connected (a pass already runs);
    /// «release» - a product's version was published (also sent once on connect for each product):
    /// told on to Studio and the other programs here. Only ids, hashes and versions travel in them.
    /// </summary>
    internal void Heard(byte[] json)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            string type = document.RootElement.TryGetProperty("type", out var kind) ? kind.GetString() ?? "" : "";
            if (type == "redactions" && document.RootElement.TryGetProperty("folderId", out var folder) && folder.GetString() is { Length: > 0 } folderId)
            {
                lock (_gate)
                    _serverRedactions.Remove(folderId);
            }
            if (type is "index" or "redactions")
                Poke();
            if (type == "release" &&
                document.RootElement.TryGetProperty("productCode", out var product) && product.GetString() is { Length: > 0 } productCode &&
                document.RootElement.TryGetProperty("version", out var version) && version.GetString() is { Length: > 0 } released)
            {
                string channel = document.RootElement.TryGetProperty("channel", out var lane) && lane.ValueKind == System.Text.Json.JsonValueKind.String
                    ? lane.GetString() ?? ""
                    : "";
                var release = new SyncRelease(productCode, channel, released);
                lock (_gate)
                    _releases[productCode + "|" + channel] = release;
                ReleasePublished?.Invoke(release);
            }
        }
        catch (System.Text.Json.JsonException)
        {
            Poke(); // something happened - look
        }
    }

    /// <summary>A short stamp of a folder's index (its paths and hashes), announced on the LAN so a partner here takes a change at once.</summary>
    private static string Stamp(SyncIndex index)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (SyncIndexEntry entry in index.Entries.OrderBy(entry => entry.RelativePath, StringComparer.Ordinal))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(entry.RelativePath));
            hash.AppendData(Encoding.UTF8.GetBytes(entry.Sha256));
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset())[..16];
    }

    private void DisposePush()
    {
        lock (_gate)
        {
            foreach (FileSystemWatcher watcher in _watchers.Values)
                watcher.Dispose();
            _watchers.Clear();
            _debounce?.Dispose();
            _debounce = null;
            foreach ((_, TaskCompletionSource done) in _passWaiters)
                done.TrySetResult();
            _passWaiters.Clear();
        }
    }
}
