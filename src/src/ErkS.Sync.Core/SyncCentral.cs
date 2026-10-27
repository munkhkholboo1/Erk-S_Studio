using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ErkS.Sync.Core;

/// <summary>
/// Erk-S Central (_shared/erks-central-design-2026-10-24.md). Owner, 2026-10-24: «гол тэр бүх
/// урсгалыг Erk-S sync маань ойлгож яг тэрэнд зориулагдаж ажиллах ёстой».
///
/// 🔴 THERE IS ONE CENTRAL. Revit writes its worksharing state (borrowing, editing requests,
/// worksets, synchronize) into the central and its _backup the moment it happens, so a copy per
/// machine diverges - the Resilio failure. A member's Revit therefore works on the host's real
/// files: every file operation it makes is carried to the host and run there on real handles,
/// with the share modes and byte-range locks NTFS gives - Revit's own rules hold unchanged.
/// </summary>
public static class SyncCentralOp
{
    public const string Stat = "stat";
    public const string List = "list";
    public const string Open = "open";
    public const string Read = "read";
    public const string Write = "write";
    public const string SetLength = "set-length";
    public const string Flush = "flush";
    public const string Close = "close";
    public const string Delete = "delete";
    public const string Move = "move";
    public const string CreateDirectory = "mkdir";
    public const string DeleteDirectory = "rmdir";
    public const string Lock = "lock";
    public const string Unlock = "unlock";
    public const string SetInfo = "set-info";

    /// <summary>PFR, before Revit's synchronize: hold the commit until <see cref="TxEnd"/>.</summary>
    public const string TxBegin = "tx-begin";

    /// <summary>PFR, after a completed synchronize: commit once the writes are closed.</summary>
    public const string TxEnd = "tx-end";
}

/// <summary>One file operation, as a member's file system asks it. Paths are relative to the central's folder.</summary>
public sealed record SyncCentralRequest
{
    public string Op { get; init; } = "";

    /// <summary>
    /// The member's call number. A call resent after a broken road (same number) is answered
    /// from the host's memory, never run twice.
    /// </summary>
    public long Seq { get; init; }

    public string Path { get; init; } = "";

    public string NewPath { get; init; } = "";

    public long Handle { get; init; }

    public FileMode Mode { get; init; } = FileMode.Open;

    public FileAccess Access { get; init; } = FileAccess.Read;

    public FileShare Share { get; init; } = FileShare.Read;

    public bool DeleteOnClose { get; init; }

    public bool Overwrite { get; init; }

    public long Offset { get; init; }

    public long Length { get; init; }

    public FileAttributes? Attributes { get; init; }

    public DateTime? CreationUtc { get; init; }

    public DateTime? LastWriteUtc { get; init; }

    public DateTime? LastAccessUtc { get; init; }
}

/// <summary>An entry of the central's folder.</summary>
public sealed record SyncCentralEntry(string Name, bool IsDirectory, long Size, FileAttributes Attributes, DateTime CreationUtc, DateTime LastWriteUtc, DateTime LastAccessUtc);

/// <summary>
/// The answer. <see cref="Error"/> names what NTFS refused, so the member's file system can
/// return the same status Revit would get from a LAN file server.
/// </summary>
public sealed record SyncCentralResponse
{
    /// <summary>The call this answers (relay).</summary>
    public long Seq { get; init; }

    public string Error { get; init; } = "";

    public int HResult { get; init; }

    public long Handle { get; init; }

    public long Count { get; init; }

    public SyncCentralEntry? Entry { get; init; }

    public IReadOnlyList<SyncCentralEntry>? Entries { get; init; }

    public bool Ok => Error.Length == 0;
}

public static class SyncCentralErrors
{
    public const string NotFound = "not_found";
    public const string PathNotFound = "path_not_found";
    public const string SharingViolation = "sharing_violation";
    public const string LockViolation = "lock_violation";
    public const string AccessDenied = "access_denied";
    public const string Exists = "exists";
    public const string DirectoryNotEmpty = "directory_not_empty";
    public const string NotCentral = "not_central";
    public const string BadHandle = "bad_handle";
    public const string BadRequest = "bad_request";
    public const string IoError = "io_error";

    /// <summary>No road to the host, or no answer in time - the mount answers «network path not found».</summary>
    public const string HostUnreachable = "host_unreachable";
}

