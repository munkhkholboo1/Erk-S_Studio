using Microsoft.Win32;

namespace ErkS.Sync;

/// <summary>
/// What SYN puts into the user's Windows: start at sign-in (HKCU Run) and the Explorer
/// right-click menu (HKCU\Software\Classes\Directory\shell). No administrator rights; the
/// installer calls «--register», the uninstaller «--unregister», and both land here so the
/// keys are written and removed by one piece of code.
/// </summary>
/// <remarks>
/// Names the Twin watches (Master, 2026-10-23): Run value «ErkS.Sync», shell key «ErkS.Sync».
/// On Windows 11 the menu appears under «Show more options» (decision 13); a first-level entry
/// needs IExplorerCommand and a sparse package, a later step.
/// </remarks>
internal static class SyncShellRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "ErkS.Sync";
    private static readonly string[] ShellRoots =
    [
        @"Software\Classes\Directory\shell\ErkS.Sync",
        @"Software\Classes\Directory\Background\shell\ErkS.Sync",
    ];

    private static readonly (string Key, string Text, string Verb)[] Commands =
    [
        ("1make", "Erk-S Sync фолдер болгох", "make"),
        ("2join", "Erk-S Sync фолдерт холбох", "join"),
        ("3status", "Erk-S Sync — төлөв", "status"),
        ("4unlink", "Холболт салгах", "unlink"),
    ];

    public static void Register(string exePath)
    {
        string quoted = "\"" + exePath + "\"";
        using (RegistryKey run = Registry.CurrentUser.CreateSubKey(RunKey))
            run.SetValue(RunValue, quoted, RegistryValueKind.String);

        foreach (string root in ShellRoots)
        {
            Registry.CurrentUser.DeleteSubKeyTree(root, throwOnMissingSubKey: false);
            using RegistryKey menu = Registry.CurrentUser.CreateSubKey(root);
            menu.SetValue("MUIVerb", "Erk-S Sync", RegistryValueKind.String);
            menu.SetValue("Icon", quoted + ",0", RegistryValueKind.String);
            menu.SetValue("SubCommands", "", RegistryValueKind.String);
            bool background = root.Contains(@"\Background\", StringComparison.OrdinalIgnoreCase);
            foreach ((string key, string text, string verb) in Commands)
            {
                using RegistryKey command = menu.CreateSubKey(@"shell\" + key);
                command.SetValue("MUIVerb", text, RegistryValueKind.String);
                using RegistryKey line = command.CreateSubKey("command");
                // %V is the folder clicked on, or the folder whose background was clicked.
                line.SetValue("", $"{quoted} {verb} \"%V\"", RegistryValueKind.String);
                _ = background;
            }
        }
    }

    public static void Unregister()
    {
        using (RegistryKey? run = Registry.CurrentUser.OpenSubKey(RunKey, writable: true))
            run?.DeleteValue(RunValue, throwOnMissingValue: false);
        foreach (string root in ShellRoots)
            Registry.CurrentUser.DeleteSubKeyTree(root, throwOnMissingSubKey: false);
    }

    /// <summary>Whether the keys point at <paramref name="exePath"/> - the tray repairs them when they do not.</summary>
    public static bool IsRegisteredTo(string exePath)
    {
        using RegistryKey? run = Registry.CurrentUser.OpenSubKey(RunKey);
        return string.Equals(run?.GetValue(RunValue) as string, "\"" + exePath + "\"", StringComparison.OrdinalIgnoreCase) &&
               Registry.CurrentUser.OpenSubKey(ShellRoots[0]) is not null;
    }
}
