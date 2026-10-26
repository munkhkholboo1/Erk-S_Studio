using System.IO.Compression;
using System.Text.Json;

namespace ErkS.Sync.Core;

/// <summary>A DWG shared block by block (_shared/cga-collab-one-file-design-2026-10-24.md, К1): who owns it, which CGA.</summary>
public sealed record SyncCollabFile
{
    public string FileId { get; init; } = "";

    /// <summary>The DWG, relative to the sync folder - its bytes never travel while it is shared.</summary>
    public string RelativePath { get; init; } = "";

    /// <summary>Read from registrations made before 0.001.97; never written - a shared file has no owner.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
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

    /// <summary>The central DWG was saved directly (requirement 7) - for status and Twin.</summary>
    public bool CentralEdited { get; init; }

    /// <summary>The CGA each writer registered with (CGA: «0.0xx+rg&lt;RoadGenerationOutputVersion&gt;»).</summary>
    public IReadOnlyList<SyncCollabVersion> Versions { get; init; } = [];

    /// <summary>Writers on different CGA builds - Generate would not give the same result (design §3).</summary>
    public bool VersionMismatch => Versions.Select(version => version.CgaVersion).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1;
}

public sealed record SyncCollabVersion(string Writer, string CgaVersion);

/// <summary>
/// A writer is a Studio SEAT, not a machine (P2P stage 1: «бичигч нь суудал, гарын үсэг нь
/// машин») - a person on a new laptop keeps the seat and its log. The id names the writer's
/// folder in the log, which the server sees, so it is the server's own opaque seat id - never
/// anything made from a name or an e-mail (a hash of an e-mail can be guessed back; CLAUDE.md §2).
/// No seat id from the server = no writer ("").
/// </summary>
public static class SyncSeats
{
    public static string Of(string seatId, string botId)
    {
        static string Clean(string value) => new(value.Trim().Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray());
        if (!string.IsNullOrWhiteSpace(botId))
            return "bot-" + Clean(botId);
        return string.IsNullOrWhiteSpace(seatId) ? "" : "seat-" + Clean(seatId);
    }

    /// <summary>
    /// The log's writer: the seat on one machine. One person signed in on two machines (the
    /// owner's desktop and laptop, К4) is one seat but two numbered logs - each machine numbers
    /// only its own, so no number is ever given twice, and each machine receives the other's
    /// work. Block ownership stays with the seat (<see cref="SeatOf"/>).
    /// </summary>
    public static string WriterOf(string seat, string deviceKeyId) =>
        seat.Length == 0 || deviceKeyId.Length < 8 ? "" : seat + "_d" + deviceKeyId[..8].ToLowerInvariant();

    /// <summary>The seat a writer belongs to: «seat-ab12_d0a55efd7» -> «seat-ab12».</summary>
    public static string SeatOf(string writer)
    {
        int at = writer.LastIndexOf("_d", StringComparison.Ordinal);
        return at > 0 && writer.Length - at == 10 ? writer[..at] : writer;
    }
}

/// <summary>
/// Who Studio is acting as on this machine (CLAUDE.md §2: the bot only when bot mode is chosen).
/// Studio writes it whenever its account changes; with Studio closed the last one stands. Local
/// only - it never travels.
/// </summary>
public sealed record SyncActiveIdentity
{
    public bool ActingAsBot { get; init; }

    public string BotId { get; init; } = "";

    public string AccountEmail { get; init; } = "";