/// <summary>Which names in a folder belong to a central: the model, its _backup and what Revit keeps beside it.</summary>
public static class SyncCentralPaths
{
    /// <summary>
    /// True when <paramref name="relativePath"/> belongs to the central <paramref name="centralFileName"/>:
    /// the model itself, anything under «stem_backup», or a file named after the stem (Revit's
    /// own temporary and lock files). Everything else in the folder is not the central's and is refused.
    /// </summary>
    public static bool Belongs(string centralFileName, string relativePath)
    {
        string stem = System.IO.Path.GetFileNameWithoutExtension(centralFileName);
        string path = (relativePath ?? "").Replace('/', '\\').TrimStart('\\');
        if (path.Length == 0 || path.Split('\\').Any(part => part is "" or "." or ".."))
            return false;
        string first = path.Split('\\')[0];
        return first.Equals(centralFileName, StringComparison.OrdinalIgnoreCase) ||
               first.Equals(stem + "_backup", StringComparison.OrdinalIgnoreCase) ||
               (!path.Contains('\\') && Regex.IsMatch(first, "^" + Regex.Escape(stem) + @"(\.|~)", RegexOptions.IgnoreCase));
    }
}

/// <summary>
/// The host half: runs a member's operations on the real central, one session per member,
/// with real handles - so a share mode or a lock one member holds refuses another exactly as
/// NTFS does. Closing a session closes every handle it still holds (a member gone away
/// releases what it held, as a dropped SMB session does).
/// </summary>
public sealed class SyncCentralHost : IDisposable
{
    private readonly string _folder;
    private readonly string _centralFileName;
    private readonly ConcurrentDictionary<long, Session> _sessions = new();

    private readonly string? _snapshots;
    private readonly string? _undo;
    private readonly int _keepSnapshots;
    private readonly object _snapshotGate = new();

    /// <param name="snapshots">
    /// Where the central and its _backup are copied before a member starts writing (Master's
    /// safeguard 2); null = none. The last <paramref name="keepSnapshots"/> are kept.
    /// </param>
    public SyncCentralHost(string folder, string centralFileName, string? snapshots = null, int keepSnapshots = 10)
    {
        _folder = System.IO.Path.GetFullPath(folder);
        _centralFileName = centralFileName;
        _snapshots = snapshots;
        _undo = snapshots is null ? null : System.IO.Path.Combine(System.IO.Path.GetDirectoryName(snapshots.TrimEnd('\\', '/')) ?? snapshots, "undo");
        _keepSnapshots = Math.Max(1, keepSnapshots);
    }

    /// <summary>Members connected now, handles they hold - counts for Twin, no names.</summary>
    public (int Sessions, int Handles, DateTimeOffset? LastSnapshotUtc) Counts =>
        (_sessions.Count, _sessions.Values.Sum(session => session.Count), LastSnapshotUtc);

    public DateTimeOffset? LastSnapshotUtc { get; private set; }

    /// <summary>
    /// Ends every session not heard from for <paramref name="lease"/> - a member whose road broke
    /// mid-synchronize releases its handles and locks instead of holding the central forever.
    /// </summary>
    public int Sweep(DateTimeOffset now, TimeSpan lease, Func<long, bool>? connected = null)
    {
        int ended = 0;
        foreach ((long id, Session session) in _sessions.ToArray())
        {
            if (now - session.LastSeenUtc > lease && connected?.Invoke(id) != true)
            {
                EndSession(id);
                ended++;
            }
        }
        return ended;
    }

    public SyncCentralResponse Handle(long session, SyncCentralRequest request, ReadOnlyMemory<byte> data, out byte[]? reply) =>
        Handle(session, request, data, DateTimeOffset.UtcNow, out reply);

    public SyncCentralResponse Handle(long session, SyncCentralRequest request, ReadOnlyMemory<byte> data, DateTimeOffset now, out byte[]? reply)
    {
        bool first = false;
        Session state = _sessions.GetOrAdd(session, _ =>
        {
            first = true;
            return new Session();
        });
        state.LastSeenUtc = now;
        // Before a member is served anything, the central as it is now is kept (owner, 2026-10-27:
        // «эхний холболтын өмнө нөөц авна»); its first write takes one again if the copy is old.
        if (first)
            Snapshot(now);
        lock (state)
        {
            if (request.Seq > 0 && state.Answered(request.Seq) is { } done)
            {
                reply = done.Reply;
                return done.Response;
            }
            SyncCentralResponse response = Run(state, request, data, now, out reply);
            if (request.Seq > 0)
                state.Remember(request.Seq, response, reply);
            return response;
        }
    }

