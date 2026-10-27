using System.Runtime.InteropServices;
using ErkS.Sync.Core;

namespace ErkS.Sync;

/// <summary>
/// While this machine's work is not yet whole on the server, Windows is asked to wait before it shuts
/// down, with the reason shown (owner, 2026-10-27: a member who sent and went offline must not leave
/// the others waiting - _shared/syn-durability-offline-2026-10-27.md §4). The person can still choose
/// «Shut down anyway»; nothing is ever forced. A hidden window, because only a window gets the question.
/// </summary>
internal sealed class SyncShutdownGuard : Form
{
    private const int WmQueryEndSession = 0x0011;
    private readonly Func<SyncState?> _state;
    private readonly Action _send;
    private bool _blocking;

    public SyncShutdownGuard(Func<SyncState?> state, Action send)
    {
        _state = state;
        _send = send;
        ShowInTaskbar = false;
        FormBorderStyle = FormBorderStyle.None;
        Opacity = 0;
        Size = new Size(0, 0);
        _ = Handle; // the window exists (hidden) and receives the session's end
    }

    /// <summary>The sentence for what is not yet on the server, or null when everything is.</summary>
    public static string? Unsent(SyncState? state)
    {
        if (state is null)
            return null;
        long changesets = state.Folders.SelectMany(folder => folder.Collabs).Sum(collab => collab.UnsentToServer);
        bool uploading = state.Folders.Any(folder => folder.Uploading || folder.UploadPendingBytes > 0);
        if (changesets == 0 && !uploading)
            return null;
        return changesets > 0
            ? $"Erk-S Sync: таны {changesets} өөрчлөлт серверт хараахан бүрэн хүрээгүй — одоо унтарвал бусад гишүүн түүнийг авахгүй. Илгээж дуусахыг хүлээнэ үү."
            : "Erk-S Sync: файлууд серверт илгээгдэж байна — одоо унтарвал бусад гишүүн хамгийн сүүлийнхийг авахгүй.";
    }

    /// <summary>After every state: the block goes as soon as everything is on the server.</summary>
    public void Follow(SyncState state)
    {
        if (_blocking && Unsent(state) is null && IsHandleCreated)
        {
            ShutdownBlockReasonDestroy(Handle);
            _blocking = false;
        }
    }

    protected override void SetVisibleCore(bool value) => base.SetVisibleCore(false);

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WmQueryEndSession && Unsent(_state()) is { } reason)
        {
            ShutdownBlockReasonCreate(Handle, reason);
            _blocking = true;
            _send(); // what is left goes now
            message.Result = IntPtr.Zero; // «wait» - Windows shows the reason; the person decides
            return;
        }
        base.WndProc(ref message);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool ShutdownBlockReasonCreate(IntPtr window, string reason);

    [DllImport("user32.dll")]
    private static extern bool ShutdownBlockReasonDestroy(IntPtr window);
}
