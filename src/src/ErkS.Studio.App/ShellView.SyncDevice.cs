using System.Net.Http;
using ErkS.Sync.Core;

namespace ErkS.Studio;

/// <summary>
/// Erk-S Sync, 2026-11-24: a machine whose key the server did not know asked for a challenge every pass
/// (865 refusals). Erk-S Sync now waits, says «device-unknown» on its pipe, and Studio - the program that
/// can register the key, being signed in - registers it and says «device-registered», so SYN asks again
/// at once instead of after its backoff.
/// </summary>
internal sealed partial class ShellView
{
    private static readonly TimeSpan SyncDeviceRegisterSpacing = TimeSpan.FromMinutes(10);

    private DateTimeOffset lastSyncDeviceRegisterAttempt = DateTimeOffset.MinValue;

    private bool syncDeviceRegistering;

    private async Task OnSyncDeviceUnknownAsync()
    {
        // Not signed in: the key is registered at sign-in (Erk-S Sync is told again when Studio subscribes next).
        if (account.Current?.Email is not { Length: > 0 } email || syncDeviceRegistering)
            return;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (now - lastSyncDeviceRegisterAttempt < SyncDeviceRegisterSpacing)
            return; // a key the server refuses again is not pressed on it in a loop
        lastSyncDeviceRegisterAttempt = now;
        syncDeviceRegistering = true;
        try
        {
            // The server is the truth here: the local «registered» mark said yes while the server said unknown.
            StudioCloudDeviceKeyRegistration registered = await account.RegisterDeviceKeyAsync();
            StudioDeviceKeyStore.MarkRegistered(registered.DeviceFingerprint, email);
            await Task.Run(() => SyncProtocol.TrySend(new SyncPipeRequest("device-registered", ""), timeoutMilliseconds: 1500));
        }
        catch (Exception exception) when (exception is StudioAccountException or HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            SetStatus("Erk-S Sync-ийн түлхүүр бүртгэгдсэнгүй: " + exception.Message);
        }
        finally
        {
            syncDeviceRegistering = false;
        }
    }
}
