using System.Diagnostics;
using System.IO;
using System.Net.Http;
using ErkS.Platform.Core;
using ErkS.Sync.Core;

namespace ErkS.Studio;

/// <summary>What a sync-folder action did: a code a caller can test and a sentence for a person.</summary>
internal sealed record StudioSyncOutcome(bool Ok, string Code, string Message, SyncFolderIdentity? Folder = null)
{
    public static StudioSyncOutcome Refused(string code, string message) => new(false, code, message);
}

/// <summary>
/// The person's half of Erk-S Sync: registering a project folder, joining one, unlinking,
/// unregistering - each a decision on a project, so each runs on the signed-in person's Cloud
/// ERA rights (SRV contract sync-folder). Studio's buttons, the Explorer verbs (through
/// ErkS.Sync.exe) and the bridge all call these and nothing else.
/// </summary>
internal sealed class StudioSyncFolders(StudioAccountService account)
{
    /// <summary>«Erk-S Sync фолдер болгох»: <paramref name="folderPath"/> becomes the project's sync folder.</summary>
    public async Task<StudioSyncOutcome> MakeAsync(string folderPath, ProjectWorkspace project, CancellationToken cancellationToken = default)
    {
        if (SyncFolderEligibility.CheckMake(folderPath) is { } refusal)
            return StudioSyncOutcome.Refused(refusal.Code, refusal.Message);
        string projectId = (project.Cloud?.ServerProjectId ?? "").Trim();
        if (projectId.Length == 0)
            return StudioSyncOutcome.Refused("sync_project_not_cloud", "Төсөл Cloud-д бүртгэгдээгүй тул синк хавтас болгох боломжгүй.");

        try
        {
            SyncServerFolder folder = await WithDeviceAsync(
                (client, token, deviceKeyId) => client.RegisterFolderAsync(token, projectId, SyncPath.Canonical(folderPath), deviceKeyId, cancellationToken),
                cancellationToken);
            SyncFolderIdentity identity = Mark(folderPath, folder);
            SyncTrayProcess.Poke();
            return new StudioSyncOutcome(true, "", $"«{folder.CanonicalPath}» нь «{folder.ProjectCode}» төслийн Erk-S Sync хавтас боллоо.", identity);
        }
        catch (SyncServerException exception)
        {
            return StudioSyncOutcome.Refused(exception.Code, exception.Message);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or StudioAccountException or IOException or UnauthorizedAccessException)
        {
            return StudioSyncOutcome.Refused("sync_failed", "Синк хавтас болгож чадсангүй: " + exception.Message);
        }
    }

    /// <summary>The project sync folders of every project this person is on the team of.</summary>
    public async Task<IReadOnlyList<SyncServerFolder>> MineAsync(CancellationToken cancellationToken = default)
    {
        (SyncServerClient client, string token) = await account.SyncServerAsync(cancellationToken);
        return await client.MyFoldersAsync(token, cancellationToken);
    }

    /// <summary>
    /// «Erk-S Sync фолдерт холбох»: this machine's folder at <paramref name="folderPath"/> joins
    /// the sync folder <paramref name="folderId"/> - or, when none is named, the one registered
    /// at exactly this path.
    /// </summary>
    public async Task<StudioSyncOutcome> JoinAsync(string folderPath, string? folderId, CancellationToken cancellationToken = default)
    {
        try
        {
            IReadOnlyList<SyncServerFolder> mine = await MineAsync(cancellationToken);
            SyncServerFolder? chosen = string.IsNullOrWhiteSpace(folderId)
                ? mine.FirstOrDefault(folder => SyncPath.Same(folder.CanonicalPath, folderPath))
                : mine.FirstOrDefault(folder => folder.FolderId.Equals(folderId.Trim(), StringComparison.OrdinalIgnoreCase));
            if (chosen is null)
            {
                string known = mine.Count == 0
                    ? "Таны багт байгаа төсөлд синк хавтас бүртгэгдээгүй байна."
                    : "Бүртгэлтэй синк хавтсууд: " + string.Join("; ", mine.Select(folder => $"{folder.ProjectCode} — {folder.CanonicalPath}"));
                return StudioSyncOutcome.Refused("sync_folder_not_chosen", "Энэ замтай синк хавтас олдсонгүй. " + known);
            }
            if (SyncFolderEligibility.CheckJoin(folderPath, chosen.CanonicalPath) is { } refusal)
                return StudioSyncOutcome.Refused(refusal.Code, refusal.Message);
            if (SyncFolderMarker.TryRead(folderPath) is { } existing &&
                !existing.FolderId.Equals(chosen.FolderId, StringComparison.OrdinalIgnoreCase))
            {
                return StudioSyncOutcome.Refused(
                    SyncFolderEligibility.AlreadySyncFolderCode,
                    $"Энэ хавтас өөр («{existing.ProjectCode}») синк хавтасных байна.");
            }

            await WithDeviceAsync(
                (client, token, deviceKeyId) => client.AttachDeviceAsync(token, chosen.FolderId, deviceKeyId, cancellationToken),
                cancellationToken);
            SyncFolderIdentity identity = Mark(folderPath, chosen);
            SyncTrayProcess.Poke();
            return new StudioSyncOutcome(true, "", $"Энэ машин «{chosen.ProjectCode}» төслийн Erk-S Sync хавтсанд холбогдлоо.", identity);
        }
        catch (SyncServerException exception)
        {
            return StudioSyncOutcome.Refused(exception.Code, exception.Message);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or StudioAccountException or IOException or UnauthorizedAccessException)
        {
            return StudioSyncOutcome.Refused("sync_failed", "Холбож чадсангүй: " + exception.Message);
        }
    }

