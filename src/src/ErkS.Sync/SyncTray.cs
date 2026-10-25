using System.Diagnostics;
using System.Text;
using ErkS.Sync.Core;

namespace ErkS.Sync;

/// <summary>
/// The tray process: the engine, the pipe, the icon. Lives until the user chooses «Гарах»
/// or Windows signs out; Studio is only a window onto it (owner, 2026-10-23).
/// </summary>
internal sealed class SyncTray : ApplicationContext
{
    private readonly NotifyIcon _icon;
    private readonly CancellationTokenSource _stop = new();
    private readonly SyncHome _home = SyncHome.Default();
    private readonly SynchronizationContext _ui;
    private SyncEngine? _engine;
    private CngSyncDevice? _device;
    private SyncCentralMounts? _mounts;

    public SyncTray(SyncPipeRequest? firstRequest)
    {
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        var menu = new ContextMenuStrip();
        menu.Items.Add("Erk-S Sync — төлөв", null, (_, _) => ShowStatus(null));
        menu.Items.Add("Дахин шалгах", null, (_, _) => _engine?.Poke());
        menu.Items.Add("Erk-S Studio нээх", null, (_, _) => OpenStudio());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Гарах", null, (_, _) => ExitThread());
        _icon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "Erk-S Sync",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _icon.DoubleClick += (_, _) => ShowStatus(null);

        _ = RunEngineAsync(_stop.Token);
        _ = ServePipeAsync(_stop.Token);
        if (firstRequest is not null)
            _ = HandleAsync(firstRequest, _stop.Token);
    }

    /// <summary>The tray icon's tooltip says how far in step the folders are - changed only when the state is written.</summary>
    private readonly HashSet<string> _baseNoticeShown = new(StringComparer.Ordinal);

    private void ShowProgress(SyncState state)
    {
        // A shared DWG waiting for AutoCAD to close it before its new base goes in: said once.
        foreach (SyncCollabStatus collab in state.Folders.SelectMany(folder => folder.Collabs))
        {
            if (collab.LastError.StartsWith("Хамтын суурь бэлэн", StringComparison.Ordinal))
            {
                if (_baseNoticeShown.Add(collab.FileId))
                    _icon.ShowBalloonTip(10000, "Erk-S Sync", collab.LastError, ToolTipIcon.Info);
            }
            else
            {
                _baseNoticeShown.Remove(collab.FileId);
            }
        }
        SyncPeerState[] peers = state.Folders.SelectMany(folder => folder.Peers).ToArray();
        SyncPeerState? moving = peers.FirstOrDefault(peer => peer.Transferring);
        SyncFolderState? sending = state.Folders.FirstOrDefault(folder => folder.Uploading);
        string text = moving is null && sending is not null
            ? $"Erk-S Sync — серверт илгээж байна {(sending.UploadPendingBytes == 0 ? 100 : sending.UploadedBytes * 100.0 / sending.UploadPendingBytes):0}%" +
              (sending.UploadBytesPerSecond > 0 ? $" · {sending.UploadBytesPerSecond / (1 << 20):0.0} МБ/с" : "")
            : moving is not null
            ? $"Erk-S Sync — хүлээн авч байна {(moving.TransferTotalBytes == 0 ? 100 : moving.TransferDoneBytes * 100.0 / moving.TransferTotalBytes):0}%" +
              (moving.TransferBytesPerSecond > 0 ? $" · {moving.TransferBytesPerSecond / (1 << 20):0.0} МБ/с" : "")
            : peers.Length == 0
                ? "Erk-S Sync"
                : "Erk-S Sync — " + SyncInStep.Words(peers);
        IReadOnlyList<string> behind = moving is null && sending is null ? SyncInStep.Behind(peers) : [];
        if (behind.Count > 0)
            text += "\n" + string.Join("\n", behind);
        _icon.Text = text.Length > 127 ? text[..126] + "…" : text;
    }

