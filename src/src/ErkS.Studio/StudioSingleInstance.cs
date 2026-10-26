using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace ErkS.Studio;

/// <summary>
/// One Studio per install folder and Windows session (Erk-S Sync, 2026-10-27: three Studio windows on
/// the laptop held its files open and the update failed). A second start brings the open window to
/// the front and ends. The folder is part of the name, so a development build beside the installed
/// one still opens.
/// </summary>
internal static class StudioSingleInstance
{
    private static Mutex? _held;

    /// <summary>
    /// Takes the folder's place. When another Studio holds it: its window comes to the front and false
    /// is returned. A Studio still closing (a restart) is waited for, at most 10 seconds.
    /// </summary>
    public static bool TryClaim()
    {
        var mutex = new Mutex(false, Name(AppContext.BaseDirectory));
        if (Take(mutex, TimeSpan.Zero))
        {
            _held = mutex;
            return true;
        }
        if (BringOtherToFront())
        {
            mutex.Dispose();
            return false;
        }
        // No window to show: the other one is on its way out.
        if (Take(mutex, TimeSpan.FromSeconds(10)))
        {
            _held = mutex;
            return true;
        }
        mutex.Dispose();
        return false;
    }

    internal static string Name(string folder)
    {
        string normal = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant();
        return @"Local\ErkS.Studio." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normal)))[..16];
    }

    private static bool Take(Mutex mutex, TimeSpan wait)
    {
        try
        {
            return mutex.WaitOne(wait);
        }
        catch (AbandonedMutexException)
        {
            return true; // the last holder ended without letting go: the place is ours
        }
    }

    private static bool BringOtherToFront()
    {
        string? self = Environment.ProcessPath;
        int selfId = Environment.ProcessId;
        foreach (Process process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(self ?? "ErkS.Studio")))
        {
            using (process)
            {
                try
                {
                    if (process.Id == selfId || !string.Equals(process.MainModule?.FileName, self, StringComparison.OrdinalIgnoreCase))
                        continue;
                    IntPtr window = process.MainWindowHandle;
                    if (window == IntPtr.Zero)
                        continue;
                    if (IsIconic(window))
                        ShowWindow(window, 9); // SW_RESTORE
                    SetForegroundWindow(window);
                    return true;
                }
                catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                }
            }
        }
        return false;
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr window);
}
