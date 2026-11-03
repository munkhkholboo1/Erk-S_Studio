using Microsoft.Win32;

namespace ErkS.Sync;

/// <summary>
/// What SYN puts into the user's Windows: start at sign-in (HKCU Run) and the Explorer
/// right-click menu (HKCU\Software\Classes\Directory\shell, and *\shell for a file). No administrator rights; the
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
    private const string DirectoryRoot = @"Software\Classes\Directory\shell\ErkS.Sync";
    private const string BackgroundRoot = @"Software\Classes\Directory\Background\shell\ErkS.Sync";
    private const string FileRoot = @"Software\Classes\*\shell\ErkS.Sync";
    private static readonly string[] ShellRoots = [DirectoryRoot, BackgroundRoot, FileRoot];

    private static readonly (string Key, string Text, string Verb)[] FolderCommands =
    [
        ("1make", "Erk-S Sync фолдер болгох", "make"),
        ("2join", "Erk-S Sync фолдерт холбох", "join"),
        ("3status", "Erk-S Sync — төлөв", "status"),
        ("4unlink", "Холболт салгах", "unlink"),
    ];

    /// <summary>
    /// A file or folder inside a sync folder left out, or taken back (owner, 2026-11-03: «фолдер болон файлыг сонгож
    /// синк хийгддэггүй болгодог болгох хэрэгтэй»). Outside a sync folder the tray answers that it is not one.
    /// </summary>
    private static readonly (string Key, string Text, string Verb)[] ChoiceCommands =
    [
        ("5exclude", "Синк хийхгүй болгох", "exclude"),
        ("6include", "Синкт буцааж оруулах", "include"),
    ];

    private static (string Key, string Text, string Verb)[] CommandsOf(string root) => root switch
    {
        DirectoryRoot => [.. FolderCommands, .. ChoiceCommands],
        BackgroundRoot => FolderCommands,
        _ => ChoiceCommands,
    };

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
            foreach ((string key, string text, string verb) in CommandsOf(root))
            {
                using RegistryKey command = menu.CreateSubKey(@"shell\" + key);
                command.SetValue("MUIVerb", text, RegistryValueKind.String);
                using RegistryKey line = command.CreateSubKey("command");
                // %V is the folder clicked on, or the folder whose background was clicked; %1 the file.
                string target = root == FileRoot ? "%1" : "%V";
                line.SetValue("", $"{quoted} {verb} \"{target}\"", RegistryValueKind.String);
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
        // An install from before «Синк хийхгүй болгох» lacks the file menu: written again.
        using RegistryKey? files = Registry.CurrentUser.OpenSubKey(FileRoot + @"\shell\5exclude");
        using RegistryKey? folders = Registry.CurrentUser.OpenSubKey(DirectoryRoot + @"\shell\5exclude");
        return string.Equals(run?.GetValue(RunValue) as string, "\"" + exePath + "\"", StringComparison.OrdinalIgnoreCase) &&
               files is not null && folders is not null;
    }
}