    private SyncCentralResponse Run(Session state, SyncCentralRequest request, ReadOnlyMemory<byte> data, DateTimeOffset now, out byte[]? reply)
    {
        reply = null;
        if (Mutates(request))
        {
            _writtenSinceSnapshot = true;
            if (!state.Writing)
            {
                state.Writing = true;
                Snapshot(now);
            }
            state.Undo ??= new UndoLog(_undo ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "erks-central-undo"));
        }
        try
        {
            switch (request.Op)
            {
                case SyncCentralOp.TxBegin:
                    state.Explicit = true;
                    return new SyncCentralResponse();
                case SyncCentralOp.TxEnd:
                    state.Explicit = false;
                    CommitIfDone(state);
                    return new SyncCentralResponse();
            }

            switch (request.Op)
            {
                case SyncCentralOp.Read:
                {
                    FileStream stream = state.Stream(request.Handle);
                    byte[] buffer = new byte[(int)Math.Clamp(request.Length, 0, SyncIndex.ChunkSize)];
                    stream.Position = request.Offset;
                    int read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
                    reply = read == buffer.Length ? buffer : buffer[..read];
                    return new SyncCentralResponse { Count = read };
                }
                case SyncCentralOp.Write:
                {
                    FileStream stream = state.Stream(request.Handle);
                    stream.Position = request.Offset;
                    stream.Write(data.Span);
                    return new SyncCentralResponse { Count = data.Length };
                }
                case SyncCentralOp.SetLength:
                    state.Stream(request.Handle).SetLength(request.Length);
                    return new SyncCentralResponse();
                case SyncCentralOp.Flush:
                    state.Stream(request.Handle).Flush(flushToDisk: true);
                    return new SyncCentralResponse();
                case SyncCentralOp.Lock:
                    state.Stream(request.Handle).Lock(request.Offset, request.Length);
                    return new SyncCentralResponse();
                case SyncCentralOp.Unlock:
                    state.Stream(request.Handle).Unlock(request.Offset, request.Length);
                    return new SyncCentralResponse();
                case SyncCentralOp.Close:
                    if (!state.Close(request.Handle))
                        return Refuse(SyncCentralErrors.BadHandle);
                    // The burst is whole once its last write handle closes - committed before the answer.
                    CommitIfDone(state);
                    return new SyncCentralResponse();
            }

            string path = Resolve(request.Path);
            switch (request.Op)
            {
                case SyncCentralOp.Stat:
                    return File.Exists(path) || Directory.Exists(path)
                        ? new SyncCentralResponse { Entry = EntryOf(new FileInfo(path).Exists ? new FileInfo(path) : new DirectoryInfo(path)) }
                        : Refuse(SyncCentralErrors.NotFound);
                case SyncCentralOp.List:
                {
                    if (!Directory.Exists(path))
                        return Refuse(SyncCentralErrors.PathNotFound);
                    bool root = request.Path.Trim('\\', '/').Length == 0;
                    var entries = new DirectoryInfo(path).EnumerateFileSystemInfos()
                        .Where(info => !root || SyncCentralPaths.Belongs(_centralFileName, info.Name))
                        .Select(EntryOf)
                        .ToList();
                    return new SyncCentralResponse { Entries = entries };
                }
                case SyncCentralOp.Open:
                {
                    bool writes = (request.Access & FileAccess.Write) != 0;
                    bool existed = File.Exists(path);
                    // Kept before the open can change it; a file someone else holds exclusively
                    // cannot be copied - and cannot be opened either, so NTFS answers below.
                    if (writes && existed && request.Mode != FileMode.CreateNew)
                        state.Undo!.TryKeep(path);
                    var options = request.DeleteOnClose ? FileOptions.DeleteOnClose : FileOptions.None;
                    var stream = new FileStream(path, request.Mode, request.Access, request.Share | FileShare.Delete, 4096, options);
                    if (!existed && state.Undo is { } undo)
                        undo.Created(path);
                    long handle = state.Add(stream, writes);
                    return new SyncCentralResponse { Handle = handle, Entry = EntryOf(new FileInfo(path)) };
                }
                case SyncCentralOp.Delete:
                    if (!File.Exists(path))
                        return Refuse(SyncCentralErrors.NotFound);
                    state.Undo!.Delete(path);
                    CommitIfDone(state);
                    return new SyncCentralResponse();
                case SyncCentralOp.Move:
                {
                    string target = Resolve(request.NewPath);
                    state.Undo!.Move(path, target, request.Overwrite);
                    CommitIfDone(state);
                    return new SyncCentralResponse();
                }
                case SyncCentralOp.CreateDirectory:
                    if (Directory.Exists(path) || File.Exists(path))
                        return Refuse(SyncCentralErrors.Exists);
                    Directory.CreateDirectory(path);
                    state.Undo!.CreatedDirectory(path);
                    CommitIfDone(state);
                    return new SyncCentralResponse();
                case SyncCentralOp.DeleteDirectory:
                    if (!Directory.Exists(path))
                        return Refuse(SyncCentralErrors.PathNotFound);
                    if (Directory.EnumerateFileSystemEntries(path).Any())
                        return Refuse(SyncCentralErrors.DirectoryNotEmpty);
                    Directory.Delete(path);
                    state.Undo!.DeletedDirectory(path);
                    CommitIfDone(state);
                    return new SyncCentralResponse();
                case SyncCentralOp.SetInfo:
                {
                    FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
                    if (!info.Exists)
                        return Refuse(SyncCentralErrors.NotFound);
                    if (info is FileInfo)
                        state.Undo!.Keep(path);
                    if (request.Attributes is { } attributes)
                        info.Attributes = attributes;
                    if (request.CreationUtc is { } created)
                        info.CreationTimeUtc = created;
                    if (request.LastWriteUtc is { } written)
                        info.LastWriteTimeUtc = written;
                    if (request.LastAccessUtc is { } accessed)
                        info.LastAccessTimeUtc = accessed;
                    return new SyncCentralResponse();
                }
                default:
                    return Refuse(SyncCentralErrors.BadRequest);
            }
        }
        catch (NotCentralException)
        {
            return Refuse(SyncCentralErrors.NotCentral);
        }
        catch (FileNotFoundException exception)
        {
            return Refuse(SyncCentralErrors.NotFound, exception.HResult);
        }
        catch (DirectoryNotFoundException exception)
        {
            return Refuse(SyncCentralErrors.PathNotFound, exception.HResult);
        }
        catch (UnauthorizedAccessException exception)
        {
            return Refuse(SyncCentralErrors.AccessDenied, exception.HResult);
        }
        catch (KeyNotFoundException)
        {
            return Refuse(SyncCentralErrors.BadHandle);
        }
        catch (IOException exception)
        {
            return Refuse((exception.HResult & 0xFFFF) switch
            {
                32 => SyncCentralErrors.SharingViolation,
                33 => SyncCentralErrors.LockViolation,
                80 or 183 => SyncCentralErrors.Exists,
                145 => SyncCentralErrors.DirectoryNotEmpty,
                2 => SyncCentralErrors.NotFound,
                3 => SyncCentralErrors.PathNotFound,
                _ => SyncCentralErrors.IoError,
            }, exception.HResult);
        }
    }

    /// <summary>A member gone (disconnected): every handle it held closes, every lock it held goes.</summary>
    public void EndSession(long session)
    {
        if (!_sessions.TryRemove(session, out Session? state))
            return;
        lock (state)
        {
            state.Dispose();
            // 🔴 A burst that never finished - the road broke mid-synchronize - is undone: the
            // central goes back exactly as it was, and Revit, whose calls failed, keeps the work
            // in the local and synchronizes again later.
            state.Undo?.RollBack();
            state.Undo = null;
        }
    }

    /// <summary>Bursts rolled back since the host started - a count for Twin.</summary>
    public int RolledBack => UndoLog.RolledBackCount;

    /// <summary>Commits the session's burst when nothing of it is still open and PFR is not holding it.</summary>
    private static void CommitIfDone(Session state)
    {
        if (state.Undo is { } undo && !state.Explicit && state.WriteHandles == 0)
        {
            undo.Commit();
            state.Undo = null;
        }
    }

    public void Dispose()
    {
        foreach (long session in _sessions.Keys.ToArray())
            EndSession(session);
    }

    private static bool Mutates(SyncCentralRequest request) =>
        request.Op is SyncCentralOp.Write or SyncCentralOp.SetLength or SyncCentralOp.Delete or SyncCentralOp.Move
            or SyncCentralOp.CreateDirectory or SyncCentralOp.DeleteDirectory or SyncCentralOp.SetInfo ||
        (request.Op == SyncCentralOp.Open && (request.Access & FileAccess.Write) != 0);

    /// <summary>
    /// A copy of the central and its _backup before a member's first write - the way back if a
    /// synchronize breaks the central. Read with sharing, so an open central is copied as it is.
    /// </summary>
    /// <summary>The central's write time a snapshot was taken from - the next copy is skipped while it is unchanged.</summary>
    private const string SourceMark = ".erks-source-write";

    /// <summary>A member wrote since the last copy (the file's time moves only when Revit closes it).</summary>
    private volatile bool _writtenSinceSnapshot;

    private void Snapshot(DateTimeOffset now)
    {
        if (_snapshots is null)
            return;
        lock (_snapshotGate)
        {
            if (LastSnapshotUtc is { } last && now - last < TimeSpan.FromMinutes(1))
                return;
            // A member that connects again and again does not copy an unchanged central each time:
            // the newest copy already holds this very write of it.
            string central = System.IO.Path.Combine(_folder, _centralFileName);
            string written = File.Exists(central) ? File.GetLastWriteTimeUtc(central).Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
            if (!_writtenSinceSnapshot &&
                Directory.Exists(_snapshots) &&
                Directory.EnumerateDirectories(_snapshots).OrderByDescending(path => path, StringComparer.Ordinal).FirstOrDefault() is { } newest &&
                File.Exists(System.IO.Path.Combine(newest, SourceMark)) &&
                File.ReadAllText(System.IO.Path.Combine(newest, SourceMark)) == written)
            {
                return;
            }
            string stem = System.IO.Path.GetFileNameWithoutExtension(_centralFileName);
            string target = System.IO.Path.Combine(_snapshots, now.UtcDateTime.ToString("yyyyMMdd-HHmmss-fff"));
            Directory.CreateDirectory(target);
            try
            {
                CopyShared(System.IO.Path.Combine(_folder, _centralFileName), System.IO.Path.Combine(target, _centralFileName));
                string backup = System.IO.Path.Combine(_folder, stem + "_backup");
                if (Directory.Exists(backup))
                {
                    foreach (string file in Directory.EnumerateFiles(backup, "*", SearchOption.AllDirectories))
                        CopyShared(file, System.IO.Path.Combine(target, stem + "_backup", System.IO.Path.GetRelativePath(backup, file)));
                }
                File.WriteAllText(System.IO.Path.Combine(target, SourceMark), written);
                LastSnapshotUtc = now;
                _writtenSinceSnapshot = false;
            }
            catch (IOException)
            {
                // A file locked exclusively: the snapshot is partial; the member's write still goes on.
            }
            foreach (string old in Directory.EnumerateDirectories(_snapshots).OrderByDescending(path => path, StringComparer.Ordinal).Skip(_keepSnapshots))
                Directory.Delete(old, recursive: true);
        }
    }

    private static void CopyShared(string source, string target)
    {
        if (!File.Exists(source))
            return;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None);
        input.CopyTo(output);
    }

    private string Resolve(string relative)
    {
        if (!SyncCentralPaths.Belongs(_centralFileName, relative) && (relative ?? "").Trim('\\', '/').Length > 0)
            throw new NotCentralException();
        string full = System.IO.Path.GetFullPath(System.IO.Path.Combine(_folder, (relative ?? "").Replace('/', '\\').TrimStart('\\')));
        if (!SyncPath.IsSameOrInside(_folder, full))
            throw new NotCentralException();
        return full;
    }

    private static SyncCentralEntry EntryOf(FileSystemInfo info) => new(
        info.Name,
        info is DirectoryInfo,
        info is FileInfo file ? file.Length : 0,
        info.Attributes,
        info.CreationTimeUtc,
        info.LastWriteTimeUtc,
        info.LastAccessTimeUtc);

    private static SyncCentralResponse Refuse(string error, int hresult = 0) => new() { Error = error, HResult = hresult };

    private sealed class NotCentralException : Exception;

    private sealed class Session : IDisposable
    {
        private readonly ConcurrentDictionary<long, FileStream> _streams = new();
        private readonly ConcurrentDictionary<long, bool> _writers = new();
        private readonly Dictionary<long, (SyncCentralResponse Response, byte[]? Reply)> _answers = new();
        private readonly Queue<long> _answerOrder = new();
        private long _next;

        public DateTimeOffset LastSeenUtc { get; set; }

        public bool Writing { get; set; }

        /// <summary>PFR holds the commit between its begin and end of a synchronize.</summary>
        public bool Explicit { get; set; }

        public UndoLog? Undo { get; set; }

        public int Count => _streams.Count;

        public int WriteHandles => _writers.Count;

        public (SyncCentralResponse Response, byte[]? Reply)? Answered(long seq) =>
            _answers.TryGetValue(seq, out var done) ? done : null;

        /// <summary>The last 256 answers - enough for every call in flight when a road breaks.</summary>
        public void Remember(long seq, SyncCentralResponse response, byte[]? reply)
        {
            if (!_answers.TryAdd(seq, (response, reply)))
                return;
            _answerOrder.Enqueue(seq);
            while (_answerOrder.Count > 256)
                _answers.Remove(_answerOrder.Dequeue());
        }

        public long Add(FileStream stream, bool writes = false)
        {
            long handle = Interlocked.Increment(ref _next);
            _streams[handle] = stream;
            if (writes)
                _writers[handle] = true;
            return handle;
        }

        public FileStream Stream(long handle) =>
            _streams.TryGetValue(handle, out FileStream? stream) ? stream : throw new KeyNotFoundException();

        public bool Close(long handle)
        {
            if (!_streams.TryRemove(handle, out FileStream? stream))
                return false;
            _writers.TryRemove(handle, out _);
            stream.Dispose();
            return true;
        }

        public void Dispose()
        {
            foreach (long handle in _streams.Keys.ToArray())
                Close(handle);
        }
    }
}

