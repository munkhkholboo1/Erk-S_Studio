using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using ErkS.Sync.Core;

namespace ErkS.Sync;

/// <summary>
/// ErkS.Sync.exe              → the tray process (one per Windows user)
/// ErkS.Sync.exe make "path"  → Explorer «Erk-S Sync фолдер болгох»
/// ErkS.Sync.exe join "path"  → Explorer «Erk-S Sync фолдерт холбох»
/// ErkS.Sync.exe status "path"→ Explorer «Erk-S Sync — төлөв»
/// ErkS.Sync.exe unlink "path"→ Explorer «Холболт салгах»
/// ErkS.Sync.exe rescan       → Studio asks for a pass now
/// A verb goes to the running tray process; when none runs, this process becomes it.
/// </summary>
internal static class Program
{
    public static readonly string[] Verbs = ["make", "join", "status", "unlink", "rescan", "quit"];

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0].Equals("--register", StringComparison.OrdinalIgnoreCase))
        {
            SyncShellRegistration.Register(Environment.ProcessPath!);
            return 0;
        }
        if (args.Length > 0 && args[0].Equals("--unregister", StringComparison.OrdinalIgnoreCase))
        {
            SyncShellRegistration.Unregister();
            return 0;
        }

        SyncPipeRequest? request = args.Length > 0 && Verbs.Contains(args[0], StringComparer.OrdinalIgnoreCase)
            ? new SyncPipeRequest(args[0].ToLowerInvariant(), args.Length > 1 ? args[1] : "")
            : null;

        using var single = new Mutex(initiallyOwned: true, SyncProtocol.MutexName, out bool first);
        if (!first)
        {
            if (request is not null && SyncProtocol.TrySend(request) is { } answer && !answer.Ok && answer.Message.Length > 0)
                MessageBox.Show(answer.Message, "Erk-S Sync", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return 0;
        }

        // An installed tray keeps its own keys pointing at itself (a moved install root, a
        // repair). A development build never touches the user's registry.
        if (IsInstalled() && !SyncShellRegistration.IsRegisteredTo(Environment.ProcessPath!))
            SyncShellRegistration.Register(Environment.ProcessPath!);

        ApplicationConfiguration.Initialize();
        using var tray = new SyncTray(request);
        Application.Run(tray);
        return 0;
    }

    private static bool IsInstalled() =>
        File.Exists(Path.Combine(AppContext.BaseDirectory, "ErkS.Studio.exe")) &&
        !File.Exists(Path.Combine(AppContext.BaseDirectory, "ErkS.Studio.devroot"));

    /// <summary>The server Studio is signed in to - Studio's own account file - or the public one.</summary>
    public static Uri ServerUrl()
    {
        try
        {
            string account = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Erk-S Studio",
                "account.json");
            if (File.Exists(account))
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(account));
                if (document.RootElement.TryGetProperty("serverUrl", out JsonElement value) &&
                    Uri.TryCreate(value.GetString(), UriKind.Absolute, out Uri? url))
                {
                    return url;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
        }
        return new Uri("https://erk-s.mn");
    }
}

/// <summary>This machine's device key, opened from Studio's key store. Null when Studio never created it.</summary>
internal sealed class CngSyncDevice : ISyncDevice, IDisposable
{
    private readonly ECDsa _key;

    private CngSyncDevice(ECDsa key)
    {
        _key = key;
        DeviceKeyId = SyncDeviceKeyIds.Of(key.ExportSubjectPublicKeyInfo());
        TlsCertificate = SyncDeviceKey.CreateTlsCertificate(key);
    }

    public static CngSyncDevice? TryOpen() => SyncDeviceKey.TryOpen() is { } key ? new CngSyncDevice(key) : null;

    public string DeviceKeyId { get; }

    public X509Certificate2 TlsCertificate { get; }

    public byte[] SignSession(byte[] nonce) => SyncDeviceKey.SignSession(_key, nonce);

    public void Dispose()
    {
        TlsCertificate.Dispose();
        _key.Dispose();
    }
}
