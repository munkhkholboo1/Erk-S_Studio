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
