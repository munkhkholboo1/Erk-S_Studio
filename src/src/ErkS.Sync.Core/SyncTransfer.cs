using System.Security.Cryptography;

namespace ErkS.Sync.Core;

public enum SyncTransferKind
{
    /// <summary>Only the peer holds the file: it comes here.</summary>
    Fetch,

    /// <summary>The peer's version came after this machine's: it replaces it.</summary>
    Replace,

    /// <summary>Both changed the same version: the peer's is kept beside this one, nothing is replaced.</summary>
    KeepBoth,
}

/// <param name="KeepPrevious">A person chose «Тэндхийг авах»: this machine's version is kept in «.erks-sync\replaced».</param>
public sealed record SyncTransferAction(SyncTransferKind Kind, SyncIndexEntry Theirs, SyncIndexEntry? Mine, bool KeepPrevious = false);

/// <summary>What one peer's index means for this machine, by count where nothing moves.</summary>
public sealed record SyncTransferPlan(
    IReadOnlyList<SyncTransferAction> Actions,
    int AheadHere,
    int Unrelated,
    int DeletedHere,
    int NotCarried);

/// <summary>
/// Decides, file by file, what a peer's copy means here - and moves nothing itself.
///
/// 🔴 NOTHING IS EVER SILENTLY OVERWRITTEN (owner, 2026-10-23). A peer's version replaces this
/// machine's only when its history contains this machine's version - the peer saw it and
/// went on. Two edits of one version are both kept. Copies that never shared a version
/// (a colleague's non-empty folder at join) stay «зөрүүтэй» for a person to decide.
/// </summary>
public static class SyncTransferPlanner
{
    /// <summary>
    /// Revit central models travel only through their host (plan §4.6 A, after 1.3); copying
    /// them file by file is exactly how Resilio splits a central in two.
    /// </summary>
    public static bool IsCarried(SyncFileKind kind) =>
        kind is not (SyncFileKind.RevitCentral or SyncFileKind.RevitCentralData);

    /// <param name="restored">Authoritative contents: what a writer put back at a reused number
    /// (<see cref="SyncCollab.Restored"/>) and the clean contents of redactions.</param>
    /// <param name="redacted">Contents redacted for privacy (<see cref="SyncRedactions"/>): never fetched, never kept.</param>
    public static SyncTransferPlan Plan(SyncIndex mine, SyncIndex theirs, SyncVersionLedger ledger, IReadOnlyList<SyncDecision>? decisions = null, IReadOnlySet<string>? restored = null, IReadOnlySet<string>? redacted = null)
    {
        ArgumentNullException.ThrowIfNull(mine);
        ArgumentNullException.ThrowIfNull(theirs);
        ArgumentNullException.ThrowIfNull(ledger);
        Dictionary<string, SyncIndexEntry> here = mine.Entries
            .GroupBy(entry => entry.RelativePath, SyncPath.Comparer)
            .ToDictionary(group => group.Key, group => group.First(), SyncPath.Comparer);
        var actions = new List<SyncTransferAction>();
        int ahead = 0, unrelated = 0, deleted = 0, notCarried = 0;
        foreach (SyncIndexEntry entry in theirs.Entries)
        {
            if (entry.Version is not { } version || version.Sha256.Length == 0)
                continue;
            if (redacted?.Contains(entry.Sha256) == true)
            {
                notCarried++; // a partner still offering a redacted content: it never comes
                continue;
            }
            if (!IsCarried(entry.Kind) || SyncExclusions.IsExcludedFile(Path.GetFileName(entry.RelativePath)))
            {
                notCarried++;
                continue;
            }
            // A CGA changeset is written once by its writer: one missing here always comes (an older
            // SYN retired logs), and one here is never replaced by other bytes - both are kept, and
            // only the writer's own repair may put the right content back (Erk-S Sync, 2026-10-26).
            bool changeset = SyncCollab.IsChangesetPath(entry.RelativePath);
            if (!here.TryGetValue(entry.RelativePath, out SyncIndexEntry? local))
            {
                if (!changeset && ledger.DeletedHere(entry.RelativePath, version))
                    deleted++;
                else
                    actions.Add(new SyncTransferAction(SyncTransferKind.Fetch, entry, null));
                continue;
            }
            if (local.Sha256.Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase))
                continue;
            if (changeset)
            {
                // The content the writer put back (or a redaction's clean one) is authoritative:
                // it replaces, and a stale copy offered against it is dropped, not kept beside.
                if (restored?.Contains(entry.Sha256) == true)
                    actions.Add(new SyncTransferAction(SyncTransferKind.Replace, entry, local));
                else if (restored?.Contains(local.Sha256) != true && !ledger.HasKept(entry.RelativePath, entry.Sha256))
                    actions.Add(new SyncTransferAction(SyncTransferKind.KeepBoth, entry, local));
                continue;
            }
            if (!IsCarried(local.Kind))
            {
                notCarried++;
                continue;
            }
            SyncFileVersion mineVersion = local.Version ?? new SyncFileVersion { Sha256 = local.Sha256 };
            bool takeTheirs = (decisions ?? []).Any(decision =>
                decision.Choice == SyncChoice.Theirs &&
                SyncPath.Comparer.Equals(decision.RelativePath, entry.RelativePath) &&
                decision.TheirSha256.Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase) &&
                decision.MySha256.Equals(local.Sha256, StringComparison.OrdinalIgnoreCase));
            if (takeTheirs)
            {
                // The person's answer: theirs here, this one kept aside - never thrown away.
                actions.Add(new SyncTransferAction(SyncTransferKind.Replace, entry, local, KeepPrevious: true));
            }
            else if (version.Knows(local.Sha256))
            {
                // Came after this version - unless a person overrode this one by decision on the
                // other machine: then it is kept aside here too.
                bool overridden = version.Overrode.Contains(local.Sha256, StringComparer.OrdinalIgnoreCase);
                actions.Add(new SyncTransferAction(SyncTransferKind.Replace, entry, local, KeepPrevious: overridden));
            }
            else if (mineVersion.Knows(entry.Sha256))
            {
                ahead++;
            }
            else if (mineVersion.History.Intersect(version.History, StringComparer.OrdinalIgnoreCase).Any())
            {
                if (!ledger.HasKept(entry.RelativePath, entry.Sha256))
                    actions.Add(new SyncTransferAction(SyncTransferKind.KeepBoth, entry, local));
            }
            else
            {
                unrelated++;
            }
        }
        return new SyncTransferPlan(actions, ahead, unrelated, deleted, notCarried);
    }
}