    private async Task RunEngineAsync(CancellationToken cancellationToken)
    {
        var http = new HttpClient { BaseAddress = Program.ServerUrl(), Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("ErkS.Sync/1.1");
        while (!cancellationToken.IsCancellationRequested)
        {
            // Studio creates the device key on first sign-in; until then the engine reports why it idles.
            _device ??= CngSyncDevice.TryOpen();
            await using (var engine = new SyncEngine(_home, new SyncServerClient(http), _device))
            {
                _engine = engine;
                engine.StateWritten += state => _ui.Post(_ => ShowProgress(state), null);
                _mounts ??= new SyncCentralMounts(_home, () => _engine?.CentralRoad);
                engine.StateWritten += state => ThreadPool.QueueUserWorkItem(_ => _mounts?.Follow(state));
                if (_device is not null)
                {
                    await engine.RunAsync(cancellationToken);
                    return;
                }
                await engine.PassAsync(cancellationToken);
            }
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task ServePipeAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var pipe = SyncProtocol.CreateServer();
                await pipe.WaitForConnectionAsync(cancellationToken);
                using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
                await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
                SyncPipeRequest? request = SyncProtocol.Deserialize<SyncPipeRequest>(await reader.ReadLineAsync(cancellationToken));
                SyncPipeResponse response = request is null
                    ? new SyncPipeResponse(false, "Хүсэлт уншигдсангүй.")
                    : await HandleAsync(request, cancellationToken);
                await writer.WriteLineAsync(SyncProtocol.Serialize(response));
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception)
            {
                // Any one request that fails must not end the pipe: the next caller (Studio, CGA,
                // Explorer) is served by a fresh instance.
            }
        }
    }

    private async Task<SyncPipeResponse> HandleAsync(SyncPipeRequest request, CancellationToken cancellationToken)
    {
        switch (request.Verb)
        {
            case "rescan":
                _engine?.Poke();
                return new SyncPipeResponse(true, "Дахин шалгаж байна.");
            case "status":
                Post(() => ShowStatus(request.Path));
                return new SyncPipeResponse(true, "");
            case "make":
            case "join":
            case "unlink":
                string action = "sync-" + request.Verb;
                (bool ok, string message) = await StudioLink.RunAsync(
                    action,
                    new Dictionary<string, string> { ["path"] = request.Path },
                    cancellationToken);
                if (ok)
                    _engine?.Poke();
                else if (message.Length > 0)
                    Post(() => _icon.ShowBalloonTip(8000, "Erk-S Sync", message, ToolTipIcon.Warning));
                return new SyncPipeResponse(ok, message);
            case "collab-register":
            case "collab-publish":
            case "collab-pull":
            case "collab-status":
            case "collab-checkpoint":
            case "collab-whoami":
                return Collab(request);
            case "central-begin":
            case "central-end":
                // PFR around Revit's synchronize: the host commits only a synchronize that finished.
                return _mounts is null
                    ? new SyncPipeResponse(true, "host")
                    : await _mounts.TransactionAsync(request.Path, request.Verb == "central-begin", cancellationToken);
            default:
                return new SyncPipeResponse(false, "Мэдэгдэхгүй үйлдэл: " + request.Verb);
        }
    }

    private void ShowStatus(string? path)
    {
        SyncState state = _engine?.LastState ?? SyncStateStore.TryLoad(_home) ?? new SyncState();
        using var form = new SyncStatusForm(state, path);
        form.ShowDialog();
    }

    private static void OpenStudio()
    {
        string studio = Path.Combine(AppContext.BaseDirectory, "ErkS.Studio.exe");
        if (File.Exists(studio))
            Process.Start(new ProcessStartInfo(studio) { UseShellExecute = true, WorkingDirectory = AppContext.BaseDirectory });
    }

    private void Post(Action action) => _ui.Post(_ => action(), null);

    private static Icon LoadIcon()
    {
        using Stream? stream = typeof(SyncTray).Assembly.GetManifestResourceStream("ErkS.Sync.logo.ico");
        return stream is null ? SystemIcons.Application : new Icon(stream);
    }

