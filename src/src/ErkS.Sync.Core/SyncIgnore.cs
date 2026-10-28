using System.Text.RegularExpressions;

namespace ErkS.Sync.Core;

/// <summary>
/// An EE project's «.erks-sync-ignore» (EE / Erk-S Sync, 2026-10-28, _shared/ee-syn-project-sync-2026-10-28.md):
/// what the Engine builds again on every machine (site/, maps/*/site/, history/, *.eepack) never travels -
/// 6.4 GB of Altai was going to a partner over a phone. gitignore-like lines, relative to the marker's
/// folder: «name/» a folder, «*.ext» a file, «a/*/b/» a path; «#» a note. Only an EE project folder (one
/// holding an .eeproj) is honoured - this is not a general ignore system. Ignored means «not carried»: what
/// is already on a member stays, it is never told as deleted, and it counts neither as different nor in «%».
/// The marker itself travels, so every member keeps the same rule.
/// </summary>
public sealed class SyncIgnore
{
    public const string FileName = ".erks-sync-ignore";

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

    /// <summary>Adds the marker in <paramref name="relativeDirectory"/> ("" = the root), if it is an EE project's.</summary>
    public void Add(string root, string relativeDirectory)
    {
        string full = Path.Combine(root, relativeDirectory);
        string marker = Path.Combine(full, FileName);
        string[] lines;
        try
        {
            if (!File.Exists(marker) || !Directory.EnumerateFiles(full, "*.eeproj").Any())
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
            string pattern = Normal(line.TrimEnd('/'));
            if (pattern.Length == 0)
                continue;
            // No slash: the name anywhere below the marker; with one: a path from the marker's folder.
            string body = string.Join("/", pattern.Split('/').Select(part => Regex.Escape(part).Replace(@"\*", "[^/]*").Replace(@"\?", "[^/]")));
            string anchored = pattern.Contains('/') ? "^" + body : "^(?:.*/)?" + body;
            _rules.Add((scope, new Regex(anchored + (directoryOnly ? "(?:/|$)" : "(?:/|$)"), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), directoryOnly));
        }
    }

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

    private static string Normal(string path) => path.Replace('\\', '/').Trim('/');
}
