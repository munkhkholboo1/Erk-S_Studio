using System.Text.RegularExpressions;

namespace ErkS.Sync.Core;

/// <summary>
/// What a sync folder never carries, decided by RULE in code - not by a list a user edits.
///
/// 🔴 A MACHINE'S OWN DEBRIS NEVER TRAVELS (plan §4.6). A Revit local copy belongs to the
/// person who made it; AutoCAD's .dwl lock means «open HERE» and is a lie on any other machine;
/// .bak/.sv$ and Office «~$» files are scratch. Carrying them is exactly how a folder synced by
/// a general tool fills with conflict copies.
///
/// ⚠ AND SYN IS NOT A GENERAL FILE MOVER. These rules apply inside a registered project folder
/// only; nothing outside one is ever looked at.
/// </summary>
public static partial class SyncExclusions
{
    /// <summary>The folder's own bookkeeping, hidden inside the folder.</summary>
    public const string MarkerDirectoryName = ".erks-sync";

    private static readonly HashSet<string> ExcludedDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        MarkerDirectoryName,
        ".sync",                       // Resilio's own folder - its presence stops registration
        "Revit_temp",
        "$RECYCLE.BIN",
        "System Volume Information",
    };

    private static readonly HashSet<string> ExcludedFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Thumbs.db",
        "desktop.ini",
        ".DS_Store",
    };

    private static readonly HashSet<string> ExcludedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".dwl", ".dwl2",               // AutoCAD «opened here» locks
        ".bak", ".sv$", ".ac$",        // AutoCAD backups and autosaves
        ".tmp",
        ".!sync",                      // Resilio partial download
        ".crdownload", ".part",
    };

    /// <summary>Revit's numbered backups: «Model.0001.rvt», «Family.0003.rfa».</summary>
    [GeneratedRegex(@"\.\d{4}\.(rvt|rfa|rte)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RevitNumberedBackup();

    public static bool IsExcludedDirectory(string name) =>
        ExcludedDirectoryNames.Contains(name);

    /// <summary>Name rules alone. Revit local copies need their folder - see <see cref="RevitLocalCopies"/>.</summary>
    public static bool IsExcludedFile(string name)
    {
        if (string.IsNullOrEmpty(name))
            return true;
        if (ExcludedFileNames.Contains(name))
            return true;
        if (name.StartsWith("~$", StringComparison.Ordinal) ||        // Office owner files
            name.StartsWith(".~lock.", StringComparison.Ordinal))      // LibreOffice locks
        {
            return true;
        }
        if (ExcludedExtensions.Contains(Path.GetExtension(name)))
            return true;
        if (name.Contains(ConflictMarker, StringComparison.Ordinal))
            return true;
        return RevitNumberedBackup().IsMatch(name);
    }

    private const string ConflictMarker = " (Erk-S зөрчил ";

    /// <summary>
    /// Where a peer's version is kept when both machines changed the same file (1.2): beside
    /// it, named for the writer and the time - «Plan (Erk-S зөрчил 1A2B3C4D 20261023-1430).dwg».
    /// It is this machine's copy for a person to decide on, so it never travels itself.
    /// </summary>
    public static string ConflictCopyPath(string relativePath, string writerDeviceKeyId, DateTimeOffset at)
    {
        string directory = Path.GetDirectoryName(relativePath) ?? "";
        string writer = (writerDeviceKeyId ?? "").Length > 8 ? writerDeviceKeyId![..8] : writerDeviceKeyId ?? "";
        string name = $"{Path.GetFileNameWithoutExtension(relativePath)}{ConflictMarker}{writer} {at.ToLocalTime():yyyyMMdd-HHmm}){Path.GetExtension(relativePath)}";
        return directory.Length == 0 ? name : Path.Combine(directory, name);
    }

    /// <summary>
    /// The Revit central models in one folder: an «X.rvt» beside its «X_backup» folder.
    /// </summary>
    public static IReadOnlySet<string> RevitCentrals(IEnumerable<string> fileNames, IEnumerable<string> directoryNames)
    {
        var backups = new HashSet<string>(directoryNames, StringComparer.OrdinalIgnoreCase);
        return fileNames
            .Where(name => Path.GetExtension(name).Equals(".rvt", StringComparison.OrdinalIgnoreCase) &&
                           backups.Contains(Path.GetFileNameWithoutExtension(name) + "_backup"))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Local copies of those centrals saved in the same folder - Revit's default name is
    /// «Central_user.rvt» - and their own «_backup» folders. They are one person's working
    /// copy and never travel.
    /// </summary>
    public static IReadOnlySet<string> RevitLocalCopies(IEnumerable<string> fileNames, IReadOnlySet<string> centrals)
    {
        List<string> stems = centrals.Select(Path.GetFileNameWithoutExtension).OfType<string>().ToList();
        var locals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in fileNames)
        {
            if (!Path.GetExtension(name).Equals(".rvt", StringComparison.OrdinalIgnoreCase))
                continue;
            // A local copy keeps a «_backup» folder of its own, so it can look like a central
            // itself; what gives it away is the central whose name it extends.
            string stem = Path.GetFileNameWithoutExtension(name);
            if (stems.Any(central =>
                    !central.Equals(stem, StringComparison.OrdinalIgnoreCase) &&
                    stem.StartsWith(central + "_", StringComparison.OrdinalIgnoreCase)))
            {
                locals.Add(name);
                locals.Add(stem + "_backup");
            }
        }
        return locals;
    }
}
