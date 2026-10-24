using System.Text.Json;

namespace ErkS.Sync.Core;

/// <summary>Where this machine keeps SYN's own state. Never inside a project folder.</summary>
public sealed class SyncHome(string root, string? statusPath = null)
{
    /// <summary>
    /// %LOCALAPPDATA%\Erk-S Sync, or ERKS_SYNC_HOME - the second instance a single-machine
    /// test runs beside the first (task 1.1: two instances, one machine). Only the real
    /// instance writes the status file the Twin watches; a second one keeps its own.
    /// </summary>
    public static SyncHome Default()
    {
        string? overridden = Environment.GetEnvironmentVariable("ERKS_SYNC_HOME");
        string localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return string.IsNullOrWhiteSpace(overridden)
            ? new SyncHome(Path.Combine(localData, "Erk-S Sync"), Path.Combine(localData, "Erk-S Platform", "Sync", "status.json"))
            : new SyncHome(overridden);
    }

    public string Root { get; } = root;

    public string StatePath => Path.Combine(Root, "state.json");

    /// <summary>The <see cref="SyncStatusReport"/> file: %LOCALAPPDATA%\Erk-S Platform\Sync\status.json for the real instance.</summary>
    public string StatusPath { get; } = statusPath ?? Path.Combine(root, "status.json");

    public string FolderRoot(string folderId) => Path.Combine(Root, "folders", Sanitize(folderId));

    public string IndexPath(string folderId) => Path.Combine(FolderRoot(folderId), "index.json");

    public string LedgerPath(string folderId) => Path.Combine(FolderRoot(folderId), "versions.json");

    /// <summary>A person's answers for files both machines hold differently (Studio writes, the tray reads).</summary>
    public string DecisionsPath(string folderId) => Path.Combine(FolderRoot(folderId), "decisions.json");

    public string PeerIndexPath(string folderId, string deviceKeyId) =>
        Path.Combine(FolderRoot(folderId), "peers", Sanitize(deviceKeyId) + ".json");

    private static string Sanitize(string value) =>
        string.Concat((value ?? "").Where(character => char.IsLetterOrDigit(character) || character is '-' or '_'));
}

/// <summary>What one machine knows about one other machine of a folder.</summary>
public sealed record SyncPeerState
{
    public string DeviceKeyId { get; init; } = "";

    /// <summary>The account that attached it, as the server reports - shown to members, sent nowhere.</summary>
    public string AccountEmail { get; init; } = "";

    public DateTimeOffset? ServerLastSeenAtUtc { get; init; }

    /// <summary>Last time its index arrived over the LAN; null when it never has.</summary>
    public DateTimeOffset? ExchangedAtUtc { get; init; }

    public string LanAddress { get; init; } = "";

    public string LastError { get; init; } = "";

    public int SameCount { get; init; }

    public int DifferentCount { get; init; }

    public int OnlyHereCount { get; init; }

    public int OnlyThereCount { get; init; }

    /// <summary>1.2: files this machine took from the peer in its last transfer (new or newer versions); see <see cref="LastReceivedAtUtc"/>.</summary>
    public int ReceivedCount { get; init; }

    /// <summary>1.2: in that transfer, the peer's versions kept beside this machine's - both changed the same version.</summary>
    public int KeptBothCount { get; init; }

    /// <summary>1.2: files to take that were open here or changed while the pieces came - next pass.</summary>
    public int WaitingCount { get; init; }

    /// <summary>Files where this machine's version is the newer one; the peer takes them.</summary>
    public int AheadHereCount { get; init; }

    /// <summary>Different copies that never shared a version: shown «зөрүүтэй», a person decides.</summary>
    public int UnrelatedCount { get; init; }

    public long BytesReceived { get; init; }

    public DateTimeOffset? LastReceivedAtUtc { get; init; }

    /// <summary>
    /// How far this machine and the peer are in step: bytes identical on both over every byte
    /// either holds (owner, 2026-10-24: «хэр хэмжээний файл хэдэн хувьтай синк хийгдсэнийг нь
    /// мэдэх хэрэгтэй»). 100 = fully in step.
    /// </summary>
    public double SyncedPercent { get; init; }

