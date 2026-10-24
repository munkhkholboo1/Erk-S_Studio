using System.Text.Json;

namespace ErkS.Sync.Core;

public enum SyncChoice
{
    /// <summary>Take the other machine's version here; this machine's is kept in «.erks-sync\replaced».</summary>
    Theirs,

    /// <summary>Keep this machine's version; the other machine takes it.</summary>
    Mine,
}

/// <summary>
/// A person's answer for one file both machines hold differently - the decision the owner said
/// is theirs to make (decision 15: «ялгаатайг "зөрүүтэй" гэж харуулж эзэн шийднэ»). It names
/// both versions it was made about, so it never applies to a later edit of either.
/// </summary>
public sealed record SyncDecision(
    string RelativePath,
    string TheirSha256,
    string MySha256,
    SyncChoice Choice,
    DateTimeOffset DecidedAtUtc);

/// <summary>
/// Studio (or the bridge) writes the decisions; the tray reads them on its next pass. One file
/// per folder in SYN's home, added to and never rewritten by the reader, so the two processes
/// never edit the same bytes.
/// </summary>
public static class SyncDecisionStore
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static IReadOnlyList<SyncDecision> Load(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<List<SyncDecision>>(File.ReadAllText(path), Options) ?? []
                : [];
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Adds <paramref name="decisions"/>, replacing an earlier answer about the same two versions.</summary>
    public static void Add(string path, IEnumerable<SyncDecision> decisions)
    {
        var all = Load(path).ToList();
        foreach (SyncDecision decision in decisions)
        {
            all.RemoveAll(item =>
                SyncPath.Comparer.Equals(item.RelativePath, decision.RelativePath) &&
                item.TheirSha256.Equals(decision.TheirSha256, StringComparison.OrdinalIgnoreCase) &&
                item.MySha256.Equals(decision.MySha256, StringComparison.OrdinalIgnoreCase));
            all.Add(decision);
        }
        // Old answers about versions long gone are dropped; a person decides at most a few thousand.
        if (all.Count > 20000)
            all = all.OrderByDescending(item => item.DecidedAtUtc).Take(20000).ToList();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".writing";
        File.WriteAllText(temporary, JsonSerializer.Serialize(all, Options));
        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>
    /// The decisions for every file <paramref name="mine"/> and <paramref name="theirs"/> hold
    /// differently - or only for <paramref name="paths"/> when given.
    /// </summary>
    public static IReadOnlyList<SyncDecision> ForDifferences(
        SyncIndex mine,
        SyncIndex theirs,
        SyncChoice choice,
        IReadOnlyCollection<string>? paths = null)
    {
        Dictionary<string, SyncIndexEntry> other = theirs.Entries
            .GroupBy(entry => entry.RelativePath, SyncPath.Comparer)
            .ToDictionary(group => group.Key, group => group.First(), SyncPath.Comparer);
        HashSet<string>? only = paths is null ? null : new HashSet<string>(paths, SyncPath.Comparer);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return mine.Entries
            .Where(entry => only is null || only.Contains(entry.RelativePath))
            .Where(entry => other.TryGetValue(entry.RelativePath, out SyncIndexEntry? them) &&
                            !them.Sha256.Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase))
            .Select(entry => new SyncDecision(entry.RelativePath, other[entry.RelativePath].Sha256, entry.Sha256, choice, now))
            .ToList();
    }
}
