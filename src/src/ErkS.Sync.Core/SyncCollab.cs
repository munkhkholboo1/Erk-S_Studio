using System.IO.Compression;
using System.Text.Json;

namespace ErkS.Sync.Core;

/// <summary>A DWG shared block by block (_shared/cga-collab-one-file-design-2026-10-24.md, К1): who owns it, which CGA.</summary>
public sealed record SyncCollabFile
{
    public string FileId { get; init; } = "";

    /// <summary>The DWG, relative to the sync folder - its bytes never travel while it is shared.</summary>
    public string RelativePath { get; init; } = "";

    public string OwnerSeat { get; init; } = "";

    public string CgaVersion { get; init; } = "";

    public DateTimeOffset RegisteredAtUtc { get; init; }
}

/// <summary>One changeset waiting for this machine: the writer, its number, and where CGA reads it.</summary>
public sealed record SyncCollabChangeset(string Writer, long Seq, string ManifestPath, string EntitiesPath);

/// <summary>A writer's log as this machine sees it - numbers only, for CGA, Studio and Twin.</summary>
public sealed record SyncCollabWriter(string Writer, long LastInLog, long Applied, int Waiting, bool Gap);

public sealed record SyncCollabStatus(string FileId, int Checkpoint, IReadOnlyList<SyncCollabWriter> Writers)
{
    public int Waiting => Writers.Sum(writer => writer.Waiting);

    /// <summary>This machine's own writer (the seat that last published here), or "".</summary>
    public string OwnWriter { get; init; } = "";

    /// <summary>Blocks of the file and those of this machine's seat - as CGA last told SYN.</summary>
    public int Blocks { get; init; }

    public int MyBlocks { get; init; }

    /// <summary>This machine's changesets not yet seen in every partner's index.</summary>
    public int PendingOut { get; init; }

    public DateTimeOffset? LastAppliedAtUtc { get; init; }

    public string LastError { get; init; } = "";

    /// <summary>The CGA each writer registered with (CGA: «0.0xx+rg&lt;RoadGenerationOutputVersion&gt;»).</summary>
    public IReadOnlyList<SyncCollabVersion> Versions { get; init; } = [];

    /// <summary>Writers on different CGA builds - Generate would not give the same result (design §3).</summary>
    public bool VersionMismatch => Versions.Select(version => version.CgaVersion).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1;
}

public sealed record SyncCollabVersion(string Writer, string CgaVersion);

/// <summary>
/// A writer is a Studio SEAT, not a machine (P2P stage 1: «бичигч нь суудал, гарын үсэг нь
/// машин») - a person on a new laptop keeps the seat and its log. The id is opaque: it names the
/// writer's folder in the log, which the server sees, so no name or e-mail is in it.
/// </summary>
public static class SyncSeats
{
    public static string Of(string botId, string accountEmail) =>
        !string.IsNullOrWhiteSpace(botId)
            ? "bot-" + new string(botId.Trim().Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray())
            : "acct-" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes((accountEmail ?? "").Trim().ToLowerInvariant())))[..16];
}

/// <summary>collab-whoami: this machine's seat and the folder's team - local to the pipe only.</summary>
public sealed record SyncCollabMember(string Seat, string DisplayName, string Role);

public sealed record SyncCollabWhoAmI(string Seat, string DisplayName, string ProjectId, IReadOnlyList<SyncCollabMember> Team);

/// <summary>This machine's notes on a shared file: its own writer, the block counts CGA gave, when it last applied.</summary>
public sealed record SyncCollabLocal
{
    public string Writer { get; init; } = "";

    public int Blocks { get; init; }

    public int MyBlocks { get; init; }

    public DateTimeOffset? LastAppliedAtUtc { get; init; }

    public string LastError { get; init; } = "";
}

/// <summary>
/// The SYN half of CGA's shared file (К1). While a DWG is shared, its bytes do not travel - two
/// people save it at once and every copy would be «changed on both sides». What travels is each
/// writer's numbered log of changesets: one immutable file per changeset under
/// «.erks-collab\&lt;fileId&gt;\log\&lt;writer&gt;\&lt;seq&gt;.cgacs» in the sync folder, carried by the
/// ordinary LAN/relay transfer - whole or not at all, never twice, never in conflict (each name is
/// written once, by one writer). A receiver takes a writer's changesets strictly in order and
/// stops at a missing number. SYN never opens a changeset's content; it only carries and keeps it.
/// The owner's checkpoint is the one way the DWG's bytes travel again (a new base).
/// </summary>
public static class SyncCollab
{
    public const string DirectoryName = ".erks-collab";
    private const string Extension = ".cgacs";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static string FileRoot(string folderRoot, string fileId) => Path.Combine(folderRoot, DirectoryName, Safe(fileId));

