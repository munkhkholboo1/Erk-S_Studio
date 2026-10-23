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
    string LastError);

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
    IReadOnlyList<SyncStatusPeer> Peers);

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
            folder.Peers.Sum(peer => peer.DifferentCount),
            folder.Peers.Select(peer => new SyncStatusPeer(
                peer.DeviceKeyId,
                peer.ServerLastSeenAtUtc,
                peer.ExchangedAtUtc,
                peer.SameCount,
                peer.DifferentCount,
                peer.OnlyHereCount,
                peer.OnlyThereCount,
                peer.LastError)).ToList())).ToList());

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
