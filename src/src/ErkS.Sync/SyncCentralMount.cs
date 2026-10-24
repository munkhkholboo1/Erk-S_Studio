using System.Security.AccessControl;
using DokanNet;
using ErkS.Sync.Core;
using DokanAccess = DokanNet.FileAccess;
using IoAccess = System.IO.FileAccess;

namespace ErkS.Sync;

/// <summary>
/// Erk-S Central on a member's machine (_shared/erks-central-design-2026-10-24.md): a Dokany file
/// system (free, open source - owner, 2026-10-24: «төлбөртэй хаалттай систем ашиглахгуй») that
/// shows the host's central and its _backup as local files. Every operation Revit makes is
/// carried to the host and run there on real handles - nothing here is a copy, so nothing can
/// diverge. When the host cannot be reached the answer is at once «bad network path», which
/// Revit treats as an unreachable central: it keeps working in the local and never hangs.
/// </summary>
internal sealed class SyncCentralMount(SyncCentralClient client) : IDokanOperations
{
    /// <summary>STATUS_BAD_NETWORK_PATH - what Windows answers for a file server that is not there.</summary>
    private static readonly NtStatus BadNetworkPath = (NtStatus)Enum.ToObject(typeof(NtStatus), unchecked((long)0xC00000BEu));

    public NtStatus CreateFile(string fileName, DokanAccess access, FileShare share, FileMode mode, FileOptions options, FileAttributes attributes, IDokanFileInfo info)
    {
        string path = Relative(fileName);
        if (path.Length == 0)
        {
            info.IsDirectory = true;
            return DokanResult.Success;
        }

        SyncCentralResponse stat = Call(new SyncCentralRequest { Op = SyncCentralOp.Stat, Path = path });
        if (stat.Error == SyncCentralErrors.HostUnreachable)
            return BadNetworkPath;
        bool exists = stat.Ok;
        bool isDirectory = stat.Entry?.IsDirectory == true;

        if (info.IsDirectory || isDirectory)
        {
            if (mode == FileMode.CreateNew)
            {
                if (exists)
                    return DokanResult.AlreadyExists;
                return Status(Call(new SyncCentralRequest { Op = SyncCentralOp.CreateDirectory, Path = path }));
            }
            if (!exists)
                return DokanResult.PathNotFound;
            if (!isDirectory)
                return DokanResult.NotADirectory;
            info.IsDirectory = true;
            return DokanResult.Success;
        }

        bool reads = (access & (DokanAccess.ReadData | DokanAccess.GenericRead | DokanAccess.GenericAll | DokanAccess.Execute | DokanAccess.GenericExecute)) != 0;
        bool writes = (access & (DokanAccess.WriteData | DokanAccess.AppendData | DokanAccess.GenericWrite | DokanAccess.GenericAll)) != 0;
        bool creates = mode is FileMode.CreateNew or FileMode.Create or FileMode.OpenOrCreate or FileMode.Truncate;

        // An open for attributes only (Explorer, Revit's existence checks) holds nothing on the host.
        if (!reads && !writes && !(creates && !exists) && mode != FileMode.Truncate)
        {
            if (!exists && mode is FileMode.Open or FileMode.Append)
                return DokanResult.FileNotFound;
            if (exists && mode == FileMode.CreateNew)
                return DokanResult.FileExists;
            return exists && mode is FileMode.OpenOrCreate or FileMode.Create ? DokanResult.AlreadyExists : DokanResult.Success;
        }

        IoAccess ioAccess = writes ? (reads ? IoAccess.ReadWrite : IoAccess.Write) : IoAccess.Read;
        if (mode is FileMode.Create or FileMode.Truncate or FileMode.CreateNew && ioAccess == IoAccess.Read)
            ioAccess = IoAccess.ReadWrite;
        SyncCentralResponse opened = Call(new SyncCentralRequest
        {
            Op = SyncCentralOp.Open,
            Path = path,
            Mode = mode == FileMode.Append ? FileMode.OpenOrCreate : mode,
            Access = ioAccess,
            Share = share,
            DeleteOnClose = (options & FileOptions.DeleteOnClose) != 0,
        });
        if (!opened.Ok)
            return Status(opened);
        info.Context = new Handle(opened.Handle);
        return exists && mode is FileMode.OpenOrCreate or FileMode.Create ? DokanResult.AlreadyExists : DokanResult.Success;
    }

