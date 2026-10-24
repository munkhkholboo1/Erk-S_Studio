using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;

namespace ErkS.Sync.Core;

/// <summary>A central this machine serves (host) or reaches (member) - counts only, for Studio and Twin.</summary>
public sealed record SyncCentralState
{
    public string CentralId { get; init; } = "";

    /// <summary>The central's path in the folder - this machine's own state, never sent to the server.</summary>
    public string RelativePath { get; init; } = "";

    /// <summary>«host» - the central is on this machine; «member» - it is on another and reached through the mount.</summary>
    public string Role { get; init; } = "";

    public bool Online { get; init; }

    public int MembersConnected { get; init; }

    public int OpenHandles { get; init; }

    public int RolledBack { get; init; }

    public DateTimeOffset? LastSnapshotUtc { get; init; }

    public string LastError { get; init; } = "";
}

/// <summary>
/// Which centrals this machine serves. Off unless a person turned it on for that central
/// (Master's safeguard 1, 2026-10-24: the first tests run on a COPY; the owner's real central is
/// served only with the owner's separate consent). central/enabled.json under the Sync home.
/// </summary>
public static class SyncCentralSwitch
{
    public static string PathOf(SyncHome home) => System.IO.Path.Combine(home.Root, "central", "enabled.json");

    public static IReadOnlySet<string> Enabled(SyncHome home)
    {
        try
        {
            string path = PathOf(home);
            return File.Exists(path)
                ? (System.Text.Json.JsonSerializer.Deserialize<List<string>>(File.ReadAllText(path)) ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public static void Set(SyncHome home, string centralId, bool on)
    {
        HashSet<string> enabled = Enabled(home).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (on)
            enabled.Add(centralId);
        else
            enabled.Remove(centralId);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PathOf(home))!);
        File.WriteAllText(PathOf(home), System.Text.Json.JsonSerializer.Serialize(enabled.Order(StringComparer.Ordinal).ToList()));
    }
}

public static class SyncCentralIds
{
    /// <summary>The id the server sees: a hash of the central's relative path, never its name (SRV 719cdea).</summary>
    public static string Of(string relativePath) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            (relativePath ?? "").Replace('/', '\\').TrimStart('\\').ToLowerInvariant())))[..32];
}

