using System.Security.Cryptography;
using System.Text.Json;

namespace ErkS.Sync.Core;

/// <summary>One file a machine holds in a sync folder.</summary>
/// <param name="RelativePath">Inside the folder, with «\».</param>
/// <param name="Sha256">Of the whole file, lower-case hex.</param>
/// <param name="ChunkSha256">Of each <see cref="SyncIndex.ChunkSize"/> piece, in order - what 1.2 sends deltas by.</param>
public sealed record SyncIndexEntry(
    string RelativePath,
    long Size,
    DateTime LastWriteUtc,
    SyncFileKind Kind,
    string Sha256,
    IReadOnlyList<string> ChunkSha256);

/// <summary>
/// What one machine holds in one sync folder: paths, sizes and hashes - never content.
///
/// 🔴 THE INDEX STAYS ON THE MACHINES. It is exchanged between members' devices only; file
/// names and hashes never reach the server (task 1.1, hard boundary), which is why it is kept
/// in this machine's own state and not in the shared folder.
/// </summary>
public sealed record SyncIndex
{
    public const int CurrentFormatVersion = 1;

    /// <summary>4 MiB: the unit a changed RVT/DWG will later be sent in.</summary>
    public const int ChunkSize = 4 * 1024 * 1024;

    public int FormatVersion { get; init; } = CurrentFormatVersion;

    public string FolderId { get; init; } = "";

    public DateTimeOffset BuiltAtUtc { get; init; }

    public IReadOnlyList<SyncIndexEntry> Entries { get; init; } = [];
}

public static class SyncIndexBuilder
{
    /// <summary>
    /// Walks <paramref name="folder"/> under the exclusion rules and hashes every file. A file
    /// whose size and write time match <paramref name="previous"/> keeps its hashes - a
    /// gigabyte model is not read again because a PDF beside it changed.
    /// </summary>
    public static SyncIndex Build(
        string folder,
        string folderId,
        SyncIndex? previous = null,
        CancellationToken cancellationToken = default)
    {
        string root = SyncPath.Canonical(folder);
        Dictionary<string, SyncIndexEntry> known = (previous?.Entries ?? [])
            .GroupBy(entry => entry.RelativePath, SyncPath.Comparer)
            .ToDictionary(group => group.Key, group => group.First(), SyncPath.Comparer);
        var entries = new List<SyncIndexEntry>();
        Walk(root, root, insideCentralBackup: false, known, entries, cancellationToken);
        entries.Sort((left, right) => SyncPath.Comparer.Compare(left.RelativePath, right.RelativePath));
        return new SyncIndex
        {
            FolderId = folderId,
            BuiltAtUtc = DateTimeOffset.UtcNow,
            Entries = entries,
        };
    }