    public void Cleanup(string fileName, IDokanFileInfo info)
    {
        Close(info);
        if (!info.DeletePending)
            return;
        Call(new SyncCentralRequest
        {
            Op = info.IsDirectory ? SyncCentralOp.DeleteDirectory : SyncCentralOp.Delete,
            Path = Relative(fileName),
        });
    }

    public void CloseFile(string fileName, IDokanFileInfo info) => Close(info);

    public NtStatus ReadFile(string fileName, byte[] buffer, out int bytesRead, long offset, IDokanFileInfo info)
    {
        bytesRead = 0;
        long handle = HandleOf(info) ?? 0;
        bool temporary = handle == 0;
        if (temporary)
        {
            // Paging I/O can read without the opener's handle: a short-lived read handle of its own.
            SyncCentralResponse opened = Call(new SyncCentralRequest { Op = SyncCentralOp.Open, Path = Relative(fileName), Access = IoAccess.Read, Share = FileShare.ReadWrite | FileShare.Delete });
            if (!opened.Ok)
                return Status(opened);
            handle = opened.Handle;
        }
        try
        {
            while (bytesRead < buffer.Length)
            {
                int want = Math.Min(buffer.Length - bytesRead, SyncIndex.ChunkSize);
                (SyncCentralResponse response, byte[] data) = client.ReadAsync(handle, offset + bytesRead, want).GetAwaiter().GetResult();
                if (!response.Ok)
                    return Status(response);
                data.CopyTo(buffer, bytesRead);
                bytesRead += data.Length;
                if (data.Length < want)
                    break;
            }
            return DokanResult.Success;
        }
        finally
        {
            if (temporary)
                Call(new SyncCentralRequest { Op = SyncCentralOp.Close, Handle = handle });
        }
    }

    public NtStatus WriteFile(string fileName, byte[] buffer, out int bytesWritten, long offset, IDokanFileInfo info)
    {
        bytesWritten = 0;
        if (HandleOf(info) is not { } handle)
            return DokanResult.InvalidHandle;
        if (info.WriteToEndOfFile || offset < 0)
        {
            SyncCentralResponse stat = Call(new SyncCentralRequest { Op = SyncCentralOp.Stat, Path = Relative(fileName) });
            if (!stat.Ok)
                return Status(stat);
            offset = stat.Entry!.Size;
        }
        while (bytesWritten < buffer.Length)
        {
            int count = Math.Min(buffer.Length - bytesWritten, SyncIndex.ChunkSize);
            SyncCentralResponse written = client.WriteAsync(handle, offset + bytesWritten, buffer.AsMemory(bytesWritten, count)).GetAwaiter().GetResult();
            if (!written.Ok)
                return Status(written);
            bytesWritten += count;
        }
        return DokanResult.Success;
    }

    public NtStatus FlushFileBuffers(string fileName, IDokanFileInfo info) =>
        HandleOf(info) is { } handle ? Status(Call(new SyncCentralRequest { Op = SyncCentralOp.Flush, Handle = handle })) : DokanResult.Success;

    public NtStatus GetFileInformation(string fileName, out FileInformation fileInfo, IDokanFileInfo info)
    {
        string path = Relative(fileName);
        if (path.Length == 0)
        {
            fileInfo = new FileInformation { FileName = "", Attributes = FileAttributes.Directory, CreationTime = DateTime.Now, LastWriteTime = DateTime.Now, LastAccessTime = DateTime.Now };
            return DokanResult.Success;
        }
        SyncCentralResponse stat = Call(new SyncCentralRequest { Op = SyncCentralOp.Stat, Path = path });
        fileInfo = stat.Entry is { } entry ? Information(entry) : default;
        return Status(stat);
    }

    public NtStatus FindFiles(string fileName, out IList<FileInformation> files, IDokanFileInfo info)
    {
        files = [];
        return DokanResult.NotImplemented; // Dokan then asks FindFilesWithPattern
    }