    /// <summary>
    /// «Холболт салгах»: this machine leaves the folder. The project's files are not touched;
    /// only the hidden marker goes, so the folder is no longer a sync folder HERE.
    /// </summary>
    public async Task<StudioSyncOutcome> UnlinkAsync(string folderPath, CancellationToken cancellationToken = default)
    {
        string? root = SyncFolderMarker.FindEnclosing(folderPath);
        SyncFolderIdentity? identity = root is null ? null : SyncFolderMarker.TryRead(root);
        if (root is null || identity is null)
            return StudioSyncOutcome.Refused("sync_folder_not_marked", "Энэ хавтас Erk-S Sync хавтас биш байна.");
        try
        {
            try
            {
                await WithDeviceAsync<object?>(
                    async (client, token, deviceKeyId) =>
                    {
                        await client.DetachDeviceAsync(token, identity.FolderId, deviceKeyId, cancellationToken);
                        return null;
                    },
                    cancellationToken);
            }
            catch (SyncServerException exception) when (exception.Code is "sync_device_not_attached" or "sync_folder_not_found")
            {
            }
            SyncFolderMarker.Remove(root, identity.FolderId);
            SyncTrayProcess.Poke();
            return new StudioSyncOutcome(true, "", $"Энэ машин «{identity.ProjectCode}» синк хавтаснаас салгагдлаа. Файлууд хэвээр.", identity);
        }
        catch (SyncServerException exception)
        {
            return StudioSyncOutcome.Refused(exception.Code, exception.Message);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or StudioAccountException or IOException or UnauthorizedAccessException)
        {
            return StudioSyncOutcome.Refused("sync_failed", "Салгаж чадсангүй: " + exception.Message);
        }
    }

    /// <summary>
    /// «Синк хавтсыг цуцлах»: the project stops having a sync folder - on the server for the
    /// whole team (the server decides who may), and this machine's marker goes with it. The
    /// files stay where they are on every machine.
    /// </summary>
    public async Task<StudioSyncOutcome> UnregisterAsync(ProjectWorkspace project, CancellationToken cancellationToken = default)
    {
        string projectId = (project.Cloud?.ServerProjectId ?? "").Trim();
        if (projectId.Length == 0)
            return StudioSyncOutcome.Refused("sync_project_not_cloud", "Төсөл Cloud-д бүртгэгдээгүй байна.");
        try
        {
            SyncServerFolder? folder = (await MineAsync(cancellationToken))
                .FirstOrDefault(entry => entry.ProjectId.Equals(projectId, StringComparison.OrdinalIgnoreCase));
            if (folder is null)
                return StudioSyncOutcome.Refused("sync_folder_not_found", "Энэ төсөлд синк хавтас бүртгэгдээгүй байна.");
            (SyncServerClient client, string token) = await account.SyncServerAsync(cancellationToken);
            await client.UnregisterFolderAsync(token, projectId, cancellationToken);
            if (Directory.Exists(folder.CanonicalPath))
                SyncFolderMarker.Remove(folder.CanonicalPath, folder.FolderId);
            SyncTrayProcess.Poke();
            return new StudioSyncOutcome(true, "", $"«{folder.ProjectCode}» төслийн синк хавтас цуцлагдлаа ({folder.CanonicalPath}). Файлууд хэвээр.");
        }
        catch (SyncServerException exception)
        {
            return StudioSyncOutcome.Refused(exception.Code, exception.Message);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or StudioAccountException or IOException or UnauthorizedAccessException)
        {
            return StudioSyncOutcome.Refused("sync_failed", "Цуцалж чадсангүй: " + exception.Message);
        }
    }

    /// <summary>
    /// Runs a person's call with this machine's device key id; a key the server has not seen
    /// for this account is registered once and the call retried.
    /// </summary>
    private async Task<T> WithDeviceAsync<T>(Func<SyncServerClient, string, string, Task<T>> call, CancellationToken cancellationToken)
    {
        string deviceKeyId = StudioDeviceKeyStore.Fingerprint();
        (SyncServerClient client, string token) = await account.SyncServerAsync(cancellationToken);
        try
        {
            return await call(client, token, deviceKeyId);
        }
        catch (SyncServerException exception) when (exception.Code == "sync_device_unknown")
        {
            await account.RegisterDeviceKeyAsync(cancellationToken);
            return await call(client, token, deviceKeyId);
        }
    }

    private static SyncFolderIdentity Mark(string folderPath, SyncServerFolder folder)
    {
        var identity = new SyncFolderIdentity
        {
            FolderId = folder.FolderId,
            ProjectCode = folder.ProjectCode,
            ProjectId = folder.ProjectId,
            CanonicalPath = folder.CanonicalPath,
            CreatedAtUtc = folder.CreatedAtUtc,
        };
        SyncFolderMarker.Write(folderPath, identity);
        return identity;
    }
}

/// <summary>The tray process from Studio's side: started when absent, asked for a pass.</summary>
internal static class SyncTrayProcess
{
    public static bool IsRunning => Process.GetProcessesByName("ErkS.Sync").Length > 0;

    public static void Poke()
    {
        if (SyncProtocol.TrySend(new SyncPipeRequest("rescan", ""), timeoutMilliseconds: 800) is not null)
            return;
        string tray = Path.Combine(AppContext.BaseDirectory, "ErkS.Sync.exe");
        if (File.Exists(tray))
        {
            try
            {
                Process.Start(new ProcessStartInfo(tray) { UseShellExecute = true, WorkingDirectory = AppContext.BaseDirectory });
            }
            catch (System.ComponentModel.Win32Exception)
            {
            }
        }
    }

    public static SyncState? State() => SyncStateStore.TryLoad(SyncHome.Default());
}
