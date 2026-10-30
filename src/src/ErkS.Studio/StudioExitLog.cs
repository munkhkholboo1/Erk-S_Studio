using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace ErkS.Studio;

/// <summary>
/// How Studio ended, written to logs/studio-exit.log: an exception nothing caught (UI thread,
/// any thread, an unobserved task) or a close with the reason the code that closed it gave.
/// </summary>
/// <remarks>
/// Мастер, 2026-10-31: Studio vanished in the middle of a Sync with nothing in the Windows event
/// log and nothing in its own - so nobody could say whether it fell or was closed, or by what.
/// The code that closes Studio sets the reason (AppDomain data «ErkS.Studio.ExitReason»); a close
/// without one is the window closed by hand. UI-thread exceptions are logged and still end the
/// process as before: carrying on after one would hide a broken state behind a working window.
/// </remarks>
internal static class StudioExitLog
{
    public const string ReasonKey = "ErkS.Studio.ExitReason";

    private static string LogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Erk-S Studio",
        "logs",
        "studio-exit.log");

    public static void Watch(Application application)
    {
        Write($"start pid {Environment.ProcessId}");
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Write($"unhandled ({(args.IsTerminating ? "terminating" : "not terminating")}): {args.ExceptionObject}");
        application.DispatcherUnhandledException += (_, args) =>
            Write("unhandled on the UI thread: " + args.Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Write("unobserved task: " + args.Exception);
            args.SetObserved();
        };
        application.Exit += (_, args) =>
            Write($"exit code {args.ApplicationExitCode}: {AppDomain.CurrentDomain.GetData(ReasonKey) as string ?? "window closed"}");
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            Write("process exit: " + (AppDomain.CurrentDomain.GetData(ReasonKey) as string ?? "no reason recorded"));
    }

    public static void Write(string line)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, $"{DateTime.UtcNow:O} {line}{Environment.NewLine}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