    public NtStatus FindFilesWithPattern(string fileName, string searchPattern, out IList<FileInformation> files, IDokanFileInfo info)
    {
        SyncCentralResponse listed = Call(new SyncCentralRequest { Op = SyncCentralOp.List, Path = Relative(fileName) });
        files = (listed.Entries ?? [])
            .Where(entry => DokanHelper.DokanIsNameInExpression(searchPattern, entry.Name, true))
            .Select(Information)
            .ToList();
        return Status(listed);
    }

    public NtStatus SetFileAttributes(string fileName, FileAttributes attributes, IDokanFileInfo info) =>
        attributes == 0 ? DokanResult.Success : Status(Call(new SyncCentralRequest { Op = SyncCentralOp.SetInfo, Path = Relative(fileName), Attributes = attributes }));

    public NtStatus SetFileTime(string fileName, DateTime? creationTime, DateTime? lastAccessTime, DateTime? lastWriteTime, IDokanFileInfo info) =>
        Status(Call(new SyncCentralRequest
        {
            Op = SyncCentralOp.SetInfo,
            Path = Relative(fileName),
            CreationUtc = creationTime?.ToUniversalTime(),
            LastAccessUtc = lastAccessTime?.ToUniversalTime(),
            LastWriteUtc = lastWriteTime?.ToUniversalTime(),
        }));

    /// <summary>Only checked here; the delete itself happens at cleanup, as NTFS does it.</summary>
    public NtStatus DeleteFile(string fileName, IDokanFileInfo info)
    {
        SyncCentralResponse stat = Call(new SyncCentralRequest { Op = SyncCentralOp.Stat, Path = Relative(fileName) });
        if (!stat.Ok)
            return Status(stat);
        return stat.Entry!.IsDirectory ? DokanResult.AccessDenied : DokanResult.Success;
    }

    public NtStatus DeleteDirectory(string fileName, IDokanFileInfo info)
    {
        SyncCentralResponse listed = Call(new SyncCentralRequest { Op = SyncCentralOp.List, Path = Relative(fileName) });
        if (!listed.Ok)
            return Status(listed);
        return listed.Entries!.Count > 0 ? DokanResult.DirectoryNotEmpty : DokanResult.Success;
    }

    public NtStatus MoveFile(string oldName, string newName, bool replace, IDokanFileInfo info)
    {
        // The handle the mover holds is closed first, as Windows needs for a rename with an open handle.
        Close(info);
        return Status(Call(new SyncCentralRequest { Op = SyncCentralOp.Move, Path = Relative(oldName), NewPath = Relative(newName), Overwrite = replace }));
    }

    public NtStatus SetEndOfFile(string fileName, long length, IDokanFileInfo info) =>
        HandleOf(info) is { } handle ? Status(Call(new SyncCentralRequest { Op = SyncCentralOp.SetLength, Handle = handle, Length = length })) : DokanResult.InvalidHandle;

    public NtStatus SetAllocationSize(string fileName, long length, IDokanFileInfo info)
    {
        if (HandleOf(info) is not { } handle)
            return DokanResult.InvalidHandle;
        SyncCentralResponse stat = Call(new SyncCentralRequest { Op = SyncCentralOp.Stat, Path = Relative(fileName) });
        if (!stat.Ok)
            return Status(stat);
        // Allocation never grows a file's length; it only cuts one that is longer.
        return length < stat.Entry!.Size
            ? Status(Call(new SyncCentralRequest { Op = SyncCentralOp.SetLength, Handle = handle, Length = length }))
            : DokanResult.Success;
    }

    public NtStatus LockFile(string fileName, long offset, long length, IDokanFileInfo info) =>
        HandleOf(info) is { } handle ? Status(Call(new SyncCentralRequest { Op = SyncCentralOp.Lock, Handle = handle, Offset = offset, Length = length })) : DokanResult.InvalidHandle;

