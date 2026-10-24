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
}

/// <summary>One file operation, as a member's file system asks it. Paths are relative to the central's folder.</summary>
public sealed record SyncCentralRequest
{
    public string Op { get; init; } = "";

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

    public SyncCentralHost(string folder, string centralFileName)
    {
        _folder = System.IO.Path.GetFullPath(folder);
        _centralFileName = centralFileName;
    }

    public SyncCentralResponse Handle(long session, SyncCentralRequest request, ReadOnlyMemory<byte> data, out byte[]? reply)
    {
        reply = null;
        Session state = _sessions.GetOrAdd(session, _ => new Session());
        try
        {
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
                    return state.Close(request.Handle)
                        ? new SyncCentralResponse()
                        : Refuse(SyncCentralErrors.BadHandle);
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
                    var options = request.DeleteOnClose ? FileOptions.DeleteOnClose : FileOptions.None;
                    var stream = new FileStream(path, request.Mode, request.Access, request.Share | FileShare.Delete, 4096, options);
                    long handle = state.Add(stream);
                    return new SyncCentralResponse { Handle = handle, Entry = EntryOf(new FileInfo(path)) };
                }
                case SyncCentralOp.Delete:
                    if (!File.Exists(path))
                        return Refuse(SyncCentralErrors.NotFound);
                    File.Delete(path);
                    return new SyncCentralResponse();
                case SyncCentralOp.Move:
                {
                    string target = Resolve(request.NewPath);
                    if (Directory.Exists(path))
                        Directory.Move(path, target);
                    else
                        File.Move(path, target, request.Overwrite);
                    return new SyncCentralResponse();
                }
                case SyncCentralOp.CreateDirectory:
                    if (Directory.Exists(path) || File.Exists(path))
                        return Refuse(SyncCentralErrors.Exists);
                    Directory.CreateDirectory(path);
                    return new SyncCentralResponse();
                case SyncCentralOp.DeleteDirectory:
                    if (!Directory.Exists(path))
                        return Refuse(SyncCentralErrors.PathNotFound);
                    if (Directory.EnumerateFileSystemEntries(path).Any())
                        return Refuse(SyncCentralErrors.DirectoryNotEmpty);
                    Directory.Delete(path);
                    return new SyncCentralResponse();
                case SyncCentralOp.SetInfo:
                {
                    FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
                    if (!info.Exists)
                        return Refuse(SyncCentralErrors.NotFound);
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
        if (_sessions.TryRemove(session, out Session? state))
            state.Dispose();
    }

    public void Dispose()
    {
        foreach (long session in _sessions.Keys.ToArray())
            EndSession(session);
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
        private long _next;

        public long Add(FileStream stream)
        {
            long handle = Interlocked.Increment(ref _next);
            _streams[handle] = stream;
            return handle;
        }

        public FileStream Stream(long handle) =>
            _streams.TryGetValue(handle, out FileStream? stream) ? stream : throw new KeyNotFoundException();

        public bool Close(long handle)
        {
            if (!_streams.TryRemove(handle, out FileStream? stream))
                return false;
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