/// <summary>
/// Erk-S Central inside the tray: every Revit central found in this machine's sync folders is
/// served to the team through the relay, as long as it is here; the centrals other machines
/// hold are listed for the mount. The host keeps serving while Studio is closed (the tray runs).
/// </summary>
public sealed class SyncCentralService(SyncHome home, Func<CancellationToken, Task<(Uri Server, string Token)?>> credentials) : IAsyncDisposable
{
    private readonly Dictionary<string, HostRun> _hosts = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public TimeSpan Lease { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// After a pass: start serving the centrals this folder holds, stop those gone, and say which
    /// centrals of the other machines this one reaches as a member.
    /// </summary>
    public IReadOnlyList<SyncCentralState> Refresh(string folderId, string physical, SyncIndex mine, IEnumerable<SyncIndex> peers)
    {
        var here = mine.Entries.Where(entry => entry.Kind == SyncFileKind.RevitCentral)
            .Select(entry => entry.RelativePath)
            .ToHashSet(SyncPath.Comparer);
        var states = new List<SyncCentralState>();
        IReadOnlySet<string> enabled = SyncCentralSwitch.Enabled(home);
        lock (_gate)
        {
            foreach (string relative in here)
            {
                string id = SyncCentralIds.Of(relative);
                string key = folderId + "|" + id;
                if (!enabled.Contains(id))
                {
                    if (_hosts.Remove(key, out HostRun? stopped))
                        stopped.Stop.Cancel();
                    states.Add(new SyncCentralState { CentralId = id, RelativePath = relative, Role = "host", LastError = "Хамтын ажиллагаа асаагүй — эзэн зөвшөөрвөл асна." });
                    continue;
                }
                if (!_hosts.TryGetValue(key, out HostRun? run))
                {
                    string full = Path.GetFullPath(Path.Combine(physical, relative));
                    var host = new SyncCentralHost(
                        Path.GetDirectoryName(full)!,
                        Path.GetFileName(full),
                        Path.Combine(home.Root, "central", id, "snapshots"));
                    run = new HostRun(folderId, id, relative, host, new SyncCentralRelayHost(host) { Lease = Lease });
                    run.Task = RunHostAsync(run);
                    _hosts[key] = run;
                }
                states.Add(run.State());
            }
            foreach ((string key, HostRun run) in _hosts.Where(pair => pair.Value.FolderId.Equals(folderId, StringComparison.OrdinalIgnoreCase) && (!here.Contains(pair.Value.RelativePath) || !enabled.Contains(pair.Value.CentralId))).ToArray())
            {
                run.Stop.Cancel();
                _hosts.Remove(key);
            }
        }
        foreach (string relative in peers.SelectMany(index => index.Entries)
                     .Where(entry => entry.Kind == SyncFileKind.RevitCentral && !here.Contains(entry.RelativePath))
                     .Select(entry => entry.RelativePath)
                     .Distinct(SyncPath.Comparer))
        {
            states.Add(new SyncCentralState { CentralId = SyncCentralIds.Of(relative), RelativePath = relative, Role = "member" });
        }
        return states;
    }

    private async Task RunHostAsync(HostRun run)
    {
        TimeSpan wait = TimeSpan.FromSeconds(5);
        CancellationToken stop = run.Stop.Token;
        while (!stop.IsCancellationRequested)
        {
            try
            {
                (Uri Server, string Token)? road = await credentials(stop);
                if (road is null)
                {
                    run.Error = "Сервертэй холбогдоогүй.";
                }
                else
                {
                    await using SyncCentralWebSocketLink link = await SyncCentralWebSocketLink.ConnectAsync(road.Value.Server, road.Value.Token, run.FolderId, run.CentralId, "host", stop);
                    run.Online = true;
                    run.Error = "";
                    wait = TimeSpan.FromSeconds(5);
                    using var beat = CancellationTokenSource.CreateLinkedTokenSource(stop);
                    Task heartbeat = HeartbeatAsync(run, link, beat.Token);
                    try
                    {
                        await run.Relay.ServeAsync(link, stop);
                    }
                    finally
                    {
                        await beat.CancelAsync();
                        await heartbeat.ContinueWith(_ => { }, TaskScheduler.Default);
                        run.Online = false;
                    }
                    // SRV be7ad1a: a refusal is a close 4000 + status with the reason code.
                    if (link.Closed is { Code: >= 4000 } refused)
                    {
                        run.Error = refused.Reason switch
                        {
                            "sync_central_not_enabled" => "Central-ын хамтын ажиллагааг сервер энэ хавтсанд зөвшөөрөөгүй (эзний багийн хавтас биш).",
                            "sync_central_host_taken" => "Энэ central-ыг өөр машин host болгож нээсэн байна — central нэг л host-той.",
                            "sync_device_not_attached" => "Энэ машин синк хавтсанд холбогдоогүй байна.",
                            "invalid_token" => "Төхөөрөмжийн нэвтрэлт хүчингүй — дахин нэвтэрнэ.",
                            _ => "Сервер татгалзлаа: " + refused.Reason,
                        };
                    }
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception) when (exception is WebSocketException or IOException or HttpRequestException or SyncServerException or InvalidDataException or System.Text.Json.JsonException)
            {
                run.Online = false;
                run.Error = exception is WebSocketException socket && socket.Message.Contains("403", StringComparison.Ordinal)
                    ? "Central-ын синкийг сервер энэ хавтсанд зөвшөөрөөгүй (эзний багийн хавтас биш)."
                    : "Central-ыг сервероор нээж чадсангүй — дахин оролдоно.";
            }
            // Members gone longer than the lease: their unfinished bursts are undone, even while the host's own road is down.
            run.Relay.Sweep(DateTimeOffset.UtcNow);
            try
            {
                await Task.Delay(wait, stop);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            wait = TimeSpan.FromSeconds(Math.Min(60, wait.TotalSeconds * 2));
        }
        run.Host.Dispose();
    }

    /// <summary>A ping every two minutes keeps the relay open (it closes after ten silent minutes); the lease sweep runs each half minute.</summary>
    private static async Task HeartbeatAsync(HostRun run, ISyncCentralRelayLink link, CancellationToken cancellationToken)
    {
        int tick = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            run.Relay.Sweep(DateTimeOffset.UtcNow);
            if (++tick % 4 == 0)
                await link.SendAsync(new SyncCentralRelayMessage(true, """{"type":"ping"}"""u8.ToArray()), cancellationToken);
        }
    }

    public async ValueTask DisposeAsync()
    {
        HostRun[] runs;
        lock (_gate)
        {
            runs = _hosts.Values.ToArray();
            _hosts.Clear();
        }
        foreach (HostRun run in runs)
        {
            await run.Stop.CancelAsync();
            if (run.Task is { } task)
                await task.ContinueWith(_ => { }, TaskScheduler.Default);
            run.Stop.Dispose();
        }
    }

    private sealed class HostRun(string folderId, string centralId, string relativePath, SyncCentralHost host, SyncCentralRelayHost relay)
    {
        public string FolderId { get; } = folderId;

        public string CentralId { get; } = centralId;

        public string RelativePath { get; } = relativePath;

        public SyncCentralHost Host { get; } = host;

        public SyncCentralRelayHost Relay { get; } = relay;

        public CancellationTokenSource Stop { get; } = new();

        public Task? Task { get; set; }

        public volatile bool Online;

        public string Error { get; set; } = "";

        public SyncCentralState State()
        {
            (int _, int handles, DateTimeOffset? snapshot) = Host.Counts;
            return new SyncCentralState
            {
                CentralId = CentralId,
                RelativePath = RelativePath,
                Role = "host",
                Online = Online,
                MembersConnected = Relay.Connected,
                OpenHandles = handles,
                RolledBack = Host.RolledBack,
                LastSnapshotUtc = snapshot,
                LastError = Error,
            };
        }
    }
}
