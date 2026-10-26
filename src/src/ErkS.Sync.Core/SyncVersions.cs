using System.Text.Json;

namespace ErkS.Sync.Core;

/// <summary>
/// One version of one file in the folder's chain (plan §4.6.3: «Хувилбар бүр гинжтэй (бичигч,
/// дугаар, суурь хэш)»): who wrote it, its number, its hash, and the hashes it replaced,
/// newest first. That history is how a machine tells «theirs came after mine» (take it) from
/// «we both changed the same version» (keep both) from «we never shared one» (show, decide).
/// </summary>
public sealed record SyncFileVersion
{
    public const int HistoryLimit = 32;

    public string Sha256 { get; init; } = "";

    /// <summary>The device key id of the machine where this content first appeared.</summary>
    public string Writer { get; init; } = "";

    public int Seq { get; init; }

    public IReadOnlyList<string> History { get; init; } = [];

    /// <summary>
    /// Versions a person chose to override with this one («Минийхийг үлдээх») rather than edit
    /// from - a machine still holding one keeps it aside when this version arrives.
    /// </summary>
    public IReadOnlyList<string> Overrode { get; init; } = [];

    /// <summary>This version's hash or one it replaced.</summary>
    public bool Knows(string sha256) =>
        Sha256.Equals(sha256, StringComparison.OrdinalIgnoreCase) ||
        History.Contains(sha256, StringComparer.OrdinalIgnoreCase);

    /// <summary>The version a local edit makes of this one.</summary>
    public SyncFileVersion Next(string sha256, string writer) => new()
    {
        Sha256 = sha256,
        Writer = writer,
        Seq = Seq + 1,
        History = new[] { Sha256 }.Concat(History).Take(HistoryLimit).ToList(),
        Overrode = Overrode,
    };
}

/// <summary>A path the ledger last knew with <see cref="Version"/>; <see cref="Deleted"/> when it left the disk here.</summary>
public sealed record SyncLedgerEntry(SyncFileVersion Version, bool Deleted = false);

/// <summary>
/// This machine's memory of every file's version in one folder - kept in SYN's home, never in
/// the shared folder, and never sent to the server. The index a peer receives carries each
/// file's version from here.
/// </summary>
public sealed record SyncVersionLedger
{
    public int FormatVersion { get; init; } = 1;

    public Dictionary<string, SyncLedgerEntry> Files { get; init; } = new(SyncPath.Comparer);

    /// <summary>Peer versions already kept beside a file as conflict copies, by path - each kept once.</summary>
    public Dictionary<string, List<string>> Kept { get; init; } = new(SyncPath.Comparer);

    /// <summary>
    /// Gives every entry of a freshly built <paramref name="index"/> its version and brings
    /// the ledger up to date: an unchanged file keeps its version; a changed one becomes the
    /// next version written here; a new one starts at 1; a vanished one becomes a tombstone,
    /// so a peer's copy is not fetched back as if it were new.
    /// </summary>
    public SyncIndex Stamp(SyncIndex index, string writer)
    {
        ArgumentNullException.ThrowIfNull(index);
        var seen = new HashSet<string>(SyncPath.Comparer);
        var stamped = new List<SyncIndexEntry>(index.Entries.Count);
        foreach (SyncIndexEntry entry in index.Entries)
        {
            seen.Add(entry.RelativePath);
            SyncFileVersion version;
            if (Files.TryGetValue(entry.RelativePath, out SyncLedgerEntry? known))
            {
                version = known.Version.Sha256.Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase)
                    ? known.Version
                    : known.Version.Next(entry.Sha256, writer);
            }
            else
            {
                version = new SyncFileVersion { Sha256 = entry.Sha256, Writer = writer, Seq = 1 };
            }
            Files[entry.RelativePath] = new SyncLedgerEntry(version);
            stamped.Add(entry with { Version = version });
        }
        foreach (string path in Files.Keys.Where(path => !seen.Contains(path)).ToList())
        {
            if (!Files[path].Deleted)
                Files[path] = Files[path] with { Deleted = true };
        }
        return index with { Entries = stamped };
    }

    /// <summary>Drops kept conflict copies no person needs to decide: whole paths <paramref name="path"/> matches, and redacted contents.</summary>
    public void ForgetKept(Func<string, bool> path, IReadOnlySet<string> redacted)
    {
        foreach (string key in Kept.Keys.ToList())
        {
            if (path(key))
            {
                Kept.Remove(key);
                continue;
            }
            Kept[key].RemoveAll(redacted.Contains);
            if (Kept[key].Count == 0)
                Kept.Remove(key);
        }
    }

    public bool HasKept(string relativePath, string sha256) =>
        Kept.TryGetValue(relativePath, out List<string>? shas) && shas.Contains(sha256, StringComparer.OrdinalIgnoreCase);

    public void Keep(string relativePath, string sha256)
    {
        if (!Kept.TryGetValue(relativePath, out List<string>? shas))
            Kept[relativePath] = shas = [];
        if (!shas.Contains(sha256, StringComparer.OrdinalIgnoreCase))
            shas.Add(sha256);
    }

    /// <summary>
    /// «Минийхийг үлдээх»: this machine's version, while it is still the one decided about,
    /// counts as coming after the other's - so the other machine takes it on its next pass.
    /// </summary>
    public void Adopt(IEnumerable<SyncDecision> decisions)
    {
        foreach (SyncDecision decision in decisions.Where(item => item.Choice == SyncChoice.Mine))
        {
            if (!Files.TryGetValue(decision.RelativePath, out SyncLedgerEntry? known) || known.Deleted ||
                !known.Version.Sha256.Equals(decision.MySha256, StringComparison.OrdinalIgnoreCase) ||
                known.Version.Knows(decision.TheirSha256))
            {
                continue;
            }
            Files[decision.RelativePath] = known with
            {
                Version = known.Version with
                {
                    History = known.Version.History.Prepend(decision.TheirSha256).Take(SyncFileVersion.HistoryLimit).ToList(),
                    Overrode = known.Version.Overrode.Prepend(decision.TheirSha256).Take(SyncFileVersion.HistoryLimit).ToList(),
                },
            };
            Kept.Remove(decision.RelativePath);
        }
    }

    /// <summary>A peer's version now lies on this disk at <paramref name="relativePath"/>.</summary>
    public void Applied(string relativePath, SyncFileVersion version)
    {
        Files[relativePath] = new SyncLedgerEntry(version);
        Kept.Remove(relativePath);
    }

    /// <summary>Whether this machine deleted <paramref name="relativePath"/> after holding <paramref name="theirs"/> or a later version of it.</summary>
    public bool DeletedHere(string relativePath, SyncFileVersion theirs) =>
        Files.TryGetValue(relativePath, out SyncLedgerEntry? known) &&
        known.Deleted &&
        known.Version.Knows(theirs.Sha256);
}

public static class SyncVersionLedgerStore
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static SyncVersionLedger Load(string path)
    {
        try
        {
            if (File.Exists(path) &&
                JsonSerializer.Deserialize<SyncVersionLedger>(File.ReadAllText(path), Options) is { } ledger)
            {
                return ledger with
                {
                    Files = new Dictionary<string, SyncLedgerEntry>(ledger.Files ?? [], SyncPath.Comparer),
                    Kept = new Dictionary<string, List<string>>(ledger.Kept ?? [], SyncPath.Comparer),
                };
            }
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
        }
        return new SyncVersionLedger();
    }

    public static void Save(SyncVersionLedger ledger, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".writing";
        File.WriteAllText(temporary, JsonSerializer.Serialize(ledger, Options));
        File.Move(temporary, path, overwrite: true);
    }
}
