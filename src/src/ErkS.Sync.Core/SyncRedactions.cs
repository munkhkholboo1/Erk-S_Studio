using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ErkS.Sync.Core;

/// <summary>
/// One redaction: the content with hash <see cref="Old"/> at <see cref="Path"/> must not exist
/// anywhere any more; <see cref="New"/> is the clean content that took its place (null: none).
/// Only hashes - the record itself carries no personal data.
/// </summary>
public sealed record SyncRedaction(
    string Path,
    string Old,
    string? New,
    string Reason,
    string By,
    DateTimeOffset AtUtc,
    string Sig);

/// <summary>
/// Personal data that leaked into a synced file is taken out everywhere, once (owner, 2026-10-26:
/// «хамтрагч онлайн болоход түүний машин дээрх имэйлтэй хуучин багцыг Erk-S Sync өөрөө цэвэрлэдэг,
/// серверт дахин илгээдэггүй»; design _shared/syn-redaction-design-2026-10-26.md).
///
/// The ledger is «.erks-collab\redactions\&lt;deviceKeyId&gt;.json» - one file per machine, append-only,
/// travelling like any file (one writer each, so two machines never write one file), and the
/// server's copy for a machine that comes back online before it has the files. Every machine, each
/// pass and before it builds or uploads its index, removes an old content here - the file, its
/// conflict copies, what was unpacked or kept aside from it - and never indexes, fetches or keeps it
/// again. The clean content arrives by the ordinary transfer.
/// </summary>
public static class SyncRedactions
{
    public const string Reason = "privacy";
    private const string Domain = "erks-redaction/1";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>An e-mail address - what must never be published in a changeset manifest.</summary>
    public static readonly Regex Email = new(@"[^@\s""'<>(),;:]+@[^@\s""'<>(),;:]+\.[a-z]{2,}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string DirectoryOf(string folderRoot) => System.IO.Path.Combine(folderRoot, SyncCollab.DirectoryName, "redactions");

    /// <summary>The bytes a redaction's signature covers - the server checks the same.</summary>
    public static byte[] SignedBytes(SyncRedaction entry) => Encoding.UTF8.GetBytes(string.Join('\n',
        Domain,
        Normal(entry.Path),
        entry.Old.ToLowerInvariant(),
        (entry.New ?? "").ToLowerInvariant(),
        entry.By,
        entry.AtUtc.UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture)));

