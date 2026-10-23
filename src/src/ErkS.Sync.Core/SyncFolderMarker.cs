using System.Text.Json;

namespace ErkS.Sync.Core;

/// <summary>
/// Which registered sync folder a directory on disk is. Written in the hidden
/// <c>.erks-sync\folder.json</c> beside the project's files.
/// </summary>
/// <remarks>
/// It names the folder and nothing else: no index, no content (task 1.1). The index lives in
/// this machine's own state, because it describes what THIS machine holds.
/// </remarks>
public sealed record SyncFolderIdentity
{
    public const int CurrentFormatVersion = 1;

    public int FormatVersion { get; init; } = CurrentFormatVersion;

    public string FolderId { get; init; } = "";

    public string ProjectCode { get; init; } = "";

    public string ProjectId { get; init; } = "";

    /// <summary>The full path every member's copy must have (see <see cref="SyncPath"/>).</summary>
    public string CanonicalPath { get; init; } = "";

    public DateTimeOffset CreatedAtUtc { get; init; }
}

public static class SyncFolderMarker
{
    public const string FileName = "folder.json";

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static string DirectoryOf(string folder) =>
        Path.Combine(SyncPath.Canonical(folder), SyncExclusions.MarkerDirectoryName);

    public static void Write(string folder, SyncFolderIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        string directory = DirectoryOf(folder);
        DirectoryInfo info = Directory.CreateDirectory(directory);
        info.Attributes |= FileAttributes.Hidden;
        string path = Path.Combine(directory, FileName);
        string temporary = path + ".writing";
        File.WriteAllText(temporary, JsonSerializer.Serialize(identity, Options));
        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>
    /// Takes away this folder's marker when it is the marker of <paramref name="folderId"/> - a
    /// folder the server no longer knows, or this machine leaving it. Only the hidden marker
    /// goes; the project's files are never touched.
    /// </summary>
    public static bool Remove(string folder, string folderId)
    {
        if (TryRead(folder) is not { } identity ||
            !identity.FolderId.Equals((folderId ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        DirectoryInfo directory = new(DirectoryOf(folder));
        directory.Attributes &= ~FileAttributes.Hidden;
        directory.Delete(recursive: true);
        return true;
    }

    /// <summary>The marker of this exact folder, or null when it has none or it is unreadable.</summary>
    public static SyncFolderIdentity? TryRead(string folder)
    {
        string path = Path.Combine(DirectoryOf(folder), FileName);
        if (!File.Exists(path))
            return null;
        try
        {
            SyncFolderIdentity? identity = JsonSerializer.Deserialize<SyncFolderIdentity>(File.ReadAllText(path), Options);
            return identity is { FolderId.Length: > 0 } ? identity : null;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The nearest folder at or above <paramref name="path"/> that carries a marker.</summary>
    public static string? FindEnclosing(string path)
    {
        DirectoryInfo? current = new(SyncPath.Canonical(path));
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, SyncExclusions.MarkerDirectoryName, FileName)))
                return SyncPath.Canonical(current.FullName);
            current = current.Parent;
        }
        return null;
    }

    /// <summary>A sync folder somewhere BELOW <paramref name="folder"/> (not the folder itself).</summary>
    public static string? FindNested(string folder)
    {
        string root = SyncPath.Canonical(folder);
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            string current = pending.Pop();
            IEnumerable<string> children;
            try
            {
                children = Directory.EnumerateDirectories(current);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            foreach (string child in children)
            {
                string name = Path.GetFileName(child);
                if (name.Equals(SyncExclusions.MarkerDirectoryName, StringComparison.OrdinalIgnoreCase))
                {
                    if (!SyncPath.Same(current, root) && File.Exists(Path.Combine(child, FileName)))
                        return SyncPath.Canonical(current);
                    continue;
                }
                if (SyncExclusions.IsExcludedDirectory(name))
                    continue;
                pending.Push(child);
            }
        }
        return null;
    }
}