/// <summary>
/// What one member's burst of writes changed, kept so it can be undone whole: the first
/// version of every file it touched, the files and folders it made, the moves it did. A
/// finished burst drops the log; a broken one replays it backwards.
/// </summary>
public sealed class UndoLog
{
    private static int s_rolledBack;
    private readonly string _root;
    private readonly List<Action> _undo = [];
    private readonly HashSet<string> _kept = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _created = new(StringComparer.OrdinalIgnoreCase);
    private int _next;

    public UndoLog(string under)
    {
        _root = System.IO.Path.Combine(under, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public static int RolledBackCount => s_rolledBack;

    /// <summary><see cref="Keep"/>, or false when another holder's share mode forbids reading it.</summary>
    public bool TryKeep(string path)
    {
        try
        {
            Keep(path);
            return true;
        }
        catch (IOException)
        {
            _kept.Remove(path);
            return false;
        }
    }

    /// <summary>Before the first change to an existing file: its whole first version is kept.</summary>
    public void Keep(string path)
    {
        if (_created.Contains(path) || !_kept.Add(path) || !File.Exists(path))
            return;
        string copy = Next();
        using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var output = new FileStream(copy, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            input.CopyTo(output);
        var info = new FileInfo(path);
        (FileAttributes attributes, DateTime written, DateTime created) = (info.Attributes, info.LastWriteTimeUtc, info.CreationTimeUtc);
        _undo.Add(() =>
        {
            File.Copy(copy, path, overwrite: true);
            File.SetAttributes(path, attributes);
            File.SetCreationTimeUtc(path, created);
            File.SetLastWriteTimeUtc(path, written);
        });
    }

    public void Created(string path)
    {
        if (!_created.Add(path))
            return;
        _undo.Add(() =>
        {
            if (File.Exists(path))
                File.Delete(path);
        });
    }

    public void CreatedDirectory(string path) => _undo.Add(() =>
    {
        if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
            Directory.Delete(path);
    });

    public void DeletedDirectory(string path) => _undo.Add(() => Directory.CreateDirectory(path));

    /// <summary>A file this burst made is simply gone; one that was there is moved aside, not deleted.</summary>
    public void Delete(string path)
    {
        if (_created.Contains(path))
        {
            File.Delete(path);
            return;
        }
        Keep(path);
        File.Delete(path);
    }

    public void Move(string from, string to, bool overwrite)
    {
        if (Directory.Exists(from))
        {
            Directory.Move(from, to);
            _undo.Add(() => Directory.Move(to, from));
            return;
        }
        if (File.Exists(to))
        {
            if (!overwrite)
                throw new IOException("exists", unchecked((int)0x800700B7));
            Keep(to);
        }
        Keep(from);
        File.Move(from, to, overwrite);
        if (!_kept.Contains(to))
            Created(to);
    }

    /// <summary>The burst finished: nothing to undo.</summary>
    public void Commit()
    {
        _undo.Clear();
        TryDelete();
    }

    /// <summary>The burst broke: every change undone, newest first.</summary>
    public void RollBack()
    {
        for (int index = _undo.Count - 1; index >= 0; index--)
        {
            try
            {
                _undo[index]();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Keep going: the snapshot taken before the burst is the last resort.
            }
        }
        _undo.Clear();
        Interlocked.Increment(ref s_rolledBack);
        TryDelete();
    }

    private string Next() => System.IO.Path.Combine(_root, (++_next).ToString(System.Globalization.CultureInfo.InvariantCulture));

    private void TryDelete()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>A road to a central's host: the LAN session or the server relay. One call, one answer.</summary>
public interface ISyncCentralChannel
{
    Task<(SyncCentralResponse Response, byte[]? Data)> CallAsync(SyncCentralRequest request, ReadOnlyMemory<byte> data, CancellationToken cancellationToken);
}

/// <summary>The wire form of a call: a JSON header frame, then the data frame when there is one.</summary>
public static class SyncCentralWire
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static byte[] Encode<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, Json);

    public static T Decode<T>(ReadOnlySpan<byte> json) =>
        JsonSerializer.Deserialize<T>(json, Json) ?? throw new InvalidDataException("empty central frame");
}

/// <summary>
/// The member half: what the mounted file system calls. It knows nothing of Revit - only files,
/// handles, share modes and locks - which is why Revit's own rules hold through it.
/// </summary>
public sealed class SyncCentralClient(ISyncCentralChannel channel)
{
    public async Task<SyncCentralResponse> CallAsync(SyncCentralRequest request, CancellationToken cancellationToken = default) =>
        (await channel.CallAsync(request, ReadOnlyMemory<byte>.Empty, cancellationToken)).Response;

    public async Task<(SyncCentralResponse Response, byte[] Data)> ReadAsync(long handle, long offset, int length, CancellationToken cancellationToken = default)
    {
        (SyncCentralResponse response, byte[]? data) = await channel.CallAsync(
            new SyncCentralRequest { Op = SyncCentralOp.Read, Handle = handle, Offset = offset, Length = length },
            ReadOnlyMemory<byte>.Empty,
            cancellationToken);
        return (response, data ?? []);
    }

    public async Task<SyncCentralResponse> WriteAsync(long handle, long offset, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default) =>
        (await channel.CallAsync(new SyncCentralRequest { Op = SyncCentralOp.Write, Handle = handle, Offset = offset }, data, cancellationToken)).Response;
}

/// <summary>A channel straight into a host in this process - for tests, and for the host's own machine.</summary>
public sealed class SyncCentralLocalChannel(SyncCentralHost host, long session) : ISyncCentralChannel
{
    public Task<(SyncCentralResponse Response, byte[]? Data)> CallAsync(SyncCentralRequest request, ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        // Through the wire form, so a test proves what travels, not only what an object holds.
        SyncCentralRequest carried = SyncCentralWire.Decode<SyncCentralRequest>(SyncCentralWire.Encode(request));
        SyncCentralResponse response = host.Handle(session, carried, data.ToArray(), out byte[]? reply);
        return Task.FromResult((SyncCentralWire.Decode<SyncCentralResponse>(SyncCentralWire.Encode(response)), reply));
    }
}
