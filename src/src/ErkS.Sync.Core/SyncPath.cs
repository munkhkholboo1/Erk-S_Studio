namespace ErkS.Sync.Core;

/// <summary>
/// The canonical form of a sync folder's path, and the one comparison of two paths.
///
/// 🔴 THE SAME FULL PATH ON EVERY MACHINE IS THE WHOLE POINT. AutoCAD xrefs and Revit links
/// hold absolute paths; they keep working on a colleague's machine only because the project
/// sits at exactly the same place there (owner, 2026-10-23). So a folder is compared by its
/// full path - never by its name, never relative to a drive the user happens to prefer.
///
/// ⚠ Windows paths are case-insensitive, so «D:\Cloud Work\Erin» and «d:\cloud work\erin» are
/// one folder; trailing separators and «.»/«..» segments are spelling, not identity.
/// </summary>
public static class SyncPath
{
    public static StringComparer Comparer { get; } = StringComparer.OrdinalIgnoreCase;

    public static string Canonical(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string full = Path.GetFullPath(path.Trim().Replace('/', '\\'));
        string root = Path.GetPathRoot(full) ?? "";
        if (full.Length > root.Length)
            full = full.TrimEnd('\\');
        // A drive letter is one letter whatever case it was typed in; the rest keeps its spelling.
        if (full.Length >= 2 && full[1] == ':')
            full = char.ToUpperInvariant(full[0]) + full[1..];
        return full;
    }

    public static bool Same(string left, string right) =>
        Comparer.Equals(Canonical(left), Canonical(right));

    public static bool IsDriveRoot(string path)
    {
        string canonical = Canonical(path);
        string root = Path.GetPathRoot(canonical) ?? "";
        return root.Length > 0 && Comparer.Equals(canonical.TrimEnd('\\'), root.TrimEnd('\\'));
    }

    /// <summary>True when <paramref name="child"/> is <paramref name="parent"/> or lies under it.</summary>
    public static bool IsSameOrInside(string parent, string child)
    {
        string p = Canonical(parent);
        string c = Canonical(child);
        if (Comparer.Equals(p, c))
            return true;
        string prefix = p.EndsWith('\\') ? p : p + "\\";
        return c.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The path of <paramref name="full"/> relative to <paramref name="root"/>, with «\».</summary>
    public static string Relative(string root, string full) =>
        Path.GetRelativePath(Canonical(root), Canonical(full)).Replace('/', '\\');
}