    /// <summary>The shared DWGs of a folder, as their registrations arrived here.</summary>
    public static IReadOnlyList<SyncCollabFile> Files(string folderRoot)
    {
        string root = Path.Combine(folderRoot, DirectoryName);
        if (!Directory.Exists(root))
            return [];
        var files = new List<SyncCollabFile>();
        foreach (string path in Directory.EnumerateFiles(root, "collab.json", SearchOption.AllDirectories))
        {
            try
            {
                if (JsonSerializer.Deserialize<SyncCollabFile>(File.ReadAllText(path), Json) is { FileId.Length: > 0 } file)
                    files.Add(file);
            }
            catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
            {
            }
        }
        return files;
    }

    /// <summary>The DWGs whose bytes stay where they are (relative paths) - the transfer skips them.</summary>
    public static IReadOnlySet<string> HeldPaths(string folderRoot) =>
        Files(folderRoot).Select(file => file.RelativePath).ToHashSet(SyncPath.Comparer);

    /// <summary>
    /// collab-register: the DWG at <paramref name="dwgFullPath"/> is shared from now on. Idempotent;
    /// a different file id for the same DWG is refused (one DWG, one shared file).
    /// </summary>
    public static SyncCollabFile Register(string folderRoot, string dwgFullPath, string fileId, string ownerSeat, string cgaVersion, DateTimeOffset now, string writer = "")
    {
        string relative = Relative(folderRoot, dwgFullPath);
        if (writer.Length > 0 && fileId.Length > 0)
        {
            // Each machine says which CGA it runs; the files travel like any other, one writer each.
            string versions = Path.Combine(FileRoot(folderRoot, fileId), "versions");
            Directory.CreateDirectory(versions);
            string path = Path.Combine(versions, Safe(writer) + ".json");
            byte[] content = JsonSerializer.SerializeToUtf8Bytes(new SyncCollabVersion(writer, cgaVersion), Json);
            if (!File.Exists(path) || !File.ReadAllBytes(path).AsSpan().SequenceEqual(content))
            {
                File.WriteAllBytes(path + ".tmp", content);
                File.Move(path + ".tmp", path, overwrite: true);
            }
        }
        SyncCollabFile? existing = Files(folderRoot).FirstOrDefault(file => SyncPath.Comparer.Equals(file.RelativePath, relative));
        if (existing is not null)
        {
            if (!existing.FileId.Equals(fileId, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Энэ DWG өөр хамтын файлын дугаартай бүртгэгдсэн байна.");
            return existing;
        }
        var file = new SyncCollabFile { FileId = fileId, RelativePath = relative, OwnerSeat = ownerSeat, CgaVersion = cgaVersion, RegisteredAtUtc = now };
        string root = FileRoot(folderRoot, fileId);
        Directory.CreateDirectory(root);
        WriteOnce(Path.Combine(root, "collab.json"), JsonSerializer.SerializeToUtf8Bytes(file, Json));
        return file;
    }

    /// <summary>
    /// collab-publish: the writer's next changeset, numbered here (one more than the highest this
    /// writer has in the log) and written once. The manifest and the entities are packed as they
    /// are - SYN does not read them.
    /// </summary>
    public static long Publish(string folderRoot, string fileId, string writer, string manifestPath, string entitiesPath, SyncHome? home = null, int? blocks = null, int? myBlocks = null)
    {
        if (home is not null)
        {
            SyncCollabLocal local = Local(home, fileId);
            SaveLocal(home, fileId, local with { Writer = writer, Blocks = blocks ?? local.Blocks, MyBlocks = myBlocks ?? local.MyBlocks });
        }
        string log = Path.Combine(FileRoot(folderRoot, fileId), "log", Safe(writer));
        Directory.CreateDirectory(log);
        long seq = Numbers(log).DefaultIfEmpty(0).Max() + 1;
        string temporary = Path.Combine(log, $".{Guid.NewGuid():N}.tmp");
        using (var zip = ZipFile.Open(temporary, ZipArchiveMode.Create))
        {
            zip.CreateEntryFromFile(manifestPath, "manifest.json", CompressionLevel.Optimal);
            zip.CreateEntryFromFile(entitiesPath, "entities.dwg", CompressionLevel.Optimal);
        }
        File.Move(temporary, Path.Combine(log, seq.ToString("D8") + Extension));
        return seq;
    }

    /// <summary>
    /// collab-pull: every writer's changesets this machine has not applied, in each writer's order,
    /// never past a missing number; unpacked under the Sync home for CGA to read. This machine's own
    /// writer is not given back. <paramref name="ack"/> first records what CGA applied.
    /// </summary>
    public static IReadOnlyList<SyncCollabChangeset> Pull(string folderRoot, SyncHome home, string fileId, string ownWriter, IReadOnlyDictionary<string, long>? ack = null)
    {
        Dictionary<string, long> applied = Applied(home, fileId);
        if (ack is not null)
        {
            foreach ((string writer, long seq) in ack)
                applied[writer] = Math.Max(applied.GetValueOrDefault(writer), seq);
            SaveApplied(home, fileId, applied);
            SaveLocal(home, fileId, Local(home, fileId) with { LastAppliedAtUtc = DateTimeOffset.UtcNow, LastError = "" });
        }
        var pending = new List<SyncCollabChangeset>();
        string logs = Path.Combine(FileRoot(folderRoot, fileId), "log");
        if (!Directory.Exists(logs))
            return pending;
        foreach (string log in Directory.EnumerateDirectories(logs).Order(StringComparer.Ordinal))
        {
            string writer = Path.GetFileName(log);
            if (writer.Equals(Safe(ownWriter), StringComparison.OrdinalIgnoreCase))
                continue;
            HashSet<long> present = Numbers(log).ToHashSet();
            for (long seq = applied.GetValueOrDefault(writer) + 1; present.Contains(seq); seq++)
            {
                string unpacked = Path.Combine(home.Root, "collab", Safe(fileId), "in", writer, seq.ToString("D8"));
                string manifest = Path.Combine(unpacked, "manifest.json");
                string entities = Path.Combine(unpacked, "entities.dwg");
                if (!File.Exists(manifest) || !File.Exists(entities))
                {
                    Directory.CreateDirectory(unpacked);
                    ZipFile.ExtractToDirectory(Path.Combine(log, seq.ToString("D8") + Extension), unpacked, overwriteFiles: true);
                }
                pending.Add(new SyncCollabChangeset(writer, seq, manifest, entities));
            }
        }
        return pending;
    }

    /// <summary>collab-status: per writer - the last number in the log here, what was applied, what waits, and whether a number is missing.</summary>
    public static SyncCollabStatus Status(string folderRoot, SyncHome home, string fileId)
    {
        Dictionary<string, long> applied = Applied(home, fileId);
        var writers = new List<SyncCollabWriter>();
        string logs = Path.Combine(FileRoot(folderRoot, fileId), "log");
        if (Directory.Exists(logs))
        {
            foreach (string log in Directory.EnumerateDirectories(logs).Order(StringComparer.Ordinal))
            {
                string writer = Path.GetFileName(log);
                HashSet<long> present = Numbers(log).ToHashSet();
                long last = present.DefaultIfEmpty(0).Max();
                long done = applied.GetValueOrDefault(writer);
                int waiting = 0;
                for (long seq = done + 1; present.Contains(seq); seq++)
                    waiting++;
                writers.Add(new SyncCollabWriter(writer, last, done, waiting, done + waiting < last));
            }
        }
        string checkpoints = Path.Combine(FileRoot(folderRoot, fileId), "checkpoints");
        int checkpoint = Directory.Exists(checkpoints)
            ? Directory.EnumerateFiles(checkpoints, "*.dwg").Select(path => int.TryParse(Path.GetFileNameWithoutExtension(path), out int n) ? n : 0).DefaultIfEmpty(0).Max()
            : 0;
        SyncCollabLocal local = Local(home, fileId);
        var versions = new List<SyncCollabVersion>();
        string versionRoot = Path.Combine(FileRoot(folderRoot, fileId), "versions");
        if (Directory.Exists(versionRoot))
        {
            foreach (string path in Directory.EnumerateFiles(versionRoot, "*.json").Order(StringComparer.Ordinal))
            {
                try
                {
                    if (JsonSerializer.Deserialize<SyncCollabVersion>(File.ReadAllText(path), Json) is { Writer.Length: > 0 } version)
                        versions.Add(version);
                }
                catch (Exception exception) when (exception is IOException or JsonException)
                {
                }
            }
        }
        return new SyncCollabStatus(fileId, checkpoint, writers)
        {
            Versions = versions,
            OwnWriter = local.Writer,
            Blocks = local.Blocks,
            MyBlocks = local.MyBlocks,
            LastAppliedAtUtc = local.LastAppliedAtUtc,
            LastError = local.LastError,
        };
    }

    /// <summary>
    /// How many of this machine's changesets some partner has not got yet: the partners' indexes
    /// (as this machine last saw them) are read for this writer's changeset files.
    /// </summary>
    public static int PendingOut(string fileId, string ownWriter, long lastInLog, IEnumerable<SyncIndex> partners)
    {
        if (ownWriter.Length == 0 || lastInLog == 0)
            return 0;
        string prefix = Path.Combine(DirectoryName, Safe(fileId), "log", Safe(ownWriter)) + Path.DirectorySeparatorChar;
        long delivered = long.MaxValue;
        foreach (SyncIndex partner in partners)
        {
            HashSet<long> seen = partner.Entries
                .Where(entry => entry.RelativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(entry => long.TryParse(Path.GetFileNameWithoutExtension(entry.RelativePath), out long seq) ? seq : 0)
                .ToHashSet();
            long contiguous = 0;
            while (seen.Contains(contiguous + 1))
                contiguous++;
            delivered = Math.Min(delivered, contiguous);
        }
        return delivered == long.MaxValue ? 0 : (int)Math.Max(0, lastInLog - delivered);
    }

    public static SyncCollabLocal Local(SyncHome home, string fileId)
    {
        try
        {
            string path = Path.Combine(home.Root, "collab", Safe(fileId), "local.json");
            return File.Exists(path) ? JsonSerializer.Deserialize<SyncCollabLocal>(File.ReadAllText(path), Json) ?? new SyncCollabLocal() : new SyncCollabLocal();
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            return new SyncCollabLocal();
        }
    }

    public static void SaveLocal(SyncHome home, string fileId, SyncCollabLocal local)
    {
        string path = Path.Combine(home.Root, "collab", Safe(fileId), "local.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(local, Json));
        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>
    /// collab-checkpoint (the owner): the DWG as it is now becomes the next base - the one way its
    /// bytes travel while shared - with the numbers it already holds, so a late joiner starts from
    /// it and pulls only what came after.
    /// </summary>
    public static int Checkpoint(string folderRoot, string fileId, string dwgFullPath, IReadOnlyDictionary<string, long> through)
    {
        string checkpoints = Path.Combine(FileRoot(folderRoot, fileId), "checkpoints");
        Directory.CreateDirectory(checkpoints);
        int next = Directory.EnumerateFiles(checkpoints, "*.dwg")
            .Select(path => int.TryParse(Path.GetFileNameWithoutExtension(path), out int n) ? n : 0)
            .DefaultIfEmpty(0).Max() + 1;
        string name = next.ToString("D6");
        WriteOnce(Path.Combine(checkpoints, name + ".json"), JsonSerializer.SerializeToUtf8Bytes(new { checkpoint = next, through }, Json));
        string temporary = Path.Combine(checkpoints, $".{Guid.NewGuid():N}.tmp");
        using (var input = new FileStream(dwgFullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            input.CopyTo(output);
        File.Move(temporary, Path.Combine(checkpoints, name + ".dwg"));
        return next;
    }

    private static IEnumerable<long> Numbers(string log) =>
        Directory.EnumerateFiles(log, "*" + Extension)
            .Select(path => long.TryParse(Path.GetFileNameWithoutExtension(path), out long seq) ? seq : 0)
            .Where(seq => seq > 0);

    private static string AppliedPath(SyncHome home, string fileId) => Path.Combine(home.Root, "collab", Safe(fileId), "applied.json");

    private static Dictionary<string, long> Applied(SyncHome home, string fileId)
    {
        try
        {
            string path = AppliedPath(home, fileId);
            return File.Exists(path)
                ? new Dictionary<string, long>(JsonSerializer.Deserialize<Dictionary<string, long>>(File.ReadAllText(path)) ?? [], StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            return new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        }
    }

    // Temporaries end in «.tmp», which the index never takes - a half-written changeset never travels.
    private static void SaveApplied(SyncHome home, string fileId, Dictionary<string, long> applied)
    {
        string path = AppliedPath(home, fileId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(applied, Json));
        File.Move(temporary, path, overwrite: true);
    }

    private static void WriteOnce(string path, byte[] content)
    {
        if (File.Exists(path))
            return;
        string temporary = path + ".tmp";
        File.WriteAllBytes(temporary, content);
        File.Move(temporary, path);
    }

    private static string Relative(string folderRoot, string fullPath)
    {
        string root = Path.GetFullPath(folderRoot);
        string full = Path.GetFullPath(fullPath);
        if (!SyncPath.IsSameOrInside(root, full))
            throw new InvalidOperationException("DWG синк хавтсанд алга.");
        return Path.GetRelativePath(root, full);
    }

    /// <summary>A name made safe for a folder: ids and seats only - letters, digits, «-», «_».</summary>
    private static string Safe(string value) =>
        new((value ?? "").Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray());
}
