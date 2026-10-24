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

    /// <summary>
    /// Follows the tray's state by CHANGE, not by clock: Windows says when «state.json» is
    /// written and only then is it read (owner, 2026-10-24: «өөрчлөлтийг мэдэрч уншдаг болгохгуй
    /// бол дэмий ачаалал үүсгэнэ»). A burst of writes is read once.
    /// </summary>
    private FileSystemWatcher? projectSyncWatcher;

    private int projectSyncRefreshQueued;

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
        Button unregister = StudioWidgets.CreateButton("Синк хавтсыг цуцлах…");
        unregister.Margin = new Thickness(6, 0, 0, 0);
        unregister.Click += async (_, _) => await UnregisterSyncFolderAsync();
        buttons.Children.Add(make);
        buttons.Children.Add(join);
        buttons.Children.Add(unregister);
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
        WatchSyncState(section);
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

        bool transferring = folder.Peers.Any(peer => peer.Transferring);
        double inStep = folder.Peers.Count == 0 ? 100 : folder.Peers.Min(peer => peer.SyncedPercent);
        string stepWord = folder.Peers.Count == 0
            ? ""
            : transferring
                ? " · синк хийж байна"
                : inStep >= 100 ? " · ✓ бүрэн синк хийгдсэн" : $" · {inStep:0.#}% синк хийгдсэн";
        projectSyncSummary.Text = folder.PresentHere
            ? $"{folder.CanonicalPath} · {folder.FileCount} файл, {SyncSize(folder.TotalBytes)}{stepWord} · шалгасан {folder.IndexedAtUtc?.ToLocalTime():yyyy-MM-dd HH:mm}" +
              (folder.ConflictCount > 0 ? $" · ⚠ шийдвэр хүлээж буй зөрчлийн хуулбар {folder.ConflictCount}" : "")
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
                : $"ижил {peer.SameCount} · зөрүүтэй {peer.DifferentCount} · зөвхөн энд {peer.OnlyHereCount} · зөвхөн тэнд {peer.OnlyThereCount}" +
                  (peer.LastReceivedAtUtc is { } received
                      ? $" · сүүлд ирсэн {peer.ReceivedCount} файл, зөрчлийн хуулбар {peer.KeptBothCount} ({received.ToLocalTime():MM-dd HH:mm})"
                      : "") +
                  (peer.WaitingCount > 0 ? $" · нээлттэй тул хүлээгдэж буй {peer.WaitingCount}" : "");
            projectSyncPanel.Children.Add(Line($"{who}: {compared}", bold: true));
            projectSyncPanel.Children.Add(Line(peer.ExchangedAtUtc is null
                ? "   синкийн хувь: тодорхойгүй (индекс ирээгүй)"
                : peer.SyncedPercent >= 100
                    ? $"   ✓ бүрэн синк хийгдсэн · {SyncSize(peer.UnionBytes)}"
                    : $"   синк хийгдсэн {peer.SyncedPercent:0.#}% · {SyncSize(peer.SyncedBytes)} / {SyncSize(peer.UnionBytes)}"));
            if (peer.Transferring)
            {
                double all = peer.TransferTotalBytes == 0 ? 100 : peer.TransferDoneBytes * 100.0 / peer.TransferTotalBytes;
                double one = peer.CurrentFileBytes == 0 ? 100 : peer.CurrentFileDoneBytes * 100.0 / peer.CurrentFileBytes;
                projectSyncPanel.Children.Add(Line($"   хүлээн авч байна: {all:0}% · {SyncSize(peer.TransferDoneBytes)} / {SyncSize(peer.TransferTotalBytes)} · {peer.TransferFilesDone}/{peer.TransferFiles} файл"));
                projectSyncPanel.Children.Add(new ProgressBar { Minimum = 0, Maximum = 100, Value = all, Height = 6, Margin = new Thickness(12, 2, 0, 4) });
                projectSyncPanel.Children.Add(Line($"   одоо: {peer.CurrentFile} — {one:0}% ({SyncSize(peer.CurrentFileDoneBytes)} / {SyncSize(peer.CurrentFileBytes)})"));
            }

            SyncIndex? theirs = SyncIndexStore.TryLoad(home.PeerIndexPath(folder.FolderId, peer.DeviceKeyId));
            if (mine is null || theirs is null)
                continue;
            // 🔴 Shown, never resolved by SYN: a person decides what a differing file should be
            // (decision 15) - for all of them at once or one by one; the replaced one is kept.
            SyncIndexDifference difference = SyncIndexComparison.Compare(mine, theirs);
            IReadOnlyList<SyncDecision> answered = SyncDecisionStore.Load(home.DecisionsPath(folder.FolderId));
            if (difference.Different.Count > 0)
            {
                var all = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
                Button takeAll = StudioWidgets.CreateButton($"Бүгдийг тэндээс авах ({difference.Different.Count})");
                takeAll.Click += (_, _) => DecideSync(folder.FolderId, mine, theirs, SyncChoice.Theirs, null);
                Button keepAll = StudioWidgets.CreateButton("Бүгдэд минийхийг үлдээх");
                keepAll.Margin = new Thickness(6, 0, 0, 0);
                keepAll.Click += (_, _) => DecideSync(folder.FolderId, mine, theirs, SyncChoice.Mine, null);
                all.Children.Add(takeAll);
                all.Children.Add(keepAll);
                projectSyncPanel.Children.Add(all);
            }
            foreach (SyncIndexEntry entry in difference.Different.Take(50))
            {
                string theirSha = theirs.Entries.FirstOrDefault(item => SyncPath.Comparer.Equals(item.RelativePath, entry.RelativePath))?.Sha256 ?? "";
                SyncDecision? decided = answered.LastOrDefault(item =>
                    SyncPath.Comparer.Equals(item.RelativePath, entry.RelativePath) &&
                    item.MySha256.Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase) &&
                    item.TheirSha256.Equals(theirSha, StringComparison.OrdinalIgnoreCase));
                var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1) };
                if (decided is null)
                {
                    var buttons = new StackPanel { Orientation = Orientation.Horizontal };
                    Button take = StudioWidgets.CreateButton("Тэндхийг авах");
                    take.Click += (_, _) => DecideSync(folder.FolderId, mine, theirs, SyncChoice.Theirs, [entry.RelativePath]);
                    Button keep = StudioWidgets.CreateButton("Минийхийг үлдээх");
                    keep.Margin = new Thickness(4, 0, 0, 0);
                    keep.Click += (_, _) => DecideSync(folder.FolderId, mine, theirs, SyncChoice.Mine, [entry.RelativePath]);
                    buttons.Children.Add(take);
                    buttons.Children.Add(keep);
                    DockPanel.SetDock(buttons, Dock.Right);
                    row.Children.Add(buttons);
                }
                string state = decided is null ? "" : decided.Choice == SyncChoice.Theirs ? " — тэндхийг авна" : " — минийх үлдэнэ";
                row.Children.Add(Line($"   зөрүүтэй · {entry.RelativePath}{state}"));
                projectSyncPanel.Children.Add(row);
            }
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

    /// <summary>
    /// A person's answer for differing files, handed to the tray - which takes the other
    /// machine's version (keeping this one in «.erks-sync\replaced») or makes this one the
    /// version the other machine takes.
    /// </summary>
    private void DecideSync(string folderId, SyncIndex mine, SyncIndex theirs, SyncChoice choice, IReadOnlyCollection<string>? paths)
    {
        IReadOnlyList<SyncDecision> decisions = SyncDecisionStore.ForDifferences(mine, theirs, choice, paths);
        if (decisions.Count == 0)
            return;
        if (paths is null &&
            StudioMessageDialog.Show(
                Window.GetWindow(Root),
                choice == SyncChoice.Theirs
                    ? $"{decisions.Count} зөрүүтэй файлыг нөгөө машины хувилбараар солих уу?{Environment.NewLine}{Environment.NewLine}Энэ машины хувилбар бүр «.erks-sync\\replaced» хавтсанд хадгалагдана."
                    : $"{decisions.Count} зөрүүтэй файлд энэ машины хувилбарыг үлдээх үү?{Environment.NewLine}{Environment.NewLine}Нөгөө машин эдгээрийг авч, өөрийн хувилбарыг «.erks-sync\\replaced»-д хадгална.",
                "Erk-S Sync — зөрүүтэй файл",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question) != MessageBoxResult.OK)
        {
            return;
        }
        SyncDecisionStore.Add(SyncHome.Default().DecisionsPath(folderId), decisions);
        SyncTrayProcess.Poke();
        SetStatus(choice == SyncChoice.Theirs
            ? $"{decisions.Count} файлыг нөгөө машинаас авна - Erk-S Sync дараагийн шалгалтаар хийнэ."
            : $"{decisions.Count} файлд энэ машины хувилбар үлдэнэ - нөгөө машин түүнийг авна.");
        RefreshProjectSyncSection();
    }

    private void WatchSyncState(FrameworkElement section)
    {
        if (projectSyncWatcher is not null)
            return;
        SyncHome home = SyncHome.Default();
        try
        {
            Directory.CreateDirectory(home.Root);
            projectSyncWatcher = new FileSystemWatcher(home.Root, Path.GetFileName(home.StatePath))
            {
                // The tray writes «state.json.writing» and moves it into place.
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                EnableRaisingEvents = true,
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return;
        }
        void Changed(object? sender, FileSystemEventArgs args)
        {
            // Several events per write; one read after they settle, and only for a page on screen.
            if (Interlocked.Exchange(ref projectSyncRefreshQueued, 1) == 1)
                return;
            _ = Root.Dispatcher.InvokeAsync(async () =>
            {
                await Task.Delay(300);
                Interlocked.Exchange(ref projectSyncRefreshQueued, 0);
                if (section.IsVisible && state.HasOpenProject)
                    RefreshProjectSyncSection();
            });
        }
        projectSyncWatcher.Changed += Changed;
        projectSyncWatcher.Created += Changed;
        projectSyncWatcher.Renamed += (sender, args) => Changed(sender, args);
    }

    private static string SyncSize(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.##} ГБ",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.#} МБ",
        >= 1L << 10 => $"{bytes / (double)(1L << 10):0} КБ",
        _ => $"{bytes} Б",
    };

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

    private async Task UnregisterSyncFolderAsync()
    {
        if (!state.HasOpenProject)
            return;
        if (StudioMessageDialog.Show(
                Window.GetWindow(Root),
                $"«{state.Project.Code}» төслийн Erk-S Sync хавтсыг бүх багийнхны хувьд цуцлах уу?" +
                Environment.NewLine + Environment.NewLine +
                "Хавтас, доторх файлууд машин бүр дээр хэвээр үлдэнэ; зөвхөн синкийн бүртгэл арилна.",
                "Синк хавтсыг цуцлах",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning) != MessageBoxResult.OK)
        {
            return;
        }
        StudioSyncOutcome outcome = await SyncFolders.UnregisterAsync(state.Project);
        SetStatus(outcome.Message);
        RefreshProjectSyncSection();
    }

    /// <summary>The person chooses in a window of its own; the work is StudioSyncFolders'.</summary>
    private async Task ShowSyncDialogAsync(string verb, string folderPath)
    {
        // An Explorer verb can start Studio: ask «owner or bot?» only once the session is back.
        await Task.WhenAny(actingIdentityKnown.Task, Task.Delay(TimeSpan.FromSeconds(60)));
        Window.GetWindow(Root)?.Activate();
        SyncFolderDialog dialog;
        if (verb == "make")
        {
            List<ProjectCatalogItem> projects = new LocalProjectCatalog().ListProjects()
                .Where(item => MaySeeProject(item.ProjectId))
                .OrderBy(item => item.ProjectCode, StringComparer.OrdinalIgnoreCase)
                .ToList();
            // A seated machine acting as its bot sees only the bot's assigned projects - none
            // until the seat's rights have arrived. Say so instead of an empty list (2026-10-24).
            string? emptyReason = projects.Count > 0 ? null
                : ActingAsBot
                    ? "Энэ Studio одоо ботын суудлаар ажиллаж байна, ботод томилогдсон төсөл алга (эсвэл суудлын эрх хараахан ирээгүй). Зүүн доод бүртгэлийн цэснээс өөрийн нэрээр ажиллахаа сонгоод дахин оролдоно уу."
                    : "Энэ төхөөрөмж дээр Studio төсөл алга. Эхлээд төслөө нээх эсвэл Cloud-оос татна уу.";
            dialog = SyncFolderDialog.ForMake(folderPath, projects, state.ProjectPath, project => MakeSyncFolderAsync(folderPath, project.ProjectPath), emptyReason);
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

    private Task<StudioSyncOutcome> MakeSyncFolderAsync(string folderPath, string projectPath) =>
        WithSyncProjectAsync(projectPath, project => SyncFolders.MakeAsync(folderPath, project));

    /// <summary>The project at <paramref name="projectPath"/> - the open one as held, any other read from disk.</summary>
    private Task<StudioSyncOutcome> WithSyncProjectAsync(string projectPath, Func<ProjectWorkspace, Task<StudioSyncOutcome>> act)
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
        return act(project);
    }

    // ---- bridge --------------------------------------------------------------------------

    /// <summary>
    /// sync-make / sync-join / sync-unlink. With the deciding argument (project= / folder=)
    /// an agent runs it straight through; without it - an Explorer verb - the person chooses
    /// in Studio's window, which the bridge's quiet mode does not answer for them.
    /// </summary>
    private async Task<StudioBridgeResponse> BridgeSyncFolderAsync(StudioBridgeRequest request, string verb)
    {
        if (verb == "unregister")
            return await BridgeSyncUnregisterAsync(request);
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
    /// sync-decide folder=<id> choice=theirs|mine [peer=<device key>] [path=<file>]: the person's
    /// answer for differing files - all of them, or one - exactly as the section's buttons give it.
    /// </summary>
    private StudioBridgeResponse BridgeSyncDecide(StudioBridgeRequest request)
    {
        string folderId = request.Arg("folder") ?? "";
        SyncChoice? choice = (request.Arg("choice") ?? "").Trim().ToLowerInvariant() switch
        {
            "theirs" => SyncChoice.Theirs,
            "mine" => SyncChoice.Mine,
            _ => null,
        };
        if (folderId.Length == 0 || choice is null)
            return StudioBridgeProtocol.Refuse(request.Id, "sync_decide_arguments", "folder=<id> choice=theirs|mine заана уу.");
        SyncHome home = SyncHome.Default();
        SyncIndex? mine = SyncIndexStore.TryLoad(home.IndexPath(folderId));
        SyncFolderState? folder = SyncTrayProcess.State()?.Folders.FirstOrDefault(item => item.FolderId.Equals(folderId, StringComparison.OrdinalIgnoreCase));
        if (mine is null || folder is null)
            return StudioBridgeProtocol.Refuse(request.Id, "sync_folder_not_here", "Энэ машинд тэр синк хавтасны индекс алга.");
        string? peerKey = request.Arg("peer");
        string? path = request.Arg("path");
        var decisions = new List<SyncDecision>();
        foreach (SyncPeerState peer in folder.Peers.Where(peer => string.IsNullOrWhiteSpace(peerKey) || peer.DeviceKeyId.Equals(peerKey, StringComparison.OrdinalIgnoreCase)))
        {
            if (SyncIndexStore.TryLoad(home.PeerIndexPath(folderId, peer.DeviceKeyId)) is { } theirs)
                decisions.AddRange(SyncDecisionStore.ForDifferences(mine, theirs, choice.Value, string.IsNullOrWhiteSpace(path) ? null : [path]));
        }
        if (decisions.Count == 0)
            return StudioBridgeProtocol.Refuse(request.Id, "sync_nothing_to_decide", "Шийдэх зөрүүтэй файл алга.");
        SyncDecisionStore.Add(home.DecisionsPath(folderId), decisions);
        SyncTrayProcess.Poke();
        RefreshProjectSyncSection();
        return StudioBridgeProtocol.Accept(request.Id, new { decided = decisions.Count }, $"{decisions.Count} файлын шийдвэр Erk-S Sync-д өгөгдлөө.");
    }

    /// <summary>sync-unregister project=<code>: «Синк хавтсыг цуцлах» for the whole team; files stay.</summary>
    private async Task<StudioBridgeResponse> BridgeSyncUnregisterAsync(StudioBridgeRequest request)
    {
        string code = request.Arg("project") ?? "";
        string? projectPath = new LocalProjectCatalog().ListProjects()
            .FirstOrDefault(item => item.ProjectCode.Equals(code, StringComparison.OrdinalIgnoreCase))?.ProjectPath;
        StudioSyncOutcome outcome = projectPath is null
            ? StudioSyncOutcome.Refused("project_not_found", $"«{code}» төсөл энэ төхөөрөмж дээр алга.")
            : await WithSyncProjectAsync(projectPath, project => SyncFolders.UnregisterAsync(project));
        RefreshProjectSyncSection();
        return outcome.Ok
            ? StudioBridgeProtocol.Accept(request.Id, null, outcome.Message)
            : StudioBridgeProtocol.Refuse(request.Id, outcome.Code, outcome.Message);
    }

    /// <summary>
    /// sync-status: this machine's SYN - running or not, the server half, and per folder the
    /// counts the Twin shows (files, peers, differing files). No file names (Master, 2026-10-23).
    /// </summary>
    /// <summary>sync-status: the same record the tray writes to the Twin's status file.</summary>
    private StudioBridgeResponse BridgeSyncStatus(StudioBridgeRequest request) =>
        StudioBridgeProtocol.Accept(request.Id, SyncStatusReport.Of(SyncTrayProcess.State(), SyncTrayProcess.IsRunning));

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
