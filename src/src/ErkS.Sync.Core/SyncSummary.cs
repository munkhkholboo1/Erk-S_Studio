namespace ErkS.Sync.Core;

public enum SyncProjectStatus
{
    /// <summary>✓ Everything this machine needs is here.</summary>
    Complete,

    /// <summary>↻ Files are coming (or going) now.</summary>
    Syncing,

    /// <summary>⚠ Something waits for a person: a differing file, a shared-file error, a folder error.</summary>
    Attention,

    /// <summary>⏸ No server: nothing moves until it answers.</summary>
    NoServer,
}

/// <param name="Percent">What this machine still needs, over online members only.</param>
/// <param name="Attention">Things a person should look at (differing files, errors).</param>
/// <param name="LastSyncedAtUtc">When something last arrived here, or the folder was last looked at.</param>
public sealed record SyncProjectSummary(
    string FolderId,
    string ProjectCode,
    string ProjectName,
    SyncProjectStatus Status,
    double Percent,
    double BytesPerSecond,
    double? SecondsLeft,
    int Attention,
    int MembersOnline,
    int MembersOffline,
    DateTimeOffset? LastSyncedAtUtc)
{
    public string Mark => Status switch
    {
        SyncProjectStatus.Complete => "✓",
        SyncProjectStatus.Syncing => "↻",
        SyncProjectStatus.Attention => "⚠",
        _ => "⏸",
    };

    /// <summary>«↻ 45% · 12 МБ/с · ~3 мин», «✓ Бүрэн», «⚠ 2 анхаарах», «⏸ Сервергүй».</summary>
    public string StateWords => Status switch
    {
        SyncProjectStatus.Complete => "✓ Бүрэн",
        SyncProjectStatus.Syncing => $"↻ {Percent:0.#}%" + SyncSummary.Rate(BytesPerSecond) + SyncSummary.Left(SecondsLeft),
        SyncProjectStatus.Attention => $"⚠ {Attention} анхаарах",
        _ => "⏸ Сервергүй",
    };
}

/// <summary>
/// The short account first, the details on asking (owner, 2026-10-26: «Erk-S Sync олон төсөл синк
/// хийх шаардлагатай болох тул синк дээрх мэдээллийг цомхон ойлгомжтой хураангуй болгоорой.
/// дэлгэрэнгүй харж болно. гэхдээ эхлээд хураангуйгаар хардаг байна»). One line per project; the
/// tray, the status window and Studio's «Erk-S Sync» section all say it from here, so they agree.
/// Member names and file counts are details, never the summary.
/// </summary>
public static class SyncSummary
{
    public static IReadOnlyList<SyncProjectSummary> Projects(SyncState state, DateTimeOffset? now = null)
    {
        DateTimeOffset at = now ?? DateTimeOffset.UtcNow;
        bool server = state.ServerStatus is "ok" or "";
        return state.Folders.Select(folder => Of(folder, server, at)).ToList();
    }

    /// <summary>One project's line.</summary>
    public static SyncProjectSummary Of(SyncFolderState folder, bool server = true, DateTimeOffset? now = null)
    {
        DateTimeOffset at = now ?? DateTimeOffset.UtcNow;
        {
            SyncPeerState[] online = folder.Peers.Where(peer => SyncInStep.IsOnline(peer, at)).ToArray();
            double percent = SyncInStep.Of(folder.Peers, at).Percent ?? 100;
            SyncPeerState? moving = folder.Peers.FirstOrDefault(peer => peer.Transferring);
            double rate = moving?.TransferBytesPerSecond ?? (folder.Uploading ? folder.UploadBytesPerSecond : 0);
            double? left = moving?.TransferSecondsLeft ?? (folder.Uploading ? folder.UploadSecondsLeft : null);
            int attention = folder.ConflictCount
                + (folder.LastError.Length > 0 ? 1 : 0)
                + folder.Collabs.Count(collab => collab.LastError.Length > 0 || collab.CentralEdited || collab.Writers.Any(writer => writer.Gap))
                + (folder.RedactionsUnsent > 0 ? 1 : 0);
            SyncProjectStatus status = !server ? SyncProjectStatus.NoServer
                : moving is not null || folder.Uploading || percent < 100 ? SyncProjectStatus.Syncing
                : attention > 0 ? SyncProjectStatus.Attention
                : SyncProjectStatus.Complete;
            DateTimeOffset? last = folder.Peers.Select(peer => peer.LastReceivedAtUtc).Where(value => value is not null).DefaultIfEmpty(folder.IndexedAtUtc).Max();
            return new SyncProjectSummary(
                folder.FolderId,
                folder.ProjectCode,
                folder.ProjectName,
                status,
                percent,
                rate,
                left,
                attention,
                online.Length,
                folder.Peers.Count - online.Length,
                last ?? folder.IndexedAtUtc);
        }
    }

    /// <summary>
    /// The one line above all projects and the tray tooltip: «3 төсөл · бүгд бүрэн»,
    /// «3 төсөл · 1 синк хийж байна 45% · ~3 мин · ⚠ 1 анхаарах», «Сервергүй».
    /// </summary>
    public static string Line(IReadOnlyList<SyncProjectSummary> projects, string serverStatus = "ok")
    {
        if (projects.Count == 0)
            return "Синк хавтас алга";
        if (serverStatus is not ("ok" or ""))
            return $"{projects.Count} төсөл · ⏸ сервертэй холбогдоогүй";
        SyncProjectSummary[] syncing = projects.Where(project => project.Status == SyncProjectStatus.Syncing).ToArray();
        int attention = projects.Sum(project => project.Attention);
        var parts = new List<string> { $"{projects.Count} төсөл" };
        if (syncing.Length == 0)
        {
            parts.Add(attention == 0 ? "бүгд бүрэн" : $"{projects.Count(project => project.Status == SyncProjectStatus.Complete)} бүрэн");
        }
        else
        {
            double percent = syncing.Min(project => project.Percent);
            double rate = syncing.Sum(project => project.BytesPerSecond);
            double? left = syncing.Select(project => project.SecondsLeft).Where(value => value is not null).DefaultIfEmpty(null).Max();
            parts.Add($"{syncing.Length} синк хийж байна {percent:0.#}%" + Rate(rate) + Left(left));
        }
        if (attention > 0)
            parts.Add($"⚠ {attention} анхаарах");
        return string.Join(" · ", parts);
    }

    internal static string Rate(double bytesPerSecond) =>
        bytesPerSecond <= 0 ? "" : $" · {bytesPerSecond / (1 << 20):0.#} МБ/с";

    internal static string Left(double? seconds) =>
        seconds is not > 0 ? ""
        : seconds < 60 ? $" · ~{seconds:0} сек"
        : seconds < 3600 ? $" · ~{seconds / 60:0} мин"
        : $" · ~{seconds / 3600:0.#} цаг";
}