    private static string PathOf(SyncHome home) => Path.Combine(home.Root, "active-identity.json");

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static SyncActiveIdentity? Read(SyncHome home)
    {
        try
        {
            string path = PathOf(home);
            return File.Exists(path) ? JsonSerializer.Deserialize<SyncActiveIdentity>(File.ReadAllText(path), Options) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Written only when it changed.</summary>
    public static void Write(SyncHome home, SyncActiveIdentity identity)
    {
        try
        {
            if (Read(home) == identity)
                return;
            Directory.CreateDirectory(home.Root);
            string path = PathOf(home);
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(identity, Options));
            File.Move(path + ".tmp", path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>
/// What seat a machine is acting as, told to its partners through the folder
/// (<c>.erks-collab\machines\d&lt;8 hex&gt;.json</c>): the seat id and role only, never a name or an
/// e-mail. The server lists the owner's desktop as its bot; while the owner works there as
/// themselves, the other machines' team shows the owner's own seat (К4, 2026-10-26).
/// </summary>
public sealed record SyncCollabMachine(string Seat, string Role)
{
    public static string PathOf(string folderRoot, string deviceKeyId) =>
        Path.Combine(folderRoot, SyncCollab.DirectoryName, "machines", "d" + deviceKeyId[..Math.Min(8, deviceKeyId.Length)].ToLowerInvariant() + ".json");

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static SyncCollabMachine? Read(string folderRoot, string deviceKeyId)
    {
        try
        {
            string path = PathOf(folderRoot, deviceKeyId);
            return File.Exists(path) ? JsonSerializer.Deserialize<SyncCollabMachine>(File.ReadAllText(path), Options) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Written only when it changed - it travels to every partner.</summary>
    public static void Write(string folderRoot, string deviceKeyId, SyncCollabMachine machine)
    {
        if (Read(folderRoot, deviceKeyId) == machine)
            return;
        string path = PathOf(folderRoot, deviceKeyId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(machine, Options));
        File.Move(path + ".tmp", path, overwrite: true);
    }
}

/// <summary>collab-whoami: this machine's seat and the folder's team - local to the pipe only.</summary>
public sealed record SyncCollabMember(string Seat, string DisplayName, string Role)
{
    /// <summary>This seat's writers - one per machine it is attached on.</summary>
    public IReadOnlyList<string> Writers { get; init; } = [];
}

public sealed record SyncCollabWhoAmI(string Seat, string DisplayName, string ProjectId, IReadOnlyList<SyncCollabMember> Team)
{
    /// <summary>This machine's writer for collab-publish / collab-pull (seat + machine).</summary>
    public string Writer { get; init; } = "";
}

/// <summary>This machine's notes on a shared file: its own writer, the block counts CGA gave, when it last applied.</summary>
public sealed record SyncCollabLocal
{
    public string Writer { get; init; } = "";

    public int Blocks { get; init; }

    public int MyBlocks { get; init; }

    public DateTimeOffset? LastAppliedAtUtc { get; init; }

    public string LastError { get; init; } = "";

    /// <summary>The checkpoint SYN last put in place of this machine's DWG, and that DWG's hash.</summary>
    public int PlacedCheckpoint { get; init; }

    public string PlacedSha256 { get; init; } = "";

    /// <summary>The newest checkpoint already weighed against a DWG edited here (not weighed again).</summary>
    public int EditedAtCheckpoint { get; init; }

    /// <summary>CGA's DWG is behind retired changesets: the next checkpoint goes in even over a copy edited here (kept aside).</summary>
    public bool NeedsBase { get; init; }

    /// <summary>The central was saved directly - kept, never written over, until collab-central-reset.</summary>
    public bool CentralEdited { get; init; }
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
    /// collab-register: the DWG at <paramref name="dwgFullPath"/> is shared from now on. One DWG is
    /// one shared file: a DWG already shared answers with ITS registration whatever id the caller
    /// brought - a member joining on another machine learns the file id here (the owner's laptop,
    /// К4 2026-10-26, offered fresh ids and was refused). A new share without an id gets one.
    /// </summary>
    public static SyncCollabFile Register(string folderRoot, string dwgFullPath, string fileId, string ownerSeat, string cgaVersion, DateTimeOffset now, string writer = "")
    {
        string relative = Relative(folderRoot, dwgFullPath);
        SyncCollabFile? shared = Files(folderRoot).FirstOrDefault(file => SyncPath.Comparer.Equals(file.RelativePath, relative));
        if (shared is not null)
            fileId = shared.FileId;
        else if (fileId.Length == 0)
            fileId = Guid.NewGuid().ToString("N");
        if (writer.Length > 0)
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
        if (shared is not null)
            return shared;
        var file = new SyncCollabFile { FileId = fileId, RelativePath = relative, CgaVersion = cgaVersion, RegisteredAtUtc = now };
        string root = FileRoot(folderRoot, fileId);
        Directory.CreateDirectory(root);
        WriteOnce(Path.Combine(root, "collab.json"), JsonSerializer.SerializeToUtf8Bytes(file, Json));
        return file;
    }

    /// <summary>
    /// collab-publish: the writer's next changeset, numbered here and written once. The manifest
    /// and the entities are packed as they are - SYN does not read them.
    ///
    /// 🔴 A NUMBER IS NEVER GIVEN TWICE (Erk-S Sync, 2026-10-26): the log alone forgets numbers an
    /// older SYN retired, and a reused number overwrote the laptop's 1 while its ack skipped it.
    /// The next number is one past the highest this writer has anywhere - log, high-water mark,
    /// partners' acks, this machine's applied cursor, what was unpacked here.
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
        long seq = HighestNumber(folderRoot, fileId, writer, home) + 1;
        string temporary = Path.Combine(log, $".{Guid.NewGuid():N}.tmp");
        Pack(temporary, manifestPath, entitiesPath);
        File.Move(temporary, Path.Combine(log, seq.ToString("D8") + Extension));
        WriteHigh(folderRoot, fileId, writer, seq);
        return seq;
    }

    /// <summary>The highest number this writer is known to have used, from every place a number leaves a trace.</summary>
    public static long HighestNumber(string folderRoot, string fileId, string writer, SyncHome? home = null)
    {
        string fileRoot = FileRoot(folderRoot, fileId);
        string name = Safe(writer);
        string log = Path.Combine(fileRoot, "log", name);
        long high = Directory.Exists(log) ? Numbers(log).DefaultIfEmpty(0).Max() : 0;
        high = Math.Max(high, ReadNumbers(Path.Combine(fileRoot, "high", name + ".json")).GetValueOrDefault("high"));
        string acks = Path.Combine(fileRoot, "acks");
        if (Directory.Exists(acks))
        {
            foreach (string path in Directory.EnumerateFiles(acks, "*.json"))
            {
                foreach ((string key, long seq) in ReadNumbers(path))
                {
                    if (Safe(key).Equals(name, StringComparison.OrdinalIgnoreCase))
                        high = Math.Max(high, seq);
                }
            }
        }
        if (home is not null)
        {
            high = Math.Max(high, Applied(home, fileId).GetValueOrDefault(name));
            string unpacked = Path.Combine(home.Root, "collab", Safe(fileId), "in", name);
            if (Directory.Exists(unpacked))
            {
                foreach (string directory in Directory.EnumerateDirectories(unpacked))
                {
                    if (long.TryParse(Path.GetFileName(directory), out long seq))
                        high = Math.Max(high, seq);
                }
            }
        }
        return high;
    }

    /// <summary>This machine publishes as <paramref name="writer"/> (what collab-publish records).</summary>
    public static void SaveWriter(SyncHome home, string fileId, string writer) =>
        SaveLocal(home, fileId, Local(home, fileId) with { Writer = writer });

    /// <summary>
    /// Whether a relative path is a changeset file - «.erks-collab\&lt;fileId&gt;\log\&lt;writer&gt;\&lt;n&gt;.cgacs».
    /// Such a file is written once by its writer: the transfer never replaces one with other bytes
    /// (unless its writer's repair names them) and always fetches one missing here.
    /// </summary>
    public static bool IsChangesetPath(string relativePath)
    {
        string[] parts = relativePath.Replace('/', '\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 5 &&
            parts[0].Equals(DirectoryName, StringComparison.OrdinalIgnoreCase) &&
            parts[2].Equals("log", StringComparison.OrdinalIgnoreCase) &&
            parts[4].EndsWith(Extension, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The contents a writer's machine put back at a reused number (restored\&lt;writer&gt;.json,
    /// written by that machine only): the transfer lets exactly these replace a changeset file.
    /// </summary>
    public static IReadOnlySet<string> Restored(string folderRoot)
    {
        var shas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string root = Path.Combine(folderRoot, DirectoryName);
        if (!Directory.Exists(root))
            return shas;
        foreach (string directory in Directory.EnumerateDirectories(root))
        {
            string restored = Path.Combine(directory, "restored");
            if (!Directory.Exists(restored))
                continue;
            foreach (string path in Directory.EnumerateFiles(restored, "*.json"))
            {
                try
                {
                    if (JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path), Json) is { } map)
                        shas.UnionWith(map.Values.Where(value => value.Length > 0));
                }
                catch (Exception exception) when (exception is IOException or JsonException)
                {
                }
            }
        }
        return shas;
    }

    /// <summary>
    /// On the writer's own machine: every changeset CGA took back here (in\&lt;writer&gt;\&lt;n&gt;) is
    /// what number n was. A log file at n with other content is a reused number - it moves to the
    /// next free number and n gets its own content back; a number retired from the log comes back.
    /// Each put-back is named in restored\&lt;writer&gt;.json so partners accept it.
    /// </summary>
    private static void RepairOwnLog(string folderRoot, SyncHome home, string fileId)
    {
        string own = Safe(Local(home, fileId).Writer);
        if (own.Length == 0)
            return;
        string unpacked = Path.Combine(home.Root, "collab", Safe(fileId), "in", own);
        if (!Directory.Exists(unpacked))
            return;
        string fileRoot = FileRoot(folderRoot, fileId);
        string log = Path.Combine(fileRoot, "log", own);
        string restoredPath = Path.Combine(fileRoot, "restored", own + ".json");
        Dictionary<string, string> restored;
        try
        {
            restored = File.Exists(restoredPath)
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(restoredPath), Json) ?? []
                : [];
        }
        catch (JsonException)
        {
            restored = [];
        }
        bool changed = false;
        foreach (string directory in Directory.EnumerateDirectories(unpacked).OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal))
        {
            if (!long.TryParse(Path.GetFileName(directory), out long seq) || seq <= 0)
                continue;
            string manifest = Path.Combine(directory, "manifest.json");
            string entities = Path.Combine(directory, "entities.dwg");
            if (!File.Exists(manifest) || !File.Exists(entities))
                continue;
            Directory.CreateDirectory(log);
            string path = Path.Combine(log, seq.ToString("D8") + Extension);
            if (File.Exists(path))
            {
                var info = new FileInfo(path);
                if (Verified.TryGetValue(path, out var seen) && seen == (info.Length, info.LastWriteTimeUtc))
                    continue;
                if (Holds(path, manifest, entities))
                {
                    Verified[path] = (info.Length, info.LastWriteTimeUtc);
                    continue;
                }
                long next = HighestNumber(folderRoot, fileId, own, home) + 1;
                File.Move(path, Path.Combine(log, next.ToString("D8") + Extension));
                WriteHigh(folderRoot, fileId, own, next);
            }
            string temporary = Path.Combine(log, $".{Guid.NewGuid():N}.tmp");
            Pack(temporary, manifest, entities);
            File.Move(temporary, path);
            restored[seq.ToString(System.Globalization.CultureInfo.InvariantCulture)] = Sha256Of(path);
            changed = true;
        }
        if (changed)
            WriteIfChanged(restoredPath, JsonSerializer.SerializeToUtf8Bytes(restored, Json));
    }

    /// <summary>
    /// A changeset that came twice under one name (a number reused before 0.001.103) is kept beside
    /// it as a conflict copy, never over it. A copy whose content (manifest and entities, not the zip
    /// bytes) is already in the writer's log under some number loses nothing and is dropped.
    /// </summary>
    private static int ConflictCopies(string fileRoot)
    {
        string logs = Path.Combine(fileRoot, "log");
        if (!Directory.Exists(logs))
            return 0;
        int left = 0;
        foreach (string log in Directory.EnumerateDirectories(logs))
        {
            string[] copies = Directory.EnumerateFiles(log, "*" + Extension)
                .Where(path => !long.TryParse(Path.GetFileNameWithoutExtension(path), out _))
                .ToArray();
            if (copies.Length == 0)
                continue;
            HashSet<string> logged = Directory.EnumerateFiles(log, "*" + Extension)
                .Where(path => long.TryParse(Path.GetFileNameWithoutExtension(path), out _))
                .Select(ContentKey)
                .Where(key => key.Length > 0)
                .ToHashSet(StringComparer.Ordinal);
            foreach (string copy in copies)
            {
                try
                {
                    string key = ContentKey(copy);
                    if (key.Length > 0 && logged.Contains(key))
                    {
                        File.Delete(copy);
                        continue;
                    }
                }
                catch (IOException)
                {
                }
                left++;
            }
        }
        return left;
    }

    /// <summary>What a changeset carries - its manifest and entities - whatever zip wrapped them; empty when unreadable.</summary>
    private static string ContentKey(string changeset)
    {
        try
        {
            using ZipArchive zip = ZipFile.OpenRead(changeset);
            using var sha = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
            foreach (string name in new[] { "manifest.json", "entities.dwg" })
            {
                if (zip.GetEntry(name) is not { } entry)
                    return "";
                using Stream stream = entry.Open();
                using var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                sha.AppendData(BitConverter.GetBytes(buffer.Length));
                sha.AppendData(buffer.ToArray());
            }
            return Convert.ToHexStringLower(sha.GetHashAndReset());
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return "";
        }
    }

    // A changeset found to hold what CGA took back is not unpacked again on every pass.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (long Length, DateTime Written)> Verified = new(StringComparer.OrdinalIgnoreCase);

    private static void Pack(string path, string manifestPath, string entitiesPath)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        zip.CreateEntryFromFile(manifestPath, "manifest.json", CompressionLevel.Optimal);
        zip.CreateEntryFromFile(entitiesPath, "entities.dwg", CompressionLevel.Optimal);
    }

    private static bool Holds(string changeset, string manifestPath, string entitiesPath)
    {
        try
        {
            using ZipArchive zip = ZipFile.OpenRead(changeset);
            return SameEntry(zip, "manifest.json", manifestPath) && SameEntry(zip, "entities.dwg", entitiesPath);
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private static bool SameEntry(ZipArchive zip, string name, string path)
    {
        if (zip.GetEntry(name) is not { } entry)
            return false;
        using Stream stream = entry.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray().AsSpan().SequenceEqual(File.ReadAllBytes(path));
    }

    private static void WriteHigh(string folderRoot, string fileId, string writer, long seq)
    {
        string path = Path.Combine(FileRoot(folderRoot, fileId), "high", Safe(writer) + ".json");
        if (ReadNumbers(path).GetValueOrDefault("high") >= seq)
            return;
        WriteIfChanged(path, JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, long> { ["high"] = seq }, Json));
    }

    private static Dictionary<string, long> ReadNumbers(string path)
    {
        try
        {
            return File.Exists(path)
                ? new Dictionary<string, long>(JsonSerializer.Deserialize<Dictionary<string, long>>(File.ReadAllText(path), Json) ?? [], StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            return new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// collab-pull: every writer's changesets this machine has not applied, in each writer's order,
    /// never past a missing number; unpacked under the Sync home for CGA to read. This machine's own
    /// writer is not given back. <paramref name="ack"/> first records what CGA applied.
    /// </summary>
    public static IReadOnlyList<SyncCollabChangeset> Pull(string folderRoot, SyncHome home, string fileId, string ownWriter, IReadOnlyDictionary<string, long>? ack = null)
    {
        Dictionary<string, long> applied = Applied(home, fileId);
        // The DWG is the truth (К5, 2026-10-26): the ack is what the DWG holds now, so it SETS the
        // cursor - a DWG closed without saving comes back lower, and what it lost is given again.
        bool ownAcked = false;
        if (ack is not null)
        {
            foreach ((string writer, long seq) in ack)
            {
                applied[Safe(writer)] = Math.Max(0, seq);
                ownAcked |= ownWriter.Length > 0 && Safe(writer).Equals(Safe(ownWriter), StringComparison.OrdinalIgnoreCase);
            }
            SaveApplied(home, fileId, applied);
            SaveLocal(home, fileId, Local(home, fileId) with { LastAppliedAtUtc = DateTimeOffset.UtcNow, LastError = "" });
            // What this machine has applied travels too (one writer each), so every machine can tell
            // when a changeset is in every partner's DWG and may be retired (К5).
            if (ownWriter.Length > 0)
                WriteIfChanged(Path.Combine(FileRoot(folderRoot, fileId), "acks", Safe(ownWriter) + ".json"), JsonSerializer.SerializeToUtf8Bytes(applied, Json));
        }
        var pending = new List<SyncCollabChangeset>();
        string logs = Path.Combine(FileRoot(folderRoot, fileId), "log");
        if (!Directory.Exists(logs))
            return pending;
        foreach (string log in Directory.EnumerateDirectories(logs).Order(StringComparer.Ordinal))
        {
            string writer = Path.GetFileName(log);
            // This machine's own changesets come back only when the DWG says how far it holds them.
            if (writer.Equals(Safe(ownWriter), StringComparison.OrdinalIgnoreCase) && !ownAcked)
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

    /// <summary>
    /// The DWG needs the newest checkpoint to start from (Revit Central model, 2026-10-26): a new
    /// local whose DWG holds no applied record (no ack), or - only for logs retired before 0.001.97 -
    /// a changeset it needs that is gone. SYN never writes over the DWG: CGA makes the new local
    /// from <see cref="LatestCheckpointFile"/> («Create New Local»).
    /// </summary>
    public static bool NeedsCheckpoint(string folderRoot, SyncHome home, string fileId, IReadOnlyDictionary<string, long>? ack = null)
    {
        var latest = LatestCheckpoint(folderRoot, fileId);
        if (latest is not { } checkpoint)
            return false;
        if (ack is null || ack.Count == 0)
            return true;
        Dictionary<string, long> applied = Applied(home, fileId);
        foreach ((string writer, long seq) in ack)
            applied[Safe(writer)] = seq; // what the DWG says it holds
        string logs = Path.Combine(FileRoot(folderRoot, fileId), "log");
        foreach ((string writer, long through) in checkpoint.Through)
        {
            string safe = Safe(writer);
            long next = applied.GetValueOrDefault(safe) + 1;
            if (next > through)
                continue;
            string log = Path.Combine(logs, safe);
            if (!File.Exists(Path.Combine(log, next.ToString("D8") + Extension)))
                return true;
        }
        return false;
    }

    /// <summary>The newest complete checkpoint - its DWG path, number and what it holds - for CGA's «Create New Local».</summary>
    public static (int Number, string DwgPath, IReadOnlyDictionary<string, long> Through)? LatestCheckpointFile(string folderRoot, string fileId) =>
        LatestCheckpoint(folderRoot, fileId) is { } latest ? (latest.Number, latest.DwgPath, latest.Through) : null;

    /// <summary>collab-status: per writer - the last number in the log here, what was applied, what waits, and whether a number is missing.</summary>
    /// <param name="ownWriter">This machine's writer (whoami), used until it has published: the
    /// writer it published as comes first. Its own changesets are neither waiting nor applied here.</param>
    public static SyncCollabStatus Status(string folderRoot, SyncHome home, string fileId, string ownWriter = "")
    {
        Dictionary<string, long> applied = Applied(home, fileId);
        SyncCollabLocal local = Local(home, fileId);
        string own = Safe(local.Writer.Length > 0 ? local.Writer : ownWriter);
        var writers = new List<SyncCollabWriter>();
        string logs = Path.Combine(FileRoot(folderRoot, fileId), "log");
        if (Directory.Exists(logs))
        {
            foreach (string log in Directory.EnumerateDirectories(logs).Order(StringComparer.Ordinal))
            {
                string writer = Path.GetFileName(log);
                HashSet<long> present = Numbers(log).ToHashSet();
                long last = present.DefaultIfEmpty(0).Max();
                if (own.Length > 0 && writer.Equals(own, StringComparison.OrdinalIgnoreCase))
                {
                    writers.Add(new SyncCollabWriter(writer, last, last, 0, false));
                    continue;
                }
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
        // Not yet in every partner's DWG: this machine's last changeset less the lowest ack of it
        // among the other machines registered on the file (acks/<writer>.json travel). К4 2026-10-26.
        long ownLast = writers.FirstOrDefault(writer => writer.Writer.Equals(own, StringComparison.OrdinalIgnoreCase))?.LastInLog ?? 0;
        long acked = long.MaxValue;
        foreach (SyncCollabVersion machine in versions)
        {
            string name = Safe(machine.Writer);
            if (name.Equals(own, StringComparison.OrdinalIgnoreCase))
                continue;
            long seen = 0;
            string ackPath = Path.Combine(FileRoot(folderRoot, fileId), "acks", name + ".json");
            try
            {
                if (File.Exists(ackPath) && JsonSerializer.Deserialize<Dictionary<string, long>>(File.ReadAllText(ackPath), Json) is { } ack)
                    seen = ack.FirstOrDefault(pair => Safe(pair.Key).Equals(own, StringComparison.OrdinalIgnoreCase)).Value;
            }
            catch (Exception exception) when (exception is IOException or JsonException)
            {
            }
            acked = Math.Min(acked, seen);
        }
        int pendingOut = own.Length == 0 || acked == long.MaxValue ? 0 : (int)Math.Max(0, ownLast - acked);
        return new SyncCollabStatus(fileId, checkpoint, writers)
        {
            Versions = versions,
            PendingOut = pendingOut,
            OwnWriter = own,
            Blocks = local.Blocks,
            MyBlocks = local.MyBlocks,
            LastAppliedAtUtc = local.LastAppliedAtUtc,
            LastError = ConflictCopies(FileRoot(folderRoot, fileId)) is > 0 and int copies
                ? $"Багцын дугаар давхардсан ({copies}) — дарж бичээгүй, хоёуланг нь хадгалсан."
                : local.LastError,
            CentralEdited = local.CentralEdited,
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
    /// <summary>
    /// The only local copy a checkpoint may be taken from (requirement 7, 2026-10-26): beside the
    /// central, named «&lt;central&gt;_d&lt;8 hex&gt;_LOCAL.dwg». Anything else is refused.
    /// </summary>
    public static bool IsLocalOf(string centralFullPath, string sourceFullPath)
    {
        string central = Path.GetFullPath(centralFullPath), source = Path.GetFullPath(sourceFullPath);
        if (!string.Equals(Path.GetDirectoryName(central), Path.GetDirectoryName(source), StringComparison.OrdinalIgnoreCase))
            return false;
        string pattern = "^" + System.Text.RegularExpressions.Regex.Escape(Path.GetFileNameWithoutExtension(central)) + @"_d[0-9a-f]{8}_LOCAL\.dwg$";
        return System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(source), pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// collab-checkpoint from a local copy: the local is read share-read and taken only if it did
    /// not change while it was copied (AutoCAD still writing it is refused, to be tried again).
    /// The central DWG in the folder then follows from the checkpoint on SYN's next pass.
    /// </summary>
    public static int CheckpointFromLocal(string folderRoot, string fileId, string centralFullPath, string sourceFullPath, IReadOnlyDictionary<string, long> through)
    {
        if (!IsLocalOf(centralFullPath, sourceFullPath))
            throw new InvalidOperationException("source нь энэ central-ын хажуудах «<central>_d<8hex>_LOCAL.dwg» биш байна.");
        var before = new FileInfo(sourceFullPath);
        (long length, DateTime written) = (before.Length, before.LastWriteTimeUtc);
        string staging = Path.Combine(Path.GetTempPath(), $"erks-collab-{Guid.NewGuid():N}.dwg");
        try
        {
            using (var input = new FileStream(sourceFullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var output = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                input.CopyTo(output);
            var after = new FileInfo(sourceFullPath);
            if (after.Length != length || after.LastWriteTimeUtc != written || new FileInfo(staging).Length != length)
                throw new IOException("Local файл хадгалагдаж байна — дахин оролдоно уу.");
            return Checkpoint(folderRoot, fileId, staging, through);
        }
        finally
        {
            File.Delete(staging);
        }
    }

    public static int Checkpoint(string folderRoot, string fileId, string dwgFullPath, IReadOnlyDictionary<string, long> through, SyncHome? home = null)
    {
        // Anyone may checkpoint, but never backwards: a DWG that holds less of some writer than the
        // newest checkpoint does is not a newer base - that checkpoint stands.
        if (LatestCheckpoint(folderRoot, fileId) is { } newest &&
            newest.Through.Any(pair => pair.Value > through.FirstOrDefault(given => Safe(given.Key).Equals(Safe(pair.Key), StringComparison.OrdinalIgnoreCase)).Value))
            return newest.Number;
        int made = MakeCheckpoint(folderRoot, fileId, dwgFullPath, through);
        // The machine that made it already holds it - it is never fast-forwarded onto itself.
        if (home is not null)
            SaveLocal(home, fileId, Local(home, fileId) with { PlacedCheckpoint = made, PlacedSha256 = Sha256Of(dwgFullPath) });
        return made;
    }

    private static int MakeCheckpoint(string folderRoot, string fileId, string dwgFullPath, IReadOnlyDictionary<string, long> through)
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

    /// <summary>How many checkpoints each shared file keeps; older ones are retired.</summary>
    public const int CheckpointsKept = 3;

    private sealed record CheckpointNote(int Checkpoint, Dictionary<string, long> Through);

    /// <summary>The newest checkpoint whose DWG and note have both arrived, or null.</summary>
    private static (int Number, string DwgPath, Dictionary<string, long> Through)? LatestCheckpoint(string folderRoot, string fileId)
    {
        string checkpoints = Path.Combine(FileRoot(folderRoot, fileId), "checkpoints");
        if (!Directory.Exists(checkpoints))
            return null;
        foreach (int n in Directory.EnumerateFiles(checkpoints, "*.json")
                     .Select(path => int.TryParse(Path.GetFileNameWithoutExtension(path), out int number) ? number : 0)
                     .Where(number => number > 0)
                     .OrderDescending())
        {
            string dwg = Path.Combine(checkpoints, n.ToString("D6") + ".dwg");
            if (!File.Exists(dwg))
                continue;
            try
            {
                CheckpointNote? note = JsonSerializer.Deserialize<CheckpointNote>(File.ReadAllText(Path.Combine(checkpoints, n.ToString("D6") + ".json")), Json);
                var through = new Dictionary<string, long>(note?.Through ?? [], StringComparer.OrdinalIgnoreCase);
                return (n, dwg, through);
            }
            catch (Exception exception) when (exception is IOException or JsonException)
            {
            }
        }
        return null;
    }

    /// <summary>
    /// К5, once a pass for every shared file in the folder:
    /// <list type="number">
    /// <item>A machine without the DWG gets the newest checkpoint as its base, placed atomically;
    /// a DWG already there is NEVER written over - each machine keeps its own local, as in a Revit
    /// Central model. The base holds the changesets through the checkpoint: applied here.</item>
    /// <item>Checkpoints older than the newest <see cref="CheckpointsKept"/> are retired.</item>
    /// <item>Changesets are never retired (owner, 2026-10-26: a saved local of any age catches up
    /// with its next Generate); they are small.</item>
    /// </list>
    /// Returns the retired paths (relative to the folder) - held, so they are neither fetched back
    /// from a partner that has not retired them yet nor sent out again.
    /// </summary>
    public static IReadOnlySet<string> Maintain(string folderRoot, SyncHome home) => Maintain(folderRoot, home, _ => null, "");

    /// <param name="known">This machine's last index entry for a relative path (its version chain).</param>
    /// <param name="deviceKeyId">This machine - a DWG version that first appeared here was edited here.</param>
    public static IReadOnlySet<string> Maintain(string folderRoot, SyncHome home, Func<string, SyncIndexEntry?> known, string deviceKeyId, Func<string, IReadOnlySet<string>>? partnerHashes = null)
    {
        partnerHashes ??= _ => new HashSet<string>();
        var retired = new HashSet<string>(SyncPath.Comparer);
        foreach (SyncCollabFile file in Files(folderRoot))
        {
            try
            {
                MaintainOne(folderRoot, home, file, retired, known, deviceKeyId, partnerHashes);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                SaveLocal(home, file.FileId, Local(home, file.FileId) with { LastError = exception.Message });
            }
        }
        return retired;
    }

    private static void MaintainOne(string folderRoot, SyncHome home, SyncCollabFile file, HashSet<string> retired, Func<string, SyncIndexEntry?> known, string deviceKeyId, Func<string, IReadOnlySet<string>> partnerHashes)
    {
        string fileRoot = FileRoot(folderRoot, file.FileId);
        string relativeRoot = Path.Combine(DirectoryName, Safe(file.FileId));
        RepairOwnLog(folderRoot, home, file.FileId);
        var latest = LatestCheckpoint(folderRoot, file.FileId);
        if (latest is { } checkpoint)
        {
            string dwg = Path.Combine(folderRoot, file.RelativePath);
            SyncCollabLocal local = Local(home, file.FileId);
            if (!File.Exists(dwg))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dwg)!);
                string temporary = Path.Combine(Path.GetDirectoryName(dwg)!, $".{Guid.NewGuid():N}.tmp");
                File.Copy(checkpoint.DwgPath, temporary);
                try
                {
                    File.Move(temporary, dwg, overwrite: false);
                    Placed(home, file.FileId, checkpoint.Number, checkpoint.Through, Sha256Of(dwg));
                }
                catch (IOException) when (File.Exists(dwg))
                {
                    // Someone put the DWG there in the meantime - theirs stands.
                    File.Delete(temporary);
                }
            }
            else if (checkpoint.Number > Math.Max(local.PlacedCheckpoint, local.EditedAtCheckpoint))
            {
                // Requirement 7: nobody edits the central - people work in their locals, and the
                // central follows the newest checkpoint. A central saved directly is never written
                // over: it is kept aside and said (status, Twin).
                string sha = Sha256Of(dwg);
                string checkpoints = Path.Combine(fileRoot, "checkpoints");
                bool fromCheckpoint = sha.Equals(local.PlacedSha256, StringComparison.OrdinalIgnoreCase) ||
                                      Directory.EnumerateFiles(checkpoints, "*.dwg").Any(path => Sha256Of(path).Equals(sha, StringComparison.OrdinalIgnoreCase)) ||
                                      partnerHashes(file.RelativePath).Contains(sha) ||
                                      (known(file.RelativePath) is { Version: { } version } entry && entry.Sha256.Equals(sha, StringComparison.OrdinalIgnoreCase) &&
                                       version.Writer.Length > 0 && !version.Writer.Equals(deviceKeyId, StringComparison.OrdinalIgnoreCase));
                string keptAside = Path.Combine(home.Root, "collab", Safe(file.FileId), "replaced", $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.dwg");
                if (sha.Equals(Sha256Of(checkpoint.DwgPath), StringComparison.OrdinalIgnoreCase))
                {
                    Placed(home, file.FileId, checkpoint.Number, checkpoint.Through, sha);
                }
                else if (!fromCheckpoint)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(keptAside)!);
                    File.Copy(dwg, keptAside);
                    SaveLocal(home, file.FileId, local with
                    {
                        EditedAtCheckpoint = checkpoint.Number,
                        CentralEdited = true,
                        LastError = $"Төв файл шууд засагдсан — дарж бичихгүй, нөөц: {keptAside}",
                    });
                }
                else if (OpenElsewhere(dwg))
                {
                    SaveLocal(home, file.FileId, local with { LastError = $"Төв файл {Path.GetFileName(dwg)} нээлттэй — хаагдмагц шинэ checkpoint-оор шинэчлэгдэнэ." });
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(keptAside)!);
                    File.Copy(dwg, keptAside);
                    string temporary = Path.Combine(Path.GetDirectoryName(dwg)!, $".{Guid.NewGuid():N}.tmp");
                    File.Copy(checkpoint.DwgPath, temporary);
                    File.Move(temporary, dwg, overwrite: true);
                    Placed(home, file.FileId, checkpoint.Number, checkpoint.Through, Sha256Of(dwg));
                }
            }
            for (int old = 1; old <= checkpoint.Number - CheckpointsKept; old++)
            {
                foreach (string extension in new[] { ".dwg", ".json" })
                {
                    string relative = Path.Combine(relativeRoot, "checkpoints", old.ToString("D6") + extension);
                    retired.Add(relative);
                    string full = Path.Combine(folderRoot, relative);
                    if (File.Exists(full))
                        File.Delete(full);
                }
            }
        }
    }

    /// <summary>
    /// collab-central-reset: the central (saved directly) is kept aside and replaced by the newest
    /// checkpoint. CGA calls it only after it made a local from the edited central and published
    /// what that local held, so nothing is lost. Refused while AutoCAD holds the central open.
    /// </summary>
    public static int ResetCentral(string folderRoot, SyncHome home, string fileId)
    {
        SyncCollabFile file = Files(folderRoot).FirstOrDefault(item => item.FileId.Equals(fileId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Хамтын файл олдсонгүй: " + fileId);
        var latest = LatestCheckpoint(folderRoot, fileId) ?? throw new InvalidOperationException("Checkpoint алга — төв файлыг сэргээх суурь байхгүй.");
        string central = Path.Combine(folderRoot, file.RelativePath);
        if (File.Exists(central))
        {
            if (OpenElsewhere(central))
                throw new IOException("Төв файл нээлттэй байна — хаагаад дахин оролдоно уу.");
            string kept = Path.Combine(home.Root, "collab", Safe(fileId), "replaced", $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.dwg");
            Directory.CreateDirectory(Path.GetDirectoryName(kept)!);
            File.Copy(central, kept);
        }
        string temporary = Path.Combine(Path.GetDirectoryName(central)!, $".{Guid.NewGuid():N}.tmp");
        File.Copy(latest.DwgPath, temporary);
        File.Move(temporary, central, overwrite: true);
        Placed(home, fileId, latest.Number, latest.Through, Sha256Of(central));
        return latest.Number;
    }

    /// <summary>The base is in place: what it holds counts as applied, and SYN remembers what it placed.</summary>
    private static void Placed(SyncHome home, string fileId, int number, IReadOnlyDictionary<string, long> through, string sha)
    {
        Dictionary<string, long> applied = Applied(home, fileId);
        foreach ((string writer, long seq) in through)
            applied[Safe(writer)] = Math.Max(applied.GetValueOrDefault(Safe(writer)), seq);
        SaveApplied(home, fileId, applied);
        SaveLocal(home, fileId, Local(home, fileId) with { PlacedCheckpoint = number, PlacedSha256 = sha, NeedsBase = false, CentralEdited = false, LastError = "" });
    }

    private static string Sha256Of(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(stream));
    }

    /// <summary>AutoCAD holds an open DWG; replacing it then would pull it from under the person.</summary>
    private static bool OpenElsewhere(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static void WriteIfChanged(string path, byte[] content)
    {
        if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(content))
            return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".tmp";
        File.WriteAllBytes(temporary, content);
        File.Move(temporary, path, overwrite: true);
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
