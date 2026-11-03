using System.Text;
using System.Text.RegularExpressions;

namespace ErkS.Sync.Core;

/// <summary>What a person did to one file or folder's place in the sync (Explorer «Синк хийхгүй болгох» / «Синкт буцааж оруулах»).</summary>
public enum SyncIgnoreChange
{
    Added,
    Removed,
    AlreadyExcluded,
    NotExcluded,
    /// <summary>Left out by another rule - a folder above it, or an EE project's pattern - so it cannot be taken back alone.</summary>
    ExcludedByRule,
    /// <summary>The sync folder itself, or SYN's own files: never left out.</summary>
    Refused,
}

/// <summary>
/// A sync folder's «.erks-sync-ignore» - what it does not carry. Two places are honoured, nowhere else (this
/// is not a general ignore system):
/// - an EE project's folder, one holding an .eeproj (EE / Erk-S Sync, 2026-10-28, _shared/ee-syn-project-sync-2026-10-28.md):
///   what the Engine builds again (site/, maps/*/site/, history/, *.eepack) - 6.4 GB of Altai was going to a partner over a phone;
/// - the sync folder's own root: the files and folders a person chose with Explorer «Синк хийхгүй болгох» (owner,
///   2026-11-03: «төслийн фолдерт байгаа фолдер болон файлыг сонгож синк хийгддэггүй болгодог болгох хэрэгтэй … зарим
///   хэрэггүй том файлууд уншаад дуусахгүй байна»).
/// gitignore-like lines, relative to the marker's folder: «name/» a folder, «*.ext» a file, «a/*/b/» a path, «/a/b.max»
/// exactly that path; «#» a note. Ignored means «not carried»: never read for the index, never taken; what is already
/// on a member stays, it is never told as deleted, and it counts neither as different nor in «%». The marker itself
/// travels, so every member keeps the same rule.
/// </summary>
public sealed class SyncIgnore
{
    public const string FileName = ".erks-sync-ignore";

    private const string ChosenHeader = "# Erk-S Sync: синк хийхгүй файл, хавтас (Explorer-ийн баруун товч «Синк хийхгүй болгох» / «Синкт буцааж оруулах»)";

    private readonly List<(string Base, Regex Pattern, bool DirectoryOnly)> _rules = [];

    public static readonly SyncIgnore None = new();

    public bool IsEmpty => _rules.Count == 0;

    /// <summary>The rules of every marker (relative paths, from an index or a walk) found in <paramref name="root"/>.</summary>
    public static SyncIgnore Of(string root, IEnumerable<string> markerRelativePaths)
    {
        var ignore = new SyncIgnore();
        foreach (string marker in markerRelativePaths)
        {
            string directory = Path.GetDirectoryName(marker) ?? "";
            ignore.Add(root, directory);
        }
        return ignore;
    }