    public long SyncedBytes { get; init; }

    public long UnionBytes { get; init; }

    /// <summary>A transfer from this peer is running right now.</summary>
    public bool Transferring { get; init; }

    public int TransferFiles { get; init; }

    public int TransferFilesDone { get; init; }

    public long TransferTotalBytes { get; init; }

    public long TransferDoneBytes { get; init; }

    /// <summary>The file arriving now - this machine's own state, never sent to the server or the Twin.</summary>
    public string CurrentFile { get; init; } = "";

    public long CurrentFileBytes { get; init; }

    public long CurrentFileDoneBytes { get; init; }

    /// <summary>The speed of the transfer running now, bytes a second; 0 before the first second.</summary>
    public double TransferBytesPerSecond { get; init; }

    /// <summary>Seconds left at that speed, or null while there is no speed yet.</summary>
    public double? TransferSecondsLeft { get; init; }

    /// <summary>The average speed of the last finished transfer from this peer.</summary>
    public double LastTransferBytesPerSecond { get; init; }

    /// <summary>Files that never travel by rule (a Revit central model and its data) - not counted as missing.</summary>
    public int NotCarriedCount { get; init; }

    public long NotCarriedBytes { get; init; }

    /// <summary>«lan» - directly on this network; «server» - through the server (cloud sync).</summary>
    public string Route { get; init; } = "";
}

/// <summary>One sync folder as THIS machine sees it.</summary>
public sealed record SyncFolderState
{
    public string FolderId { get; init; } = "";

    public string ProjectId { get; init; } = "";

    public string ProjectCode { get; init; } = "";

    public string ProjectName { get; init; } = "";

    public string CanonicalPath { get; init; } = "";

    /// <summary>The folder on this disk carries the marker of this folder id.</summary>
    public bool PresentHere { get; init; }

    public DateTimeOffset? IndexedAtUtc { get; init; }

    public int FileCount { get; init; }

    public long TotalBytes { get; init; }

    public IReadOnlyList<SyncPeerState> Peers { get; init; } = [];

    /// <summary>Conflict copies kept in this folder here (1.2) - each one a decision waiting for a person.</summary>
    public int ConflictCount { get; init; }

    /// <summary>Erk-S Central: the Revit centrals of this folder this machine serves or reaches.</summary>
    public IReadOnlyList<SyncCentralState> Centrals { get; init; } = [];

    /// <summary>Cloud sync: this machine's pieces going on the server now, for partners on other networks.</summary>
    public bool Uploading { get; init; }

    public long UploadPendingBytes { get; init; }

    public long UploadedBytes { get; init; }

    public double UploadBytesPerSecond { get; init; }

    public double? UploadSecondsLeft { get; init; }

    public double LastUploadBytesPerSecond { get; init; }

    public string LastError { get; init; } = "";
}

/// <summary>
/// The tray process's account of itself, written atomically for Studio's page and the
/// bridge to read. Counts and folder paths only - file names stay in the index files.
/// </summary>
public sealed record SyncState
{
    public int FormatVersion { get; init; } = 1;

    public DateTimeOffset UpdatedAtUtc { get; init; }

    public int ProcessId { get; init; }

    public string DeviceKeyId { get; init; } = "";

    /// <summary>«ok», «no_device_key», «session_refused», «offline» - why the server half may be idle.</summary>
    public string ServerStatus { get; init; } = "";

    public string ServerMessage { get; init; } = "";

    public int LanPort { get; init; }

    public IReadOnlyList<SyncFolderState> Folders { get; init; } = [];
}

public static class SyncStateStore
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static void Save(SyncHome home, SyncState state)
    {
        Directory.CreateDirectory(home.Root);
        string temporary = home.StatePath + ".writing";
        File.WriteAllText(temporary, JsonSerializer.Serialize(state, Options));
        File.Move(temporary, home.StatePath, overwrite: true);
    }

    public static SyncState? TryLoad(SyncHome home)
    {
        try
        {
            return File.Exists(home.StatePath)
                ? JsonSerializer.Deserialize<SyncState>(File.ReadAllText(home.StatePath), Options)
                : null;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
