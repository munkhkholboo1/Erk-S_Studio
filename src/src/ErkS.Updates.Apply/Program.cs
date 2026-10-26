using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace ErkS.Updates.Apply;

/// <summary>
/// <c>ErkS.Updates.Apply --install DIR --staging DIR [--wait PID] [--launch EXE]...</c>
/// Waits for the program to close by itself (it is never closed here - the person decides about
/// their work), puts the staged update in place with rollback, then starts the programs again.
/// A failed apply leaves the old version whole and still starts it.
/// </summary>
internal static class Program
{
    private static StreamWriter? _log;

    private static int Main(string[] args)
    {
        Dictionary<string, List<string>> options = Parse(args);
        string install = One(options, "install");
        string staging = One(options, "staging");
        Directory.CreateDirectory(staging);
        using (_log = new StreamWriter(Path.Combine(staging, "apply.log"), append: true) { AutoFlush = true })
        {
            int exit = 0;
            try
            {
                foreach (string pid in Many(options, "wait"))
                    WaitFor(int.Parse(pid));
                WaitForFolder(install, Many(options, "close"));
                WaitForOthers(install);
                // One applier at a time per install: a second one (the program's own at close, an agent's)
                // waits, then finds nothing left to do.
                using var only = new Mutex(false, @"Local\ErkS.Updates.Apply." + FolderKey(install));
                try
                {
                    only.WaitOne();
                }
                catch (AbandonedMutexException)
                {
                }
                try
                {
                    UpdateManifest target = UpdateManifest.FromJson(File.ReadAllText(Path.Combine(staging, "target.json")));
                    UpdateManifest installed = InstallManifest.LoadOrScan(install, target.ProductCode, "?");
                    UpdatePlan plan = UpdatePlanner.Plan(installed, target);
                    Log($"apply {target.ProductCode} {installed.Version} -> {target.Version}: {plan.Changed.Count} changed, {plan.Removed.Count} removed");
                    if (plan.Changed.Count > 0 || plan.Removed.Count > 0 || installed.Version != target.Version)
                        UpdateApplier.Apply(install, staging, plan);
                    File.WriteAllText(Path.Combine(staging, "applied.txt"), target.Version);
                    Log("applied");
                    UninstallEntries.Show(install, target.Version, Log);
                }
                finally
                {
                    only.ReleaseMutex();
                }
            }
            catch (Exception exception)
            {
                Log("failed, old version kept: " + exception);
                // Studio reads this once at its next start and tells the person (Erk-S Sync, 2026-10-27).
                TryWrite(Path.Combine(staging, "failed.txt"), exception is OtherWindowsException
                    ? "Шинэчлэлт дутуу — Studio-гийн өөр цонх нээлттэй байсан. Studio-гийн бүх цонхыг хаагаад дахин оролдоно уу."
                    : "Шинэчлэлт дутуу — хуучин хувилбар хэвээр. Studio-гийн бүх цонхыг хаагаад дахин оролдоно уу.");
                exit = 1;
            }
            foreach (string launch in Many(options, "launch"))
            {
                try
                {
                    string path = Path.IsPathRooted(launch) ? launch : Path.Combine(install, launch);
                    if (File.Exists(path))
                        Process.Start(new ProcessStartInfo { FileName = path, WorkingDirectory = install, UseShellExecute = true });
                }
                catch (Exception exception)
                {
                    Log("launch failed: " + launch + ": " + exception.Message);
                }
            }
            return exit;
        }
    }

    /// <summary>The program closes itself; this only waits (no time limit - nothing is forced).</summary>
    private static void WaitFor(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            Log("waiting for " + processId);
            process.WaitForExit();
        }
        catch (ArgumentException)
        {
            // already gone
        }
    }

    /// <summary>
    /// Any other program still running from the install folder - another window of the same program
    /// (Erk-S Sync, 2026-10-27: three Studio windows on the laptop held ErkS.CloudEra.Client.dll; only
    /// one was waited for). It holds a person's work, so it is never ended: half a minute to close,
    /// then the update is left for later and the old version stays whole.
    /// </summary>
    private static void WaitForOthers(string install)
    {
        string prefix = Path.GetFullPath(install).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        int self = Process.GetCurrentProcess().Id;
        DateTime giveUpAfter = DateTime.UtcNow.AddSeconds(30);
        bool logged = false;
        while (true)
        {
            List<string> running = new List<string>();
            foreach (Process process in Process.GetProcesses())
            {
                using (process)
                {
                    if (process.Id != self && From(process, prefix))
                        running.Add(process.ProcessName + " " + process.Id);
                }
            }
            if (running.Count == 0)
                return;
            if (!logged)
            {
                Log("waiting for other windows: " + string.Join(", ", running));
                logged = true;
            }
            if (DateTime.UtcNow > giveUpAfter)
                throw new OtherWindowsException(string.Join(", ", running));
            Thread.Sleep(500);
        }
    }

    private sealed class OtherWindowsException(string running) : Exception("still running from the install folder: " + running);

    private static void TryWrite(string path, string text)
    {
        try
        {
            File.WriteAllText(path, text);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Background helpers of the product (named by --close, e.g. ErkS.Sync) that run from the
    /// install folder hold its files open. They were asked to quit; one that has not after a minute
    /// holds no person's work and is ended.
    /// </summary>
    private static void WaitForFolder(string install, List<string> names)
    {
        string prefix = Path.GetFullPath(install).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        DateTime endAfter = DateTime.UtcNow.AddMinutes(1);
        while (true)
        {
            List<Process> running = names.SelectMany(Process.GetProcessesByName).Where(process => From(process, prefix)).ToList();
            if (running.Count == 0)
                return;
            foreach (Process process in running)
            {
                using (process)
                {
                    if (DateTime.UtcNow < endAfter)
                        continue;
                    Log("ending " + process.ProcessName);
                    try
                    {
                        process.Kill();
                        process.WaitForExit(5000);
                    }
                    catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
                    {
                    }
                }
            }
            Thread.Sleep(300);
        }
    }

    private static string FolderKey(string install)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        byte[] hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(install).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant()));
        return BitConverter.ToString(hash, 0, 8).Replace("-", "");
    }

    private static bool From(Process process, string prefix)
    {
        try
        {
            string? path = process.MainModule?.FileName;
            return path is not null && path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static Dictionary<string, List<string>> Parse(string[] args)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index + 1 < args.Length; index += 2)
        {
            if (!args[index].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException("Буруу аргумент: " + args[index]);
            string key = args[index].Substring(2);
            if (!result.TryGetValue(key, out List<string>? values))
                result[key] = values = new List<string>();
            values.Add(args[index + 1]);
        }
        return result;
    }

    private static string One(Dictionary<string, List<string>> options, string key) =>
        options.TryGetValue(key, out List<string>? values) && values.Count == 1
            ? values[0]
            : throw new ArgumentException("--" + key + " шаардлагатай.");

    private static List<string> Many(Dictionary<string, List<string>> options, string key) =>
        options.TryGetValue(key, out List<string>? values) ? values : new List<string>();

    private static void Log(string text) => _log?.WriteLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss ") + text);
}
