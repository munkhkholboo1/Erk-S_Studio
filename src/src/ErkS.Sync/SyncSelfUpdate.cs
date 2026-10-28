using System.Diagnostics;
using ErkS.Sync.Core;
using ErkS.Updates;

namespace ErkS.Sync;

/// <summary>
/// Erk-S Sync keeps the install up to date while Studio is closed (Erk-S Sync, 2026-10-27: «гишүүн
/// Studio нээгээгүй байсан ч синкийн протоколын засвар ирэх ёстой»). Told by the server when a Studio is
/// published (release push - never polled), it stages the same delta Studio would (ProductUpdate), then
/// starts the applier and steps aside; the applier starts Erk-S Sync again. Studio open from the same
/// folder takes the update itself, at its own close - this then does nothing. Work not yet on the server
/// goes first: the update waits for it.
/// </summary>
internal sealed class SyncSelfUpdate(Func<SyncState?> state, Action stepAside)
{
    private const string ProductCode = "ErkS.Studio";
    private SyncRelease? _pending;
    private int _busy;

    public static string InstallRoot => AppContext.BaseDirectory;

    public static string UpdatesRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Erk-S Studio", "Updates");

    /// <summary>A published version (release push, or after connecting): newer than the install - taken when it can be.</summary>
    public void Heard(SyncRelease release)
    {
        if (!release.ProductCode.Equals(ProductCode, StringComparison.OrdinalIgnoreCase) || release.Channel.Length > 0)
            return;
        string installed = SyncLagging.InstalledRelease(InstallRoot);
        if (installed.Length == 0 || UpdateStaging.Compare(release.Version, installed) <= 0)
            return;
        _pending = release;
        Try();
    }

    /// <summary>After every state: a waiting update goes once nothing is left to send.</summary>
    public void Try()
    {
        if (_pending is not { } release || SyncShutdownGuard.Unsent(state()) is not null || StudioRunning())
            return;
        if (Interlocked.Exchange(ref _busy, 1) == 1)
            return;
        _ = RunAsync(release);
    }

    private async Task RunAsync(SyncRelease release)
    {
        try
        {
            using HttpClient http = NetworkFailure.CreateClient(TimeSpan.FromMinutes(10));
            ProductUpdateStage stage = await ProductUpdate.StageAsync(
                http, Program.ServerUrl(), ProductCode, InstallRoot, release.Version, UpdatesRoot, null, CancellationToken.None);
            _pending = null;
            if (stage.Outcome != ProductUpdateOutcome.ReadyToRestart || StudioRunning())
                return; // nothing to restart for - or Studio opened meanwhile and takes it at its close
            ProductUpdate.LaunchApplier(InstallRoot, stage.Staging, [Environment.ProcessId], [], ["ErkS.Sync.exe"]);
            stepAside();
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidDataException or InvalidOperationException or TaskCanceledException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            // The next release notice (or the next connection's) tries again.
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    private static bool StudioRunning()
    {
        string prefix = Path.GetFullPath(InstallRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (Process process in Process.GetProcessesByName("ErkS.Studio"))
        {
            using (process)
            {
                try
                {
                    if (process.MainModule?.FileName is { } path && path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    return true; // cannot tell: leave the update to Studio
                }
            }
        }
        return false;
    }
}