    /// <summary>Adds the marker in <paramref name="relativeDirectory"/> ("" = the root), if it is one SYN honours.</summary>
    public void Add(string root, string relativeDirectory)
    {
        string full = Path.Combine(root, relativeDirectory);
        string marker = Path.Combine(full, FileName);
        string[] lines;
        try
        {
            if (!File.Exists(marker) || !Honoured(full))
                return;
            lines = File.ReadAllLines(marker);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return;
        }
        string scope = Normal(relativeDirectory);
        foreach (string raw in lines)
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;
            bool directoryOnly = line.EndsWith('/');
            bool rooted = line.StartsWith('/');
            string pattern = Normal(line.TrimEnd('/'));
            if (pattern.Length == 0)
                continue;
            // No slash: the name anywhere below the marker; with one (or a leading «/»): a path from the marker's folder.
            string body = string.Join("/", pattern.Split('/').Select(part => Regex.Escape(part).Replace(@"\*", "[^/]*").Replace(@"\?", "[^/]")));
            string anchored = rooted || pattern.Contains('/') ? "^" + body : "^(?:.*/)?" + body;
            _rules.Add((scope, new Regex(anchored + "(?:/|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), directoryOnly));
        }
    }

    /// <summary>An EE project's folder, or a sync folder's root (it holds SYN's own «.erks-sync»).</summary>
    private static bool Honoured(string directory) =>
        Directory.Exists(Path.Combine(directory, SyncExclusions.MarkerDirectoryName)) ||
        Directory.EnumerateFiles(directory, "*.eeproj").Any();

    /// <summary>Whether <paramref name="relativePath"/> (a file, or a folder when <paramref name="isDirectory"/>) is not carried.</summary>
    public bool Ignores(string relativePath, bool isDirectory = false)
    {
        if (_rules.Count == 0)
            return false;
        string path = Normal(relativePath);
        if (Path.GetFileName(path).Equals(FileName, StringComparison.OrdinalIgnoreCase))
            return false; // the marker always travels
        foreach ((string scope, Regex pattern, bool directoryOnly) in _rules)
        {
            string inside;
            if (scope.Length == 0)
                inside = path;
            else if (path.StartsWith(scope + "/", StringComparison.OrdinalIgnoreCase))
                inside = path[(scope.Length + 1)..];
            else
                continue;
            Match match = pattern.Match(inside);
            if (!match.Success)
                continue;
            // «site/» is a folder: a file named «site» is not it, a file inside it is.
            bool matchedWhole = match.Length == inside.Length;
            if (directoryOnly && matchedWhole && !isDirectory)
                continue;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Leaves one file or folder of the sync folder at <paramref name="root"/> out: a «/path» line in the root's
    /// marker, which travels to every member. A folder takes everything below it.
    /// </summary>
    public static SyncIgnoreChange Exclude(string root, string relativePath, bool isDirectory)
    {
        string path = Normal(relativePath);
        if (!Choosable(path))
            return SyncIgnoreChange.Refused;
        if (Governing(root, path).Ignores(path, isDirectory))
            return SyncIgnoreChange.AlreadyExcluded;
        List<string> lines = ChosenLines(root);
        lines.Add(Line(path, isDirectory));
        Write(root, lines);
        return SyncIgnoreChange.Added;
    }

    /// <summary>Takes back what <see cref="Exclude"/> left out; a thing left out by another rule stays out.</summary>
    public static SyncIgnoreChange Include(string root, string relativePath, bool isDirectory)
    {
        string path = Normal(relativePath);
        if (!Choosable(path))
            return SyncIgnoreChange.Refused;
        List<string> lines = ChosenLines(root);
        int removed = lines.RemoveAll(line => line.Trim().Equals(Line(path, isDirectory), StringComparison.OrdinalIgnoreCase));
        if (removed > 0)
            Write(root, lines);
        if (Governing(root, path).Ignores(path, isDirectory))
            return SyncIgnoreChange.ExcludedByRule;
        return removed > 0 ? SyncIgnoreChange.Removed : SyncIgnoreChange.NotExcluded;
    }

    /// <summary>What a person left out of the sync folder at <paramref name="root"/>, as paths (a folder ends with «/»).</summary>
    public static IReadOnlyList<string> Chosen(string root) =>
        ChosenLines(root)
            .Select(line => line.Trim())
            .Where(line => line.StartsWith('/'))
            .Select(line => line[1..].Replace('/', Path.DirectorySeparatorChar))
            .ToList();

    private static string Line(string path, bool isDirectory) => "/" + path + (isDirectory ? "/" : "");

    /// <summary>Never the folder itself, the marker, nor SYN's own bookkeeping and shared-file area.</summary>
    private static bool Choosable(string path)
    {
        if (path.Length == 0 || path == "." || path.StartsWith("../", StringComparison.Ordinal) || path == "..")
            return false;
        string first = path.Split('/')[0];
        return !first.Equals(SyncExclusions.MarkerDirectoryName, StringComparison.OrdinalIgnoreCase) &&
               !first.Equals(SyncCollab.DirectoryName, StringComparison.OrdinalIgnoreCase) &&
               !Path.GetFileName(path).Equals(FileName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The rules that reach <paramref name="path"/>: the markers of the root and of every folder above it.</summary>
    private static SyncIgnore Governing(string root, string path)
    {
        var ignore = new SyncIgnore();
        ignore.Add(root, "");
        string[] parts = path.Split('/');
        for (int depth = 1; depth < parts.Length; depth++)
            ignore.Add(root, Path.Combine(parts[..depth]));
        return ignore;
    }

    private static List<string> ChosenLines(string root)
    {
        string marker = Path.Combine(root, FileName);
        return File.Exists(marker) ? File.ReadAllLines(marker).ToList() : [ChosenHeader];
    }

    /// <summary>Written whole and moved into place: a pass reading it never sees half a rule.</summary>
    private static void Write(string root, List<string> lines)
    {
        string marker = Path.Combine(root, FileName);
        string temporary = marker + ".tmp";
        File.WriteAllText(temporary, string.Join("\r\n", lines) + "\r\n", new UTF8Encoding(false));
        File.Move(temporary, marker, overwrite: true);
    }

    private static string Normal(string path) => path.Replace('\\', '/').Trim('/');
}
