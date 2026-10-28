using System.Text.Encodings.Web;
using System.Text.Json;

namespace ErkS.Sync.Core;

public sealed record SyncStatusPeer(
    string DeviceKeyId,
    DateTimeOffset? ServerLastSeenAtUtc,
    DateTimeOffset? ExchangedAtUtc,
    int SameCount,
    int DifferentCount,
    int OnlyHereCount,
    int OnlyThereCount,
    string LastError,
    int ReceivedCount = 0,
    int KeptBothCount = 0,
    int WaitingCount = 0,
    int UnrelatedCount = 0,
    long BytesReceived = 0,
    DateTimeOffset? LastReceivedAtUtc = null,
    double SyncedPercent = 0,
    bool Transferring = false,
    long TransferTotalBytes = 0,
    long TransferDoneBytes = 0,
    double BytesPerSecond = 0,
    int NotCarriedCount = 0)
{
    /// <summary>The last transfer from this peer: seconds waiting for pieces, hashing, writing, and its connections (Twin).</summary>
    public double LastWaitSeconds { get; init; }

    public double LastHashSeconds { get; init; }

    public double LastDiskSeconds { get; init; }

    public int LastStreams { get; init; }

    /// <summary>This partner's files not yet whole on the server (its author may be offline) - information, nobody waits.</summary>
    public int ServerIncomplete { get; init; }
}

public sealed record SyncStatusFolder(
    string FolderId,
    string ProjectId,
    string ProjectCode,
    string CanonicalPath,
    bool PresentHere,
    int FileCount,
    long TotalBytes,
    DateTimeOffset? IndexedAtUtc,
    string LastError,
    int PeerCount,
    int DifferingFileCount,
    IReadOnlyList<SyncStatusPeer> Peers,
    int ConflictCount = 0,
    double SyncedPercent = 100)
{
    /// <summary>CGA shared files (К1) for Twin - counts only, no file names.</summary>
    public IReadOnlyList<SyncStatusCollab> Collab { get; init; } = [];

    /// <summary>Redactions recorded here the server does not hold yet, and why (Twin).</summary>
    public int RedactionsUnsent { get; init; }

    /// <summary>Changeset delivery to this machine, seconds (p50/p95 of the last 50) - Twin.</summary>
    public double? ChangesetLatencyP50Seconds { get; init; }

    public double? ChangesetLatencyP95Seconds { get; init; }

    public bool EventsConnected { get; init; }

    public string RedactionError { get; init; } = "";
}

/// <summary>
/// One shared DWG in numbers (Master, 2026-10-25): writers on it, blocks, waiting to be applied
/// here (pendingIn), this machine's not yet with every partner (pendingOut), a missing number.
/// </summary>
public sealed record SyncStatusCollab(
    string FileId,
    int Members,
    int Blocks,
    int MyBlocks,
    int PendingInCount,
    int PendingOutCount,
    bool Gap,
    int Checkpoint,
    DateTimeOffset? LastAppliedAtUtc,
    string LastError,
    bool VersionMismatch = false,
    IReadOnlyList<SyncCollabVersion>? Versions = null,
    bool CentralEdited = false)
{
    /// <summary>This machine's changesets not yet whole on the server (an unexpected shutdown would keep them from the others).</summary>
    public long UnsentToServer { get; init; }

    /// <summary>Writers with changesets in the folder that are not yet whole on the server (their author may be offline) - information only, nobody waits.</summary>
    public int WritersNotOnServer { get; init; }

    /// <summary>Machines with the same changesets and a different drawing - a real fault.</summary>
    public int DivergedCount { get; init; }

    /// <summary>Machines that have not taken everything this one has - their Generate is due.</summary>
    public int BehindCount { get; init; }
}

