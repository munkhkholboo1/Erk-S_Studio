using System.IO;
using System.Windows;
using System.Windows.Controls;
using ErkS.Platform.Core;
using ErkS.Sync.Core;
using Microsoft.Win32;

namespace ErkS.Studio;

/// <summary>
/// The project page's «Erk-S Sync» section and the bridge's sync actions. Every button and
/// every action calls <see cref="StudioSyncFolders"/> - one function each, no copies.
/// </summary>
internal sealed partial class ShellView
{
    private readonly StackPanel projectSyncPanel = new();
    private readonly TextBlock projectSyncSummary = new()
    {
        Foreground = StudioTheme.MutedTextBrush,
        TextWrapping = TextWrapping.Wrap,
    };

    private StudioSyncFolders? syncFolders;

    private StudioSyncFolders SyncFolders => syncFolders ??= new StudioSyncFolders(account);

    private UIElement BuildProjectSyncSection()
    {
        var section = new StackPanel { Margin = new Thickness(0, 4, 0, 16) };
        var heading = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        Button make = StudioWidgets.CreateButton("Синк хавтас болгох…");
        make.Click += async (_, _) => await MakeSyncFolderFromPickerAsync();
        Button join = StudioWidgets.CreateButton("Хавтсанд холбох…");
        join.Margin = new Thickness(6, 0, 0, 0);
        join.Click += async (_, _) => await JoinSyncFolderFromPickerAsync();
        Button refresh = StudioWidgets.CreateButton("Шинэчлэх");
        refresh.Margin = new Thickness(6, 0, 0, 0);
        refresh.Click += (_, _) =>
        {
            SyncTrayProcess.Poke();
            RefreshProjectSyncSection();
        };
        buttons.Children.Add(make);
        buttons.Children.Add(join);
        buttons.Children.Add(refresh);
        DockPanel.SetDock(buttons, Dock.Right);
        heading.Children.Add(buttons);
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(new TextBlock
        {
            Text = "Erk-S Sync",
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = StudioTheme.TextBrush,
        });
        words.Children.Add(projectSyncSummary);
        heading.Children.Add(words);
        section.Children.Add(heading);
        section.Children.Add(projectSyncPanel);
        return section;
    }

