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
    private void ShowProgress(SyncState state)
    {
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
        _icon.Text = text.Length > 63 ? text[..63] : text;
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
            catch (Exception exception) when (exception is IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
            {
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

    protected override void ExitThreadCore()
    {
        _stop.Cancel();
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