    public NtStatus UnlockFile(string fileName, long offset, long length, IDokanFileInfo info) =>
        HandleOf(info) is { } handle ? Status(Call(new SyncCentralRequest { Op = SyncCentralOp.Unlock, Handle = handle, Offset = offset, Length = length })) : DokanResult.InvalidHandle;

    public NtStatus GetDiskFreeSpace(out long freeBytesAvailable, out long totalNumberOfBytes, out long totalNumberOfFreeBytes, IDokanFileInfo info)
    {
        freeBytesAvailable = totalNumberOfFreeBytes = 512L << 30;
        totalNumberOfBytes = 1024L << 30;
        return DokanResult.Success;
    }

    public NtStatus GetVolumeInformation(out string volumeLabel, out FileSystemFeatures features, out string fileSystemName, out uint maximumComponentLength, IDokanFileInfo info)
    {
        volumeLabel = "Erk-S Central";
        features = FileSystemFeatures.CasePreservedNames | FileSystemFeatures.UnicodeOnDisk;
        fileSystemName = "NTFS";
        maximumComponentLength = 255;
        return DokanResult.Success;
    }

    public NtStatus GetFileSecurity(string fileName, out FileSystemSecurity? security, AccessControlSections sections, IDokanFileInfo info)
    {
        security = null;
        return DokanResult.NotImplemented;
    }

    public NtStatus SetFileSecurity(string fileName, FileSystemSecurity security, AccessControlSections sections, IDokanFileInfo info) =>
        DokanResult.NotImplemented;

    public NtStatus Mounted(string mountPoint, IDokanFileInfo info) => DokanResult.Success;

    public NtStatus Unmounted(IDokanFileInfo info) => DokanResult.Success;

    public NtStatus FindStreams(string fileName, out IList<FileInformation> streams, IDokanFileInfo info)
    {
        streams = [];
        return DokanResult.NotImplemented;
    }

    private SyncCentralResponse Call(SyncCentralRequest request) => client.CallAsync(request).GetAwaiter().GetResult();

    private void Close(IDokanFileInfo info)
    {
        if (info.Context is Handle handle && handle.TryClose())
            Call(new SyncCentralRequest { Op = SyncCentralOp.Close, Handle = handle.Id });
    }

    private static long? HandleOf(IDokanFileInfo info) => info.Context is Handle { Closed: false } handle ? handle.Id : null;

    private static string Relative(string fileName) => (fileName ?? "").Replace('/', '\\').Trim('\\');

    private static FileInformation Information(SyncCentralEntry entry) => new()
    {
        FileName = entry.Name,
        Attributes = entry.IsDirectory ? entry.Attributes | FileAttributes.Directory : entry.Attributes,
        Length = entry.Size,
        CreationTime = entry.CreationUtc.ToLocalTime(),
        LastWriteTime = entry.LastWriteUtc.ToLocalTime(),
        LastAccessTime = entry.LastAccessUtc.ToLocalTime(),
    };

    /// <summary>What NTFS answered on the host, as the NTSTATUS Revit would get from a file server.</summary>
    internal static NtStatus Status(SyncCentralResponse response) => response.Error switch
    {
        "" => DokanResult.Success,
        SyncCentralErrors.NotFound => DokanResult.FileNotFound,
        SyncCentralErrors.PathNotFound => DokanResult.PathNotFound,
        SyncCentralErrors.SharingViolation => DokanResult.SharingViolation,
        SyncCentralErrors.LockViolation => NtStatus.FileLockConflict,
        SyncCentralErrors.AccessDenied => DokanResult.AccessDenied,
        SyncCentralErrors.Exists => DokanResult.FileExists,
        SyncCentralErrors.DirectoryNotEmpty => DokanResult.DirectoryNotEmpty,
        SyncCentralErrors.NotCentral => DokanResult.AccessDenied,
        SyncCentralErrors.BadHandle => DokanResult.InvalidHandle,
        SyncCentralErrors.HostUnreachable => BadNetworkPath,
        _ => DokanResult.Error,
    };

    private sealed class Handle(long id)
    {
        private int _closed;

        public long Id { get; } = id;

        public bool Closed => _closed != 0;

        public bool TryClose() => Interlocked.Exchange(ref _closed, 1) == 0;
    }
}