    /// <summary>The open project's sync folder as THIS machine's SYN last saw it.</summary>
    private void RefreshProjectSyncSection()
    {
        projectSyncPanel.Children.Clear();
        if (!state.HasOpenProject)
        {
            projectSyncSummary.Text = "";
            return;
        }

        string projectId = state.Project.Cloud?.ServerProjectId ?? "";
        SyncState? sync = SyncTrayProcess.State();
        SyncFolderState? folder = sync?.Folders.FirstOrDefault(entry =>
            entry.ProjectId.Equals(projectId, StringComparison.OrdinalIgnoreCase));
        if (!SyncTrayProcess.IsRunning)
        {
            projectSyncSummary.Text = "Erk-S Sync ажиллахгүй байна — «Шинэчлэх» дарвал эхэлнэ.";
            return;
        }
        if (folder is null)
        {
            projectSyncSummary.Text = sync?.ServerStatus is "ok" or null
                ? "Энэ төсөлд энэ машин синк хавтсаар холбогдоогүй байна."
                : "Erk-S Sync: " + sync.ServerMessage;
            return;
        }

        projectSyncSummary.Text = folder.PresentHere
            ? $"{folder.CanonicalPath} · {folder.FileCount} файл · шалгасан {folder.IndexedAtUtc?.ToLocalTime():yyyy-MM-dd HH:mm}"
            : $"{folder.CanonicalPath} · {folder.LastError}";
        if (folder.Peers.Count == 0)
        {
            projectSyncPanel.Children.Add(Line("Өөр холбогдсон машин алга."));
            return;
        }

        SyncHome home = SyncHome.Default();
        SyncIndex? mine = SyncIndexStore.TryLoad(home.IndexPath(folder.FolderId));
        foreach (SyncPeerState peer in folder.Peers)
        {
            string who = string.IsNullOrWhiteSpace(peer.AccountEmail) ? peer.DeviceKeyId[..Math.Min(8, peer.DeviceKeyId.Length)] : peer.AccountEmail;
            string compared = peer.ExchangedAtUtc is null
                ? "индекс ирээгүй" + (peer.LastError.Length > 0 ? $" ({peer.LastError})" : "")
                : $"ижил {peer.SameCount} · зөрүүтэй {peer.DifferentCount} · зөвхөн энд {peer.OnlyHereCount} · зөвхөн тэнд {peer.OnlyThereCount}";
            projectSyncPanel.Children.Add(Line($"{who}: {compared}", bold: true));

            SyncIndex? theirs = SyncIndexStore.TryLoad(home.PeerIndexPath(folder.FolderId, peer.DeviceKeyId));
            if (mine is null || theirs is null)
                continue;
            // 🔴 Shown, never resolved: the owner decides what a differing file should be.
            SyncIndexDifference difference = SyncIndexComparison.Compare(mine, theirs);
            foreach (SyncIndexEntry entry in difference.Different.Take(50))
                projectSyncPanel.Children.Add(Line($"   зөрүүтэй · {entry.RelativePath}"));
            foreach (SyncIndexEntry entry in difference.OnlyHere.Take(20))
                projectSyncPanel.Children.Add(Line($"   зөвхөн энд · {entry.RelativePath}"));
            foreach (SyncIndexEntry entry in difference.OnlyThere.Take(20))
                projectSyncPanel.Children.Add(Line($"   зөвхөн тэнд · {entry.RelativePath}"));
            int hidden = Math.Max(0, difference.Different.Count - 50) + Math.Max(0, difference.OnlyHere.Count - 20) + Math.Max(0, difference.OnlyThere.Count - 20);
            if (hidden > 0)
                projectSyncPanel.Children.Add(Line($"   … бас {hidden} файл"));
        }

        static TextBlock Line(string text, bool bold = false) => new()
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 2),
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = bold ? StudioTheme.TextBrush : StudioTheme.MutedTextBrush,
        };
    }

    private async Task MakeSyncFolderFromPickerAsync()
    {
        var picker = new OpenFolderDialog { Title = "Erk-S Sync хавтас болгох төслийн хавтас" };
        if (picker.ShowDialog() != true)
            return;
        await ShowSyncDialogAsync("make", picker.FolderName);
    }

    private async Task JoinSyncFolderFromPickerAsync()
    {
        var picker = new OpenFolderDialog { Title = "Erk-S Sync хавтсанд холбох хавтас (яг ижил замтай)" };
        if (picker.ShowDialog() != true)
            return;
        await ShowSyncDialogAsync("join", picker.FolderName);
    }

    /// <summary>The person chooses in a window of its own; the work is StudioSyncFolders'.</summary>
    private async Task ShowSyncDialogAsync(string verb, string folderPath)
    {
        Window.GetWindow(Root)?.Activate();
        SyncFolderDialog dialog;
        if (verb == "make")
        {
            List<ProjectCatalogItem> projects = new LocalProjectCatalog().ListProjects()
                .Where(item => MaySeeProject(item.ProjectId))
                .OrderBy(item => item.ProjectCode, StringComparer.OrdinalIgnoreCase)
                .ToList();
            dialog = SyncFolderDialog.ForMake(folderPath, projects, state.ProjectPath, project => MakeSyncFolderAsync(folderPath, project.ProjectPath));
        }
        else
        {
            IReadOnlyList<SyncServerFolder> folders;
            try
            {
                folders = await SyncFolders.MineAsync();
            }
            catch (Exception exception) when (exception is SyncServerException or System.Net.Http.HttpRequestException or TaskCanceledException or StudioAccountException)
            {
                SetStatus("Синк хавтсуудыг авч чадсангүй: " + exception.Message);
                return;
            }
            dialog = SyncFolderDialog.ForJoin(folderPath, folders, folder => SyncFolders.JoinAsync(folderPath, folder.FolderId));
        }
        dialog.Owner = Window.GetWindow(Root);
        dialog.ShowDialog();
        if (dialog.Outcome is { } outcome)
            SetStatus(outcome.Message);
        RefreshProjectSyncSection();
    }

    /// <summary>The project at <paramref name="projectPath"/> - the open one as held, any other read from disk.</summary>
    private Task<StudioSyncOutcome> MakeSyncFolderAsync(string folderPath, string projectPath)
    {
        ProjectWorkspace project;
        try
        {
            string file = projectPath.EndsWith(".erksproject", StringComparison.OrdinalIgnoreCase)
                ? projectPath
                : Path.Combine(projectPath, "project.erksproject");
            project = state.HasOpenProject && string.Equals(Path.GetFullPath(state.ProjectPath ?? ""), Path.GetFullPath(file), StringComparison.OrdinalIgnoreCase)
                ? state.Project
                : ProjectWorkspaceStore.Load(file);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return Task.FromResult(StudioSyncOutcome.Refused("sync_project_unreadable", "Төслийг уншиж чадсангүй: " + exception.Message));
        }
        return SyncFolders.MakeAsync(folderPath, project);
    }

    // ---- bridge --------------------------------------------------------------------------

    /// <summary>
    /// sync-make / sync-join / sync-unlink. With the deciding argument (project= / folder=)
    /// an agent runs it straight through; without it - an Explorer verb - the person chooses
    /// in Studio's window, which the bridge's quiet mode does not answer for them.
    /// </summary>
    private async Task<StudioBridgeResponse> BridgeSyncFolderAsync(StudioBridgeRequest request, string verb)
    {
        string? path = request.Arg("path");
        if (string.IsNullOrWhiteSpace(path))
            return StudioBridgeProtocol.Refuse(request.Id, "sync_path_missing", "path=<хавтас> заана уу.");

        StudioSyncOutcome? outcome = null;
        switch (verb)
        {
            case "unlink":
                outcome = await SyncFolders.UnlinkAsync(path);
                break;
            case "make" when request.Arg("project") is { } code:
                string? projectPath = new LocalProjectCatalog().ListProjects()
                    .FirstOrDefault(item => item.ProjectCode.Equals(code, StringComparison.OrdinalIgnoreCase))?.ProjectPath;
                outcome = projectPath is null
                    ? StudioSyncOutcome.Refused("project_not_found", $"«{code}» төсөл энэ төхөөрөмж дээр алга.")
                    : await MakeSyncFolderAsync(path, projectPath);
                break;
            case "join" when request.Arg("folder") is { } folderId:
                outcome = await SyncFolders.JoinAsync(path, folderId);
                break;
            case "make":
            case "join":
                _ = Root.Dispatcher.BeginInvoke(new Action(async () => await ShowSyncDialogAsync(verb, path)));
                return StudioBridgeProtocol.Accept(request.Id, new { interactive = true }, "Studio-д сонголтоо хийнэ үү.");
        }

        RefreshProjectSyncSection();
        return outcome is { Ok: true }
            ? StudioBridgeProtocol.Accept(request.Id, outcome.Folder, outcome.Message)
            : StudioBridgeProtocol.Refuse(request.Id, outcome?.Code ?? "sync_failed", outcome?.Message ?? "");
    }

    /// <summary>
    /// sync-status: this machine's SYN - running or not, the server half, and per folder the
    /// counts the Twin shows (files, peers, differing files). No file names (Master, 2026-10-23).
    /// </summary>
    private StudioBridgeResponse BridgeSyncStatus(StudioBridgeRequest request)
    {
        SyncState? sync = SyncTrayProcess.State();
        return StudioBridgeProtocol.Accept(request.Id, new
        {
            running = SyncTrayProcess.IsRunning,
            processId = sync?.ProcessId ?? 0,
            updatedAtUtc = sync?.UpdatedAtUtc,
            deviceKeyId = sync?.DeviceKeyId ?? "",
            serverStatus = sync?.ServerStatus ?? "not_started",
            serverMessage = sync?.ServerMessage ?? "",
            lanPort = sync?.LanPort ?? 0,
            folders = (sync?.Folders ?? []).Select(folder => new
            {
                folder.FolderId,
                folder.ProjectId,
                folder.ProjectCode,
                folder.CanonicalPath,
                folder.PresentHere,
                folder.FileCount,
                folder.TotalBytes,
                folder.IndexedAtUtc,
                folder.LastError,
                peerCount = folder.Peers.Count,
                differingFileCount = folder.Peers.Sum(peer => peer.DifferentCount),
                peers = folder.Peers.Select(peer => new
                {
                    peer.DeviceKeyId,
                    peer.ServerLastSeenAtUtc,
                    peer.ExchangedAtUtc,
                    peer.SameCount,
                    peer.DifferentCount,
                    peer.OnlyHereCount,
                    peer.OnlyThereCount,
                    peer.LastError,
                }),
            }),
        });
    }

    /// <summary>sync-folders: the server's sync folders of this person's projects, and whether each is on this disk.</summary>
    private async Task<StudioBridgeResponse> BridgeSyncFoldersAsync(StudioBridgeRequest request)
    {
        try
        {
            IReadOnlyList<SyncServerFolder> folders = await SyncFolders.MineAsync();
            return StudioBridgeProtocol.Accept(request.Id, folders.Select(folder => new
            {
                folder.FolderId,
                folder.ProjectId,
                folder.ProjectCode,
                folder.ProjectName,
                folder.CanonicalPath,
                folder.CreatedAtUtc,
                markedHere = SyncFolderMarker.TryRead(folder.CanonicalPath)?.FolderId.Equals(folder.FolderId, StringComparison.OrdinalIgnoreCase) == true,
            }).ToList());
        }
        catch (Exception exception) when (exception is SyncServerException or System.Net.Http.HttpRequestException or TaskCanceledException or StudioAccountException)
        {
            return StudioBridgeProtocol.Refuse(request.Id, exception is SyncServerException server ? server.Code : "sync_server_unreachable", exception.Message);
        }
    }
}
