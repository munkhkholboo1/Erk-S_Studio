using System;
using System.IO;
using Microsoft.Win32;

namespace ErkS.Updates;

/// <summary>
/// After an update is in place, the product's entry in Windows «Apps» names the new version (Master,
/// 2026-10-27: Windows still said V0.001.106 with 126 installed). Every product goes through this one
/// applier, so this is the one place - and a product that updates live (PFR at Revit start) calls the
/// same function (one function, owner 2026-10-19). The entry is found by its install folder; the per-user
/// one is always writable, a machine-wide one only with the right - otherwise it is logged and left (an
/// update is never refused over it). Windows only.
/// </summary>
#if NET5_0_OR_GREATER
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
#endif
public static class UninstallEntries
{
    private const string UninstallPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";

    /// <summary>Names <paramref name="version"/> in the Windows «Apps» entry of the install at <paramref name="install"/>.</summary>
    public static void Show(string install, string version, Action<string>? log = null)
    {
        log ??= _ => { };
        string root = Path.GetFullPath(install).TrimEnd(Path.DirectorySeparatorChar);
        Visit(RegistryHive.CurrentUser, RegistryView.Default, root, version, log);
        Visit(RegistryHive.LocalMachine, RegistryView.Registry64, root, version, log);
        Visit(RegistryHive.LocalMachine, RegistryView.Registry32, root, version, log);
    }

    private static void Visit(RegistryHive hive, RegistryView view, string root, string version, Action<string> log)
    {
        try
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view);
            using RegistryKey? uninstall = baseKey.OpenSubKey(UninstallPath);
            if (uninstall is null)
                return;
            foreach (string name in uninstall.GetSubKeyNames())
            {
                string? displayVersion, displayName;
                using (RegistryKey? entry = uninstall.OpenSubKey(name))
                {
                    if (entry is null || !Belongs(entry, root))
                        continue;
                    displayVersion = entry.GetValue("DisplayVersion") as string;
                    displayName = entry.GetValue("DisplayName") as string;
                }
                string nextVersion = UninstallEntryText.WithVersion(displayVersion, version, isDisplayVersion: true);
                string nextName = UninstallEntryText.WithVersion(displayName, version, isDisplayVersion: false);
                if (nextVersion == displayVersion && nextName == displayName)
                    continue;
                try
                {
                    using RegistryKey? writable = uninstall.OpenSubKey(name, writable: true);
                    if (writable is null)
                        continue;
                    writable.SetValue("DisplayVersion", nextVersion, RegistryValueKind.String);
                    if (displayName is not null && nextName != displayName)
                        writable.SetValue("DisplayName", nextName, RegistryValueKind.String);
                    log($"apps entry {hive}\\{name}: {displayVersion} -> {nextVersion}");
                }
                catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException or IOException)
                {
                    log($"apps entry {hive}\\{name} left as {displayVersion} (no right to write it)");
                }
            }
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
        }
    }

    /// <summary>The entry is this install's: its InstallLocation is the folder, or its uninstaller lives in it.</summary>
    private static bool Belongs(RegistryKey entry, string root)
    {
        string location = (entry.GetValue("InstallLocation") as string ?? "").Trim().Trim('"').TrimEnd('\\', '/');
        if (location.Length > 0)
        {
            try
            {
                return string.Equals(Path.GetFullPath(location).TrimEnd(Path.DirectorySeparatorChar), root, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return false;
            }
        }
        string uninstaller = entry.GetValue("UninstallString") as string ?? "";
        return uninstaller.IndexOf(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
