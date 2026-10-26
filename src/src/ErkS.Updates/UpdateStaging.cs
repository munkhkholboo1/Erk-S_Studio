using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace ErkS.Updates;

/// <summary>
/// What an update leaves in the user's folder (Master, 2026-10-27: PFR's %LOCALAPPDATA% held 150–300 MB of
/// every update's packages; Studio's held a folder per release since July - gigabytes). One place for
/// every product: a finished staging keeps only its log; folders of versions already installed go.
/// </summary>
public static class UpdateStaging
{
    /// <summary>The small records a finished staging keeps (the program reads them once at its next start).</summary>
    public static readonly string[] Kept = { "apply.log", "applied.txt", "failed.txt" };

    private static readonly Regex VersionToken = new(@"\d+(?:\.\d+)+", RegexOptions.CultureInvariant);

    /// <summary>After the update is in place: the staged files go, the log stays.</summary>
    public static void Finished(string stagingDirectory)
    {
        if (!Directory.Exists(stagingDirectory))
            return;
        foreach (string file in Directory.EnumerateFiles(stagingDirectory))
        {
            if (!Kept.Contains(Path.GetFileName(file), StringComparer.OrdinalIgnoreCase))
                TryDelete(() => File.Delete(file));
        }
        foreach (string folder in Directory.EnumerateDirectories(stagingDirectory))
            TryDelete(() => Directory.Delete(folder, recursive: true));
    }

    /// <summary>
    /// The product's update folders (each named with a version: «delta-v0.001.127», «v0.001.83» …) whose
    /// version is not newer than the installed one - nothing in them is needed any more. Returns how many went.
    /// A folder whose name carries no version is left.
    /// </summary>
    public static int Tidy(string updatesRoot, string installedVersion)
    {
        if (!Directory.Exists(updatesRoot) || string.IsNullOrWhiteSpace(installedVersion))
            return 0;
        int removed = 0;
        foreach (string folder in Directory.EnumerateDirectories(updatesRoot))
        {
            Match match = VersionToken.Match(Path.GetFileName(folder));
            if (!match.Success || Compare(match.Value, installedVersion) > 0)
                continue;
            if (TryDelete(() => Directory.Delete(folder, recursive: true)))
                removed++;
        }
        return removed;
    }

    /// <summary>Numeric comparison of the dotted versions in two texts («v0.001.9» &lt; «0.001.10»).</summary>
    public static int Compare(string left, string right)
    {
        int[] a = Parts(left), b = Parts(right);
        for (int index = 0; index < Math.Max(a.Length, b.Length); index++)
        {
            int x = index < a.Length ? a[index] : 0, y = index < b.Length ? b[index] : 0;
            if (x != y)
                return x.CompareTo(y);
        }
        return 0;
    }

    private static int[] Parts(string text)
    {
        Match match = VersionToken.Match(text ?? "");
        return match.Success
            ? match.Value.Split('.').Select(part => int.TryParse(part, out int value) ? value : 0).ToArray()
            : Array.Empty<int>();
    }

    private static bool TryDelete(Action delete)
    {
        try
        {
            delete();
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false; // held open: the next tidy takes it
        }
    }
}
