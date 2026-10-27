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

    /// <summary>The folders as saved by the SYN that ran before: the engine's first writes have none yet.</summary>
    private readonly SyncState? _savedAtStart = SyncStateStore.TryLoad(SyncHome.Default());
    private readonly SynchronizationContext _ui;
    private SyncEngine? _engine;
    private CngSyncDevice? _device;
    private SyncCentralMounts? _mounts;
    private readonly SyncShutdownGuard _guard;
    private readonly SyncSelfUpdate _selfUpdate;

    public SyncTray(SyncPipeRequest? firstRequest)
    {
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        var menu = new ContextMenuStrip();
        menu.Items.Add("Erk-S Sync — төлөв", null, (_, _) => ShowStatus(null));
        menu.Items.Add("Дахин шалгах", null, (_, _) => _engine?.Poke());
        menu.Items.Add("Erk-S Studio нээх", null, (_, _) => OpenStudio());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Гарах", null, (_, _) => ExitAsked());
        _icon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "Erk-S Sync",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _icon.DoubleClick += (_, _) => ShowStatus(null);
        _guard = new SyncShutdownGuard(() => _engine?.LastState, () => _engine?.Poke());
        _selfUpdate = new SyncSelfUpdate(() => _engine?.LastState, () => Post(ExitThread));
        Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerModeChanged;

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
        // One summary line, never a list of members (owner, 2026-10-26: «синк дээрх мэдээллийг цомхон
        // ойлгомжтой хураангуй болгоорой … эхлээд хураангуйгаар хардаг байна»). Details: «Төлөв».
        string text = "Erk-S Sync — " + SyncSummary.Line(SyncSummary.Projects(state), state.ServerStatus);
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
            await using (var engine = new SyncEngine(_home, new SyncServerClient(http) { ClientVersion = SyncLagging.InstalledRelease(AppContext.BaseDirectory) }, _device))
            {
                _engine = engine;
                engine.StateWritten += state => _ui.Post(_ => ShowProgress(state), null);
                engine.StateWritten += state => _ui.Post(_ => _guard.Follow(state), null);
                engine.CollabArrived += Broadcast;
                engine.CollabOnServer += (fileId, unsent) =>
                    Send(_subscribers, SyncProtocol.Serialize(new { @event = "collab-on-server", fileId, unsentToServer = unsent }));
                engine.CollabCheckpointPlaced += (fileId, checkpoint) =>
                    Send(_subscribers, SyncProtocol.Serialize(new { @event = "collab-checkpoint-placed", fileId, checkpoint }));
                engine.ReleasePublished += AnnounceRelease;
                engine.ReleasePublished += _selfUpdate.Heard;
                engine.StateWritten += _ => _selfUpdate.Try();
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
            System.IO.Pipes.NamedPipeServerStream? pipe = null;
            bool heldOpen = false;
            try
            {
                pipe = SyncProtocol.CreateServer();
                await pipe.WaitForConnectionAsync(cancellationToken);
                System.IO.Pipes.NamedPipeServerStream connected = pipe;
                // Never an empty answer: a caller (CGA) takes silence for an old SYN (Erk-S Sync, 2026-10-27).
                heldOpen = await SyncPipeServer.ServeAsync(
                    pipe,
                    (request, reader, writer) => HoldAsync(connected, request, reader, writer, cancellationToken),
                    HandleAsync,
                    PipeFault,
                    cancellationToken);
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
            finally
            {
                if (!heldOpen && pipe is not null)
                    await pipe.DisposeAsync();
            }
        }
    }

    /// <summary>The subscribe verbs keep their connection; every other verb is answered once.</summary>
    private async Task<bool> HoldAsync(
        System.IO.Pipes.NamedPipeServerStream pipe,
        SyncPipeRequest request,
        StreamReader reader,
        StreamWriter writer,
        CancellationToken cancellationToken)
    {
        if (request.Verb == "collab-subscribe")
        {
            // CGA keeps this one connection: a line per changeset that arrives (plan §4).
            await writer.WriteLineAsync(SyncProtocol.Serialize(new SyncPipeResponse(true, "")));
            _ = HoldSubscriberAsync(pipe, reader, writer, _subscribers, cancellationToken);
            return true;
        }
        if (request.Verb == "release-subscribe")
        {
            // Studio (and any program here) keeps this connection: a line per published version,
            // first the ones already known (owner, 2026-10-26: told when published, not polled).
            await writer.WriteLineAsync(SyncProtocol.Serialize(new SyncPipeResponse(true, "")));
            foreach (SyncRelease release in _engine?.Releases ?? [])
                await writer.WriteLineAsync(ReleaseLine(release));
            _ = HoldSubscriberAsync(pipe, reader, writer, _releaseSubscribers, cancellationToken);
            return true;
        }
        return false;
    }

    /// <summary>A request that failed inside SYN, kept on this machine (the last 200 lines) for whoever looks into it.</summary>
    private void PipeFault(string verb, Exception exception)
    {
        try
        {
            string path = Path.Combine(_home.Root, "pipe-faults.log");
            Directory.CreateDirectory(_home.Root);
            var lines = File.Exists(path) ? File.ReadAllLines(path).TakeLast(199).ToList() : [];
            lines.Add($"{DateTime.UtcNow:O} {verb} {exception.GetType().FullName}: {exception.Message} | {exception.StackTrace?.Replace(Environment.NewLine, " / ")}");
            File.WriteAllLines(path, lines);
        }
        catch (Exception logging) when (logging is IOException or UnauthorizedAccessException)
        {
        }
    }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, StreamWriter> _subscribers = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, StreamWriter> _releaseSubscribers = new();

    /// <summary>A subscribe connection lives until its program closes it; the lines it is sent are all it needs.</summary>
    private async Task HoldSubscriberAsync(
        System.IO.Pipes.NamedPipeServerStream pipe,
        StreamReader reader,
        StreamWriter writer,
        System.Collections.Concurrent.ConcurrentDictionary<Guid, StreamWriter> subscribers,
        CancellationToken cancellationToken)
    {
        Guid id = Guid.NewGuid();
        subscribers[id] = writer;
        try
        {
            while (await reader.ReadLineAsync(cancellationToken) is not null)
            {
                // Nothing is asked on this connection; it only tells when it closes.
            }
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException or ObjectDisposedException)
        {
        }
        finally
        {
            subscribers.TryRemove(id, out _);
            writer.Dispose();
            reader.Dispose();
            await pipe.DisposeAsync();
        }
    }

    /// <summary>Each other member's changeset that arrived, to every CGA listening: it only says so - Generate takes it in.</summary>
    private void Broadcast(SyncCollabArrival arrival) =>
        Send(_subscribers, SyncProtocol.Serialize(new { @event = "collab-arrived", fileId = arrival.FileId, writer = arrival.Writer, seq = arrival.Seq, path = arrival.Path }));

    private static string ReleaseLine(SyncRelease release) =>
        SyncProtocol.Serialize(new { @event = "release", productCode = release.ProductCode, channel = release.Channel, version = release.Version });

    /// <summary>A published version, to every program here that listens (Studio shows «Шинэчлэлт бэлэн»).</summary>
    private void AnnounceRelease(SyncRelease release) => Send(_releaseSubscribers, ReleaseLine(release));

    private static void Send(System.Collections.Concurrent.ConcurrentDictionary<Guid, StreamWriter> subscribers, string line)
    {
        foreach ((Guid id, StreamWriter writer) in subscribers)
        {
            try
            {
                lock (writer)
                    writer.WriteLine(line);
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException)
            {
                subscribers.TryRemove(id, out _);
            }
        }
    }

    private async Task<SyncPipeResponse> HandleAsync(SyncPipeRequest request, CancellationToken cancellationToken)
    {
        switch (request.Verb)
        {
            case "rescan":
                _mounts?.RetryDokany(); // Studio just installed it: try again now, not in five minutes
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
            case "collab-central-reset":
            case "collab-checkpoint-prune":
            case "collab-report":
                // A Generate takes the newest work: what the server or the LAN has is fetched first, at most 2 s.
                if (request.Verb == "collab-pull" && _engine is { } engine)
                    await engine.RefreshAsync(TimeSpan.FromSeconds(2), cancellationToken);
                return Collab(request);
            case "redact":
                return await RedactAsync(request, cancellationToken);
            case "quit":
                // An update (ErkS.Updates) replaces this folder's files: the tray closes the way
                // «Гарах» does, and the updater starts it again when the files are in place.
                Post(ExitThread);
                return new SyncPipeResponse(true, "Хаагдаж байна.");
            case "central-info":
                // PFR: is this file a central here, and which side - from SYN's list, read only.
                return new SyncPipeResponse(true, SyncCentralRoles.Of(KnownState(), request.Path, new SyncPathMap()));
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
    /// <summary>
    /// A shared file's status with what only the running engine knows: which changesets are whole on the
    /// server, and whether a checkpoint of it is arriving right now (CGA waits before a new local).
    /// </summary>
    private SyncCollabStatus Fresh(SyncCollabStatus status, string root)
    {
        if (_engine is not { } engine)
            return status;
        SyncFolderState? folder = engine.LastState.Folders.FirstOrDefault(item =>
            item.PresentHere && SyncPath.Same(new SyncPathMap().Physical(item.CanonicalPath), root));
        if (folder is null)
            return status;
        string checkpoints = Path.Combine(SyncCollab.DirectoryName, status.FileId, "checkpoints");
        SyncPeerState? arriving = folder.Peers.FirstOrDefault(peer => peer.Transferring && peer.CurrentFile.StartsWith(checkpoints, StringComparison.OrdinalIgnoreCase));
        return SyncCollab.WithServer(status, root, engine.OnServer(folder.FolderId), engine.ServerKnown(folder.FolderId)) with
        {
            CheckpointArriving = arriving is not null,
            CheckpointPercent = arriving is { CurrentFileBytes: > 0 } peer ? (int)Math.Clamp(100 * peer.CurrentFileDoneBytes / peer.CurrentFileBytes, 0, 100) : 0,
        };
    }

    private SyncPipeResponse Collab(SyncPipeRequest request)
    {
        Dictionary<string, string> args = request.Args ?? [];
        string Arg(string name) => args.TryGetValue(name, out string? value) ? value : "";
        string? root = FolderRootOf(request.Path);
        if (root is null)
            return new SyncPipeResponse(false, "DWG нь энэ машины Erk-S Sync хавтсанд алга.");
        string fileId = Arg("fileId");
        // CGA is about to make a new local: the newest checkpoint comes here (checkpoints are lazy).
        if (fileId.Length > 0 && Arg("wantCheckpoint").Equals("true", StringComparison.OrdinalIgnoreCase))
            _engine?.AskCheckpoint(fileId);
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
                    bool needsCheckpoint = SyncCollab.NeedsCheckpoint(root, _home, fileId, ack);
                    var newLocal = needsCheckpoint ? SyncCollab.LatestCheckpointFile(root, fileId) : null;
                    // CGA v0.043 reads {items, needsCheckpoint} when a base is needed, the plain
                    // list otherwise (as every CGA before it did).
                    return new SyncPipeResponse(
                        true,
                        needsCheckpoint ? "Шинэ local: хамтын файлын хамгийн сүүлийн checkpoint-оос эхэлнэ (Create New Local)." : "",
                        needsCheckpoint
                            ? SyncProtocol.Serialize(new
                            {
                                items = pending,
                                needsCheckpoint = true,
                                checkpoint = newLocal?.Number ?? 0,
                                checkpointPath = newLocal?.DwgPath ?? "",
                                through = newLocal?.Through ?? new Dictionary<string, long>(),
                            })
                            : SyncProtocol.Serialize(pending),
                        needsCheckpoint ? true : null);
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
                        .Select(file => Fresh(SyncCollab.Status(root, _home, file.FileId, writer), root))
                        .ToList();
                    return new SyncPipeResponse(true, "", SyncProtocol.Serialize(statuses));
                }
                case "collab-report":
                {
                    // CGA's account of its drawing (applied changesets + a digest): kept in the shared folder,
                    // so every machine sees whether the drawings agree (Erk-S Sync, 2026-10-27).
                    Dictionary<string, long> applied = Arg("applied").Length > 0
                        ? System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, long>>(Arg("applied")) ?? []
                        : [];
                    SyncCollab.Report(root, fileId, writer, applied, Arg("digest"), DateTimeOffset.UtcNow);
                    _engine?.Poke();
                    return new SyncPipeResponse(true, "", SyncProtocol.Serialize(SyncCollab.HealthOf(root, fileId, writer)));
                }
                case "collab-central-reset":
                {
                    int checkpoint = SyncCollab.ResetCentral(root, _home, fileId);
                    _engine?.Poke();
                    return new SyncPipeResponse(true, "", SyncProtocol.Serialize(new { checkpoint }));
                }
                case "collab-checkpoint-prune":
                {
                    int keep = int.TryParse(Arg("keep"), out int kept) ? kept : 1;
                    bool purgeReplaced = Arg("purgeReplaced").Equals("true", StringComparison.OrdinalIgnoreCase);
                    int floor = SyncCollab.PruneCheckpoints(root, _home, fileId, keep, purgeReplaced, SyncDeviceKey.TryDeviceKeyId() ?? "");
                    _engine?.Poke();
                    return new SyncPipeResponse(true, "", SyncProtocol.Serialize(new { keptFrom = floor }));
                }
                default: // collab-checkpoint
                {
                    Dictionary<string, long> through = Arg("through").Length > 0
                        ? System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, long>>(Arg("through")) ?? []
                        : [];
                    // From a local beside the central (requirement 7), or - older CGA - the central itself.
                    int checkpoint = Arg("source").Length > 0
                        ? SyncCollab.CheckpointFromLocal(root, fileId, request.Path, Arg("source"), through)
                        : SyncCollab.Checkpoint(root, fileId, request.Path, through, _home);
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

    /// <summary>
    /// redact {path, old, new?}: the agent that scrubbed a file names the old content's hash and the
    /// clean one's (_shared/syn-redaction-design-2026-10-26.md). Signed with this device's key,
    /// recorded in the folder's ledger and on the server, and taken out here at once.
    /// </summary>
    private async Task<SyncPipeResponse> RedactAsync(SyncPipeRequest request, CancellationToken cancellationToken)
    {
        Dictionary<string, string> args = request.Args ?? [];
        string Arg(string name) => args.TryGetValue(name, out string? value) ? value : "";
        string? root = FolderRootOf(request.Path);
        if (root is null)
            return new SyncPipeResponse(false, "Файл энэ машины Erk-S Sync хавтсанд алга.");
        using System.Security.Cryptography.ECDsa? key = SyncDeviceKey.TryOpen();
        if (key is null)
            return new SyncPipeResponse(false, "Энэ машины төхөөрөмжийн түлхүүр алга — Studio-д нэвтэрсний дараа дахин оролдоно уу.");
        try
        {
            string deviceKeyId = SyncDeviceKeyIds.Of(key.ExportSubjectPublicKeyInfo());
            SyncRedaction entry = SyncRedactions.Add(root, Path.GetRelativePath(root, request.Path), Arg("old"), Arg("new"), deviceKeyId,
                (bytes, hash) => key.SignData(bytes, hash), DateTimeOffset.UtcNow);
            int removed = SyncRedactions.Apply(root, _home, SyncRedactions.Load(root));
            var map = new SyncPathMap();
            SyncState state = _engine?.LastState ?? SyncStateStore.TryLoad(_home) ?? new SyncState();
            string folderId = state.Folders
                .FirstOrDefault(folder => SyncPath.Same(Path.GetFullPath(map.Physical(folder.CanonicalPath)), root))?.FolderId ?? "";
            string sent = _engine is not null && folderId.Length > 0
                ? await _engine.SendRedactionAsync(folderId, entry, cancellationToken)
                : "Сервертэй холбогдоогүй — дараагийн шалгалтаар илгээнэ.";
            _engine?.Poke();
            return new SyncPipeResponse(true, sent, SyncProtocol.Serialize(new { recorded = true, removed, server = sent.Length == 0 }));
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            return new SyncPipeResponse(false, exception.Message);
        }
    }

    /// <summary>
    /// The folders as last known: the running engine's once it has them, before that what the previous
    /// SYN saved - a question right after SYN starts (an update, a sign-in) is not told «not a sync
    /// folder» (Erk-S Sync, 2026-10-27). The state file itself is no help then: the new engine's first
    /// writes carry no folders yet.
    /// </summary>
    private SyncState KnownState() =>
        _engine?.LastState is { Folders.Count: > 0 } live ? live
        : _savedAtStart is { Folders.Count: > 0 } saved ? saved
        : _engine?.LastState ?? new SyncState();

    /// <summary>The sync folder (on this machine's disk) that holds <paramref name="path"/>.</summary>
    private string? FolderRootOf(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        string full = Path.GetFullPath(path);
        var map = new SyncPathMap();
        return KnownState().Folders
            .Select(folder => Path.GetFullPath(map.Physical(folder.CanonicalPath)))
            .FirstOrDefault(root => SyncPath.IsSameOrInside(root, full));
    }

    /// <summary>Sleep cannot be held off: the person is at least told what has not reached the server.</summary>
    private void OnPowerModeChanged(object? sender, Microsoft.Win32.PowerModeChangedEventArgs args)
    {
        if (args.Mode == Microsoft.Win32.PowerModes.Suspend && SyncShutdownGuard.Unsent(_engine?.LastState) is { } reason)
            Post(() => _icon.ShowBalloonTip(10000, "Erk-S Sync", reason, ToolTipIcon.Warning));
    }

    /// <summary>«Гарах» while work is still on its way to the server asks first; leaving is the person's choice.</summary>
    private void ExitAsked()
    {
        if (SyncShutdownGuard.Unsent(_engine?.LastState) is { } reason &&
            MessageBox.Show(reason + "\n\nОдоо гарах уу?", "Erk-S Sync", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            _engine?.Poke();
            return;
        }
        ExitThread();
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
            Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            _guard.Dispose();
            _icon.Dispose();
            _device?.Dispose();
            _stop.Dispose();
        }
        base.Dispose(disposing);
    }
}