    /// <summary>
    /// CGA's shared file (К1, _shared/cga-collab-one-file-design-2026-10-24.md §4.5): CGA hands its
    /// changesets here and takes the others' from here; SYN carries them, never opens them.
    /// </summary>
    private SyncPipeResponse Collab(SyncPipeRequest request)
    {
        Dictionary<string, string> args = request.Args ?? [];
        string Arg(string name) => args.TryGetValue(name, out string? value) ? value : "";
        string? root = FolderRootOf(request.Path);
        if (root is null)
            return new SyncPipeResponse(false, "DWG нь энэ машины Erk-S Sync хавтсанд алга.");
        string fileId = Arg("fileId");
        // The writer is the Studio seat (P2P stage 1) - this device's, unless CGA names one.
        SyncCollabWhoAmI? who = _engine?.WhoAmI(root);
        string writer = Arg("writer").Length > 0 ? Arg("writer") : who?.Writer ?? "";
        if (request.Verb == "collab-whoami")
        {
            if (who is null)
                return new SyncPipeResponse(false, "Энэ машин синк хавтсанд холбогдоогүй эсвэл серверээс хараахан мэдээлэл аваагүй.");
            return who.Seat.Length == 0
                ? new SyncPipeResponse(false, "Сервер энэ дансны суудлын дугаарыг (seatId) хараахан өгөөгүй — сервер шинэчлэгдсэний дараа дахин оролдоно уу.")
                : new SyncPipeResponse(true, "", SyncProtocol.Serialize(who));
        }
        if (writer.Length == 0)
            return new SyncPipeResponse(false, "Энэ машины суудал тодорхойгүй — Studio-д нэвтэрсний дараа дахин оролдоно уу.");
        try
        {
            switch (request.Verb)
            {
                case "collab-register":
                {
                    SyncCollabFile file = SyncCollab.Register(root, request.Path, fileId, Arg("ownerSeat"), Arg("cgaVersion"), DateTimeOffset.UtcNow, writer);
                    _engine?.Poke();
                    return new SyncPipeResponse(true, "", SyncProtocol.Serialize(file));
                }
                case "collab-publish":
                {
                    long seq = SyncCollab.Publish(root, fileId, writer, Arg("manifest"), Arg("entities"), _home,
                        int.TryParse(Arg("blocks"), out int blocks) ? blocks : null,
                        int.TryParse(Arg("myBlocks"), out int mine) ? mine : null);
                    _engine?.Poke();
                    return new SyncPipeResponse(true, "", SyncProtocol.Serialize(new { writer, seq }));
                }
                case "collab-pull":
                {
                    Dictionary<string, long>? ack = Arg("ack").Length > 0
                        ? System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, long>>(Arg("ack"))
                        : null;
                    IReadOnlyList<SyncCollabChangeset> pending = SyncCollab.Pull(root, _home, fileId, writer, ack);
                    return new SyncPipeResponse(true, "", SyncProtocol.Serialize(pending));
                }
                case "collab-status":
                {
                    // No file id: the shared file of the DWG asked about, if it is one; else all.
                    string? relativeAsked = File.Exists(request.Path) || request.Path.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase)
                        ? Path.GetRelativePath(root, request.Path)
                        : null;
                    var statuses = SyncCollab.Files(root)
                        .Where(file => fileId.Length > 0
                            ? file.FileId.Equals(fileId, StringComparison.OrdinalIgnoreCase)
                            : relativeAsked is null || !SyncCollab.Files(root).Any(any => SyncPath.Comparer.Equals(any.RelativePath, relativeAsked)) || SyncPath.Comparer.Equals(file.RelativePath, relativeAsked))
                        .Select(file => SyncCollab.Status(root, _home, file.FileId))
                        .ToList();
                    return new SyncPipeResponse(true, "", SyncProtocol.Serialize(statuses));
                }
                default: // collab-checkpoint
                {
                    Dictionary<string, long> through = Arg("through").Length > 0
                        ? System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, long>>(Arg("through")) ?? []
                        : [];
                    int checkpoint = SyncCollab.Checkpoint(root, fileId, request.Path, through, _home);
                    _engine?.Poke();
                    return new SyncPipeResponse(true, "", SyncProtocol.Serialize(new { checkpoint }));
                }
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return new SyncPipeResponse(false, exception.Message);
        }
    }

    /// <summary>The sync folder (on this machine's disk) that holds <paramref name="path"/>.</summary>
    private string? FolderRootOf(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        string full = Path.GetFullPath(path);
        var map = new SyncPathMap();
        SyncState state = _engine?.LastState ?? SyncStateStore.TryLoad(_home) ?? new SyncState();
        return state.Folders
            .Select(folder => Path.GetFullPath(map.Physical(folder.CanonicalPath)))
            .FirstOrDefault(root => SyncPath.IsSameOrInside(root, full));
    }

    protected override void ExitThreadCore()
    {
        _stop.Cancel();
        _mounts?.Dispose(); // the mounts and their links go with the tray; the central stays on its host
        _icon.Visible = false;
        base.ExitThreadCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _icon.Dispose();
            _device?.Dispose();
            _stop.Dispose();
        }
        base.Dispose(disposing);
    }
}
