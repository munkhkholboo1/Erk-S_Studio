using System.IO;
using ErkS.Updates;

namespace ErkS.Studio;

/// <summary>
/// The last reasons Studio could not reach the server, on this machine only (Master, 2026-10-28: «лог/Twin-д
/// reasonCode-оор»): %LOCALAPPDATA%\Erk-S Studio\logs\network-failures.log, the last 200 lines, and the
/// newest one for the bridge's «net-diag». Code, cause and the certificate seen - no account, no name.
/// </summary>
internal static class StudioNetworkFailureLog
{
    private static readonly object Gate = new();

    public static NetworkFailureReason? Last { get; private set; }

    public static string PathOnDisk => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Erk-S Studio", "logs", "network-failures.log");

    public static void Note(NetworkFailureReason reason)
    {
        lock (Gate)
        {
            Last = reason;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PathOnDisk)!);
                List<string> lines = File.Exists(PathOnDisk) ? File.ReadAllLines(PathOnDisk).TakeLast(199).ToList() : [];
                lines.Add($"{DateTime.UtcNow:O} {reason.Code}/{reason.Detail} {reason.Message.Replace(Environment.NewLine, " ")}");
                File.WriteAllLines(PathOnDisk, lines);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