/// <summary>
/// SYN's account of itself for whoever watches from outside: the bridge's «sync-status» answer
/// and the file the Twin reads while Studio is closed (Master, 2026-10-23) are this one record,
/// so the two never drift. Counts and folder paths only - no file name, no account e-mail, no
/// LAN address.
/// </summary>
public sealed record SyncStatusReport(
    bool Running,
    int ProcessId,
    DateTimeOffset? UpdatedAtUtc,
    string DeviceKeyId,
    string ServerStatus,
    string ServerMessage,
    int LanPort,
    IReadOnlyList<SyncStatusFolder> Folders)
{
    /// <summary>The installed Dokany («2.3.1.1000»), "" when none - Erk-S Central's mount needs it (Twin).</summary>
    public string DokanyVersion { get; init; } = "";

    /// <summary>Whether the server answered the last pass (Erk-S Sync, 2026-10-28).</summary>
    public bool ServerReachable => ServerStatus is "ok" or "server_refused" or "session_refused";

    /// <summary>tls | dns | connect | refused | proxy | timeout | other when the server was not reached.</summary>
    public string ServerReason { get; init; } = "";

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
    };

    public static SyncStatusReport Of(SyncState? state, bool running) => new(
        running,
        state?.ProcessId ?? 0,
        state?.UpdatedAtUtc,
        state?.DeviceKeyId ?? "",
        state?.ServerStatus ?? "not_started",
        state?.ServerMessage ?? "",
        state?.LanPort ?? 0,
        (state?.Folders ?? []).Select(folder => new SyncStatusFolder(
            folder.FolderId,
            folder.ProjectId,
            folder.ProjectCode,
            folder.CanonicalPath,
            folder.PresentHere,
            folder.FileCount,
            folder.TotalBytes,
            folder.IndexedAtUtc,
            folder.LastError,
            folder.Peers.Count,
            // A lagging member's old files are no difference for the others (owner, 2026-10-26) - its own
            // row still says them, the folder's count and the Twin do not (Erk-S Sync, 2026-10-28).
            folder.Peers.Where(peer => !peer.Lagging).Sum(peer => peer.DifferentCount),
            folder.Peers.Select(peer => new SyncStatusPeer(
                peer.DeviceKeyId,
                peer.ServerLastSeenAtUtc,
                peer.ExchangedAtUtc,
                peer.SameCount,
                peer.DifferentCount,
                peer.OnlyHereCount,
                peer.OnlyThereCount,
                peer.LastError,
                peer.ReceivedCount,
                peer.KeptBothCount,
                peer.WaitingCount,
                peer.UnrelatedCount,
                peer.BytesReceived,
                peer.LastReceivedAtUtc,
                peer.SyncedPercent,
                peer.Transferring,
                peer.TransferTotalBytes,
                peer.TransferDoneBytes,
                peer.Transferring ? peer.TransferBytesPerSecond : peer.LastTransferBytesPerSecond,
                peer.NotCarriedCount)
            {
                LastWaitSeconds = peer.LastTransferWaitSeconds,
                LastHashSeconds = peer.LastTransferHashSeconds,
                LastDiskSeconds = peer.LastTransferDiskSeconds,
                LastStreams = peer.LastTransferStreams,
                ServerIncomplete = peer.ServerIncompleteCount,
            }).ToList(),
            folder.ConflictCount,
            SyncInStep.Of(folder.Peers).Percent ?? 100)
        {
            RedactionsUnsent = folder.RedactionsUnsent,
            ChangesetLatencyP50Seconds = folder.ChangesetLatencyP50Seconds,
            ChangesetLatencyP95Seconds = folder.ChangesetLatencyP95Seconds,
            EventsConnected = folder.EventsConnected,
            RedactionError = folder.RedactionError,
            Collab = folder.Collabs.Select(collab => new SyncStatusCollab(
                collab.FileId,
                collab.Writers.Count,
                collab.Blocks,
                collab.MyBlocks,
                collab.Writers.Where(writer => !writer.Writer.Equals(collab.OwnWriter, StringComparison.OrdinalIgnoreCase)).Sum(writer => writer.Waiting),
                collab.PendingOut,
                collab.Writers.Any(writer => writer.Gap),
                collab.Checkpoint,
                collab.LastAppliedAtUtc,
                collab.LastError,
                collab.VersionMismatch,
                collab.Versions,
                collab.CentralEdited)
            {
                UnsentToServer = collab.UnsentToServer,
                WritersNotOnServer = collab.OnServerThrough.Count == 0
                    ? 0
                    : collab.Writers.Count(writer => writer.LastInLog > collab.OnServerThrough.GetValueOrDefault(writer.Writer)),
                DivergedCount = collab.Health.Count(health => health.State == "diverged"),
                BehindCount = collab.Health.Count(health => health.State == "behind"),
            }).ToList(),
        }).ToList())
    {
        DokanyVersion = SyncDokany.InstalledVersion(),
        ServerReason = state?.ServerReason ?? "",
    };

    /// <summary>tmp → replace, so a reader holding the file with share-delete never sees half of it.</summary>
    public static void Write(string path, SyncStatusReport report)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".writing";
        File.WriteAllText(temporary, JsonSerializer.Serialize(report, Options));
        File.Move(temporary, path, overwrite: true);
    }

    public static SyncStatusReport? TryRead(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<SyncStatusReport>(File.ReadAllText(path), Options) : null;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

/// <summary>
/// How far a folder is in step over its partners. A partner whose index has not arrived yet is
/// waiting, not 0% (owner, 2026-10-24: the laptop read «0% синк хийгдсэн» while it was fully in
/// step with the one partner it could see; the other had not joined yet).
/// </summary>
public static class SyncInStep
{
    /// <summary>A partner neither the server nor this machine has heard from for this long is offline (a pass is a minute; a long transfer can hold one for some minutes).</summary>
    public static readonly TimeSpan OfflineAfter = TimeSpan.FromMinutes(15);

    public static bool IsOnline(SyncPeerState peer, DateTimeOffset now) =>
        (peer.ServerLastSeenAtUtc is { } seen && now - seen <= OfflineAfter) ||
        (peer.ExchangedAtUtc is { } exchanged && now - exchanged <= OfflineAfter);

    /// <summary>
    /// How much of what this machine needs has arrived, over ONLINE partners only (owner,
    /// 2026-10-26: «оффлайн гишүүний файл хэдэн хувьтай байгаа нь онлайн гишүүний файлын хувьд нөлөөлж
    /// болохгуй. онлайн гишүүнийн зөвхөн өөрийнх нь файл хэдэн хувь синк хийгдсэн нь чухал»): what a
    /// partner still needs from here, or an offline partner's copies, never hold it down. With no
    /// partner online there is nothing to wait for: 100.
    /// </summary>
    public static (double? Percent, int Waiting) Of(IEnumerable<SyncPeerState> peers, DateTimeOffset? now = null)
    {
        SyncPeerState[] all = (peers ?? []).ToArray();
        SyncPeerState[] known = all.Where(peer => peer.ExchangedAtUtc is not null).ToArray();
        DateTimeOffset at = now ?? DateTimeOffset.UtcNow;
        SyncPeerState[] online = known.Where(peer => IsOnline(peer, at) && !peer.Lagging).ToArray();
        return (known.Length == 0 ? null : online.Length == 0 ? 100 : online.Min(peer => peer.InPercent), all.Length - known.Length);
    }

    /// <summary>
    /// Who is behind and why, one line each: «budbayasgalan офлайн (10/25 22:49-өөс) — 1 файл
    /// (1.1 МБ) хүлээгдэж байна». Names are this machine's own UI only.
    /// </summary>
    public static IReadOnlyList<string> Behind(IEnumerable<SyncPeerState> peers, DateTimeOffset? now = null)
    {
        DateTimeOffset at = now ?? DateTimeOffset.UtcNow;
        var lines = new List<string>();
        foreach (SyncPeerState peer in peers ?? [])
        {
            if (peer.ExchangedAtUtc is null || peer.NeededFiles == 0)
                continue;
            int files = peer.NeededFiles;
            long bytes = peer.NeededBytes;
            string state = IsOnline(peer, at)
                ? "онлайн"
                : peer.ServerLastSeenAtUtc is { } seen ? $"офлайн ({seen.ToLocalTime():MM/dd HH:mm}-өөс)" : "офлайн";
            string what = files > 0 ? $"{files} файл ({Size(bytes)}) хүлээгдэж байна" : $"{Size(bytes)} хүлээгдэж байна";
            lines.Add($"{NameOf(peer)} {state} — {what}");
        }
        return lines;
    }

    private static string NameOf(SyncPeerState peer)
    {
        string email = peer.AccountEmail ?? "";
        int at = email.IndexOf('@');
        return at > 0 ? email[..at] : peer.DeviceKeyId.Length >= 8 ? "төхөөрөмж " + peer.DeviceKeyId[..8] : "хамтрагч";
    }

    private static string Size(long bytes) =>
        bytes >= 1 << 30 ? $"{bytes / (double)(1 << 30):0.0} ГБ"
        : bytes >= 1 << 20 ? $"{bytes / (double)(1 << 20):0.0} МБ"
        : bytes >= 1 << 10 ? $"{bytes / 1024.0:0} КБ" : $"{bytes} Б";

    /// <summary>One line for a person: «✓ бүрэн синк хийгдсэн», «N% синк хийгдсэн», and the partners still waiting.</summary>
    public static string Words(IEnumerable<SyncPeerState> peers, DateTimeOffset? now = null)
    {
        (double? percent, int waiting) = Of(peers, now);
        string step = percent is null ? "" : percent >= 100 ? "✓ бүрэн синк хийгдсэн" : $"{percent:0.#}% синк хийгдсэн";
        string wait = waiting == 0 ? "" : $"{waiting} хамтрагч холбогдохыг хүлээж байна";
        return step.Length > 0 && wait.Length > 0 ? step + " · " + wait : step + wait;
    }
}