public enum SyncApplyOutcome
{
    Applied,

    /// <summary>The file here is open in a program, or changed since the index: tried again next pass.</summary>
    Waiting,
}

public sealed record SyncApplyResult(SyncApplyOutcome Outcome, string RelativePath, int ChunksFetched, int ChunksReused, string Reason = "");

/// <summary>
/// Puts a peer's version of one file on this disk.
///
/// 🔴 HALF A FILE IS NEVER SEEN (plan §4.6.3). The pieces are written into the folder's own
/// hidden «.erks-sync\tmp» - the same volume - every piece and then the whole file checked
/// against its hash, and only then is the name switched in one move. A piece this machine
/// already holds anywhere in the folder is read from disk instead of the network: a gigabyte
/// model with one changed part moves one part.
/// </summary>
public static class SyncFileAssembler
{
    public static async Task<SyncApplyResult> ApplyAsync(
        string root,
        SyncTransferAction action,
        SyncIndex mine,
        Func<int, CancellationToken, Task<byte[]>> fetchChunk,
        DateTimeOffset now,
        CancellationToken cancellationToken,
        Action<long>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(action);
        SyncIndexEntry theirs = action.Theirs;
        string relative = action.Kind == SyncTransferKind.KeepBoth
            ? SyncExclusions.ConflictCopyPath(theirs.RelativePath, theirs.Version?.Writer ?? "", now)
            : theirs.RelativePath;
        string target = Path.GetFullPath(Path.Combine(root, relative));
        if (!SyncPath.IsSameOrInside(root, target) || SyncPath.Same(root, target))
            throw new InvalidDataException("A path outside the sync folder: " + relative);

        if (Hold(action, target) is { } held)
            return new SyncApplyResult(SyncApplyOutcome.Waiting, theirs.RelativePath, 0, 0, held);

        string scratch = Path.Combine(root, SyncExclusions.MarkerDirectoryName, "tmp");
        Directory.CreateDirectory(scratch);
        string temporary = Path.Combine(scratch, Guid.NewGuid().ToString("N") + ".part");
        int fetched = 0, reused = 0;
        Dictionary<string, (string Path, long Offset)> local = LocalPieces(root, mine);
        try
        {
            using (var whole = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, useAsync: true))
            {
                for (int index = 0; index < theirs.ChunkSha256.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string expected = theirs.ChunkSha256[index];
                    long length = Math.Min(SyncIndex.ChunkSize, theirs.Size - (long)index * SyncIndex.ChunkSize);
                    byte[]? piece = local.TryGetValue(expected, out var source)
                        ? ReadLocal(source.Path, source.Offset, (int)length, expected)
                        : null;
                    if (piece is not null)
                    {
                        reused++;
                    }
                    else
                    {
                        piece = await fetchChunk(index, cancellationToken);
                        if (!Convert.ToHexStringLower(SHA256.HashData(piece)).Equals(expected, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException($"Piece {index} of {theirs.RelativePath} did not match its hash.");
                        fetched++;
                    }
                    whole.AppendData(piece);
                    await output.WriteAsync(piece, cancellationToken);
                    progress?.Invoke(piece.Length);
                }
                if (!Convert.ToHexStringLower(whole.GetHashAndReset()).Equals(theirs.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"{theirs.RelativePath} did not match its hash after assembly.");
            }

            // Checked again at the last moment: a file opened or saved while the pieces came.
            if (Hold(action, target) is { } late)
                return new SyncApplyResult(SyncApplyOutcome.Waiting, theirs.RelativePath, fetched, reused, late);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (action.KeepPrevious && File.Exists(target))
            {
                string kept = Path.Combine(root, SyncExclusions.MarkerDirectoryName, "replaced", now.ToLocalTime().ToString("yyyyMMdd-HHmmss"), relative);
                Directory.CreateDirectory(Path.GetDirectoryName(kept)!);
                File.Copy(target, kept, overwrite: true);
            }
            File.Move(temporary, target, overwrite: action.Kind == SyncTransferKind.Replace);
            return new SyncApplyResult(SyncApplyOutcome.Applied, theirs.RelativePath, fetched, reused);
        }
        finally
        {
            try
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>Why the file must not be written now, or null when it may.</summary>
    private static string? Hold(SyncTransferAction action, string target)
    {
        if (action.Kind != SyncTransferKind.Replace)
            return File.Exists(target) ? "appeared_here" : null;
        if (action.Mine is not { } mine)
            return "changed_here";
        try
        {
            var info = new FileInfo(target);
            if (!info.Exists || info.Length != mine.Size || info.LastWriteTimeUtc != mine.LastWriteUtc)
                return "changed_here";
            // Opened in AutoCAD or Revit: never pulled from under the program.
            using var probe = new FileStream(target, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return "open_here";
        }
    }

    private static Dictionary<string, (string Path, long Offset)> LocalPieces(string root, SyncIndex mine)
    {
        var pieces = new Dictionary<string, (string, long)>(StringComparer.OrdinalIgnoreCase);
        foreach (SyncIndexEntry entry in mine.Entries)
        {
            for (int index = 0; index < entry.ChunkSha256.Count; index++)
                pieces.TryAdd(entry.ChunkSha256[index], (Path.Combine(root, entry.RelativePath), (long)index * SyncIndex.ChunkSize));
        }
        return pieces;
    }

    /// <summary>A piece read from a local file, or null when that file no longer holds it.</summary>
    private static byte[]? ReadLocal(string path, long offset, int length, string expected)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length < offset + length)
                return null;
            stream.Position = offset;
            byte[] piece = new byte[length];
            stream.ReadExactly(piece);
            return Convert.ToHexStringLower(SHA256.HashData(piece)).Equals(expected, StringComparison.OrdinalIgnoreCase) ? piece : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

/// <summary>
/// The speed of a transfer as it runs (owner, 2026-10-24: «синк ямар хурдаар хийгдэж байгаа нь
/// харагддаг болгоорой»): bytes over at least a second, smoothed so one slow piece does not swing it.
/// </summary>
public sealed class SyncTransferRate
{
    private long _sampleBytes = -1;
    private DateTimeOffset _sampleAt;

    public double BytesPerSecond { get; private set; }

    /// <summary><paramref name="totalDone"/>: every byte of this transfer so far, at <paramref name="at"/>.</summary>
    public void Add(long totalDone, DateTimeOffset at)
    {
        if (_sampleBytes < 0)
        {
            (_sampleBytes, _sampleAt) = (totalDone, at);
            return;
        }
        double seconds = (at - _sampleAt).TotalSeconds;
        if (seconds < 1)
            return;
        double now = Math.Max(0, totalDone - _sampleBytes) / seconds;
        BytesPerSecond = BytesPerSecond == 0 ? now : BytesPerSecond * 0.5 + now * 0.5;
        (_sampleBytes, _sampleAt) = (totalDone, at);
    }

    /// <summary>Seconds until <paramref name="total"/> at this speed, or null while there is no speed yet.</summary>
    public double? SecondsLeft(long total, long done) =>
        BytesPerSecond <= 0 ? null : Math.Max(0, total - done) / BytesPerSecond;
}