    /// <summary>
    /// Records a redaction by this machine (the `redact` verb): signed with the device key
    /// (ECDSA P-256, SHA-256, IEEE P1363), appended to this machine's ledger file.
    /// </summary>
    public static SyncRedaction Add(string folderRoot, string relativePath, string oldSha256, string? newSha256, string deviceKeyId, Func<byte[], HashAlgorithmName, byte[]> sign, DateTimeOffset now)
    {
        if (!IsSha(oldSha256))
            throw new InvalidOperationException("Хуучин агуулгын SHA-256 буруу байна.");
        string? clean = string.IsNullOrWhiteSpace(newSha256) ? null : newSha256.Trim().ToLowerInvariant();
        var entry = new SyncRedaction(Normal(relativePath), oldSha256.Trim().ToLowerInvariant(), clean, Reason, deviceKeyId, now, "");
        entry = entry with { Sig = Convert.ToBase64String(sign(SignedBytes(entry), HashAlgorithmName.SHA256)) };
        string path = System.IO.Path.Combine(DirectoryOf(folderRoot), Safe(deviceKeyId) + ".json");
        List<SyncRedaction> mine = Read(path);
        if (!mine.Any(item => item.Path.Equals(entry.Path, StringComparison.OrdinalIgnoreCase) && item.Old == entry.Old))
        {
            mine.Add(entry);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(mine, Json));
            File.Move(temporary, path, overwrite: true);
        }
        return entry;
    }

    /// <summary>Every machine's redactions in this folder, plus <paramref name="fromServer"/>.</summary>
    public static IReadOnlyList<SyncRedaction> Load(string folderRoot, IEnumerable<SyncRedaction>? fromServer = null)
    {
        var all = new List<SyncRedaction>(fromServer ?? []);
        string directory = DirectoryOf(folderRoot);
        if (Directory.Exists(directory))
        {
            foreach (string path in Directory.EnumerateFiles(directory, "*.json"))
                all.AddRange(Read(path));
        }
        return all
            .Where(entry => IsSha(entry.Old) && entry.Path.Length > 0)
            .GroupBy(entry => (Normal(entry.Path).ToLowerInvariant(), entry.Old.ToLowerInvariant()))
            .Select(group => group.First())
            .ToList();
    }

    /// <summary>The contents that must never be indexed, fetched or kept.</summary>
    public static IReadOnlySet<string> OldHashes(IEnumerable<SyncRedaction> redactions) =>
        redactions.Select(entry => entry.Old).ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>The clean contents that took their place - authoritative, like a writer's put-back.</summary>
    public static IReadOnlySet<string> NewHashes(IEnumerable<SyncRedaction> redactions) =>
        redactions.Select(entry => entry.New).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Takes every redacted content out of this machine: the file at the path (the clean one comes
    /// by the transfer), its conflict copies beside it, copies kept in «.erks-sync\replaced», what
    /// was unpacked from a changeset under the Sync home, central copies kept aside there. Nothing
    /// is kept "just in case" - that is the point. A file held open stays until the next pass.
    /// </summary>
    public static int Apply(string folderRoot, SyncHome home, IReadOnlyList<SyncRedaction> redactions)
    {
        if (redactions.Count == 0)
            return 0;
        IReadOnlySet<string> old = OldHashes(redactions);
        int removed = 0;
        foreach (SyncRedaction entry in redactions)
        {
            string target = System.IO.Path.GetFullPath(System.IO.Path.Combine(folderRoot, Normal(entry.Path)));
            if (!SyncPath.IsSameOrInside(folderRoot, target) || SyncPath.Same(folderRoot, target))
                continue;
            string? directory = System.IO.Path.GetDirectoryName(target);
            if (directory is not null && Directory.Exists(directory))
            {
                string stem = System.IO.Path.GetFileNameWithoutExtension(target);
                foreach (string candidate in Directory.EnumerateFiles(directory, stem + "*"))
                    removed += RemoveIfOld(candidate, old);
            }
            // What CGA unpacked from a redacted changeset: unpacked again from the clean one.
            if (Changeset(entry.Path) is { } changeset)
            {
                string unpacked = System.IO.Path.Combine(home.Root, "collab", changeset.FileId, "in", changeset.Writer, changeset.Number);
                if (Directory.Exists(unpacked))
                {
                    TryDeleteDirectory(unpacked);
                    removed++;
                }
            }
        }
        foreach (string kept in new[]
                 {
                     System.IO.Path.Combine(folderRoot, SyncExclusions.MarkerDirectoryName, "replaced"),
                     System.IO.Path.Combine(home.Root, "collab"),
                 })
        {
            if (!Directory.Exists(kept))
                continue;
            foreach (string file in Directory.EnumerateFiles(kept, "*", SearchOption.AllDirectories)
                         .Where(path => path.Contains(System.IO.Path.DirectorySeparatorChar + "replaced" + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
            {
                removed += RemoveIfOld(file, old);
            }
        }
        return removed;
    }

    /// <summary>Whether a manifest (JSON text) carries an e-mail; the publish is refused.</summary>
    public static bool CarriesEmail(string text) => Email.IsMatch(text);

    private static int RemoveIfOld(string path, IReadOnlySet<string> old)
    {
        try
        {
            if (!old.Contains(Sha256Of(path)))
                return 0;
            File.Delete(path);
            return 1;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return 0; // open: the next pass
        }
    }

    private static (string FileId, string Writer, string Number)? Changeset(string relativePath)
    {
        if (!SyncCollab.IsChangesetPath(relativePath))
            return null;
        string[] parts = Normal(relativePath).Split('\\', StringSplitOptions.RemoveEmptyEntries);
        return (parts[1], parts[3], System.IO.Path.GetFileNameWithoutExtension(parts[4]));
    }

    private static List<SyncRedaction> Read(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<List<SyncRedaction>>(File.ReadAllText(path), Json) ?? [] : [];
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    // Kept-aside centrals are large and looked at every pass: a file is hashed again only when it changed.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (long Length, DateTime Written, string Sha)> Hashed = new(StringComparer.OrdinalIgnoreCase);

    private static string Sha256Of(string path)
    {
        var info = new FileInfo(path);
        if (Hashed.TryGetValue(path, out var seen) && seen.Length == info.Length && seen.Written == info.LastWriteTimeUtc)
            return seen.Sha;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        string sha = Convert.ToHexStringLower(SHA256.HashData(stream));
        Hashed[path] = (info.Length, info.LastWriteTimeUtc, sha);
        return sha;
    }

    private static bool IsSha(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static string Normal(string relativePath) => relativePath.Replace('/', '\\').Trim('\\');

    private static string Safe(string value) =>
        string.Concat(value.Select(character => System.IO.Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
}