    private static void Walk(
        string root,
        string directory,
        bool insideCentralBackup,
        Dictionary<string, SyncIndexEntry> known,
        List<SyncIndexEntry> entries,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string[] files;
        string[] directories;
        try
        {
            files = Directory.GetFiles(directory);
            directories = Directory.GetDirectories(directory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return;
        }

        string[] fileNames = files.Select(Path.GetFileName).OfType<string>().ToArray();
        string[] directoryNames = directories.Select(Path.GetFileName).OfType<string>().ToArray();
        IReadOnlySet<string> candidates = SyncExclusions.RevitCentrals(fileNames, directoryNames);
        IReadOnlySet<string> locals = SyncExclusions.RevitLocalCopies(fileNames, candidates);
        IReadOnlySet<string> centrals = candidates.Where(name => !locals.Contains(name)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var centralBackups = centrals
            .Select(name => Path.GetFileNameWithoutExtension(name) + "_backup")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (string file in files)
        {
            string name = Path.GetFileName(file);
            if (SyncExclusions.IsExcludedFile(name) || locals.Contains(name))
                continue;
            FileInfo info;
            try
            {
                info = new FileInfo(file);
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                    continue;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            string relative = SyncPath.Relative(root, file);
            SyncFileKind kind = SyncFileKinds.Of(relative, centrals.Contains(name), insideCentralBackup);
            DateTime written = info.LastWriteTimeUtc;
            if (known.TryGetValue(relative, out SyncIndexEntry? previous) &&
                previous.Size == info.Length &&
                previous.LastWriteUtc == written)
            {
                entries.Add(previous with { RelativePath = relative, Kind = kind });
                continue;
            }

            (string sha, IReadOnlyList<string> chunks)? hashed = TryHash(file, cancellationToken);
            if (hashed is null)
                continue;
            entries.Add(new SyncIndexEntry(relative, info.Length, written, kind, hashed.Value.sha, hashed.Value.chunks));
        }

        foreach (string child in directories)
        {
            string name = Path.GetFileName(child);
            if (SyncExclusions.IsExcludedDirectory(name) || locals.Contains(name))
                continue;
            try
            {
                if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0)
                    continue;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            Walk(root, child, insideCentralBackup || centralBackups.Contains(name), known, entries, cancellationToken);
        }
    }

    private static (string Sha, IReadOnlyList<string> Chunks)? TryHash(string path, CancellationToken cancellationToken)
    {
        try
        {
            using FileStream stream = new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 1024 * 1024);
            using var whole = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var chunks = new List<string>();
            byte[] buffer = new byte[SyncIndex.ChunkSize];
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int filled = 0;
                while (filled < buffer.Length)
                {
                    int read = stream.Read(buffer, filled, buffer.Length - filled);
                    if (read == 0)
                        break;
                    filled += read;
                }
                if (filled == 0)
                    break;
                whole.AppendData(buffer, 0, filled);
                chunks.Add(Convert.ToHexStringLower(SHA256.HashData(buffer.AsSpan(0, filled))));
                if (filled < buffer.Length)
                    break;
            }
            return (Convert.ToHexStringLower(whole.GetHashAndReset()), chunks);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Open with no sharing (a DWG being saved) - it is indexed on the next pass.
            return null;
        }
    }
}

public static class SyncIndexStore
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = false };

    public static void Save(SyncIndex index, string path)
    {
        ArgumentNullException.ThrowIfNull(index);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".writing";
        File.WriteAllText(temporary, JsonSerializer.Serialize(index, Options));
        File.Move(temporary, path, overwrite: true);
    }

    public static SyncIndex? TryLoad(string path)
    {
        if (!File.Exists(path))
            return null;
        try
        {
            return JsonSerializer.Deserialize<SyncIndex>(File.ReadAllText(path), Options);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static string Serialize(SyncIndex index) => JsonSerializer.Serialize(index, Options);

    public static SyncIndex? Deserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<SyncIndex>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>How two machines' copies of one folder differ - by path and hash, never by content.</summary>
public sealed record SyncIndexDifference(
    int SameCount,
    IReadOnlyList<SyncIndexEntry> Different,
    IReadOnlyList<SyncIndexEntry> OnlyHere,
    IReadOnlyList<SyncIndexEntry> OnlyThere)
{
    public bool IsIdentical => Different.Count == 0 && OnlyHere.Count == 0 && OnlyThere.Count == 0;
}

public static class SyncIndexComparison
{
    /// <summary>
    /// 🔴 A DIFFERENT FILE IS REPORTED, NEVER RESOLVED. Owner, 2026-10-23: a colleague's
    /// non-empty folder keeps what matches and shows the rest as «зөрүүтэй» for a person to
    /// decide - nothing is silently overwritten. So this only lists; it chooses nothing.
    /// </summary>
    public static SyncIndexDifference Compare(SyncIndex here, SyncIndex there)
    {
        ArgumentNullException.ThrowIfNull(here);
        ArgumentNullException.ThrowIfNull(there);
        Dictionary<string, SyncIndexEntry> theirs = there.Entries
            .GroupBy(entry => entry.RelativePath, SyncPath.Comparer)
            .ToDictionary(group => group.Key, group => group.First(), SyncPath.Comparer);
        int same = 0;
        var different = new List<SyncIndexEntry>();
        var onlyHere = new List<SyncIndexEntry>();
        var seen = new HashSet<string>(SyncPath.Comparer);
        foreach (SyncIndexEntry mine in here.Entries)
        {
            if (!seen.Add(mine.RelativePath))
                continue;
            if (!theirs.TryGetValue(mine.RelativePath, out SyncIndexEntry? other))
                onlyHere.Add(mine);
            else if (string.Equals(mine.Sha256, other.Sha256, StringComparison.OrdinalIgnoreCase))
                same++;
            else
                different.Add(mine);
        }
        List<SyncIndexEntry> onlyThere = theirs.Values
            .Where(entry => !seen.Contains(entry.RelativePath))
            .OrderBy(entry => entry.RelativePath, SyncPath.Comparer)
            .ToList();
        return new SyncIndexDifference(same, different, onlyHere, onlyThere);
    }
}
